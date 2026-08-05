using HarmonyLib;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 隐藏助手渲染
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PawnRenderer), "RenderPawnAt")]
    public static class PawnRenderer_HideWorker_Patch
    {
        public static bool Prefix(Pawn ___pawn)
        {
            if (___pawn?.Name is NameTriple n && n.First == "God" && n.Nick == "Hand" && n.Last == "Worker")
            {
                return false; // 跳过渲染
            }
            return true;
        }
    }
}
