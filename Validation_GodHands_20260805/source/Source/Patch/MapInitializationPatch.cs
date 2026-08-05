using HarmonyLib;
using Verse;

namespace GodHandMod
{
    // 初始化地图图形变换
    [HarmonyPatch(typeof(Map), "ConstructComponents")]
    public static class Map_ConstructComponents_Patch
    {
        public static void Postfix(Map __instance)
        {
            // 检查是否已经有这个组件
            var existing = __instance.GetComponent<MapComponentGraphicTransform>();
            if (existing == null)
            {
                // 添加我们的MapComponent
                var component = new MapComponentGraphicTransform(__instance);
                __instance.components.Add(component);
                GodHandModMain.DebugLog($"[神之手] 已为地图添加 MapComponentGraphicTransform");
            }
        }
    }
}
