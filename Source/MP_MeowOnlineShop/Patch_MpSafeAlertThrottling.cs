using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer-safe adaptation of MissileGirl's alert throttling idea.
    ///
    /// Only medium-priority, readout-driven recalculations are delayed. High and
    /// critical alerts, forced removals, sync-command execution, and single-player
    /// play always use the vanilla path. The cache is local UI state only.
    /// </summary>
    internal static class Patch_MpSafeAlertThrottling
    {
        private const int MaxTrackedAlerts = 1024;
        private static readonly Dictionary<Alert, int> LastCheckTickByAlert =
            new Dictionary<Alert, int>();

        private static bool _applied;
        private static long _executed;
        private static long _skipped;
        private static long _highPriorityBypass;
        private static long _forcedRemovalBypass;
        private static long _elapsedStopwatchTicks;
        private static int _lastTelemetryTick = int.MinValue;
        private static bool _loggedComplexMapBypass;

        private struct TimingState
        {
            public bool measure;
            public long startedAt;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;

            _applied = true;
            try
            {
                MethodInfo target = AccessTools.DeclaredMethod(
                    typeof(AlertsReadout),
                    "CheckAddOrRemoveAlert",
                    new[] { typeof(Alert), typeof(bool) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_MpSafeAlertThrottling),
                    nameof(CheckAddOrRemoveAlert_Prefix));
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_MpSafeAlertThrottling),
                    nameof(CheckAddOrRemoveAlert_Postfix));
                MethodInfo readoutUpdate = AccessTools.DeclaredMethod(
                    typeof(AlertsReadout),
                    nameof(AlertsReadout.AlertsReadoutUpdate),
                    Type.EmptyTypes);
                MethodInfo readoutPrefix = AccessTools.Method(
                    typeof(Patch_MpSafeAlertThrottling),
                    nameof(AlertsReadoutUpdate_Prefix));
                MethodInfo readoutFinalizer = AccessTools.Method(
                    typeof(Patch_MpSafeAlertThrottling),
                    nameof(AlertsReadoutUpdate_Finalizer));

                if (target == null || prefix == null || postfix == null ||
                    target.ReturnType != typeof(void) ||
                    readoutUpdate == null || readoutPrefix == null ||
                    readoutFinalizer == null ||
                    readoutUpdate.ReturnType != typeof(void))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] MP-safe alert throttling target shape mismatch; " +
                        "feature disabled and vanilla alert updates preserved without " +
                        "the alert UI Rand guard.");
                    return;
                }

                Patches existing = Harmony.GetPatchInfo(target);
                string[] foreignOwners = existing?.Owners
                    .Where(owner => !string.Equals(owner, harmony.Id, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(owner => owner, StringComparer.Ordinal)
                    .ToArray() ?? Array.Empty<string>();
                if (foreignOwners.Length > 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] MP-safe alert throttling found foreign patches on " +
                        "AlertsReadout.CheckAddOrRemoveAlert; feature skipped to avoid patch-order " +
                        $"risk. owners={string.Join(",", foreignOwners)}");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
                harmony.Patch(
                    readoutUpdate,
                    prefix: new HarmonyMethod(readoutPrefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(readoutFinalizer) { priority = Priority.Last });
                Log.Message(
                    "[MP-MeowOnlineShop] MP-safe alert throttling initialized " +
                    "(medium only; high/critical and forced removals always vanilla); " +
                    "alert UI Rand isolation and exception-state recovery are active.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] MP-safe alert throttling initialization failed; " +
                    "vanilla alert updates preserved: " + e);
            }
        }

        private static bool CheckAddOrRemoveAlert_Prefix(
            Alert alert,
            bool forceRemove,
            ref TimingState __state)
        {
            __state = default(TimingState);

            if (!IsFeatureActive())
                return true;

            if (forceRemove)
            {
                _forcedRemovalBypass++;
                return true;
            }

            if (alert == null)
                return true;

            if (alert.Priority != AlertPriority.Medium)
            {
                _highPriorityBypass++;
                return true;
            }

            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick < 0)
            {
                _executed++;
                if (OptimizationGate.IsTelemetryEnabled)
                    BeginMeasurement(ref __state);
                return true;
            }

            int interval = GetConfiguredIntervalTicks();
            if (LastCheckTickByAlert.TryGetValue(alert, out int lastTick) &&
                tick >= lastTick &&
                tick - lastTick < interval)
            {
                _skipped++;
                FlushTelemetryIfNeeded(tick, interval);
                return false;
            }

            if (LastCheckTickByAlert.Count >= MaxTrackedAlerts)
                LastCheckTickByAlert.Clear();
            LastCheckTickByAlert[alert] = tick;

            _executed++;
            if (OptimizationGate.IsTelemetryEnabled)
                BeginMeasurement(ref __state);
            return true;
        }

        private static void CheckAddOrRemoveAlert_Postfix(ref TimingState __state)
        {
            if (!__state.measure)
                return;

            _elapsedStopwatchTicks += Stopwatch.GetTimestamp() - __state.startedAt;

            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick >= 0)
                FlushTelemetryIfNeeded(tick, GetConfiguredIntervalTicks());
        }

        private static void AlertsReadoutUpdate_Prefix(ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;

            Rand.PushState();
            __state = true;
        }

        private static Exception AlertsReadoutUpdate_Finalizer(
            Exception __exception,
            bool __state,
            ref int ___mouseoverAlertIndex)
        {
            if (__state)
                Rand.PopState();

            // Vanilla resets this field only at the normal end of the method.
            // A modded alert returning a null report can throw before that line,
            // leaving the same broken hover path active on every UI frame.
            if (__exception != null)
                ___mouseoverAlertIndex = -1;

            return __exception;
        }

        private static bool IsFeatureActive()
        {
            var settings = MpMeowOnlineShopMod.Settings;
            if (!(settings?.enableMpSafeAlertThrottling ?? true) ||
                !MP.IsInMultiplayer ||
                MP.IsExecutingSyncCommand ||
                Find.TickManager?.CurTimeSpeed == TimeSpeed.Ultrafast)
            {
                return false;
            }

            if (MpRuntimeInfo.RequiresVanillaPerMapPipelines(out string reason))
            {
                if (!_loggedComplexMapBypass)
                {
                    _loggedComplexMapBypass = true;
                    LastCheckTickByAlert.Clear();
                    Log.Message(
                        "[MP-MeowOnlineShop] Alert throttling bypassed: " +
                        $"vanilla per-map alert evaluation retained ({reason}).");
                }
                return false;
            }

            return true;
        }

        private static int GetConfiguredIntervalTicks()
        {
            int configured =
                MpMeowOnlineShopMod.Settings?.mediumAlertRecheckIntervalTicks ?? 180;
            if (configured < 30) return 30;
            if (configured > 600) return 600;
            return configured;
        }

        private static void BeginMeasurement(ref TimingState state)
        {
            state.measure = true;
            state.startedAt = Stopwatch.GetTimestamp();
        }

        private static string GetDiagnosticsSnapshot()
        {
            return
                $"executed={_executed} skipped={_skipped} " +
                $"highCriticalBypass={_highPriorityBypass} " +
                $"forcedRemovalBypass={_forcedRemovalBypass} " +
                $"trackedAlerts={LastCheckTickByAlert.Count} " +
                $"intervalTicks={GetConfiguredIntervalTicks()}";
        }

        private static void FlushTelemetryIfNeeded(int tick, int interval)
        {
            if (!OptimizationGate.IsTelemetryEnabled)
                return;

            int telemetryInterval = OptimizationGate.TelemetryIntervalTicks;
            if (_lastTelemetryTick != int.MinValue &&
                tick >= _lastTelemetryTick &&
                tick - _lastTelemetryTick < telemetryInterval)
            {
                return;
            }

            _lastTelemetryTick = tick;
            double observedMs = _elapsedStopwatchTicks * 1000.0 / Stopwatch.Frequency;
            double averageMs = _executed > 0 ? observedMs / _executed : 0.0;
            double estimatedSavedMs = averageMs * _skipped;
            Log.Message(
                $"[MP-MeowOnlineShop][AlertPerf] tick={tick} intervalTicks={interval} " +
                $"executed={_executed} skipped={_skipped} highCriticalBypass={_highPriorityBypass} " +
                $"forcedRemovalBypass={_forcedRemovalBypass} " +
                $"observedMs={observedMs:F3} avgMs={averageMs:F4} " +
                $"estimatedSavedMs={estimatedSavedMs:F3}");

            _executed = 0;
            _skipped = 0;
            _highPriorityBypass = 0;
            _forcedRemovalBypass = 0;
            _elapsedStopwatchTicks = 0;
        }
    }
}
