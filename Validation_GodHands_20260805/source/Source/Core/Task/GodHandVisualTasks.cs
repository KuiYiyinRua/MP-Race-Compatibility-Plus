using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 材料吸取任务
    public class MaterialVacuumTask : GodHandTaskBase
    {
        private Thing thing;
        private Vector3 startPos;
        public Vector3 TargetPos { get; set; }
        private float progress;
        private const float FLIGHT_TIME = 0.8f;

        public MaterialVacuumTask(Thing thing, IntVec3 startCell, Vector3 targetVec)
        {
            this.thing = thing;
            this.startPos = startCell.ToVector3Shifted() + new Vector3(0, 0.5f, 0);
            this.TargetPos = targetVec;
            this.progress = 0f;
        }

        public MaterialVacuumTask(Thing thing, Vector3 startVec, Vector3 targetVec)
        {
            this.thing = thing;
            this.startPos = startVec;
            this.TargetPos = targetVec;
            this.progress = 0f;
        }

        public override void Update(float deltaTime)
        {
            progress += deltaTime / FLIGHT_TIME;
            if (progress >= 1f) isFinished = true;
        }

        private bool isFinished;
        public new bool IsFinished
        {
            get => isFinished;
            protected set => base.IsFinished = value; // 隐藏基类设置器
            // 基类已有设置器
            // 可直接赋值
        }

        public override void Draw()
        {
            if (thing == null || thing.Destroyed) return;

            float t = progress * (2 - progress); // EaseOut
            Vector3 curPos = Vector3.Lerp(startPos, TargetPos, t);

            float scale = Mathf.Lerp(1f, 0.2f, t);
            float angle = t * 720f;

            if (thing.Graphic != null)
            {
                // 确保尸体、武器或复杂 Graphic 可以正确渲染
                if (thing is Corpse || thing is Pawn)
                {
                    // 尸体与小人因为复合了服装等材质无法用矩阵直接缩放渲染，使用原版官方的多段动态图形通道渲染
                    thing.DynamicDrawPhaseAt(DrawPhase.Draw, curPos + new Vector3(0, 3f * t, 0));
                }
                else
                {
                    Matrix4x4 m = Matrix4x4.TRS(curPos + new Vector3(0, 3f, 0),
                        Quaternion.AngleAxis(angle, Vector3.up),
                        new Vector3(scale, 1, scale));

                    Mesh mesh = thing.Graphic.MeshAt(thing.Rotation);
                    Material mat = thing.Graphic.MatAt(thing.Rotation, thing);
                    if (mesh != null && mat != null)
                    {
                        Graphics.DrawMesh(mesh, m, mat, 0);
                    }
                }
            }
        }
    }

    // 产物弹出任务
    public class GodHandProductPopTask : GodHandTaskBase
    {
        private Thing product;
        private Vector3 startPos;
        private Vector3 endPos;
        private float progress;
        private const float POP_DURATION = 0.5f;

        public GodHandProductPopTask(Thing product, Vector3 centerPos)
        {
            this.product = product;
            this.startPos = centerPos;
            this.endPos = product.DrawPos;
            this.progress = 0f;
        }

        public override void Update(float deltaTime)
        {
            progress += deltaTime / POP_DURATION;
            if (progress >= 1f) IsFinished = true;
        }

        public override void Draw()
        {
            if (progress >= 1f) return;

            float arc = Mathf.Sin(progress * Mathf.PI) * 2f;
            Vector3 pos = Vector3.Lerp(startPos, endPos, progress) + new Vector3(0, arc, 0);

            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(0.5f, 1f, 0.5f));
            Graphics.DrawMesh(MeshPool.plane10, m, OrbMat, 0);
        }
    }
}
