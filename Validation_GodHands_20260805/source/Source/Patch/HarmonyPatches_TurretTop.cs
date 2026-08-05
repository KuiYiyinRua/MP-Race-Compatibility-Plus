using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using System.Reflection;

namespace GodHandMod
{
    // 应用炮塔顶部自定义变换
    [HarmonyPatch(typeof(TurretTop))]
    [HarmonyPatch("DrawTurret")]
    public static class Patch_TurretTop_DrawTurret
    {
        private static FieldInfo parentTurretField;
        private static PropertyInfo curRotationProperty;
        private static FieldInfo artworkRotationField;

        static Patch_TurretTop_DrawTurret()
        {
            // 缓存反射字段
            parentTurretField = typeof(TurretTop).GetField("parentTurret",
                BindingFlags.Instance | BindingFlags.NonPublic);
            curRotationProperty = typeof(TurretTop).GetProperty("CurRotation",
                BindingFlags.Instance | BindingFlags.Public);
            artworkRotationField = typeof(TurretTop).GetField("ArtworkRotation",
                BindingFlags.Static | BindingFlags.Public);
        }

        // 前缀补丁替换渲染逻辑
        public static bool Prefix(object __instance, Vector3 drawLoc, Vector3 recoilDrawOffset, float recoilAngleOffset)
        {
            try
            {
                // 获取父炮塔
                if (parentTurretField == null || curRotationProperty == null)
                    return true; // 反射失败走原版

                Building_Turret parentTurret = parentTurretField.GetValue(__instance) as Building_Turret;
                if (parentTurret == null)
                    return true; // 走原版

                // 检查是否有自定义Transform
                string turretTopKey = $"{parentTurret.thingIDNumber}_turretTop";
                var transform = GraphicTransformManager.GetByKey(turretTopKey);

                // 无变换则走原版
                if (transform == null || !transform.HasTransform)
                {
                    return true; // 执行原版方法
                }

                // 执行自定义渲染逻辑

                float curRotation = (float)curRotationProperty.GetValue(__instance);
                int artworkRotation = artworkRotationField != null ? (int)artworkRotationField.GetValue(null) : -90;

                // 复制原版渲染逻辑
                Vector3 v = new Vector3(
                    parentTurret.def.building.turretTopOffset.x,
                    0f,
                    parentTurret.def.building.turretTopOffset.y
                ).RotatedBy(curRotation);

                float turretTopDrawSize = parentTurret.def.building.turretTopDrawSize;
                v = v.RotatedBy(recoilAngleOffset);
                v += recoilDrawOffset;

                float aimAngle = parentTurret.CurrentEffectiveVerb?.AimAngleOverride ?? curRotation;
                Vector3 pos = drawLoc + Altitudes.AltIncVect + v;
                Quaternion rotation = ((float)artworkRotation + aimAngle).ToQuat();
                Vector3 scale = new Vector3(turretTopDrawSize, 1f, turretTopDrawSize);

                // === 应用自定义Transform ===

                // 应用位置偏移
                pos += new Vector3(transform.offset.x, 0f, transform.offset.y);

                // 应用旋转角度
                rotation *= Quaternion.Euler(0f, transform.rotation, 0f);

                // 应用缩放比例
                scale = Vector3.Scale(scale, transform.scale);

                // 应用图层偏移
                if (transform.drawLayer != 0)
                {
                    pos.y += transform.drawLayer * 0.01f; // 层级偏移
                }

                // 绘制
                Matrix4x4 matrix = Matrix4x4.TRS(pos, rotation, scale);
                Graphics.DrawMesh(
                    mesh: MeshPool.plane10,
                    matrix: matrix,
                    material: parentTurret.TurretTopMaterial,
                    layer: 0
                );

                // 返回false跳过原方法
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[神之手] TurretTop.DrawTurret补丁失败: {ex}");
                return true; // 出错时走原版
            }
        }
    }
}
