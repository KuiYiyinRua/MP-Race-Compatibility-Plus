using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace GodHandMod
{
    // 拦截艺术描述生成
    [HarmonyPatch(typeof(CompArt), "GenerateImageDescription")]
    public static class Patch_CompArt_GenerateImageDescription
    {
        [HarmonyPrefix]
        public static bool Prefix(CompArt __instance, ref TaggedString __result)
        {
            if (__instance.parent == null) return true;

            var world = Find.World;
            if (world != null)
            {
                var comp = world.GetComponent<WorldComponent_GodHandData>();
                if (comp != null && comp.TryGetDescription(__instance.parent.thingIDNumber, out string desc))
                {
                    __result = desc;
                    return false; // 跳过原方法使用自定义描述
                }
            }
            return true;
        }
    }
}
