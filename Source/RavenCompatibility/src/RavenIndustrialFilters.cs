using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace MP_MeowOnlineShop
{
    internal record RavenPortFilterContext(ThingComp Port) : ThingFilterContext
    {
        public override ThingFilter Filter => (ThingFilter)AccessTools.Property(Port.GetType(), "Filter").GetValue(Port);
        public override ThingFilter ParentFilter => (ThingFilter)AccessTools.Property(Port.GetType(), "ParentFilter").GetValue(Port);
    }

    internal static class RavenIndustrialFilters
    {
        private static ThingComp drawnCuttingMachine;
        internal static void Apply(Harmony harmony)
        {
            var portDialog = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.Dialog_RavenConveyorPortFilter");
            var cuttingDialog = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.Dialog_RavenCuttingMachineFilter");
            if (portDialog == null || cuttingDialog == null) throw new TypeLoadException("Raven filter dialogs");
            harmony.Patch(AccessTools.DeclaredMethod(portDialog, "DoWindowContents"),
                prefix: new HarmonyMethod(typeof(RavenIndustrialFilters), nameof(BeforePort)),
                finalizer: new HarmonyMethod(typeof(RavenIndustrialFilters), nameof(AfterPort)));
            harmony.Patch(AccessTools.DeclaredMethod(cuttingDialog, "DoWindowContents"),
                prefix: new HarmonyMethod(typeof(RavenIndustrialFilters), nameof(BeforeCutting)),
                finalizer: new HarmonyMethod(typeof(RavenIndustrialFilters), nameof(AfterCutting)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingDef), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(RavenIndustrialFilters), nameof(AllowStone)));
            MP.RegisterSyncMethod(typeof(RavenIndustrialFilters), nameof(SetStone));
        }

        private static void BeforePort(ThingComp ___port, out bool __state)
        {
            __state = MP.InInterface && ___port?.parent != null && !___port.parent.Destroyed;
            if (__state) MP.SetThingFilterContext(new RavenPortFilterContext(___port));
        }
        private static void AfterPort(bool __state)
        {
            if (__state) MP.SetThingFilterContext(null);
        }

        private static void BeforeCutting(ThingComp ___cuttingMachine, out ThingComp __state)
        {
            __state = drawnCuttingMachine;
            if (MP.InInterface) drawnCuttingMachine = ___cuttingMachine;
        }
        private static void AfterCutting(ThingComp __state) => drawnCuttingMachine = __state;

        private static ThingFilter Filter(ThingComp comp, string property)
            => (ThingFilter)AccessTools.Property(comp.GetType(), property).GetValue(comp);

        // The custom stone list bypasses ThingFilterUI, so only its exact filter is intercepted.
        private static bool AllowStone(ThingFilter __instance, ThingDef __0, bool __1)
        {
            if (!MP.InInterface || drawnCuttingMachine == null ||
                !ReferenceEquals(__instance, Filter(drawnCuttingMachine, "StoneFilter"))) return true;
            SetStone(drawnCuttingMachine, __0, __1);
            return false;
        }

        private static void SetStone(ThingComp comp, ThingDef def, bool allow)
        {
            if (comp?.parent == null || comp.parent.Destroyed || def == null) return;
            if (allow && !Filter(comp, "StoneParentFilter").Allows(def)) return;
            Filter(comp, "StoneFilter").SetAllow(def, allow);
        }
    }
}
