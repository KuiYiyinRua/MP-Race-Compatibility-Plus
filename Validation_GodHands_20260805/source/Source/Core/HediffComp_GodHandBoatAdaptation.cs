using Verse;
using RimWorld;

namespace GodHandMod
{
    // 负责自动演进进度
    public class HediffComp_GodHandBoatAdaptation : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            // 禁用时停止演进
            if (GodHandModMain.Settings.disableNSFW) return;

            // 每1000Tick演进一次
            if (parent.pawn.IsHashIntervalTick(1000))
            {
                bool isOnBoat = CompPawnCaptureOnTouch.pawnToGenerator.ContainsKey(parent.pawn);

                // 兼容外部逻辑接管
                if (CompatibilityBridge.GetOrificeLoosenessStage == null)
                {
                    if (isOnBoat)
                    {
                        // 机上进度持续增加
                        parent.Severity += 0.012f;
                        if (parent.Severity < 1.0f)
                        {
                            parent.Severity += 0.012f;
                        }
                    }
                    else
                    {
                        // 离机时的恢复逻辑
                        // 娇嫩阶段缓慢恢复
                        if (parent.Severity < 0.3f)
                        {
                            parent.Severity -= 0.005f; // 自然恢复较慢
                        }
                        // 贯通后不再自愈
                    }
                }
                else
                {
                    // 同步外部状态
                    int stage = CompatibilityBridge.GetOrificeLoosenessStage(parent.pawn);
                    float targetSeverity = stage switch
                    {
                        0 => 0.01f,
                        1 => 0.35f,
                        2 => 0.65f,
                        3 => 0.95f,
                        _ => 0f
                    };
                    if (parent.Severity < targetSeverity) parent.Severity = targetSeverity;
                }
            }
        }

        public override string CompLabelInBracketsExtra => (parent.Severity * 100f).ToString("F0") + "%";

        // 自愈移除检查
        public override bool CompShouldRemove => parent.Severity <= 0f && !CompPawnCaptureOnTouch.pawnToGenerator.ContainsKey(parent.pawn);

        public int GetLoosenessStage()
        {
            float sev = parent.Severity;
            if (sev < 0.3f) return 0; // 紧致
            if (sev < 0.6f) return 1; // 适应
            if (sev < 0.9f) return 2; // 松弛
            return 3; // 完全开发
        }
    }

    public class HediffCompProperties_GodHandBoatAdaptation : HediffCompProperties
    {
        public HediffCompProperties_GodHandBoatAdaptation()
        {
            this.compClass = typeof(HediffComp_GodHandBoatAdaptation);
        }
    }
}
