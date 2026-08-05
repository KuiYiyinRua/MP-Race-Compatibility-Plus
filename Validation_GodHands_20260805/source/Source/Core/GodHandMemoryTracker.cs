using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 记忆追踪器
    public static class GodHandMemoryTracker
    {
        // 过期时间
        private const int MemoryDuration = 180000;

        // 最大记忆条数
        private const int MaxMemories = 10;

        // 记忆存储字典
        private static Dictionary<int, List<Pair<string, int>>> memories = new Dictionary<int, List<Pair<string, int>>>();

        // 添加记忆
        public static void AddMemory(Pawn pawn, string text)
        {
            if (pawn == null || pawn.Dead) return;

            int id = pawn.thingIDNumber;
            if (!memories.ContainsKey(id))
                memories[id] = new List<Pair<string, int>>();

            int currentTick = Find.TickManager.TicksGame;
            var pawnMemories = memories[id];

            // 检查重复
            int existingIndex = pawnMemories.FindIndex(m => m.First.StartsWith(text));
            string finalContent = text;

            if (existingIndex != -1)
            {
                var existing = pawnMemories[existingIndex];
                int ticksAgo = currentTick - existing.Second;

                // 连续事件合并
                if (ticksAgo < 2500)
                {
                    finalContent = existing.First;
                }
                else
                {
                    // 追加时间标记
                    finalContent = $"{text} ({"GodHand.Memory.LastTimeWas".Translate()})";
                }
                pawnMemories.RemoveAt(existingIndex);
            }

            // 新增记录
            pawnMemories.Add(new Pair<string, int>(finalContent, currentTick));
            if (pawnMemories.Count > MaxMemories) pawnMemories.RemoveAt(0);
        }

        // 获取上下文记忆
        public static string GetMemoriesForContext(Pawn pawn)
        {
            int id = pawn.thingIDNumber;
            if (!memories.ContainsKey(id)) return "";

            int currentTick = Find.TickManager.TicksGame;
            var pawnMemories = memories[id];

            // 移除过期记忆
            pawnMemories.RemoveAll(m => currentTick - m.Second >= MemoryDuration);

            if (pawnMemories.Count == 0)
            {
                memories.Remove(id);
                return "";
            }

            // 格式化输出
            StringBuilder sb = new StringBuilder();
            sb.Append("GodHand.Memory.ContextPrefix".Translate());

            foreach (var memory in pawnMemories)
            {
                int ticksAgo = currentTick - memory.Second;
                string timeDesc = GetTimeDescription(ticksAgo);
                sb.Append($"\n- [{timeDesc}] {memory.First}");
            }

            return sb.ToString();
        }

        // 获取时间描述
        private static string GetTimeDescription(int ticksAgo)
        {
            if (ticksAgo < 2500) return "GodHand.Time.JustNow".Translate();
            if (ticksAgo < 15000) return "GodHand.Time.Recently".Translate();
            if (ticksAgo < 60000) return "GodHand.Time.Today".Translate();
            if (ticksAgo < 120000) return "GodHand.Time.Yesterday".Translate();
            return "GodHand.Time.LongAgo".Translate();
        }

        // 检查近期是否有记忆
        public static bool HasRecentMemory(Pawn pawn, int ticksThreshold)
        {
            int id = pawn.thingIDNumber;
            if (!memories.TryGetValue(id, out var pawnMemories)) return false;

            int currentTick = Find.TickManager.TicksGame;
            foreach (var memory in pawnMemories)
            {
                if (currentTick - memory.Second < ticksThreshold) return true;
            }
            return false;
        }

        // 清理缓存
        public static void Clear() => memories.Clear();
    }
}
