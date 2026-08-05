using HarmonyLib;
using Verse;
using RimWorld;

namespace GodHandMod.Patch
{
    // 隐藏船上 Pawn 的原版求救图标(问号/感叹号)
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ShouldShowQuestionMark))]
    public static class Patch_HideDownedOverlay
    {
        public static bool Prefix(Pawn __instance, ref bool __result)
        {
            // 捕获时不显示图标
            if (__instance.health.hediffSet.HasHediff(GodHandDefOf.GodHand_IsActuallyABoat))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
