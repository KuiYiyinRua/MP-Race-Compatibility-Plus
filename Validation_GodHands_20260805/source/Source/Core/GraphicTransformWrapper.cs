using UnityEngine;
using Verse;

namespace GodHandMod
{
    // Graphic包装器 应用变换
    public class GraphicTransformWrapper : Graphic
    {
        public Graphic BaseGraphic { get; private set; }
        private GraphicTransformManager.TransformData transform;

        public GraphicTransformWrapper(Graphic baseGraphic, GraphicTransformManager.TransformData transform)
        {
            this.BaseGraphic = baseGraphic;
            this.transform = transform;

            // 复制基础属性
            this.data = baseGraphic.data;
            this.path = baseGraphic.path;
            this.color = baseGraphic.color;
            this.colorTwo = baseGraphic.colorTwo;
            this.drawSize = baseGraphic.drawSize;
        }

        // 重写绘制逻辑

        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
        {
            // 处理位置偏移
            Vector3 transformedLoc = loc + new Vector3(transform.offset.x, 0f, transform.offset.y);
            transformedLoc += Altitudes.AltIncVect * transform.drawLayer;

            // 确定朝向
            Rot4 effectiveRot = transform.overrideRot.HasValue ? new Rot4(transform.overrideRot.Value) : rot;

            // 计算旋转
            float angle = effectiveRot.AsAngle + extraRotation + transform.rotation;

            // 独立计算矩阵缩放
            Vector2 size = BaseGraphic.drawSize;
            size = new Vector2(size.x * transform.scale.x, size.y * transform.scale.z);

            Mesh mesh = BaseGraphic.MeshAt(effectiveRot);
            Material mat = BaseGraphic.MatAt(effectiveRot, thing);

            // TRS矩阵组合渲染
            if (mesh != null && mat != null)
            {
                Matrix4x4 matrix = Matrix4x4.TRS(
                    transformedLoc,
                    Quaternion.AngleAxis(angle, Vector3.up),
                    new Vector3(size.x, 1f, size.y)
                );

                Graphics.DrawMesh(mesh, matrix, mat, 0, null, 0);
            }

            // 处理阴影绘制
            if (BaseGraphic.ShadowGraphic != null && thing != null)
            {
                BaseGraphic.ShadowGraphic.DrawWorker(transformedLoc, effectiveRot, thingDef, thing, extraRotation);
            }
        }

        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            // 自定义打印逻辑

            // 计算位置
            Vector3 center = thing.DrawPos + new Vector3(transform.offset.x, 0f, transform.offset.y);
            center += Altitudes.AltIncVect * transform.drawLayer;

            // 计算旋转
            float rot = extraRotation + transform.rotation;
            float angle = rot + thing.Rotation.AsAngle;

            // 计算尺寸
            Vector2 size = BaseGraphic.drawSize;
            size = new Vector2(size.x * transform.scale.x, size.y * transform.scale.z);

            // 获取材质
            Rot4 effectiveRot = transform.overrideRot.HasValue ? new Rot4(transform.overrideRot.Value) : thing.Rotation;
            Material mat = BaseGraphic.MatAt(effectiveRot, thing);

            // 执行打印
            Printer_Plane.PrintPlane(layer, center, size, mat, angle);
        }

        // 代理其他方法

        public override Material MatSingle => BaseGraphic.MatSingle;
        public override Material MatSingleFor(Thing thing) => BaseGraphic.MatSingleFor(thing);
        public override Material MatAt(Rot4 rot, Thing thing = null) => BaseGraphic.MatAt(rot, thing);
        public override Mesh MeshAt(Rot4 rot) => BaseGraphic.MeshAt(rot);
        public override void Init(GraphicRequest req) => BaseGraphic.Init(req);
        public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
            => new GraphicTransformWrapper(BaseGraphic.GetColoredVersion(newShader, newColor, newColorTwo), transform);
    }
}
