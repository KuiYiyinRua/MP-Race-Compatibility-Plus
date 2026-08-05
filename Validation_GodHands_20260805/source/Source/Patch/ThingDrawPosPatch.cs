/*
using HarmonyLib;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 直接修改位置属性
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("DrawPos", MethodType.Getter)]
    public static class Thing_DrawPos_Patch
    {
        public static void Postfix(Thing __instance, ref Vector3 __result)
        {
            var transform = GraphicTransformManager.Get(__instance);

            if (transform != null && transform.HasTransform)
            {
                // 应用偏移
                __result += new Vector3(transform.offset.x, 0f, transform.offset.y);

                // 应用渲染层
                __result += Altitudes.AltIncVect * transform.drawLayer;
            }
        }
    }

    // 处理旋转变换
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Rotation", MethodType.Getter)]
    public static class Thing_Rotation_Patch
    {
        public static void Postfix(Thing __instance, ref Rot4 __result)
        {
            var transform = GraphicTransformManager.Get(__instance);

            if (transform != null && transform.HasTransform && transform.rotation != 0f)
            {
                // 将旋转角度转换为最近的Rot4
                // 每90度旋转一次
                int rotSteps = Mathf.RoundToInt(transform.rotation / 90f);
                for (int i = 0; i < Mathf.Abs(rotSteps); i++)
                {
                    __result = __result.Rotated(rotSteps > 0 ? RotationDirection.Clockwise : RotationDirection.Counterclockwise);
                }
            }
        }
    }
}
*/
