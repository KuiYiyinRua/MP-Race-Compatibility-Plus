using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350HandlerStateMp
    {
        private static Type compType;
        private static FieldInfo mode, handler;
        private static object specific;
        private static MethodInfo notifyGone, notifyDied;
        private static bool installed;

        internal static void Apply(Harmony harmony)
        {
            if (installed || !MP.enabled || !ModsConfig.IsActive("fluffy.animaltab")) return;
            try
            {
                compType = AccessTools.TypeByName("AnimalTab.CompHandlerSettings") ?? throw new TypeLoadException("AnimalTab.CompHandlerSettings");
                if (!typeof(ThingComp).IsAssignableFrom(compType)) throw new InvalidOperationException("Handler comp base changed");
                mode = AccessTools.DeclaredField(compType, "_mode");
                handler = AccessTools.DeclaredField(compType, "_handler");
                if (mode == null || !mode.FieldType.IsEnum || handler == null || handler.FieldType != typeof(Pawn)) throw new InvalidOperationException("Handler fields changed");
                specific = Enum.Parse(mode.FieldType, "Specific");
                notifyGone = RequireMethod("Notify_HandlerGone", Type.EmptyTypes);
                notifyDied = RequireMethod("Notify_HandlerDied", Type.EmptyTypes);
                MethodInfo valid = AccessTools.DeclaredPropertyGetter(compType, "IsValid");
                MethodInfo allows = RequireMethod("Allows", new[] { typeof(Pawn), typeof(string).MakeByRefType() });
                if (valid == null || valid.ReturnType != typeof(bool) || allows.ReturnType != typeof(bool)) throw new InvalidOperationException("Handler query signatures changed");
                harmony.Patch(valid, prefix: new HarmonyMethod(typeof(Patch_Light350HandlerStateMp), nameof(ValidPrefix)));
                harmony.Patch(allows, prefix: new HarmonyMethod(typeof(Patch_Light350HandlerStateMp), nameof(AllowsPrefix)));
                installed = true;
                Log.Message("[MP-MeowOnlineShop][Light350-B11] AnimalTab: read-only invalid handler queries and deterministic map cleanup installed.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][Light350-B11] REQUIRED TARGET FAILED AnimalTab: " + e);
            }
        }

        private static MethodInfo RequireMethod(string name, Type[] args) => AccessTools.DeclaredMethod(compType, name, args) ?? throw new MissingMethodException(compType.FullName, name);

        private static bool InvalidSpecific(object comp, out Pawn assigned)
        {
            assigned = null;
            if (!Equals(mode.GetValue(comp), specific)) return false;
            assigned = (Pawn)handler.GetValue(comp);
            return assigned == null || assigned.Destroyed || assigned.Dead;
        }

        private static bool ValidPrefix(object __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !InvalidSpecific(__instance, out _)) return true;
            __result = false;
            return false;
        }

        private static bool AllowsPrefix(object __instance, ref string reason, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !InvalidSpecific(__instance, out _)) return true;
            // A stale reference can be queried before this map's next cleanup tick.
            // Keep the query read-only; the shared map tick will reset the policy.
            reason = "Fluffy.AnimalTab.NotAllowed.SpecificHandler".Translate(((ThingComp)__instance).parent.LabelShort, "?");
            __result = false;
            return false;
        }

        internal static void Tick(Map map)
        {
            if (!installed || !MP.IsInMultiplayer || !map.IsPlayerHome || GenTicks.TicksGame % 250 != 0) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber).ToArray())
            {
                ThingComp comp = pawn.AllComps.FirstOrDefault(c => compType.IsInstanceOfType(c));
                if (comp == null || !InvalidSpecific(comp, out Pawn assigned)) continue;
                // Preserve the original destroyed/null branch and the complete
                // death notification, including policy defaults and message state.
                if (assigned == null || assigned.Destroyed) notifyGone.Invoke(comp, null);
                else notifyDied.Invoke(comp, null);
            }
        }
    }

    public sealed class Light350HandlerCleanup : MapComponent
    {
        public Light350HandlerCleanup(Map map) : base(map) { }
        public override void MapComponentTick() {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("light350")) return; Patch_Light350HandlerStateMp.Tick(map); }
    }
}
