using HarmonyLib;
using Verse;
using RimWorld;
using System;
using System.Collections.Generic;

namespace GodHandMod
{
    // 神之扳手补丁处理叠放存档

    // 追踪叠放建筑位置
    public static class WrenchStackedBuildingTracker
    {
        // 存储叠放位置键值
        private static HashSet<string> stackedPositions = new HashSet<string>();

        // 生成位置唯一识别键
        private static string GetPositionKey(Thing thing)
        {
            if (thing?.Map == null) return null;
            return $"{thing.Map.uniqueID}_{thing.Position.x}_{thing.Position.z}";
        }

        private static string GetPositionKey(int mapId, IntVec3 pos)
        {
            return $"{mapId}_{pos.x}_{pos.z}";
        }

        // 标记位置为叠放
        public static void MarkAsStacked(Thing thing)
        {
            var key = GetPositionKey(thing);
            if (key != null)
            {
                stackedPositions.Add(key);
            }
        }

        // 检查建筑叠放状态
        public static bool IsStacked(Thing thing)
        {
            var key = GetPositionKey(thing);
            return key != null && stackedPositions.Contains(key);
        }

        // 检查坐标叠放状态
        public static bool IsStackedPosition(int mapId, IntVec3 pos)
        {
            return stackedPositions.Contains(GetPositionKey(mapId, pos));
        }

        // 移除叠放标记状态
        public static void Unmark(Thing thing)
        {
            var key = GetPositionKey(thing);
            if (key != null)
            {
                stackedPositions.Remove(key);
            }
        }

        // 序列化叠放数据
        public static void ExposeData()
        {
            List<string> posList = new List<string>(stackedPositions);
            Scribe_Collections.Look(ref posList, "godhand_stackedPositions", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars && posList != null)
            {
                stackedPositions = new HashSet<string>(posList);
                GodHandModMain.DebugLog($"[神之手] 加载了 {stackedPositions.Count} 个叠放位置");
            }
        }

        // 获取叠放计数
        public static int Count => stackedPositions.Count;
    }

    // 追踪存档加载阶段
    public static class WrenchLoadingTracker
    {
        public static bool IsLoading = false;
    }

    // 合并存档加载补丁
    [HarmonyPatch(typeof(Game), "ExposeSmallComponents")]
    public static class Game_ExposeSmallComponents_Patch
    {
        public static void Postfix()
        {
            // 读写时处理叠放数据
            WrenchStackedBuildingTracker.ExposeData();

            // 加载时标记IsLoading
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                WrenchLoadingTracker.IsLoading = true;
                GodHandModMain.DebugLog($"[神之手] 开始加载，已加载 {WrenchStackedBuildingTracker.Count} 个叠放位置");
            }
        }
    }

    // 加载结束重置标记
    [HarmonyPatch(typeof(Map), "FinalizeLoading")]
    public static class Map_FinalizeLoading_Patch
    {
        public static void Postfix()
        {
            WrenchLoadingTracker.IsLoading = false;
        }
    }

    // 跳过叠放建筑冲突检测
    [HarmonyPatch(typeof(GenSpawn), "SpawnBuildingAsPossible")]
    public static class GenSpawn_SpawnBuildingAsPossible_Patch
    {
        public static bool Prefix(Building building, Map map, bool respawningAfterLoad)
        {
            try
            {
                // 叠放建筑加载时跳过检测
                if (WrenchLoadingTracker.IsLoading && respawningAfterLoad &&
                    WrenchStackedBuildingTracker.IsStackedPosition(map.uniqueID, building.Position))
                {
                    // 绕过常规生成逻辑
                    building.SpawnSetup(map, respawningAfterLoad);
                    GodHandModMain.DebugLog($"[神之扳手] 跳过叠放建筑冲突检测: {building.LabelCap} at {building.Position}");
                    return false; // 跳过原方法
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[神之手] Error in SpawnBuildingAsPossible Patch: {ex}", 984756322);
            }

            return true; // 继续执行原方法
        }
    }

    // 压缩列表排除叠放项
    [HarmonyPatch(typeof(MapFileCompressor), "BuildCompressedString")]
    public static class MapFileCompressor_BuildCompressedString_Patch
    {
        public static void Prefix(MapFileCompressor __instance)
        {
            try
            {
                // 使用反射获取map字段
                var mapField = typeof(MapFileCompressor).GetField("map", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (mapField == null) return;

                Map map = mapField.GetValue(__instance) as Map;
                if (map == null) return;

                // 获取compressibilityDecider字段
                var deciderField = typeof(MapFileCompressor).GetField("compressibilityDecider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (deciderField == null) return;

                var decider = deciderField.GetValue(__instance);
                if (decider == null) return;

                // 获取compressedThings列表
                var compressedThingsField = decider.GetType().GetField("compressedThings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (compressedThingsField == null) return;

                var compressedThings = compressedThingsField.GetValue(decider) as HashSet<Thing>;
                if (compressedThings == null) return;

                // 从压缩列表中移除叠放的物品（它们会通过正常方式保存）
                var toRemove = new List<Thing>();
                foreach (var thing in compressedThings)
                {
                    if (WrenchStackedBuildingTracker.IsStacked(thing))
                    {
                        toRemove.Add(thing);
                    }
                }

                foreach (var thing in toRemove)
                {
                    compressedThings.Remove(thing);
                    GodHandModMain.DebugLog($"[神之扳手] 从压缩列表移除叠放建筑: {thing.LabelCap}");
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[神之手] Error in BuildCompressedString Prefix: {ex}", 984756325);
            }
        }
    }
}
