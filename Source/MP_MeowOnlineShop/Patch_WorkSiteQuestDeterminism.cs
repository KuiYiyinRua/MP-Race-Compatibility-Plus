using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
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
    /// The quest generation runs in the storyteller world tick but is not
    /// covered by any deterministic scope, and it reads player-relative
    /// faction/map context while generating. Fix: wrap the whole `RunInt`
    /// body in an unconditional deterministic Rand scope (ignoring the local
    /// performance gate) and temporarily enforce Multiplayer's spectator
    /// faction context, so both peers evaluate the same candidate set and
    /// consume the same Rand/unique IDs during site/faction/leader generation.
    /// Singleplayer is untouched.
    /// </summary>
    internal static class Patch_WorkSiteQuestDeterminism
    {
        private const string WorkSiteQuestTypeName =
            "RimWorld.QuestGen.QuestNode_Root_WorkSite";
        private const int WorkSiteSeedOffset = 0x57574B53;

        private static bool _applied;
        private static FieldInfo _ofPlayerField;
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;
        private static bool _loggedPrefixFailure;
        private static bool _loggedRestoreFailure;

        [ThreadStatic]
        private static Map _mapForRandPop;

        private sealed class WorkSiteScopeState
        {
            internal int RandState;
            internal Faction SavedFaction;
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

                _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
                Type multiplayerType = AccessTools.TypeByName(
                    "Multiplayer.Client.Multiplayer");
                _worldCompProperty = multiplayerType == null
                    ? null
                    : AccessTools.Property(multiplayerType, "WorldComp");
                _spectatorFactionField = null;
                if (_worldCompProperty?.PropertyType != null)
                {
                    _spectatorFactionField = AccessTools.Field(
                        _worldCompProperty.PropertyType,
                        "spectatorFaction");
                }

                if (runInt == null || prefix == null || finalizer == null ||
                    _ofPlayerField == null || _worldCompProperty == null ||
                    _spectatorFactionField == null ||
                    _spectatorFactionField.FieldType != typeof(Faction))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Work-site quest determinism target " +
                        $"resolution failed: quest={runInt != null} prefix={prefix != null} " +
                        $"finalizer={finalizer != null} factionCtx=" +
                        $"{_ofPlayerField != null && _spectatorFactionField != null}; " +
                        "the workstation quest can still desync.");
                    return;
                }

                harmony.Patch(
                    runInt,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

                Log.Message(
                    "[MP-MeowOnlineShop] Work-site quest determinism active: " +
                    "RunInt uses a deterministic Rand scope and the spectator " +
                    "faction context.");
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
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer)
                return;

            try
            {
                Faction spectator = TryGetSpectatorFaction();
                FactionManager factionManager = Find.FactionManager;
                if (spectator != null && factionManager != null)
                {
                    __state.SavedFaction =
                        _ofPlayerField.GetValue(factionManager) as Faction;
                    _ofPlayerField.SetValue(factionManager, spectator);
                }

                int seed = Gen.HashCombineInt(
                    WorkSiteSeedOffset,
                    Find.TickManager?.TicksAbs ?? 0);
                int state = 0;
                DeterministicRandScope.Begin(
                    null,
                    seed,
                    WorkSiteSeedOffset + 1,
                    ref state,
                    out Map mapForPop,
                    ignoreGate: true);
                _mapForRandPop = mapForPop;
                __state.RandState = state;
            }
            catch (Exception e)
            {
                __state.RandState = 0;
                __state.SavedFaction = null;
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
                    _mapForRandPop);
                _mapForRandPop = null;

                if (__state.SavedFaction != null)
                {
                    try
                    {
                        FactionManager factionManager = Find.FactionManager;
                        if (factionManager != null)
                            _ofPlayerField.SetValue(
                                factionManager,
                                __state.SavedFaction);
                    }
                    catch (Exception e)
                    {
                        if (!_loggedRestoreFailure)
                        {
                            _loggedRestoreFailure = true;
                            Log.Warning(
                                "[MP-MeowOnlineShop] Work-site quest determinism " +
                                "faction restore failed: " + e.Message);
                        }
                    }
                }
            }

            return __exception;
        }

        private static Faction TryGetSpectatorFaction()
        {
            try
            {
                object worldComp = _worldCompProperty.GetValue(null, null);
                return worldComp == null
                    ? null
                    : _spectatorFactionField.GetValue(worldComp) as Faction;
            }
            catch
            {
                return null;
            }
        }
    }
}
