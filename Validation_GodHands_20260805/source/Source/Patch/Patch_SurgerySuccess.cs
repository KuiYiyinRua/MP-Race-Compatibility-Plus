using HarmonyLib;
using RimWorld;
using Verse;
using System.Collections.Generic;

namespace GodHandMod
{
    // 强制手术成功补丁
    [HarmonyPatch(typeof(Recipe_Surgery), "CheckSurgeryFail")]
    public static class Patch_CheckSurgeryFail_GodHand
    {
        // 手术前置检查
        public static bool Prefix(Pawn surgeon, ref bool __result)
        {
            // 如果执行者是“神之手”代理执行者
            if (surgeon != null && surgeon == MapComponent_GodAssistant.GodHandWorker)
            {
                // 强制返回结果为 false（没失败）
                __result = false;
                // 跳过原版属性检查防止报错
                return false;
            }
            return true;
        }
    }
}
