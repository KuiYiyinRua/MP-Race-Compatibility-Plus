using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace GodHandMod
{
    // 为蓝图提供 3D 渲染虚影
    [HarmonyPatch(typeof(Blueprint), "DrawAt")]
    public static class Patch_Blueprint_Draw
    {
        [HarmonyPostfix]
        public static void Postfix(Blueprint __instance, Vector3 drawLoc)
        {
            if (__instance != null && __instance.def?.entityDefToBuild is ThingDef targetDef)
            {
                // 使用传入的 drawLoc 保持位置精确同步
                RenderGhostIfApplicable(targetDef, drawLoc, __instance.Rotation);
            }
        }

        public static void RenderGhostIfApplicable(ThingDef targetDef, Vector3 pos, Rot4 rot)
        {
            if (targetDef == null) return;

            if (targetDef.defName == "GodHand_EndRodGenerator")
                GodHandGeneratorController.RenderEndRodMachine(pos, rot, 0f, null);
            else if (targetDef.defName == "GodHand_GearboxGenerator")
                GodHandGeneratorController.RenderGearbox(pos, rot, 0f, true);
        }
    }

    // 为建筑架提供 3D 渲染虚影
    [HarmonyPatch(typeof(Frame), "DrawAt")]
    public static class Patch_Frame_Draw
    {
        [HarmonyPostfix]
        public static void Postfix(Frame __instance, Vector3 drawLoc)
        {
            if (__instance != null && __instance.def?.entityDefToBuild is ThingDef targetDef)
            {
                Patch_Blueprint_Draw.RenderGhostIfApplicable(targetDef, drawLoc, __instance.Rotation);
            }
        }
    }
}
