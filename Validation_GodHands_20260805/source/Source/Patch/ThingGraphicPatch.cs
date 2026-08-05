using HarmonyLib;
using Verse;

namespace GodHandMod
{
    // 替换渲染图形
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Graphic", MethodType.Getter)]
    public static class Thing_Graphic_Patch
    {
        public static void Postfix(Thing __instance, ref Graphic __result)
        {
            // 仅处理建筑和植物 排除Pawn和载具
            if (!(__instance is Building || __instance is RimWorld.Plant)) return;

            var transform = GraphicTransformManager.Get(__instance);

            // 返回包装后的图形
            if (transform != null && transform.HasTransform && __result != null)
            {
                // 避免重复包装
                if (!(__result is GraphicTransformWrapper))
                {
                    __result = new GraphicTransformWrapper(__result, transform);
                    GodHandModMain.DebugLog($"[GraphicPatch] 已包装 {__instance.LabelShort} 的Graphic");
                }
            }
        }
    }
}
