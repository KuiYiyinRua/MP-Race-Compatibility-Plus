using HarmonyLib;
using RimWorld;
using Verse;

namespace GodHandMod.Patch
{
    // 禁止救援正在船上的 Pawn
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.WantsToBeRescued))]
    public static class Patch_InhibitRescue
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            // 船上无需救援
            if (__result && pawn.health.hediffSet.HasHediff(GodHandDefOf.GodHand_IsActuallyABoat))
            {
                __result = false;
            }
        }
    }
}
