using HarmonyLib;
using Verse;
using RimWorld;
using RimWorld.Planet;
using System.Reflection;

namespace GodHandMod
{
    // 永恒停滞组件补丁
    public static class HarmonyPatches_StasisLock
    {
        // 辅助判断方法
        private static bool IsStasisLocked(Pawn pawn)
        {
            if (StasisLockManager.activeLockCount <= 0 || pawn == null) return false;
            return StasisLockManager.lockedPawnIDs.Contains(pawn.thingIDNumber);
        }

        // 锁定能力数值
        [HarmonyPatch(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.GetLevel))]
        public static class Patch_Capacities_Lock
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn ___pawn, ref float __result)
            {
                if (IsStasisLocked(___pawn)) __result = 0.01f;
            }
        }

        // 彻底拦截所有频率的 Tick
        [HarmonyPatch(typeof(Pawn), "Tick")]
        public static class Patch_Pawn_Tick_Freeze
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn __instance)
            {
                if (IsStasisLocked(__instance))
                {
                    // 允许基础渲染刷新
                    if (__instance.Spawned)
                    {
                        __instance.Drawer?.renderer.EffectersTick(false);
                        __instance.Drawer?.renderer.ProcessPostTickVisuals(1);
                    }
                    return false; // 拦截逻辑Tick
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Pawn), "TickInterval")]
        public static class Patch_Pawn_TickInterval_Freeze
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn __instance) => !IsStasisLocked(__instance);
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.TickRare))]
        public static class Patch_Pawn_TickRare_Freeze
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn __instance) => !IsStasisLocked(__instance);
        }

        [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.TickLong))]
        public static class Patch_Pawn_TickLong_Freeze
        {
            [HarmonyPrefix]
            public static bool Prefix(ThingWithComps __instance)
            {
                if (__instance is Pawn p) return !IsStasisLocked(p);
                return true;
            }
        }

        // 拦截思想与精神损耗
        [HarmonyPatch(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval))]
        public static class Patch_Needs_Freeze
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn) => !IsStasisLocked(___pawn);
        }


        // 拦截健康状态进展
        [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.HealthTick))]
        public static class Patch_Health_Freeze
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn) => !IsStasisLocked(___pawn);
        }

        // 锁定状态进展（针对已经存在的 Hediff）
        [HarmonyPatch(typeof(Hediff), nameof(Hediff.Tick))]
        public static class Patch_Hediff_Freeze
        {
            [HarmonyPrefix]
            public static bool Prefix(Hediff __instance)
            {
                if (IsStasisLocked(__instance.pawn))
                {
                    // 仅允许锁本身 Tick
                    return __instance.def == GodHandDefOf.GodHand_StasisLock;
                }
                return true;
            }
        }
    }
}
