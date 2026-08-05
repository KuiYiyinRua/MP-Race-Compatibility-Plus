using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace GodHandMod
{
    // 重定向手术产物生成位置
    // 解决虚拟角色报错
    [HarmonyPatch(typeof(MedicalRecipesUtility), "SpawnNaturalPartIfClean")]
    public static class Patch_SpawnNaturalPartIfClean_GodHand
    {
        public static bool Prefix(Pawn pawn, BodyPartRecord part, ref IntVec3 pos, ref Map map, ref Thing __result)
        {
            // 检查地图和越界
            if ((map == null || !pos.InBounds(map)) && pawn != null && pawn.Spawned)
            {
                map = pawn.Map;
                pos = pawn.Position;
            }
            return true;
        }
    }

    // 重定向 Hediff 产物生成位置（如移除仿生零件）
    [HarmonyPatch(typeof(MedicalRecipesUtility), "SpawnThingsFromHediffs")]
    public static class Patch_SpawnThingsFromHediffs_GodHand
    {
        public static bool Prefix(Pawn pawn, BodyPartRecord part, ref IntVec3 pos, ref Map map)
        {
            if ((map == null || !pos.InBounds(map)) && pawn != null && pawn.Spawned)
            {
                map = pawn.Map;
                pos = pawn.Position;
            }
            return true;
        }
    }
}
