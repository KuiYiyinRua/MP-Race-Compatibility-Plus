using HarmonyLib;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 补丁强制倒地
    // 让BodyAngle生效
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GetPosture))]
    public static class HarmonyPatch_Pawn_GetPosture
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn p, ref PawnPosture __result)
        {
            if (p != null && CompPawnCaptureOnTouch.pawnToGenerator.ContainsKey(p))
            {
                // 强制倒地状态
                __result = PawnPosture.LayingOnGroundNormal;
            }
        }
    }
}
