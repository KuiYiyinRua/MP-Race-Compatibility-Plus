using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multifaction TPS 优化：
    /// 1) 默认启用单一 TPS 优化开关（默认开启），联机中可保守回退到 vanilla。
    /// 2) 联机里把玩家多基地地图 Tick 改为确定性轮转调度，降低同 tick 全量多地图成本。
    /// 3) 关键路径异常时自动降级回 vanilla，优先保证联机稳定。
    /// </summary>
    internal static class Patch_MultifactionTpsOptimize
    {
        internal const string HarmonyId = "mp.meowonlineshop.multifactiontpsopt";

        private enum SchedulerMode
        {
            Conservative = 0,
            Aggressive = 1,
            EventDriven = 2
        }

        private enum ThreatLevel
        {
            None = 0,
            Watch = 1,
            Combat = 2
        }

        private enum SchedulerRiskLevel
        {
            Low = 0,
            Medium = 1,
            High = 2,
            Critical = 3
        }

        private enum CandidateMatchSource
        {
            None = 0,
            IsPlayerHome = 1,
            MpFactionData = 2,
            MpCustomFactionData = 3,
            FallbackIsPlayerHome = 4
        }

        private const SchedulerMode DefaultSchedulerMode = SchedulerMode.Aggressive;
        private const bool EnableTelemetry = true;
        private const int TelemetryIntervalTicks = 300;
        private const int DefaultAsyncFallbackCooldownTicks = 420;
        private const int SchedulerGatesCacheIntervalTicks = 240;
        private const int MinAsyncFallbackCooldownTicks = 120;
        private const int MaxAsyncFallbackCooldownTicks = 2400;
        private const int MultifactionProbeIntervalTicks = 240;
        // 默认启用；若后续验证出现风险可临时置为 true 强制退回 vanilla。
        private const bool EnableMultiplayerSafetyBypass = false;

        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        private static readonly List<Map> ThrottleCandidateMapCache = new List<Map>();
        private static readonly MapEventWakeRegistry EventWakeRegistry = new MapEventWakeRegistry();
        private static readonly Dictionary<int, int> MapIndexByIdCache = new Dictionary<int, int>();
        private static readonly List<MapRuntimeSnapshot> SnapshotBuffer = new List<MapRuntimeSnapshot>();
        private static readonly Dictionary<int, MapTelemetryCounter> TelemetryByMapId = new Dictionary<int, MapTelemetryCounter>();
        private static readonly HashSet<int> CachedPlayerFactionIds = new HashSet<int>();

        private static bool _applied;
        private static bool _scanLogged;
        private static bool _hardDisabled;
        private static int _cachedMapsAtTick = int.MinValue;
        private static int _lastSchedulerComputedTick = int.MinValue;
        private static int _lastRotationSlot;
        private static int _lastMapCount;
        private static SchedulerMode _lastEffectiveMode = DefaultSchedulerMode;
        private static bool _lastAsyncActive;
        private static int _fallbackUntilTick = int.MinValue;
        private static SchedulerRiskLevel _lastRiskLevel;
        private static int _lastTelemetryFlushTick = int.MinValue;
        private static int _lastMultifactionCheckTick = int.MinValue;
        private static bool _lastMultifactionActive;
        private static int _lastCandidateByHome;
        private static int _lastCandidateByMpFactionData;
        private static int _lastCandidateByMpCustomFactionData;
        private static int _lastCandidateByFallback;
        private static int _lastCandidateByUnknown;
        private static bool _loggedGateNotMultiplayerOnce;
        private static bool _loggedGateNoCandidateOnce;
        private static bool _loggedSingleCandidateOnce;
        private static bool _loggedMultifactionBypassOnce;
        private static bool _loggedMultiplayerSafeModeOnce;
        private static bool _loggedSchedulerDisabledOnce;
        private static bool _loggedExperimentalSchedulerDisabledOnce;
        private static bool _loggedSchedulerFuseOnce;
        private static int _lastModeTransitionLogTick = int.MinValue;
        private static bool _optimizationConflictWarningShown;
        private static bool _lastOptimizationEnabledState = true;
        private static bool _schedulerGatesCacheValid;
        private static bool _cachedTpsOptimizationEnabled = true;
        private static bool _cachedExperimentalSchedulerEnabled;
        private static int _schedulerGatesCacheTick = int.MinValue;
        private static int _nextMultifactionProbeTick = int.MinValue;
        private static int _cachedPlayerFactionIdsTick = int.MinValue;
        private static int _lastObservedMapCount = -1;
        private static int _lastObservedPlayerFactionCount = -1;
        private static int _consecutiveSchedulerErrors;
        private static int _schedulerFuseUntilTick = int.MinValue;
        private const int SchedulerErrorFuseThreshold = 3;
        private const int SchedulerErrorFuseCooldownTicks = 1800;

        public static void Apply()
        {
            if (_applied)
                return;

            _applied = true;

            try
            {
                MpRuntimeInfo.EnsureInitialized(forceLog: true);
                PatchMapSchedulerHook();
                Log.Message("[MP-MeowOnlineShop] Multifaction TPS optimize patch initialized.");
            }
            catch (Exception e)
            {
                _hardDisabled = true;
                Log.Warning("[MP-MeowOnlineShop] Multifaction TPS optimize init failed, feature disabled: " + e);
            }
        }


        private static void PatchMapSchedulerHook()
        {
            var mapPostTick = AccessTools.Method(typeof(Map), "MapPostTick");
            if (mapPostTick == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Multifaction TPS optimize: Map.MapPostTick not found, scheduler hook skipped.");
                return;
            }

            var prefix = AccessTools.Method(typeof(Patch_MultifactionTpsOptimize), nameof(MapPostTick_Prefix));
            Harmony.Patch(mapPostTick, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
        }

        private static bool IsMultifactionActive()
        {
            bool active;
            if (MpRuntimeInfo.TryGetMultifactionActive(out active))
                return active;

            if (!_scanLogged)
            {
                _scanLogged = true;
                Log.Warning("[MP-MeowOnlineShop] Multifaction TPS optimize: could not resolve Multiplayer.GameComp.multifaction, fallback to conservative policy.");
            }

            return false;
        }

        private static bool IsAsyncTimeActive()
        {
            bool active;
            return MpRuntimeInfo.TryGetAsyncTimeActive(out active) && active;
        }

        private static bool MapPostTick_Prefix(Map __instance)
        {
            if (_hardDisabled)
                return true;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick < _schedulerFuseUntilTick)
            {
                LogGateDecisionOnce(
                    ref _loggedSchedulerFuseOnce,
                    $"[MP-MeowOnlineShop] Multifaction TPS optimize: scheduler fuse active until tick={_schedulerFuseUntilTick}, fallback to vanilla MapPostTick.");
                OptimizationTelemetry.Skip("tick.scheduler.fuse_active");
                return true;
            }

            try
            {
                if (!_schedulerGatesCacheValid ||
                    currentTick - _schedulerGatesCacheTick >= SchedulerGatesCacheIntervalTicks)
                {
                    _cachedTpsOptimizationEnabled = IsTpsOptimizationEnabled();
                    _cachedExperimentalSchedulerEnabled = IsExperimentalTickSchedulerEnabled();
                    _schedulerGatesCacheTick = currentTick;
                    _schedulerGatesCacheValid = true;
                }

                bool optimizationEnabled = _cachedTpsOptimizationEnabled;
                if (!optimizationEnabled)
                {
                    if (_lastOptimizationEnabledState)
                        ResetSchedulerRuntimeState();
                    _lastOptimizationEnabledState = false;
                    LogGateDecisionOnce(
                        ref _loggedSchedulerDisabledOnce,
                        "[MP-MeowOnlineShop] Multifaction TPS optimize: single TPS optimization switch is disabled, fallback to vanilla MapPostTick.");
                    return true;
                }

                _lastOptimizationEnabledState = true;
                MaybeShowOptimizationConflictWarning();

                if (!_cachedExperimentalSchedulerEnabled)
                {
                    LogGateDecisionOnce(
                        ref _loggedExperimentalSchedulerDisabledOnce,
                        "[MP-MeowOnlineShop] Multifaction TPS optimize: experimental tick scheduler disabled by settings, fallback to vanilla MapPostTick.");
                    OptimizationTelemetry.Skip("tick.scheduler.experimental_disabled");
                    return true;
                }

                if (IsSchedulerSafetyModeActive())
                {
                    LogGateDecisionOnce(
                        ref _loggedMultiplayerSafeModeOnce,
                        "[MP-MeowOnlineShop] Multifaction TPS optimize: multiplayer safety mode active; scheduler bypassed and vanilla MapPostTick is enforced.");
                    OptimizationTelemetry.Skip("tick.scheduler.safe_mode");
                    return true;
                }

                if (!ShouldThrottleMapTick(__instance))
                    return true;

                int tick = Find.TickManager?.TicksGame ?? 0;
                var maps = GetSortedThrottleCandidateMapsForTick(tick);
                int count = maps.Count;
                if (count <= 1)
                {
                    LogGateDecisionOnce(
                        ref _loggedSingleCandidateOnce,
                        $"[MP-MeowOnlineShop] Multifaction TPS optimize: candidate map count <= 1 (candidates={count}, totalMaps={Find.Maps?.Count ?? 0}), scheduler skipped for current tick.");
                    OptimizationTelemetry.Skip("tick.scheduler.single_candidate");
                    return true;
                }

                ComputeSchedulerSnapshot(tick, maps);
                int index = GetMapIndexById(maps, __instance.uniqueID);
                if (index < 0)
                    return true;

                var snapshot = GetSnapshotByIndexV2(index);
                bool shouldRun = ShouldRunMapTick(__instance, tick, maps, index);
                RecordTelemetryV2(__instance, shouldRun, snapshot);
                _consecutiveSchedulerErrors = 0;
                if (shouldRun)
                    OptimizationTelemetry.Hit("tick.scheduler.run");
                else
                    OptimizationTelemetry.Skip("tick.scheduler.skip");
                return shouldRun;
            }
            catch (Exception e)
            {
                _consecutiveSchedulerErrors++;
                if (_consecutiveSchedulerErrors >= SchedulerErrorFuseThreshold)
                {
                    _schedulerFuseUntilTick = currentTick + SchedulerErrorFuseCooldownTicks;
                    _consecutiveSchedulerErrors = 0;
                    _loggedSchedulerFuseOnce = false;
                }
                OptimizationTelemetry.Error("tick.scheduler.error");
                Log.Warning("[MP-MeowOnlineShop] Multifaction TPS optimize scheduler failed, fallback to vanilla: " + e);
                return true;
            }
        }

        private static bool IsTpsOptimizationEnabled()
        {
            SyncAuthoritativeTpsSwitchWithHost();
            var comp = Current.Game?.GetComponent<MultifactionTpsOptimizeComponent>();
            if (comp == null)
                return GetDefaultTpsSwitchFromModSettings();
            return comp.tpsOptimizeEnabled;
        }

        private static bool IsExperimentalTickSchedulerEnabled()
        {
            SyncAuthoritativeTpsSwitchWithHost();
            var comp = Current.Game?.GetComponent<MultifactionTpsOptimizeComponent>();
            if (comp == null)
                return MpMeowOnlineShopMod.Settings?.enableExperimentalTickScheduler ?? false;
            return comp.experimentalTickSchedulerEnabled;
        }

        internal static void NotifyModSettingsUpdated()
        {
            _schedulerGatesCacheValid = false;
            SyncAuthoritativeTpsSwitchWithHost(forceLog: true);
        }

        private static bool GetDefaultTpsSwitchFromModSettings()
        {
            return MpMeowOnlineShopMod.Settings?.tpsOptimizeEnabled ?? true;
        }

        private static void SyncAuthoritativeTpsSwitchWithHost(bool forceLog = false)
        {
            var game = Current.Game;
            if (game == null)
                return;

            var comp = game.GetComponent<MultifactionTpsOptimizeComponent>();
            if (comp == null)
                return;

            // The component is part of the synchronized game snapshot. Never
            // overwrite it from process-local mod settings after Multiplayer
            // has started: the host may do so after producing join data while
            // the client retains the saved value, creating different map tick
            // policies. Runtime changes require an explicit synchronized
            // command; until then, the saved component is authoritative.
            if (MP.IsInMultiplayer)
                return;

            bool desired = comp.tpsOptimizeEnabled;
            bool desiredExperimental = comp.experimentalTickSchedulerEnabled;
            int desiredSchedulerMode = comp.schedulerMode;
            int desiredAsyncFallbackCooldownTicks = comp.asyncFallbackCooldownTicks;
            int desiredAggressiveCombatIntervalTicks = comp.aggressiveCombatIntervalTicks;
            int desiredAggressiveWatchIntervalTicks = comp.aggressiveWatchIntervalTicks;
            int desiredAggressiveAwakeIntervalTicks = comp.aggressiveAwakeIntervalTicks;
            int desiredAggressiveIdleBaseIntervalTicks = comp.aggressiveIdleBaseIntervalTicks;
            int desiredAggressiveIdlePerMapFactorTicks = comp.aggressiveIdlePerMapFactorTicks;
            int desiredEventDrivenCombatIntervalTicks = comp.eventDrivenCombatIntervalTicks;
            int desiredEventDrivenWatchIntervalTicks = comp.eventDrivenWatchIntervalTicks;
            int desiredEventDrivenAwakeIntervalTicks = comp.eventDrivenAwakeIntervalTicks;
            int desiredEventDrivenSparseBaseIntervalTicks = comp.eventDrivenSparseBaseIntervalTicks;
            int desiredEventDrivenSparsePerMapFactorTicks = comp.eventDrivenSparsePerMapFactorTicks;
            var settings = MpMeowOnlineShopMod.Settings;
            desired = settings?.tpsOptimizeEnabled ?? true;
            desiredExperimental = settings?.enableExperimentalTickScheduler ?? false;
            desiredSchedulerMode = ClampInterval(settings?.schedulerMode ?? 1, 0, 2);
            desiredAsyncFallbackCooldownTicks = ClampInterval(settings?.asyncFallbackCooldownTicks ?? 420, 120, 2400);
            desiredAggressiveCombatIntervalTicks = ClampInterval(settings?.aggressiveCombatIntervalTicks ?? 4, 1, 30);
            desiredAggressiveWatchIntervalTicks = ClampInterval(settings?.aggressiveWatchIntervalTicks ?? 6, 2, 60);
            desiredAggressiveAwakeIntervalTicks = ClampInterval(settings?.aggressiveAwakeIntervalTicks ?? 8, 2, 90);
            desiredAggressiveIdleBaseIntervalTicks = ClampInterval(settings?.aggressiveIdleBaseIntervalTicks ?? 160, 30, 3600);
            desiredAggressiveIdlePerMapFactorTicks = ClampInterval(settings?.aggressiveIdlePerMapFactorTicks ?? 26, 1, 200);
            desiredEventDrivenCombatIntervalTicks = ClampInterval(settings?.eventDrivenCombatIntervalTicks ?? 4, 1, 30);
            desiredEventDrivenWatchIntervalTicks = ClampInterval(settings?.eventDrivenWatchIntervalTicks ?? 8, 2, 90);
            desiredEventDrivenAwakeIntervalTicks = ClampInterval(settings?.eventDrivenAwakeIntervalTicks ?? 14, 2, 120);
            desiredEventDrivenSparseBaseIntervalTicks = ClampInterval(settings?.eventDrivenSparseBaseIntervalTicks ?? 220, 30, 4800);
            desiredEventDrivenSparsePerMapFactorTicks = ClampInterval(settings?.eventDrivenSparsePerMapFactorTicks ?? 34, 1, 240);

            if (comp.tpsOptimizeEnabled == desired
                && comp.experimentalTickSchedulerEnabled == desiredExperimental
                && comp.schedulerMode == desiredSchedulerMode
                && comp.asyncFallbackCooldownTicks == desiredAsyncFallbackCooldownTicks
                && comp.aggressiveCombatIntervalTicks == desiredAggressiveCombatIntervalTicks
                && comp.aggressiveWatchIntervalTicks == desiredAggressiveWatchIntervalTicks
                && comp.aggressiveAwakeIntervalTicks == desiredAggressiveAwakeIntervalTicks
                && comp.aggressiveIdleBaseIntervalTicks == desiredAggressiveIdleBaseIntervalTicks
                && comp.aggressiveIdlePerMapFactorTicks == desiredAggressiveIdlePerMapFactorTicks
                && comp.eventDrivenCombatIntervalTicks == desiredEventDrivenCombatIntervalTicks
                && comp.eventDrivenWatchIntervalTicks == desiredEventDrivenWatchIntervalTicks
                && comp.eventDrivenAwakeIntervalTicks == desiredEventDrivenAwakeIntervalTicks
                && comp.eventDrivenSparseBaseIntervalTicks == desiredEventDrivenSparseBaseIntervalTicks
                && comp.eventDrivenSparsePerMapFactorTicks == desiredEventDrivenSparsePerMapFactorTicks)
                return;

            comp.tpsOptimizeEnabled = desired;
            comp.experimentalTickSchedulerEnabled = desiredExperimental;
            comp.schedulerMode = desiredSchedulerMode;
            comp.asyncFallbackCooldownTicks = desiredAsyncFallbackCooldownTicks;
            comp.aggressiveCombatIntervalTicks = desiredAggressiveCombatIntervalTicks;
            comp.aggressiveWatchIntervalTicks = desiredAggressiveWatchIntervalTicks;
            comp.aggressiveAwakeIntervalTicks = desiredAggressiveAwakeIntervalTicks;
            comp.aggressiveIdleBaseIntervalTicks = desiredAggressiveIdleBaseIntervalTicks;
            comp.aggressiveIdlePerMapFactorTicks = desiredAggressiveIdlePerMapFactorTicks;
            comp.eventDrivenCombatIntervalTicks = desiredEventDrivenCombatIntervalTicks;
            comp.eventDrivenWatchIntervalTicks = desiredEventDrivenWatchIntervalTicks;
            comp.eventDrivenAwakeIntervalTicks = desiredEventDrivenAwakeIntervalTicks;
            comp.eventDrivenSparseBaseIntervalTicks = desiredEventDrivenSparseBaseIntervalTicks;
            comp.eventDrivenSparsePerMapFactorTicks = desiredEventDrivenSparsePerMapFactorTicks;
            if (forceLog || Prefs.DevMode)
            {
                Log.Message(
                    $"[MP-MeowOnlineShop] TPS optimize local settings applied outside multiplayer: enabled={desired}, experimental={desiredExperimental}, mode={desiredSchedulerMode}, asyncCooldown={desiredAsyncFallbackCooldownTicks}.");
            }
        }

        private static void MaybeShowOptimizationConflictWarning()
        {
            if (!MP.IsInMultiplayer || _optimizationConflictWarningShown)
                return;

            _optimizationConflictWarningShown = true;
            const string warning = "[MP-MeowOnlineShop] 已启用联机TPS优化。为避免冲突，请不要同时启用其他TPS/性能优化类模组。";
            Log.Warning(warning);
            if (Current.ProgramState == ProgramState.Playing)
                Messages.Message(warning, MessageTypeDefOf.CautionInput, historical: false);
        }

        private static bool ShouldThrottleMapTick(Map map)
        {
            if (map == null)
                return false;
            if (!MP.IsInMultiplayer)
            {
                LogGateDecisionOnce(
                    ref _loggedGateNotMultiplayerOnce,
                    "[MP-MeowOnlineShop] Multifaction TPS optimize: not in multiplayer, scheduler disabled.");
                return false;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < 0)
                return false;

            bool multifactionLikely = IsMultifactionLikelyActive(tick);
            if (!multifactionLikely)
            {
                LogGateDecisionOnce(
                    ref _loggedMultifactionBypassOnce,
                    "[MP-MeowOnlineShop] Multifaction TPS optimize: Multifaction state unresolved, continue with candidate-map policy.");
            }

            // 仅处理玩家派系相关地图；其它地图维持原逻辑，降低行为偏差。
            bool includeMpOwnership = ShouldProbeMpOwnershipForCandidates();
            if (!IsThrottleCandidateMap(map, includeMpOwnership))
            {
                LogGateDecisionOnce(
                    ref _loggedGateNoCandidateOnce,
                    "[MP-MeowOnlineShop] Multifaction TPS optimize: current map is not a throttle candidate, fallback to vanilla tick.");
                return false;
            }

            return true;
        }

        private static bool IsSchedulerSafetyModeActive()
        {
            if (!MP.IsInMultiplayer)
                return false;

            // Multiplayer async-time 已接管 map tick 管线时，本补丁不叠加第二层节流。
            bool asyncTimeActive;
            if (MpRuntimeInfo.TryGetAsyncTimeActive(out asyncTimeActive) && asyncTimeActive)
                return true;

            return EnableMultiplayerSafetyBypass;
        }

        private static bool IsMultifactionLikelyActive(int tick)
        {
            if (tick >= _nextMultifactionProbeTick)
            {
                _lastMultifactionCheckTick = tick;
                _lastMultifactionActive = IsMultifactionActive();
                _nextMultifactionProbeTick = tick + MultifactionProbeIntervalTicks;
            }

            return _lastMultifactionActive;
        }

        private static bool ShouldProbeMpOwnershipForCandidates()
        {
            return true;
        }

        private static void LogGateDecisionOnce(ref bool logged, string message)
        {
            if (logged)
                return;
            logged = true;
            Log.Message(message);
        }

        private static void ResetSchedulerRuntimeState()
        {
            _cachedMapsAtTick = int.MinValue;
            _lastSchedulerComputedTick = int.MinValue;
            _lastRotationSlot = 0;
            _lastMapCount = 0;
            _lastEffectiveMode = DefaultSchedulerMode;
            _lastAsyncActive = false;
            _fallbackUntilTick = int.MinValue;
            _lastRiskLevel = SchedulerRiskLevel.Low;
            _lastTelemetryFlushTick = int.MinValue;
            _lastMultifactionCheckTick = int.MinValue;
            _lastMultifactionActive = false;
            _lastCandidateByHome = 0;
            _lastCandidateByMpFactionData = 0;
            _lastCandidateByMpCustomFactionData = 0;
            _lastCandidateByFallback = 0;
            _lastCandidateByUnknown = 0;
            _loggedGateNotMultiplayerOnce = false;
            _loggedGateNoCandidateOnce = false;
            _loggedSingleCandidateOnce = false;
            _loggedMultifactionBypassOnce = false;
            _loggedMultiplayerSafeModeOnce = false;
            _loggedSchedulerDisabledOnce = false;
            _loggedExperimentalSchedulerDisabledOnce = false;
            _loggedSchedulerFuseOnce = false;
            _lastModeTransitionLogTick = int.MinValue;
            _nextMultifactionProbeTick = int.MinValue;
            _cachedPlayerFactionIdsTick = int.MinValue;
            _lastObservedMapCount = -1;
            _lastObservedPlayerFactionCount = -1;
            _consecutiveSchedulerErrors = 0;
            _schedulerFuseUntilTick = int.MinValue;
            EventWakeRegistry.Clear();
            MapIndexByIdCache.Clear();
            SnapshotBuffer.Clear();
            TelemetryByMapId.Clear();
            CachedPlayerFactionIds.Clear();
        }

        private static SchedulerMode GetConfiguredSchedulerMode()
        {
            var comp = Current.Game?.GetComponent<MultifactionTpsOptimizeComponent>();
            if (comp != null)
            {
                var mode = (SchedulerMode)comp.schedulerMode;
                if (mode < SchedulerMode.Conservative || mode > SchedulerMode.EventDriven)
                {
                    comp.schedulerMode = (int)DefaultSchedulerMode;
                    return DefaultSchedulerMode;
                }
                return mode;
            }

            return DefaultSchedulerMode;
        }

        private static SchedulerMode ResolveEffectiveSchedulerMode(bool asyncTimeActive, int mapCount)
        {
            var configured = GetConfiguredSchedulerMode();
            if (configured < SchedulerMode.Conservative || configured > SchedulerMode.EventDriven)
                configured = DefaultSchedulerMode;

            if (asyncTimeActive && configured == SchedulerMode.EventDriven)
                return SchedulerMode.Conservative;

            if (mapCount >= 8 && configured == SchedulerMode.EventDriven)
                return SchedulerMode.Aggressive;

            return configured;
        }

        private static void ComputeSchedulerSnapshot(int tick, List<Map> maps)
        {
            ComputeSchedulerSnapshotV2(tick, maps);
        }

        private static void RemoveExpiredWakeups(int tick)
        {
            EventWakeRegistry.RemoveExpired(tick);
        }

        private static void UpdateEventWakeups(List<Map> maps, int tick)
        {
            if (maps == null)
                return;

            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                if (map == null)
                    continue;

                int duration = EvaluateWakeDuration(map);
                if (duration > 0)
                    ExtendWakeup(map.uniqueID, tick + duration);
            }
        }

        private static int EvaluateWakeDuration(Map map)
        {
            int duration = 0;

            try
            {
                var danger = map?.dangerWatcher?.DangerRating ?? StoryDanger.None;
                if (danger != StoryDanger.None)
                    duration = Math.Max(duration, 420);
            }
            catch
            {
                // ignore
            }

            if (HasHostileThreat(map))
                duration = Math.Max(duration, 300);

            if (HasDraftedColonist(map))
                duration = Math.Max(duration, 180);

            return duration;
        }

        private static void ExtendWakeup(int mapId, int untilTick)
        {
            EventWakeRegistry.Extend(mapId, untilTick);
        }

        private static bool IsMapAwake(int mapId, int tick)
        {
            return EventWakeRegistry.IsAwake(mapId, tick);
        }

        private static int PickPrimaryMapId(List<Map> maps, int tick, int rotationSlot)
        {
            if (maps == null || maps.Count == 0)
                return -1;

            int bestMapId = -1;
            int bestScore = int.MinValue;
            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                if (map == null)
                    continue;

                int score = GetPrimaryScore(map, tick);
                if (score > bestScore || (score == bestScore && map.uniqueID < bestMapId))
                {
                    bestScore = score;
                    bestMapId = map.uniqueID;
                }
            }

            if (bestMapId >= 0 && bestScore > 0)
                return bestMapId;

            int safeSlot = PositiveMod(rotationSlot, maps.Count);
            var fallback = maps[safeSlot];
            return fallback?.uniqueID ?? maps[0].uniqueID;
        }

        private static int GetPrimaryScore(Map map, int tick)
        {
            int score = 0;
            int mapId = map.uniqueID;

            if (IsMapAwake(mapId, tick))
                score += 3500;

            try
            {
                var danger = map.dangerWatcher?.DangerRating ?? StoryDanger.None;
                if (danger != StoryDanger.None)
                    score += 2000 + (int)danger * 500;
            }
            catch
            {
                // ignore
            }

            if (HasHostileThreat(map))
                score += 1200;

            if (HasDraftedColonist(map))
                score += 600;

            try
            {
                int colonists = map.mapPawns?.FreeColonistsSpawnedCount ?? 0;
                if (colonists > 0)
                    score += Math.Min(300, colonists * 20);
            }
            catch
            {
                // ignore
            }

            score += PositiveMod(mapId, 31);
            return score;
        }

        private static bool HasHostileThreat(Map map)
        {
            if (map == null)
                return false;

            try
            {
                var pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                    return false;

                for (int i = 0; i < pawns.Count; i++)
                {
                    var pawn = pawns[i];
                    if (pawn == null || pawn.Dead || pawn.Faction == null || pawn.Faction == Faction.OfPlayer)
                        continue;
                    if (pawn.HostileTo(Faction.OfPlayer))
                        return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool HasDraftedColonist(Map map)
        {
            if (map == null)
                return false;

            try
            {
                var colonists = map.mapPawns?.FreeColonistsSpawned;
                if (colonists == null)
                    return false;

                for (int i = 0; i < colonists.Count; i++)
                {
                    var pawn = colonists[i];
                    if (pawn?.drafter?.Drafted == true)
                        return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool ShouldRunMapTick(Map map, int tick, List<Map> maps, int index)
        {
            if (map == null)
                return true;

            var snapshot = GetSnapshotByIndexV2(index);
            if (snapshot == null)
                return true;

            if (_lastEffectiveMode == SchedulerMode.Conservative)
                return ShouldRunConservativeTickV2(snapshot, tick, maps, index);

            if (_lastEffectiveMode == SchedulerMode.Aggressive)
                return ShouldRunAggressiveTickV2(snapshot, tick);

            return ShouldRunEventDrivenTickV2(snapshot, tick);
        }

        private static bool ShouldRunConservativeTick(Map map, int tick, List<Map> maps, int index)
        {
            int count = maps?.Count ?? 0;
            if (count <= 1)
                return true;

            if (index == _lastRotationSlot)
                return true;

            int catchupInterval = Math.Min(90, count * 8);
            if (catchupInterval < 8)
                catchupInterval = 8;
            return PositiveMod(tick + map.uniqueID, catchupInterval) == 0;
        }

        private static bool ShouldRunAggressiveTick(Map map, int tick)
        {
            if (IsMapAwake(map.uniqueID, tick))
                return PositiveMod(tick + map.uniqueID, 3) == 0;

            int interval = Math.Max(20, _lastMapCount * 7);
            return PositiveMod(tick + map.uniqueID, interval) == 0;
        }

        private static bool ShouldRunEventDrivenTick(Map map, int tick)
        {
            if (IsMapAwake(map.uniqueID, tick))
                return PositiveMod(tick + map.uniqueID, 2) == 0;

            int sparseInterval = Math.Max(120, _lastMapCount * 24);
            return PositiveMod(tick + map.uniqueID, sparseInterval) == 0;
        }

        private static bool IsThrottleCandidateMap(Map map, bool includeMpOwnership)
        {
            CandidateMatchSource source;
            return TryMatchThrottleCandidateMap(map, includeMpOwnership, out source);
        }

        private static bool TryMatchThrottleCandidateMap(Map map, bool includeMpOwnership, out CandidateMatchSource source)
        {
            source = CandidateMatchSource.None;
            if (map == null)
                return false;

            if (map.IsPlayerHome)
            {
                source = CandidateMatchSource.IsPlayerHome;
                return true;
            }

            if (includeMpOwnership && TryIsMultifactionPlayerMap(map, out source))
                return true;

            if (!includeMpOwnership)
                return false;

            // 无法读取 MP 地图组件时，保守回退到 IsPlayerHome 逻辑，避免行为扩散。
            source = CandidateMatchSource.FallbackIsPlayerHome;
            return map.IsPlayerHome;
        }

        private static void UpdateCandidateCacheDirtyState()
        {
            var maps = Find.Maps;
            int mapCount = maps?.Count ?? 0;
            int playerFactionCount = GetPlayerFactionIds().Count;
            if (mapCount != _lastObservedMapCount || playerFactionCount != _lastObservedPlayerFactionCount)
            {
                _lastObservedMapCount = mapCount;
                _lastObservedPlayerFactionCount = playerFactionCount;
                _cachedMapsAtTick = int.MinValue;
            }
        }

        private static bool TryIsMultifactionPlayerMap(Map map, out CandidateMatchSource source)
        {
            source = CandidateMatchSource.None;
            if (map == null)
                return false;

            var playerFactionIds = GetPlayerFactionIds();
            bool matchedFactionData;
            bool matchedCustomFactionData;
            if (!MpRuntimeInfo.TryMapCompContainsAnyPlayerFaction(map, playerFactionIds, out matchedFactionData, out matchedCustomFactionData))
                return false;

            if (matchedFactionData)
            {
                source = CandidateMatchSource.MpFactionData;
                return true;
            }

            if (matchedCustomFactionData)
            {
                source = CandidateMatchSource.MpCustomFactionData;
                return true;
            }

            return false;
        }

        private static HashSet<int> GetPlayerFactionIds()
        {
            int tick = Find.TickManager?.TicksGame ?? int.MinValue;
            if (_cachedPlayerFactionIdsTick == tick)
                return CachedPlayerFactionIds;

            _cachedPlayerFactionIdsTick = tick;
            CachedPlayerFactionIds.Clear();
            var factions = Find.FactionManager?.AllFactions;
            if (factions == null)
                return CachedPlayerFactionIds;

            foreach (var faction in factions)
            {
                if (faction != null && faction.IsPlayer)
                    CachedPlayerFactionIds.Add(faction.loadID);
            }

            return CachedPlayerFactionIds;
        }

        private static void CountCandidateSource(CandidateMatchSource source)
        {
            switch (source)
            {
                case CandidateMatchSource.IsPlayerHome:
                    _lastCandidateByHome++;
                    break;
                case CandidateMatchSource.MpFactionData:
                    _lastCandidateByMpFactionData++;
                    break;
                case CandidateMatchSource.MpCustomFactionData:
                    _lastCandidateByMpCustomFactionData++;
                    break;
                case CandidateMatchSource.FallbackIsPlayerHome:
                    _lastCandidateByFallback++;
                    break;
                default:
                    _lastCandidateByUnknown++;
                    break;
            }
        }

        private static List<Map> GetSortedThrottleCandidateMapsForTick(int tick)
        {
            UpdateCandidateCacheDirtyState();
            if (_cachedMapsAtTick == tick)
                return ThrottleCandidateMapCache;

            _cachedMapsAtTick = tick;
            ThrottleCandidateMapCache.Clear();
            _lastCandidateByHome = 0;
            _lastCandidateByMpFactionData = 0;
            _lastCandidateByMpCustomFactionData = 0;
            _lastCandidateByFallback = 0;
            _lastCandidateByUnknown = 0;

            var maps = Find.Maps;
            if (maps == null || maps.Count == 0)
                return ThrottleCandidateMapCache;

            bool includeMpOwnership = ShouldProbeMpOwnershipForCandidates();
            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                CandidateMatchSource source;
                if (!TryMatchThrottleCandidateMap(map, includeMpOwnership, out source))
                    continue;

                CountCandidateSource(source);
                ThrottleCandidateMapCache.Add(map);
            }

            ThrottleCandidateMapCache.Sort((a, b) => a.uniqueID.CompareTo(b.uniqueID));
            return ThrottleCandidateMapCache;
        }

        private static int GetMapIndexById(List<Map> maps, int mapId)
        {
            int cached;
            if (MapIndexByIdCache.TryGetValue(mapId, out cached))
                return cached;

            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && maps[i].uniqueID == mapId)
                {
                    OptimizationCacheUtility.EnsureBound(MapIndexByIdCache, 4096);
                    MapIndexByIdCache[mapId] = i;
                    return i;
                }
            }

            return -1;
        }

        private static void ComputeSchedulerSnapshotV2(int tick, List<Map> maps)
        {
            if (_lastSchedulerComputedTick == tick)
                return;

            _lastSchedulerComputedTick = tick;
            _lastMapCount = maps?.Count ?? 0;
            _lastRotationSlot = PositiveMod(tick, Math.Max(1, _lastMapCount));
            _lastAsyncActive = IsAsyncTimeActive();

            RemoveExpiredWakeups(tick);
            BuildMapSnapshotsV2(tick, maps);

            var configuredMode = ResolveConfiguredSchedulerModeV2(_lastAsyncActive, _lastMapCount);
            _lastEffectiveMode = ResolveModeWithAsyncFallbackV2(configuredMode, tick);
            MaybeLogModeTransitionV2(tick, configuredMode, _lastEffectiveMode, _lastRiskLevel, _fallbackUntilTick);
            MaybeFlushTelemetryV2(tick, maps);
        }

        private static SchedulerMode ResolveConfiguredSchedulerModeV2(bool asyncTimeActive, int mapCount)
        {
            var configured = GetConfiguredSchedulerMode();
            if (configured < SchedulerMode.Conservative || configured > SchedulerMode.EventDriven)
                configured = DefaultSchedulerMode;

            if (mapCount >= 10 && configured == SchedulerMode.EventDriven)
                configured = SchedulerMode.Aggressive;

            if (!asyncTimeActive && configured == SchedulerMode.EventDriven)
                configured = SchedulerMode.Aggressive;

            return configured;
        }

        private static SchedulerMode ResolveModeWithAsyncFallbackV2(SchedulerMode configuredMode, int tick)
        {
            if (!_lastAsyncActive)
            {
                _lastRiskLevel = SchedulerRiskLevel.Low;
                return configuredMode;
            }

            _lastRiskLevel = EvaluateRiskLevelV2();
            int fallbackCooldown = GetAsyncFallbackCooldownTicks();
            if (_lastRiskLevel >= SchedulerRiskLevel.High)
                _fallbackUntilTick = Math.Max(_fallbackUntilTick, tick + fallbackCooldown);

            if (tick < _fallbackUntilTick)
                return SchedulerMode.Conservative;

            return configuredMode;
        }

        private static SchedulerRiskLevel EvaluateRiskLevelV2()
        {
            int combatMaps = 0;
            int watchMaps = 0;
            int awakeMaps = 0;
            for (int i = 0; i < SnapshotBuffer.Count; i++)
            {
                var snapshot = SnapshotBuffer[i];
                if (snapshot.threatLevel == ThreatLevel.Combat)
                    combatMaps++;
                else if (snapshot.threatLevel == ThreatLevel.Watch)
                    watchMaps++;

                if (snapshot.isAwake)
                    awakeMaps++;
            }

            if (combatMaps >= 3 || (combatMaps >= 2 && _lastMapCount >= 8))
                return SchedulerRiskLevel.Critical;
            if (combatMaps >= 2 || (combatMaps >= 1 && _lastMapCount >= 6) || watchMaps >= 4)
                return SchedulerRiskLevel.High;
            if (watchMaps >= 2 || awakeMaps >= 4 || _lastMapCount >= 10)
                return SchedulerRiskLevel.Medium;
            return SchedulerRiskLevel.Low;
        }

        private static int GetAsyncFallbackCooldownTicks()
        {
            int configured = Current.Game?.GetComponent<MultifactionTpsOptimizeComponent>()?.asyncFallbackCooldownTicks
                ?? DefaultAsyncFallbackCooldownTicks;
            if (configured < MinAsyncFallbackCooldownTicks)
                return MinAsyncFallbackCooldownTicks;
            if (configured > MaxAsyncFallbackCooldownTicks)
                return MaxAsyncFallbackCooldownTicks;
            return configured;
        }

        private static void MaybeLogModeTransitionV2(int tick, SchedulerMode configuredMode, SchedulerMode effectiveMode, SchedulerRiskLevel risk, int fallbackUntilTick)
        {
            if (!Prefs.DevMode)
                return;
            if (_lastModeTransitionLogTick == tick)
                return;

            bool fallbackActive = tick < fallbackUntilTick;
            bool shouldLog = configuredMode != effectiveMode || fallbackActive || risk >= SchedulerRiskLevel.High;
            if (!shouldLog)
                return;

            _lastModeTransitionLogTick = tick;
            Log.Message(
                $"[MP-MeowOnlineShop] TPS scheduler transition tick={tick} configured={configuredMode} effective={effectiveMode} risk={risk} async={_lastAsyncActive} fallbackUntil={fallbackUntilTick} mapCount={_lastMapCount}");
        }

        private static void BuildMapSnapshotsV2(int tick, List<Map> maps)
        {
            SnapshotBuffer.Clear();
            MapIndexByIdCache.Clear();
            if (maps == null)
                return;

            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                if (map == null)
                    continue;

                var snapshot = EvaluateMapSnapshotV2(map, i, tick);
                MapIndexByIdCache[snapshot.mapId] = SnapshotBuffer.Count;
                SnapshotBuffer.Add(snapshot);
            }
        }

        private static MapRuntimeSnapshot EvaluateMapSnapshotV2(Map map, int orderIndex, int tick)
        {
            var snapshot = new MapRuntimeSnapshot
            {
                map = map,
                mapId = map.uniqueID,
                orderIndex = orderIndex,
                danger = GetDangerRatingV2(map),
                colonistCount = map.mapPawns?.FreeColonistsSpawnedCount ?? 0
            };

            snapshot.hasHostile = HasHostileThreat(map);
            snapshot.hasDrafted = HasDraftedColonist(map);
            snapshot.wakeUntilTick = GetWakeUntilTickV2(snapshot.mapId);
            snapshot.isAwake = snapshot.wakeUntilTick > tick;

            int wakeDuration = ComputeWakeDurationV2(snapshot);
            if (wakeDuration > 0)
            {
                ExtendWakeup(snapshot.mapId, tick + wakeDuration);
                snapshot.wakeUntilTick = GetWakeUntilTickV2(snapshot.mapId);
                snapshot.isAwake = snapshot.wakeUntilTick > tick;
            }

            snapshot.threatLevel = ResolveThreatLevelV2(snapshot);
            return snapshot;
        }

        private static StoryDanger GetDangerRatingV2(Map map)
        {
            try
            {
                return map?.dangerWatcher?.DangerRating ?? StoryDanger.None;
            }
            catch
            {
                return StoryDanger.None;
            }
        }

        private static int ComputeWakeDurationV2(MapRuntimeSnapshot snapshot)
        {
            int duration = 0;
            if (snapshot.danger != StoryDanger.None)
                duration = Math.Max(duration, 420);
            if (snapshot.hasHostile)
                duration = Math.Max(duration, 300);
            if (snapshot.hasDrafted)
                duration = Math.Max(duration, 180);
            return duration;
        }

        private static ThreatLevel ResolveThreatLevelV2(MapRuntimeSnapshot snapshot)
        {
            if (snapshot.danger != StoryDanger.None || snapshot.hasHostile)
                return ThreatLevel.Combat;
            if (snapshot.hasDrafted || snapshot.isAwake)
                return ThreatLevel.Watch;
            return ThreatLevel.None;
        }

        private static int GetWakeUntilTickV2(int mapId)
        {
            return EventWakeRegistry.GetWakeUntil(mapId);
        }

        private static MapRuntimeSnapshot GetSnapshotByIndexV2(int index)
        {
            if (index < 0 || index >= SnapshotBuffer.Count)
                return null;
            return SnapshotBuffer[index];
        }

        private static bool ShouldRunConservativeTickV2(MapRuntimeSnapshot snapshot, int tick, List<Map> maps, int index)
        {
            int count = maps?.Count ?? 0;
            if (count <= 1)
                return true;

            if (index == _lastRotationSlot)
                return true;

            int catchupInterval = Math.Min(90, count * 8);
            if (catchupInterval < 8)
                catchupInterval = 8;
            return PositiveMod(tick + snapshot.mapId, catchupInterval) == 0;
        }

        private static bool ShouldRunAggressiveTickV2(MapRuntimeSnapshot snapshot, int tick)
        {
            var comp = Current.Game?.GetComponent<MultifactionTpsOptimizeComponent>();
            int combatInterval = ClampInterval(comp?.aggressiveCombatIntervalTicks ?? 4, 1, 30);
            int watchInterval = ClampInterval(comp?.aggressiveWatchIntervalTicks ?? 6, 2, 60);
            int awakeInterval = ClampInterval(comp?.aggressiveAwakeIntervalTicks ?? 8, 2, 90);
            int idleBaseInterval = ClampInterval(comp?.aggressiveIdleBaseIntervalTicks ?? 160, 30, 3600);
            int idleMapFactor = ClampInterval(comp?.aggressiveIdlePerMapFactorTicks ?? 26, 1, 200);

            if (snapshot.threatLevel == ThreatLevel.Combat)
                return PositiveMod(tick + snapshot.mapId, combatInterval) == 0;

            if (snapshot.threatLevel == ThreatLevel.Watch)
                return PositiveMod(tick + snapshot.mapId, watchInterval) == 0;

            if (snapshot.isAwake)
                return PositiveMod(tick + snapshot.mapId, awakeInterval) == 0;

            int interval = Math.Max(idleBaseInterval, _lastMapCount * idleMapFactor);
            return PositiveMod(tick + snapshot.mapId, interval) == 0;
        }

        private static bool ShouldRunEventDrivenTickV2(MapRuntimeSnapshot snapshot, int tick)
        {
            var comp = Current.Game?.GetComponent<MultifactionTpsOptimizeComponent>();
            int combatInterval = ClampInterval(comp?.eventDrivenCombatIntervalTicks ?? 4, 1, 30);
            int watchInterval = ClampInterval(comp?.eventDrivenWatchIntervalTicks ?? 8, 2, 90);
            int awakeInterval = ClampInterval(comp?.eventDrivenAwakeIntervalTicks ?? 14, 2, 120);
            int sparseBaseInterval = ClampInterval(comp?.eventDrivenSparseBaseIntervalTicks ?? 220, 30, 4800);
            int sparseMapFactor = ClampInterval(comp?.eventDrivenSparsePerMapFactorTicks ?? 34, 1, 240);

            if (snapshot.threatLevel == ThreatLevel.Combat)
                return PositiveMod(tick + snapshot.mapId, combatInterval) == 0;

            if (snapshot.threatLevel == ThreatLevel.Watch)
                return PositiveMod(tick + snapshot.mapId, watchInterval) == 0;

            if (snapshot.isAwake)
                return PositiveMod(tick + snapshot.mapId, awakeInterval) == 0;

            int sparseInterval = Math.Max(sparseBaseInterval, _lastMapCount * sparseMapFactor);
            return PositiveMod(tick + snapshot.mapId, sparseInterval) == 0;
        }

        private static int ClampInterval(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static void RecordTelemetryV2(Map map, bool didRun, MapRuntimeSnapshot snapshot)
        {
            if (!EnableTelemetry || !OptimizationGate.IsTelemetryEnabled || !Prefs.DevMode || map == null || snapshot == null)
                return;

            MapTelemetryCounter counter;
            if (!TelemetryByMapId.TryGetValue(map.uniqueID, out counter))
            {
                counter = new MapTelemetryCounter();
                OptimizationCacheUtility.EnsureBound(TelemetryByMapId, 256);
                TelemetryByMapId[map.uniqueID] = counter;
            }

            if (didRun)
                counter.runs++;
            else
                counter.skips++;

            if (snapshot.isAwake)
                counter.awakeHits++;
            if (snapshot.threatLevel == ThreatLevel.Combat)
                counter.combatHits++;
            else if (snapshot.threatLevel == ThreatLevel.Watch)
                counter.watchHits++;
        }

        private static void MaybeFlushTelemetryV2(int tick, List<Map> maps)
        {
            if (!EnableTelemetry || !OptimizationGate.IsTelemetryEnabled || !Prefs.DevMode)
                return;
            int interval = OptimizationGate.TelemetryIntervalTicks > 0 ? OptimizationGate.TelemetryIntervalTicks : TelemetryIntervalTicks;
            if (tick <= 0 || PositiveMod(tick, interval) != 0)
                return;
            if (_lastTelemetryFlushTick == tick)
                return;

            _lastTelemetryFlushTick = tick;
            Log.Message($"[MP-MeowOnlineShop] TPS telemetry tick={tick} mode={_lastEffectiveMode} risk={_lastRiskLevel} async={_lastAsyncActive} maps={_lastMapCount} fairness=enabled fallbackUntil={_fallbackUntilTick} mapSources(home={_lastCandidateByHome}, mpFactionData={_lastCandidateByMpFactionData}, mpCustomFactionData={_lastCandidateByMpCustomFactionData}, fallback={_lastCandidateByFallback}, unknown={_lastCandidateByUnknown})");

            if (maps == null)
                return;

            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                if (map == null)
                    continue;

                MapTelemetryCounter counter;
                if (!TelemetryByMapId.TryGetValue(map.uniqueID, out counter))
                    continue;

                Log.Message($"[MP-MeowOnlineShop]   map={map.uniqueID} run={counter.runs} skip={counter.skips} awake={counter.awakeHits} watch={counter.watchHits} combat={counter.combatHits}");
                counter.Reset();
            }
        }

        private sealed class MapRuntimeSnapshot
        {
            public Map map;
            public int mapId;
            public int orderIndex;
            public StoryDanger danger;
            public bool hasHostile;
            public bool hasDrafted;
            public bool isAwake;
            public int wakeUntilTick;
            public int colonistCount;
            public ThreatLevel threatLevel;
        }

        private sealed class MapTelemetryCounter
        {
            public int runs;
            public int skips;
            public int awakeHits;
            public int watchHits;
            public int combatHits;

            public void Reset()
            {
                runs = 0;
                skips = 0;
                awakeHits = 0;
                watchHits = 0;
                combatHits = 0;
            }
        }

        private static int PositiveMod(int value, int mod)
        {
            if (mod <= 0)
                return 0;
            int r = value % mod;
            return r < 0 ? r + mod : r;
        }
    }

    /// <summary>联机会话存档态：保留调度参数，并兼容读取旧版开关字段。</summary>
    public class MultifactionTpsOptimizeComponent : GameComponent
    {
        public bool tpsOptimizeEnabled = true;
        public bool experimentalTickSchedulerEnabled = false;
        public int schedulerMode = 0;
        public int asyncFallbackCooldownTicks = 420;
        public int aggressiveCombatIntervalTicks = 4;
        public int aggressiveWatchIntervalTicks = 6;
        public int aggressiveAwakeIntervalTicks = 8;
        public int aggressiveIdleBaseIntervalTicks = 160;
        public int aggressiveIdlePerMapFactorTicks = 26;
        public int eventDrivenCombatIntervalTicks = 4;
        public int eventDrivenWatchIntervalTicks = 8;
        public int eventDrivenAwakeIntervalTicks = 14;
        public int eventDrivenSparseBaseIntervalTicks = 220;
        public int eventDrivenSparsePerMapFactorTicks = 34;

        public MultifactionTpsOptimizeComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            bool legacyEnabled = false;
            bool legacyForceEnable = false;
            Scribe_Values.Look(ref legacyEnabled, "mp_meow_multifaction_tps_opt_enabled", false);
            Scribe_Values.Look(ref legacyForceEnable, "mp_meow_multifaction_tps_opt_force_enabled", false);
            Scribe_Values.Look(ref tpsOptimizeEnabled, "mp_meow_multifaction_tps_opt_single_enabled", true);
            Scribe_Values.Look(ref experimentalTickSchedulerEnabled, "mp_meow_multifaction_tps_opt_experimental_enabled", false);
            Scribe_Values.Look(ref schedulerMode, "mp_meow_multifaction_tps_opt_mode", 0);
            Scribe_Values.Look(ref asyncFallbackCooldownTicks, "mp_meow_multifaction_tps_opt_async_fallback_cooldown", 420);
            Scribe_Values.Look(ref aggressiveCombatIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_combat_interval", 4);
            Scribe_Values.Look(ref aggressiveWatchIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_watch_interval", 6);
            Scribe_Values.Look(ref aggressiveAwakeIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_awake_interval", 8);
            Scribe_Values.Look(ref aggressiveIdleBaseIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_idle_base_interval", 160);
            Scribe_Values.Look(ref aggressiveIdlePerMapFactorTicks, "mp_meow_multifaction_tps_opt_aggressive_idle_map_factor", 26);
            Scribe_Values.Look(ref eventDrivenCombatIntervalTicks, "mp_meow_multifaction_tps_opt_event_combat_interval", 4);
            Scribe_Values.Look(ref eventDrivenWatchIntervalTicks, "mp_meow_multifaction_tps_opt_event_watch_interval", 8);
            Scribe_Values.Look(ref eventDrivenAwakeIntervalTicks, "mp_meow_multifaction_tps_opt_event_awake_interval", 14);
            Scribe_Values.Look(ref eventDrivenSparseBaseIntervalTicks, "mp_meow_multifaction_tps_opt_event_sparse_base_interval", 220);
            Scribe_Values.Look(ref eventDrivenSparsePerMapFactorTicks, "mp_meow_multifaction_tps_opt_event_sparse_map_factor", 34);
        }
    }

    /// <summary>
    /// 独立于 MpMeowOnlineShopBootstrap：确保尽早安装调度补丁，不依赖 Host UI 生命周期。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MultifactionTpsOptimizeBootstrap
    {
        static MultifactionTpsOptimizeBootstrap()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Patch_MultifactionTpsOptimize.Apply();
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Multifaction TPS optimize bootstrap failed: " + e.Message);
                }
            });
        }
    }
}
