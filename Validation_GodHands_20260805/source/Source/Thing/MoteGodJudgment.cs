using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 神之裁决自定义动画
    public class MoteGodJudgment : Mote
    {
        private Vector3 targetPos;
        private const float dropHeight = 30f; // 降落高度

        // 动画阶段时间配置（Ticks）
        public const int DROP_TICKS = 8;     // 砸落时间
        public const int HOLD_TICKS = 60;    // 静止时间
        public const int LIFT_TICKS = 45;    // 抬起时间

        private int age = 0;

        // 初始化配置
        public void Setup(Vector3 target, float size)
        {
            // 计算手指偏移
            float offsetZ = size * 3.5f / 20f;
            this.targetPos = target + new Vector3(0, 0, offsetZ);
            this.Scale = size;

            // 初始位置在高空
            this.exactPosition = this.targetPos + new Vector3(0, 0, dropHeight);

            // 确保在同一层级渲染
            this.exactPosition.y = target.y;
        }

        protected override void Tick()
        {
            // 自定义更新逻辑
            // 但我们需要保持存活检查
            if (Destroyed) return;

            age++;

            UpdatePositionAndAlpha();

            // 动画结束
            if (age > DROP_TICKS + HOLD_TICKS + LIFT_TICKS)
            {
                Destroy();
            }
        }

        private void UpdatePositionAndAlpha()
        {
            // 加速砸落阶段
            if (age <= DROP_TICKS)
            {
                float progress = (float)age / DROP_TICKS;
                // 平方插值实现加速下落效果
                float t = progress * progress;
                float currentHeight = Mathf.Lerp(dropHeight, 0f, t);

                Vector3 pos = targetPos;
                pos.z += currentHeight;
                exactPosition = pos;

                // 确保完全不透明
                instanceColor.a = 1f;
            }
            // 地面静止阶段
            else if (age <= DROP_TICKS + HOLD_TICKS)
            {
                exactPosition = targetPos;
                instanceColor.a = 1f;
            }
            // 抬起消隐阶段
            else
            {
                int liftAge = age - (DROP_TICKS + HOLD_TICKS);
                float progress = (float)liftAge / LIFT_TICKS;

                // 缓慢抬起
                float t = Mathf.Sin(progress * Mathf.PI * 0.5f); // EaseOut
                float currentHeight = Mathf.Lerp(0f, dropHeight, t);

                Vector3 pos = targetPos;
                pos.z += currentHeight;
                exactPosition = pos;

                // 渐隐
                instanceColor.a = 1f - progress;
            }
        }

        // 确保绘制时使用我们的位置
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(this.exactPosition, flip);
        }
    }
}
