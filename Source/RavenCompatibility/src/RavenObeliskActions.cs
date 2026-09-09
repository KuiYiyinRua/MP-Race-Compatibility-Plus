using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Verse;

namespace MP_MeowOnlineShop
{
    internal record RavenObeliskFilterContext(ThingComp Pulse) : ThingFilterContext
    {
        public override ThingFilter Filter => (ThingFilter)AccessTools.Property(Pulse.GetType(), "CorpseFilter").GetValue(Pulse);
        public override ThingFilter ParentFilter => (ThingFilter)AccessTools.Property(Pulse.GetType(), "CorpseParentFilter").GetValue(Pulse);
    }
    internal static class RavenObeliskActions
    {
        private static ThingComp drawn;
        private static Type pulseType;
        private static ISyncMethod filterCommand, optionCommand;
        internal static void Apply(Harmony harmony)
        {
            pulseType = AccessTools.TypeByName("RavenRace.Features.ObeliskPulse.CompRavenObeliskPulse");
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(pulseType, "TryStartPulse") ?? throw new MissingMethodException("Raven obelisk pulse"));
            filterCommand = MP.RegisterSyncMethod(typeof(RavenObeliskActions), nameof(SetFilter));
            optionCommand = MP.RegisterSyncMethod(typeof(RavenObeliskActions), nameof(SetOption));
            foreach (string property in new[] { "AbsorbEquipment", "AbsorbInventory" })
                harmony.Patch(AccessTools.DeclaredPropertySetter(pulseType, property), prefix: new HarmonyMethod(typeof(RavenObeliskActions), nameof(BeforeOption)));
            harmony.Patch(AccessTools.DeclaredMethod(pulseType, "CooldownTicksRemaining"), prefix: new HarmonyMethod(typeof(RavenObeliskActions), nameof(Cooldown)));
            harmony.Patch(AccessTools.DeclaredMethod(pulseType, "PostExposeData"), postfix: new HarmonyMethod(typeof(RavenObeliskActions), nameof(ExposeWarning)));
            var collector = AccessTools.TypeByName("RavenRace.Features.ObeliskPulse.RavenObeliskPulseTargetCollector");
            harmony.Patch(AccessTools.DeclaredMethod(collector, "Collect"), postfix: new HarmonyMethod(typeof(RavenObeliskActions), nameof(OrderTargets)));
            var custom = AccessTools.TypeByName("RavenRace.Features.ObeliskPulse.UI.Dialog_RavenObeliskConsole");
            var standard = AccessTools.TypeByName("RavenRace.Features.ObeliskPulse.UI.Dialog_RavenObeliskCorpseFilter");
            harmony.Patch(AccessTools.DeclaredMethod(custom, "DoWindowContents"), prefix: new HarmonyMethod(typeof(RavenObeliskActions), nameof(BeforeCustom)), finalizer: new HarmonyMethod(typeof(RavenObeliskActions), nameof(AfterCustom)));
            harmony.Patch(AccessTools.DeclaredMethod(standard, "DoWindowContents"), prefix: new HarmonyMethod(typeof(RavenObeliskActions), nameof(BeforeStandard)), finalizer: new HarmonyMethod(typeof(RavenObeliskActions), nameof(AfterStandard)));
            foreach (var defType in new[] { typeof(ThingDef), typeof(SpecialThingFilterDef) })
                harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingFilter), "SetAllow", new[] { defType, typeof(bool) }), prefix: new HarmonyMethod(typeof(RavenObeliskActions), nameof(BeforeFilter)));
        }
        private static bool BeforeOption(ThingComp __instance, bool __0, MethodBase __originalMethod)
        {
            if (!MP.InInterface) return true;
            string property = __originalMethod.Name.Substring(4);
            if ((bool)AccessTools.Property(pulseType, property).GetValue(__instance) != __0)
                optionCommand.DoSync(null, __instance, property == "AbsorbInventory", __0);
            return false;
        }
        private static void SetOption(ThingComp pulse, bool inventory, bool value)
        {
            if (pulse?.parent?.Spawned == true) AccessTools.Property(pulseType, inventory ? "AbsorbInventory" : "AbsorbEquipment").SetValue(pulse, value);
        }
        private static void BeforeCustom(ThingComp ___pulse, out ThingComp __state) { __state = drawn; drawn = MP.InInterface ? ___pulse : null; }
        private static void AfterCustom(ThingComp __state) => drawn = __state;
        private static void BeforeStandard(ThingComp ___pulse, out bool __state)
        {
            __state = MP.InInterface && ___pulse?.parent?.Spawned == true;
            if (__state) MP.SetThingFilterContext(new RavenObeliskFilterContext(___pulse));
        }
        private static void AfterStandard(bool __state) { if (__state) MP.SetThingFilterContext(null); }
        private static bool BeforeFilter(ThingFilter __instance, Def __0, bool __1)
        {
            if (!MP.InInterface || drawn == null) return true;
            var filter = (ThingFilter)AccessTools.Property(pulseType, "CorpseFilter").GetValue(drawn);
            if (filter != __instance) return true;
            bool current = __0 is ThingDef thing ? filter.Allows(thing) : filter.Allows((SpecialThingFilterDef)__0);
            // The custom list calls SetAllow every frame, even with no edit.
            if (current != __1) filterCommand.DoSync(null, drawn, __0, __1);
            return false;
        }
        private static void SetFilter(ThingComp pulse, Def def, bool allow)
        {
            if (pulse?.parent?.Spawned != true || def == null) return;
            var filter = (ThingFilter)AccessTools.Property(pulseType, "CorpseFilter").GetValue(pulse);
            if (def is ThingDef thing)
            {
                if (!thing.IsCorpse) return;
                filter.SetAllow(thing, allow);
            }
            else if (def is SpecialThingFilterDef special) filter.SetAllow(special, allow);
        }
        private static bool Cooldown(ThingComp __instance, object __0, ref int __result)
        {
            if (!MP.IsInMultiplayer || __instance.parent.MapHeld == null) return true;
            var state = AccessTools.Field(pulseType, "cooldownState").GetValue(__instance);
            __result = state == null ? 0 : (int)AccessTools.Method(state.GetType(), "Remaining").Invoke(state, new[] { __0, (object)__instance.parent.MapHeld.AsyncTime().mapTicks });
            return false;
        }
        private static void ExposeWarning(ThingComp __instance)
        {
            var field = AccessTools.Field(pulseType, "brothOutputWarningShown");
            bool shown = (bool)field.GetValue(__instance);
            Scribe_Values.Look(ref shown, "mpBrothOutputWarningShown");
            if (Scribe.mode == LoadSaveMode.LoadingVars) field.SetValue(__instance, shown);
        }
        private static void OrderTargets(IList __result)
        {
            if (!MP.IsInMultiplayer || __result == null || __result.Count < 2) return;
            var targets = __result.Cast<object>().OrderBy(t => (float)AccessTools.Field(t.GetType(), "distance").GetValue(t))
                .ThenBy(t => ((Thing)AccessTools.Field(t.GetType(), "target").GetValue(t)).thingIDNumber).ToArray();
            for (int i = 0; i < targets.Length; i++) __result[i] = targets[i];
        }
    }
}
