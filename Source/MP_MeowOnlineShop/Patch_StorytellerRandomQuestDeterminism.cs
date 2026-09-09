using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-136: "一瞬间刷新大量的事件". At tick 1560453 both peers entered
    /// `StorytellerComp_RandomQuest.GenerateParms` ->
    /// `NaturalRandomQuestChooser.ChooseNaturalRandomQuest`, which calls
    /// `CanRun`/TestRun for every root-random quest script in one burst. The
    /// burst jits dozens of incident workers and consumes a large amount of
    /// static Rand; the first divergent draw is inside that burst
    /// (`Milira.IncidentWorker_Milian_SmallCluster_SingleTurret..ctor` Rand on
    /// the client vs `QuestNode_GetRandomNegativeGameCondition` RandomElement
    /// on the host).
    ///
    /// Fix: wrap the whole `ChooseNaturalRandomQuest` body in an unconditional
    /// deterministic Rand scope (ignoring the local performance gate). Keep the
    /// faction context installed by Multiplayer's FactionRepeater: replacing it
    /// with the spectator faction makes many faction/colonist-dependent quests
    /// fail CanRun while broad quests such as PollutionDump remain eligible.
    /// The child scope is derived from one draw of the already-synchronized
    /// parent Rand stream. This is important: seeding only from
    /// tick/points/target makes every repeated request in the same tick replay
    /// the same weighted choice.
    /// Singleplayer is untouched.
    /// </summary>
    internal static class Patch_StorytellerRandomQuestDeterminism
    {
        private const int RandomQuestSeedOffset = 0x52515531;

        private static bool _applied;
        private static FieldInfo _lastCheckCanRunTickField;

        [ThreadStatic]
        private static Map _mapForRandPop;

        private sealed class RandomQuestScopeState
        {
            internal int RandState;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(NaturalRandomQuestChooser),
                    nameof(NaturalRandomQuestChooser.ChooseNaturalRandomQuest),
                    new[] { typeof(float), typeof(IIncidentTarget) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_StorytellerRandomQuestDeterminism),
                    nameof(ScopePrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_StorytellerRandomQuestDeterminism),
                    nameof(ScopeFinalizer));
                _lastCheckCanRunTickField = AccessTools.Field(
                    typeof(QuestScriptDef), "lastCheckCanRunTick");

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller random-quest determinism " +
                        $"target resolution failed: target={target != null}; " +
                        "random-quest bursts can still desync.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

                Log.Message(
                    "[MP-MeowOnlineShop] Storyteller random-quest determinism active: " +
                    "ChooseNaturalRandomQuest uses a deterministic Rand scope and " +
                    "preserves Multiplayer's active faction context; " +
                    $"CanRun context cache isolation={_lastCheckCanRunTickField != null}.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Storyteller random-quest determinism " +
                    "apply failed: " + e.Message);
            }
        }

        private static void ScopePrefix(
            float points,
            IIncidentTarget target,
            ref RandomQuestScopeState __state)
        {
            __state = new RandomQuestScopeState();
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer)
                return;

            int state = 0;
            Map mapForPop = null;
            try
            {
                Faction callerFaction = Faction.OfPlayer;

                // Derive a unique child stream from the synchronized parent
                // stream. PushState/PopState alone does not advance the parent;
                // without this draw, multiple requests with identical arguments
                // in a paused/single tick replay exactly the same quest choice.
                int invocationSeed = Rand.Int;
                int seed = Gen.HashCombineInt(
                    RandomQuestSeedOffset,
                    invocationSeed);
                seed = Gen.HashCombineInt(
                    seed,
                    Find.TickManager?.TicksAbs ?? 0);
                seed = Gen.HashCombineInt(
                    seed,
                    BitConverter.ToInt32(
                        BitConverter.GetBytes(points), 0));
                seed = Gen.HashCombineInt(seed, StableTargetKey(target));
                seed = Gen.HashCombineInt(seed, callerFaction?.loadID ?? 0);

                if (!DeterministicRandScope.Begin(
                    null,
                    seed,
                    RandomQuestSeedOffset + 1,
                    ref state,
                    out mapForPop,
                    ignoreGate: true))
                {
                    return;
                }
                _mapForRandPop = mapForPop;
                __state.RandState = state;

                // QuestScriptDef.CanRun caches only tick + points. Multiplayer
                // evaluates random quests for the world and for each map under
                // different faction/target contexts in the same tick, so that
                // vanilla cache can reuse a spectator/world result for a map.
                // Reset immediately before selection and again in the finalizer
                // so neither an earlier caller nor this caller leaks a candidate
                // set across contexts. This remains narrow to the infrequent
                // natural-random-quest chooser.
                InvalidateRandomQuestCanRunCache();
            }
            catch (Exception e)
            {
                if (state != 0)
                    DeterministicRandScope.End(state, mapForPop);
                _mapForRandPop = null;
                __state.RandState = 0;
                if (!_loggedPrefixFailure)
                {
                    _loggedPrefixFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller random-quest scope " +
                        "prefix failed open: " + e.Message);
                }
            }
        }

        private static Exception ScopeFinalizer(
            Exception __exception,
            RandomQuestScopeState __state)
        {
            if (__state != null)
            {
                try
                {
                    if (MP.IsInMultiplayer)
                        InvalidateRandomQuestCanRunCache();
                }
                finally
                {
                    DeterministicRandScope.End(
                        __state.RandState,
                        _mapForRandPop);
                    _mapForRandPop = null;
                }
            }

            return __exception;
        }

        private static void InvalidateRandomQuestCanRunCache()
        {
            if (_lastCheckCanRunTickField == null)
            {
                if (!_loggedCanRunCacheFieldMissing)
                {
                    _loggedCanRunCacheFieldMissing = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller random-quest " +
                        "CanRun cache field was not found; target/faction " +
                        "cache isolation is unavailable.");
                }
                return;
            }

            try
            {
                foreach (QuestScriptDef quest in
                    DefDatabase<QuestScriptDef>.AllDefsListForReading)
                {
                    if (quest != null && quest.IsRootRandomSelected)
                        _lastCheckCanRunTickField.SetValue(quest, int.MinValue);
                }
            }
            catch (Exception e)
            {
                if (!_loggedCanRunCacheResetFailure)
                {
                    _loggedCanRunCacheResetFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller random-quest " +
                        "CanRun cache reset failed: " + e.Message);
                }
            }
        }

        private static int StableTargetKey(IIncidentTarget target)
        {
            if (target == null)
                return 0;
            if (target is Map map)
                return map.uniqueID;
            if (target is RimWorld.Planet.WorldObject worldObject)
                return worldObject.ID;
            string name = target.GetType().FullName ?? target.GetType().Name ?? "";
            int hash = 0;
            foreach (char c in name)
                hash = Gen.HashCombineInt(hash, c);
            return hash;
        }

        private static bool _loggedPrefixFailure;
        private static bool _loggedCanRunCacheFieldMissing;
        private static bool _loggedCanRunCacheResetFailure;
    }
}
