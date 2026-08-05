using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod.Patch
{
    // 用于隐藏建筑与工作类型的补丁
    [StaticConstructorOnStartup]
    public static class Patch_GeneratorHiding
    {
        static Patch_GeneratorHiding()
        {
            var harmony = new Harmony("Palpha.godhands.generatorhiding");

            // 隐藏设计器
            var originalVisible = AccessTools.PropertyGetter(typeof(Designator_Build), nameof(Designator_Build.Visible));
            var prefixVisible = new HarmonyMethod(typeof(Patch_GeneratorHiding), nameof(Prefix_DesignatorVisible));
            harmony.Patch(originalVisible, prefixVisible);

            // 隐藏工作类型
            var originalWorkVisible = AccessTools.PropertyGetter(typeof(WorkTypeDef), nameof(WorkTypeDef.VisibleCurrently));
            var postfixWorkVisible = new HarmonyMethod(typeof(Patch_GeneratorHiding), nameof(Postfix_WorkTypeVisible));
            harmony.Patch(originalWorkVisible, postfix: postfixWorkVisible);
        }

        // 隐藏建筑
        public static bool Prefix_DesignatorVisible(Designator_Build __instance, ref bool __result)
        {
            if (!GodHandModMain.Settings.enableHandCrankGenerator && __instance.PlacingDef == GodHandDefOf.GodHand_GearboxGenerator)
            {
                __result = false;
                return false;
            }
            return true;
        }

        // 隐藏工作面板中的工作类型
        public static void Postfix_WorkTypeVisible(WorkTypeDef __instance, ref bool __result)
        {
            if (__result && !GodHandModMain.Settings.enableHandCrankGenerator && __instance.defName == "GodHand_HandCrank")
            {
                __result = false;
            }
        }
    }
}
