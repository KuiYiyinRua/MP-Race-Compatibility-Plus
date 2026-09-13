using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_CumpilationRecipeQueryMp
    {
        private static HediffDef cumflation;

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || !ModsConfig.IsActive("vegapnk.cumpilation")) return;
            try
            {
                var type = AccessTools.TypeByName("Cumpilation.Leaking.Recipe_ExtractCum")
                    ?? throw new TypeLoadException("Cumpilation.Leaking.Recipe_ExtractCum");
                var target = AccessTools.DeclaredMethod(type, "AvailableOnNow",
                    new[] { typeof(Thing), typeof(BodyPartRecord) });
                cumflation = DefDatabase<HediffDef>.GetNamedSilentFail("Cumpilation_Cumflation");
                if (target == null || target.ReturnType != typeof(bool) || cumflation == null)
                    throw new InvalidOperationException("Cumpilation extraction query/hediff definition changed");
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patch_CumpilationRecipeQueryMp), nameof(AvailablePrefix))
                    { priority = Priority.First });
                Log.Message("[MP-MeowOnlineShop] Cumpilation extraction availability is read-only in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED TARGET FAILED Cumpilation extraction query: " + e);
            }
        }

        private static bool AvailablePrefix(Thing __0, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !(__0 is Pawn pawn)) return true;
            // AvailableOnNow is also called while merely drawing surgery menus.
            // Its GetOrCreate call adds a zero-severity hediff on that peer only.
            // HealthTick runs its leaking comp BEFORE removing it on the next tick,
            // consuming an extra map Rand draw even though no surgery was ordered.
            var existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(cumflation);
            if (existing != null && existing.Severity > 0f) return true;
            __result = false;
            return false;
        }
    }
}
