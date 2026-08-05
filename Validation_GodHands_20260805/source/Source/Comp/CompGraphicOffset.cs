using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 处理贴图变换的组件
    public class CompGraphicOffset : ThingComp
    {
        public Vector2 offset = Vector2.zero;
        public float rotation = 0f; // 旋转角度（度）
        public Vector3 scale = Vector3.one; // 缩放
        public int drawLayer = 0; // 渲染层偏移

        private Matrix4x4 transformMatrix = Matrix4x4.identity;
        private bool hasTransform = false;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref offset.x, "graphicOffsetX", 0f);
            Scribe_Values.Look(ref offset.y, "graphicOffsetY", 0f);
            Scribe_Values.Look(ref rotation, "graphicRotation", 0f);
            Scribe_Values.Look(ref scale.x, "graphicScaleX", 1f);
            Scribe_Values.Look(ref scale.y, "graphicScaleY", 1f);
            Scribe_Values.Look(ref scale.z, "graphicScaleZ", 1f);
            Scribe_Values.Look(ref drawLayer, "graphicDrawLayer", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                UpdateTransformMatrix();
            }
        }

        public void ApplyTransform()
        {
            GodHandModMain.DebugLog($"[CompGraphicOffset] ApplyTransform被调用: {parent?.LabelShort}");

            UpdateTransformMatrix();

            GodHandModMain.DebugLog($"[CompGraphicOffset] hasTransform={hasTransform}, offset={offset}, rotation={rotation}, scale={scale}");

            // 标记地图需要重绘
            if (parent?.Map?.mapDrawer != null)
            {
                parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things);
                parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Buildings);
                GodHandModMain.DebugLog($"[CompGraphicOffset] 已标记重绘: {parent.Position}");
            }
            else
            {
                Log.Warning($"[CompGraphicOffset] 无法标记重绘: parent={parent != null}, Map={parent?.Map != null}, mapDrawer={parent?.Map?.mapDrawer != null}");
            }
        }

        private void UpdateTransformMatrix()
        {
            hasTransform = offset != Vector2.zero || rotation != 0f || scale != Vector3.one;

            if (hasTransform)
            {
                transformMatrix = Matrix4x4.TRS(
                    new Vector3(offset.x, 0f, offset.y),
                    Quaternion.Euler(0f, rotation, 0f),
                    scale
                );
            }
            else
            {
                transformMatrix = Matrix4x4.identity;
            }
        }

        // 使用PostPrintOnto绘制变换后的图形
        public override void PostPrintOnto(SectionLayer layer)
        {
            base.PostPrintOnto(layer);

            if (hasTransform && parent != null)
            {
                // 标记该物体需要特殊绘制
                GodHandModMain.DebugLog($"[CompGraphicOffset] {parent.LabelShort} 有变换: offset={offset}, rotation={rotation}, scale={scale}");
            }
        }

        // 绘制变换后的图形
        public override void PostDraw()
        {
            base.PostDraw();

            // 有变换则重绘图形
            if (hasTransform && parent != null && parent.Graphic != null)
            {
                GodHandModMain.DebugLog($"[CompGraphicOffset] PostDraw绘制: {parent.LabelShort}");
                DrawTransformedGraphic();
            }
        }

        private void DrawTransformedGraphic()
        {
            // 获取基础位置
            Vector3 basePos = parent.DrawPos;

            // 计算偏移后的位置
            Vector3 offsetPos = basePos + new Vector3(offset.x, 0f, offset.y);

            // 计算最终旋转（建筑原始旋转 + 额外旋转）
            Rot4 baseRot = parent.Rotation;
            float finalRotation = baseRot.AsAngle + rotation;

            // 计算最终高度（基础高度 + 层级偏移）
            Vector3 finalPos = offsetPos + Altitudes.AltIncVect * drawLayer;

            // 构建变换矩阵
            Matrix4x4 matrix = Matrix4x4.TRS(
                finalPos,
                Quaternion.AngleAxis(finalRotation, Vector3.up),
                scale
            );

            // 获取正确的mesh
            Mesh mesh = parent.Graphic.MeshAt(baseRot);
            if (mesh == null)
            {
                mesh = MeshPool.plane10;
            }

            // 绘制变换后的图形（使用更高的渲染优先级覆盖原图）
            Material mat = parent.Graphic.MatAt(baseRot);
            if (mat != null)
            {
                Graphics.DrawMesh(
                    mesh,
                    matrix,
                    mat,
                    0, // layer
                    null, // camera
                    0, // submeshIndex
                    null, // properties
                    false, // castShadows
                    false  // receiveShadows
                );
            }
        }
    }
}
