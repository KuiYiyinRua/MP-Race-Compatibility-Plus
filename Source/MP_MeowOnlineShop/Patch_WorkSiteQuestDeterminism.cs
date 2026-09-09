using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-133: "已检测到附近由...控制的钢铁采掘工作站" is a vanilla work-site
    /// quest (`RimWorld.QuestGen.QuestNode_Root_WorkSite`). `RunInt` picks a
    /// player home map by weighted Rand, then `GenerateSite` creates a new
    /// temporary faction (faction ID, color/name/ideo Rand, leader pawn and its
    /// gear thing IDs) and a Site (world-object ID). In this multifaction
    /// session the first map-8 divergence, two ticks later, was a ~19 thing-ID
    /// offset between fire entities on the two peers.
    ///
    /// The quest generation runs in the storyteller world tick and reads
    /// player-relative faction/map context while generating. Multiplayer's
    /// FactionRepeater already installs the owning player faction before it
    /// calls this quest. Replacing that context with the spectator faction is
    /// invalid: `TestRunInt` succeeds for the owner, then `RunInt` sees no
    /// `Map.IsPlayerHome` candidates, weighted selection returns null, and the
    /// vanilla `map.Tile` access throws. A storyteller batch repeats that same
    /// failure once for every queued quest.
    ///
    /// Fix: preserve FactionRepeater's active faction and wrap only the Rand
    /// streams. Derive each child scope from one draw of the synchronized
    /// parent stream so multiple work-site requests in the same tick do not
    /// replay the same site/faction/leader sequence. Singleplayer is untouched.
    /// </summary>
    internal static class Patch_WorkSiteQuestDeterminism
    {
        private const string WorkSiteQuestTypeName =
            "RimWorld.QuestGen.QuestNode_Root_WorkSite";
        private const int WorkSiteSeedOffset = 0x57574B53;

        private static bool _applied;
        private static bool _loggedPrefixFailure;

        private sealed class WorkSiteScopeState
        {
            internal int RandState;
            internal Map MapForRandPop;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type questType = AccessTools.TypeByName(WorkSiteQuestTypeName);
                MethodInfo runInt = questType == null
                    ? null
                    : AccessTools.Method(questType, "RunInt", Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_WorkSiteQuestDeterminism),
                    nameof(RunIntPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_WorkSiteQuestDeterminism),
                    nameof(RunIntFinalizer));

                if (runInt == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Work-site quest determinism target " +
                        $"resolution failed: quest={runInt != null} prefix={prefix != null} " +
                        $"finalizer={finalizer != null}; " +
                        "the workstation quest can still desync.");
                    return;
                }

                harmony.Patch(
                    runInt,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

                Log.Message(
                    "[MP-MeowOnlineShop] Work-site quest determinism active: " +
                    "RunInt uses a per-invocation deterministic Rand scope and " +
                    "preserves Multiplayer's active faction context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Work-site quest determinism apply failed: " +
                    e.Message);
            }
        }

        private static void RunIntPrefix(
            ref WorkSiteScopeState __state)
        {
            __state = new WorkSiteScopeState();
            if (!MP.IsInMultiplayer)
                return;

            int state = 0;
            Map mapForPop = null;
            try
            {
                Faction callerFaction = Faction.OfPlayer;
                int invocationSeed = Rand.Int;
                int seed = Gen.HashCombineInt(
                    WorkSiteSeedOffset,
                    invocationSeed);
                seed = Gen.HashCombineInt(
                    seed,
                    Find.TickManager?.TicksAbs ?? 0);
                seed = Gen.HashCombineInt(
                    seed,
                    callerFaction?.loadID ?? 0);
                if (!DeterministicRandScope.Begin(
                    null,
                    seed,
                    WorkSiteSeedOffset + 1,
                    ref state,
                    out mapForPop,
                    ignoreGate: true))
                {
                    return;
                }
                __state.RandState = state;
                __state.MapForRandPop = mapForPop;
            }
            catch (Exception e)
            {
                if (state != 0)
                    DeterministicRandScope.End(state, mapForPop);
                __state.RandState = 0;
                __state.MapForRandPop = null;
                if (!_loggedPrefixFailure)
                {
                    _loggedPrefixFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Work-site quest determinism prefix " +
                        "failed open: " + e.Message);
                }
            }
        }

        private static Exception RunIntFinalizer(
            Exception __exception,
            WorkSiteScopeState __state)
        {
            if (__state != null)
            {
                DeterministicRandScope.End(
                    __state.RandState,
                    __state.MapForRandPop);
            }

            return __exception;
        }
    }
}
