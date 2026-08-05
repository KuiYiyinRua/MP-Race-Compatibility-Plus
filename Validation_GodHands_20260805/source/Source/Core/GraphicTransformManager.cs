using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace GodHandMod
{
    // 全局变换管理器
    public static class GraphicTransformManager
    {
        // 变换数据
        public class TransformData : IExposable
        {
            public Vector2 offset = Vector2.zero;
            public float rotation = 0f;
            public Vector3 scale = Vector3.one;
            public int drawLayer = 0;
            public int? overrideRot = null;

            public bool HasTransform =>
                offset != Vector2.zero ||
                rotation != 0f ||
                scale != Vector3.one ||
                drawLayer != 0 ||
                overrideRot.HasValue;

            public void ExposeData()
            {
                Scribe_Values.Look(ref offset, "offset");
                Scribe_Values.Look(ref rotation, "rotation");
                Scribe_Values.Look(ref scale, "scale", Vector3.one);
                Scribe_Values.Look(ref drawLayer, "drawLayer");
                Scribe_Values.Look(ref overrideRot, "overrideRot");
            }
        }

        // 物体ID对应变换数据
        private static Dictionary<int, TransformData> transforms = new Dictionary<int, TransformData>();

        // 键对应变换数据用于子组件
        private static Dictionary<string, TransformData> transformsByKey = new Dictionary<string, TransformData>();

        // 缓存反射字段
        private static readonly System.Reflection.FieldInfo graphicIntField = AccessTools.Field(typeof(Thing), "graphicInt");

        // 检查是否是合法可变换类型
        private static bool IsValidTransformTarget(Thing thing)
        {
            if (thing == null) return false;
            // 严禁操作 Pawn 及其子类 (包含载具框架所有载具)
            if (thing is Pawn) return false;
            return thing is Building || thing is RimWorld.Plant;
        }

        // 获取或创建物体变换
        public static TransformData GetOrCreate(Thing thing)
        {
            if (!IsValidTransformTarget(thing)) return null;
            int id = thing.thingIDNumber;
            if (!transforms.ContainsKey(id))
            {
                transforms[id] = new TransformData();
                GodHandModMain.DebugLog($"[TransformManager] 为 {thing.LabelShort} 创建变换数据");
            }
            return transforms[id];
        }

        // 获取物体变换数据
        public static TransformData Get(Thing thing)
        {
            if (thing == null || !IsValidTransformTarget(thing)) return null;
            int id = thing.thingIDNumber;
            return transforms.TryGetValue(id, out var data) ? data : null;
        }

        // 获取或创建组件变换
        public static TransformData GetOrCreateByKey(string key)
        {
            if (!transformsByKey.ContainsKey(key))
            {
                transformsByKey[key] = new TransformData();
                GodHandModMain.DebugLog($"[TransformManager] 为组件 {key} 创建变换数据");
            }
            return transformsByKey[key];
        }

        // 获取组件变换数据
        public static TransformData GetByKey(string key)
        {
            return transformsByKey.TryGetValue(key, out var data) ? data : null;
        }

        // 设置物体变换
        public static void Set(Thing thing, Vector2 offset, float rotation, Vector3 scale, int drawLayer, int? overrideRot = null)
        {
            if (thing == null || !IsValidTransformTarget(thing))
            {
                GodHandModMain.DebugLog($"[TransformManager] 跳过非法类型 {thing?.GetType().Name}");
                return;
            }

            var data = GetOrCreate(thing);
            data.offset = offset;
            data.rotation = rotation;
            data.scale = scale;
            data.drawLayer = drawLayer;
            data.overrideRot = overrideRot;

            // 强制注入包装器到渲染字段
            if (data.HasTransform)
            {
                try
                {
                    if (graphicIntField != null)
                    {
                        var currentGraphic = (Graphic)graphicIntField.GetValue(thing);

                        // 如果为空则初始化
                        if (currentGraphic == null)
                            currentGraphic = thing.Graphic;

                        // 检查是否已包装
                        if (currentGraphic != null && !(currentGraphic is GraphicTransformWrapper))
                        {
                            var wrapper = new GraphicTransformWrapper(currentGraphic, data);
                            graphicIntField.SetValue(thing, wrapper);
                            GodHandModMain.DebugLog($"[TransformManager] 已注入包装器到 {thing.LabelShort}");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Log.Warning($"[TransformManager] 注入包装器失败 {e.Message}");
                }
            }

            GodHandModMain.DebugLog($"[TransformManager] 设置变换 {thing.LabelShort}");

            // 标记重绘时增加防御性检查
            try
            {
                if (thing?.Map?.mapDrawer != null)
                {
                    thing.Map.mapDrawer.MapMeshDirty(thing.Position, RimWorld.MapMeshFlagDefOf.Things);
                    thing.Map.mapDrawer.MapMeshDirty(thing.Position, RimWorld.MapMeshFlagDefOf.Buildings);
                }
            }
            catch (System.Exception e)
            {
                 GodHandModMain.DebugLog($"[TransformManager] 重绘标记失败: {e.Message}");
            }
        }

        // 获取变换物体
        public static IEnumerable<Thing> GetAllTransformedThings()
        {
            Map map = Find.CurrentMap;
            if (map == null) yield break;

            foreach (var kvp in transforms)
            {
                if (kvp.Value.HasTransform)
                {
                    // 查找物体
                    foreach (var t in map.spawnedThings)
                    {
                        if (t.thingIDNumber == kvp.Key)
                        {
                            yield return t;
                            break;
                        }
                    }
                }
            }
        }

        // 移除物体变换数据
        public static void Remove(Thing thing)
        {
            int id = thing.thingIDNumber;
            if (transforms.Remove(id))
            {
                GodHandModMain.DebugLog($"[TransformManager] 移除 {thing.LabelShort} 变换数据");
            }
        }

        // 清空所有数据
        public static void Clear()
        {
            transforms.Clear();
            transformsByKey.Clear();
            GodHandModMain.DebugLog("[TransformManager] 清空变换数据");
        }

        // 保存加载入口
        public static void ExposeData()
        {
            ExposeThingTransforms();
            ExposeKeyTransforms();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                GodHandModMain.DebugLog($"[TransformManager] 加载完成");
            }
        }

        // 保存加载物体变换
        private static void ExposeThingTransforms()
        {
            List<int> transKeys = null;
            List<TransformData> transValues = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var valid = transforms.Where(kvp => kvp.Value.HasTransform).ToList();
                transKeys = valid.Select(kvp => kvp.Key).ToList();
                transValues = valid.Select(kvp => kvp.Value).ToList();
            }

            Scribe_Collections.Look(ref transKeys, "transformKeys", LookMode.Value);
            Scribe_Collections.Look(ref transValues, "transformValues", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars && transKeys != null && transValues != null)
            {
                transforms.Clear();
                for (int i = 0; i < transKeys.Count && i < transValues.Count; i++)
                {
                    transforms[transKeys[i]] = transValues[i];
                }
            }
        }

        // 保存加载键值变换
        private static void ExposeKeyTransforms()
        {
            List<string> strKeys = null;
            List<TransformData> strValues = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var valid = transformsByKey.Where(kvp => kvp.Value.HasTransform).ToList();
                strKeys = valid.Select(kvp => kvp.Key).ToList();
                strValues = valid.Select(kvp => kvp.Value).ToList();
            }

            Scribe_Collections.Look(ref strKeys, "transformKeyStrings", LookMode.Value);
            Scribe_Collections.Look(ref strValues, "transformValueStrings", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars && strKeys != null && strValues != null)
            {
                transformsByKey.Clear();
                for (int i = 0; i < strKeys.Count && i < strValues.Count; i++)
                {
                    transformsByKey[strKeys[i]] = strValues[i];
                }
            }
        }
    }
}
