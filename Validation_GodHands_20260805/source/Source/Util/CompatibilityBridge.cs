using System;
using Verse;

namespace GodHandMod
{
    // 兼容性桥接类 - 允许外部程序集注入逻辑
    public static class CompatibilityBridge
    {
        // 当被捕获的 Pawn 进行 Tick 时触发
        public static Action<Pawn> OnPawnCapturedTick;

        // 自定义图标发射逻辑 (爱心/碎心)
        public static Action<Pawn> ShowCustomIcon;

        // 获取器官松弛阶段 (用于心情/状态)
        public static Func<Pawn, int> GetOrificeLoosenessStage;
    }
}
