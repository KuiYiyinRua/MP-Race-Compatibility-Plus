using System;
using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class OptimizationGate
    {
        private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

        public static bool IsTelemetryEnabled => MpMeowOnlineShopMod.Settings?.enableOptimizationTelemetry ?? true;
        public static bool IsUiBatchingEnabled => MpMeowOnlineShopMod.Settings?.enableUiActionBatching ?? true;
        public static bool IsDeterministicRandScopeEnabled => MpMeowOnlineShopMod.Settings?.enableDeterministicRandRefactor ?? true;
        public static bool IsTickListFullReconcileEnabled => MpMeowOnlineShopMod.Settings?.enableTickListFullReconcile ?? false;
        public static bool IsThirdPartyPerfCleanupEnabled => MpMeowOnlineShopMod.Settings?.enableThirdPartyPerfCleanup ?? false;
        public static bool IsMpServerLagThrottleEnabled => MpMeowOnlineShopMod.Settings?.enableMpServerLagThrottle ?? false;
        public static bool IsProjectileLauncherDeterminismEnabled => MpMeowOnlineShopMod.Settings?.enableProjectileLauncherDeterminism ?? false;
        public static bool IsMiliraWeaponModeCompatEnabled => MpMeowOnlineShopMod.Settings?.enableMiliraWeaponModeCompat ?? false;
        public static bool IsExperimentalTickSchedulerEnabled => MpMeowOnlineShopMod.Settings?.enableExperimentalTickScheduler ?? false;

        public static bool AllowUiOptimization => MP.IsInMultiplayer && !MP.IsExecutingSyncCommand;

        public static int TelemetryIntervalTicks
        {
            get
            {
                int configured = MpMeowOnlineShopMod.Settings?.optimizationTelemetryIntervalTicks ?? 300;
                if (configured < 60) return 60;
                return configured;
            }
        }

        public static void LogOnce(string key, string message)
        {
            if (!LoggedOnce.Add(key))
                return;
            Log.Message(message);
        }
    }

    internal static class OptimizationTelemetry
    {
        private sealed class Counter
        {
            public int hit;
            public int skip;
            public int error;
        }

        private static readonly Dictionary<string, Counter> Counters = new Dictionary<string, Counter>();
        private static int _lastFlushTick = int.MinValue;

        public static void Hit(string key) => Track(key, c => c.hit++);
        public static void Skip(string key) => Track(key, c => c.skip++);
        public static void Error(string key) => Track(key, c => c.error++);

        private static void Track(string key, Action<Counter> updater)
        {
            if (!OptimizationGate.IsTelemetryEnabled || string.IsNullOrEmpty(key))
                return;

            Counter counter;
            if (!Counters.TryGetValue(key, out counter))
            {
                counter = new Counter();
                Counters[key] = counter;
            }

            updater(counter);
            FlushIfNeeded();
        }

        private static void FlushIfNeeded()
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick < 0)
                return;
            if (_lastFlushTick == tick)
                return;
            if (tick == 0 || (tick % OptimizationGate.TelemetryIntervalTicks) != 0)
                return;

            _lastFlushTick = tick;
            foreach (var kv in Counters)
            {
                var c = kv.Value;
                if (c.hit == 0 && c.skip == 0 && c.error == 0)
                    continue;
                Log.Message($"[MP-MeowOnlineShop][OptTelemetry] key={kv.Key} hit={c.hit} skip={c.skip} error={c.error} tick={tick}");
                c.hit = 0;
                c.skip = 0;
                c.error = 0;
            }
        }
    }

    internal static class UiActionBatcher
    {
        private static readonly Dictionary<string, int> LastActionTickByKey = new Dictionary<string, int>();
        private const int MaxEntries = 512;

        public static bool Allow(string key, int cooldownTicks = 1)
        {
            if (!OptimizationGate.IsUiBatchingEnabled || !OptimizationGate.AllowUiOptimization)
                return true;
            if (string.IsNullOrEmpty(key))
                return true;

            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick < 0)
                return true;

            int lastTick;
            if (LastActionTickByKey.TryGetValue(key, out lastTick) && tick - lastTick < cooldownTicks)
            {
                OptimizationTelemetry.Skip("ui.batch");
                return false;
            }

            if (LastActionTickByKey.Count > MaxEntries)
                LastActionTickByKey.Clear();
            LastActionTickByKey[key] = tick;
            OptimizationTelemetry.Hit("ui.batch");
            return true;
        }
    }

    internal static class OptimizationCacheUtility
    {
        public static void EnsureBound<TKey, TValue>(Dictionary<TKey, TValue> dict, int maxEntries)
        {
            if (dict == null || dict.Count < maxEntries)
                return;
            dict.Clear();
        }
    }
}
