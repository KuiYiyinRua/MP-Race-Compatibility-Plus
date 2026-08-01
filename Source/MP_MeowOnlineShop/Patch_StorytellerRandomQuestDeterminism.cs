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
    /// deterministic Rand scope (ignoring the local performance gate) and
    /// enforce Multiplayer's spectator faction context, so every peer evaluates
    /// the same candidate set and consumes the same Rand sequence during the
    /// batch regardless of per-player faction/map context. Singleplayer is
    /// untouched.
    /// </summary>
    internal static class Patch_StorytellerRandomQuestDeterminism
    {
        private const int RandomQuestSeedOffset = 0x52515531;

        private static bool _applied;
        private static FieldInfo _ofPlayerField;
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;

        [ThreadStatic]
        private static Map _mapForRandPop;

        private sealed class RandomQuestScopeState
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

                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");
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

                if (target == null || prefix == null || finalizer == null ||
                    _ofPlayerField == null || _worldCompProperty == null ||
                    _spectatorFactionField == null ||
                    _spectatorFactionField.FieldType != typeof(Faction))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller random-quest determinism " +
                        $"target resolution failed: target={target != null}, " +
                        $"factionCtx={_ofPlayerField != null && _spectatorFactionField != null}; " +
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
                    "the spectator faction context.");
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
                    RandomQuestSeedOffset,
                    Find.TickManager?.TicksAbs ?? 0);
                seed = Gen.HashCombineInt(
                    seed,
                    BitConverter.ToInt32(
                        BitConverter.GetBytes(points), 0));
                seed = Gen.HashCombineInt(seed, StableTargetKey(target));

                int state = 0;
                DeterministicRandScope.Begin(
                    null,
                    seed,
                    RandomQuestSeedOffset + 1,
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
                                "[MP-MeowOnlineShop] Storyteller random-quest " +
                                "faction restore failed: " + e.Message);
                        }
                    }
                }
            }

            return __exception;
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
        private static bool _loggedRestoreFailure;

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
