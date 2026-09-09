using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-152: "一瞬間刷新大量的事件" - the storyteller world tick processes
    /// one interval of every story comp (OnOffCycle, CategoryMTB, RandomQuest,
    /// TaleOfMilira, ...) in a single burst. The 3.0.81 fix isolates only
    /// `NaturalRandomQuestChooser.ChooseNaturalRandomQuest`; in 152 the first
    /// divergent trace (tick 1849323) has the identical world Rand state on
    /// both peers but one peer is still in `OnOffCycle.GenerateIncident` while
    /// the other has already entered `CategoryMTB.MakeIntervalIncidents`, so
    /// the burst consumes the shared world Rand through different comps and
    /// desyncs ("Wrong random state for the world").
    ///
    /// Fix: in multiplayer, run the whole `Storyteller.StorytellerTick()` body
    /// inside an unconditional deterministic Rand scope seeded from the tick,
    /// current faction, and Multiplayer's actual world/map execution context.
    /// Multiplayer invokes StorytellerTick for the world and for every map; a
    /// seed without that context replays the same incident/quest sequence on
    /// every map. The random-quest TryFire gate also prevents one storyteller
    /// context from successfully materializing the same refresh burst more
    /// than once in one tick. Singleplayer is untouched; failures fail open.
    /// </summary>
    internal static class Patch_StorytellerIntervalDeterminism
    {
        private const int StorytellerIntervalSeedOffset = 0x5354494E;
        private const int StorytellerIntervalWorldSeedOffset = 0x53545752;
        private const int WorldExecutionContextKey = 0x574F524C;
        private const int MapExecutionContextKey = 0x4D415000;

        [ThreadStatic]
        private static Map _mapForRandPop;

        private static bool _applied;
        private static bool _loggedActive;
        private static bool _loggedFailure;
        private static bool _loggedRandomQuestSuppression;
        private static PropertyInfo _mapContextProperty;

        private sealed class RandomQuestBurstState
        {
            internal int Tick;
            internal int ContextKey;
            internal bool Fired;
        }

        // StorytellerTick is replayed by Multiplayer for each map/faction
        // context. Keep one state per Storyteller instance so the limiter is
        // local to that intended execution context rather than global.
        private static readonly Dictionary<Storyteller, RandomQuestBurstState> RandomQuestBurstStates =
            new Dictionary<Storyteller, RandomQuestBurstState>();

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(Storyteller),
                    "StorytellerTick",
                    Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_StorytellerIntervalDeterminism),
                    nameof(StorytellerTickPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_StorytellerIntervalDeterminism),
                    nameof(StorytellerTickFinalizer));
                MethodInfo tryFireTarget = AccessTools.Method(
                    typeof(Storyteller),
                    "TryFire",
                    new[] { typeof(FiringIncident), typeof(bool) });
                MethodInfo tryFirePrefix = AccessTools.Method(
                    typeof(Patch_StorytellerIntervalDeterminism),
                    nameof(RandomQuestTryFirePrefix));
                MethodInfo tryFirePostfix = AccessTools.Method(
                    typeof(Patch_StorytellerIntervalDeterminism),
                    nameof(RandomQuestTryFirePostfix));
                Type multiplayerType = AccessTools.TypeByName(
                    "Multiplayer.Client.Multiplayer");
                _mapContextProperty = multiplayerType == null
                    ? null
                    : AccessTools.Property(multiplayerType, "MapContext");

                if (target == null || prefix == null || finalizer == null ||
                    _mapContextProperty == null ||
                    !typeof(Map).IsAssignableFrom(
                        _mapContextProperty.PropertyType))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller interval determinism " +
                        "target resolution failed (including Multiplayer.MapContext); " +
                        "event bursts can still desync.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(finalizer)
                    {
                        priority = Priority.Last
                    });

                bool randomQuestLimiterApplied = false;
                if (tryFireTarget != null && tryFirePrefix != null && tryFirePostfix != null)
                {
                    harmony.Patch(
                        tryFireTarget,
                        prefix: new HarmonyMethod(tryFirePrefix)
                        {
                            priority = Priority.First
                        },
                        postfix: new HarmonyMethod(tryFirePostfix)
                        {
                            priority = Priority.Last
                        });
                    randomQuestLimiterApplied = true;
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller random-quest burst limiter " +
                        "target resolution failed; deterministic Rand scope remains active.");
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Storyteller interval determinism active: " +
                    "the whole event burst runs in a deterministic Rand scope; " +
                    $"random quest burst limiter={(randomQuestLimiterApplied ? "one per storyteller/context/tick" : "inactive")}.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Storyteller interval determinism apply " +
                    "failed: " + e.Message);
            }
        }

        private static void StorytellerTickPrefix(ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer)
                return;

            Map mapForPop = null;
            try
            {
                int seed = Gen.HashCombineInt(
                    StorytellerIntervalSeedOffset,
                    Find.TickManager?.TicksAbs ?? 0);
                Faction faction = Faction.OfPlayer;
                seed = Gen.HashCombineInt(
                    seed,
                    faction?.loadID ?? 0);
                Map contextMap = TryGetMapContext();
                int contextKey = contextMap == null
                    ? WorldExecutionContextKey
                    : Gen.HashCombineInt(
                        MapExecutionContextKey,
                        contextMap.uniqueID);
                seed = Gen.HashCombineInt(seed, contextKey);

                if (DeterministicRandScope.Begin(
                        null,
                        seed,
                        StorytellerIntervalWorldSeedOffset,
                        ref __state,
                        out mapForPop,
                        ignoreGate: true))
                {
                    _mapForRandPop = mapForPop;
                    if (!_loggedActive)
                    {
                        _loggedActive = true;
                        Log.Message(
                            "[MP-MeowOnlineShop] Storyteller interval uses a " +
                            "deterministic Rand scope " +
                            $"(faction={faction?.Name ?? "null"}, " +
                            $"context={(contextMap == null ? "world" : "map:" + contextMap.uniqueID)}).");
                    }
                }
                else
                {
                    __state = 0;
                }
            }
            catch (Exception e)
            {
                if (__state != 0)
                    DeterministicRandScope.End(__state, mapForPop);
                __state = 0;
                _mapForRandPop = null;
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller interval determinism " +
                        "prefix failed open: " + e.Message);
                }
            }
        }

        private static Exception StorytellerTickFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
            {
                try
                {
                    DeterministicRandScope.End(__state, _mapForRandPop);
                }
                finally
                {
                    _mapForRandPop = null;
                }
            }

            return __exception;
        }

        private static bool RandomQuestTryFirePrefix(
            Storyteller __instance,
            FiringIncident fi,
            bool queued,
            ref bool __result)
        {
            if (!ShouldLimitRandomQuest(__instance, fi, queued))
                return true;

            RandomQuestBurstState state = GetRandomQuestBurstState(__instance);
            if (!state.Fired)
                return true;

            __result = false;
            if (!_loggedRandomQuestSuppression)
            {
                _loggedRandomQuestSuppression = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Suppressed a repeated random quest incident " +
                    $"in one Storyteller context/tick (tick={state.Tick}, context={state.ContextKey}).");
            }
            return false;
        }

        private static void RandomQuestTryFirePostfix(
            Storyteller __instance,
            FiringIncident fi,
            bool queued,
            bool __result)
        {
            if (!__result || !ShouldLimitRandomQuest(__instance, fi, queued))
                return;

            GetRandomQuestBurstState(__instance).Fired = true;
        }

        private static bool ShouldLimitRandomQuest(
            Storyteller storyteller,
            FiringIncident fi,
            bool queued)
        {
            return MP.IsInMultiplayer && storyteller != null && !queued && fi != null &&
                fi.def == IncidentDefOf.GiveQuest_Random;
        }

        private static RandomQuestBurstState GetRandomQuestBurstState(Storyteller storyteller)
        {
            int tick = Find.TickManager?.TicksAbs ?? 0;
            Map contextMap = TryGetMapContext();
            int contextKey = contextMap == null
                ? WorldExecutionContextKey
                : Gen.HashCombineInt(MapExecutionContextKey, contextMap.uniqueID);

            if (!RandomQuestBurstStates.TryGetValue(storyteller, out var state) ||
                state.Tick != tick || state.ContextKey != contextKey)
            {
                state = new RandomQuestBurstState
                {
                    Tick = tick,
                    ContextKey = contextKey,
                    Fired = false
                };
                RandomQuestBurstStates[storyteller] = state;
            }

            return state;
        }

        private static Map TryGetMapContext()
        {
            try
            {
                return _mapContextProperty?.GetValue(null, null) as Map;
            }
            catch
            {
                return null;
            }
        }
    }
}
