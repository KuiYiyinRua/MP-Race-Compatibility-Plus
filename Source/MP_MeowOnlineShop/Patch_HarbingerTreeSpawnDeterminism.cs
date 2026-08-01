using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// "厄兆噬树萌芽" (Harbinger tree sprout) uses
    /// `IncidentWorker_SpecialTreeSpawn.CanFireNowSub`, whose virtual
    /// `TryFindRootCell` runs `CellFinderLoose.TryGetRandomCellWith` and
    /// consumes map Rand while the storyteller evaluates the incident. In
    /// multifaction/async-time sessions the set or order of evaluated maps can
    /// differ per peer (Desync-104 already showed
    /// `IncidentWorker_HarbingerTreeSpawn.CanFireNowSub` consuming Rand on one
    /// peer only), so that probing advances the synchronized stream on one
    /// side and later desyncs map/world state. Desync-122's final "Trace
    /// hashes don't match" follows the user-triggered Harbinger tree sprout.
    ///
    /// The existing incident stabilizer wraps TryExecute/TryExecuteWorker, but
    /// not CanFireNowSub. Isolate the whole base `CanFireNowSub` body in a
    /// deterministic scope seeded from the shared incident target seed and
    /// world tick, so eligibility probing never advances the live stream.
    /// The scope ignores the local performance gate
    /// (`enableDeterministicRandRefactor`): a host/client setting mismatch must
    /// not let one peer consume the live map stream during eligibility probing
    /// (Desync-132). Singleplayer is untouched.
    /// </summary>
    internal static class Patch_HarbingerTreeSpawnDeterminism
    {
        private const int HarbingerCanFireSeedOffset = 0x48415242;
        private const int HarbingerCanFireWorldSeedOffset = 0x48545245;

        [ThreadStatic]
        private static Map _mapForRandPop;

        private static bool _applied;
        private static bool _loggedActive;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type specialTreeWorkerType =
                    AccessTools.TypeByName("RimWorld.IncidentWorker_SpecialTreeSpawn");
                MethodInfo target = specialTreeWorkerType == null
                    ? null
                    : AccessTools.Method(
                        specialTreeWorkerType,
                        "CanFireNowSub",
                        new[] { typeof(IncidentParms) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_HarbingerTreeSpawnDeterminism),
                    nameof(CanFireNowSubPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_HarbingerTreeSpawnDeterminism),
                    nameof(CanFireNowSubFinalizer));

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Harbinger/special-tree CanFireNowSub guard " +
                        "skipped (target method not resolved).");
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

                Log.Message(
                    "[MP-MeowOnlineShop] Harbinger/special-tree CanFireNowSub Rand isolation active.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Harbinger/special-tree CanFireNowSub guard failed: " +
                    e.Message);
            }
        }

        private static void CanFireNowSubPrefix(
            IncidentWorker __instance,
            IncidentParms parms,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Map map = parms?.target as Map;
            int targetSeed = parms?.target?.ConstantRandSeed ?? 0;
            int seed = Gen.HashCombineInt(HarbingerCanFireSeedOffset, targetSeed);
            seed = Gen.HashCombineInt(seed, __instance.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    HarbingerCanFireWorldSeedOffset,
                    ref __state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Harbinger/special-tree eligibility probing " +
                        $"isolated from the synchronized stream; targetSeed={targetSeed}.");
                }
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception CanFireNowSubFinalizer(Exception __exception, int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }
    }

    /// <summary>
    /// Desync-144: after the CanFireNowSub probe is isolated, the actual
    /// Harbinger tree sprout execution still desyncs map 8. The first
    /// divergent trace (record 2757, tick 1752325) shows the same map Rand
    /// state but a different Long-TickList member on each peer
    /// (host `Fire331622` vs client `Plant_YellowTallGrass229044`), right
    /// after `IncidentWorker_HarbingerTreeSpawn.TryGetHarbingerTreeSpawnCell`
    /// ran on the client at tick 1752323.
    ///
    /// The cell search reads player-relative state:
    /// `Current.Game.AnyPlayerHomeMap.listerThings.ThingsOfDef(...).Count` to
    /// derive `maxProximityToSameTree`, then filters candidate cells with
    /// `CanSpawnAt`. `AnyPlayerHomeMap` is vanilla and compares
    /// `map.ParentFaction == Faction.OfPlayer`, so when the two peers tick the
    /// target map under different `FactionManager.ofPlayer` contexts the tree
    /// can be placed on different cells, destroying different plants and
    /// leaving different Long-TickList contents even though every Rand draw is
    /// identical.
    ///
    /// Fix: run the whole special-tree spawn execution under the target map's
    /// parent-faction context (spectator fallback), so
    /// `AnyPlayerHomeMap`/`IsPlayerHome` evaluate the same set on every peer.
    /// Singleplayer is untouched; failures fail open.
    /// </summary>
    internal static class Patch_HarbingerTreeSpawnExecutionDeterminism
    {
        private static bool _applied;
        private static bool _loggedActive;
        private static bool _loggedFailure;
        private static FieldInfo _ofPlayerField;

        private sealed class TreeSpawnScopeState
        {
            internal bool Active;
            internal Faction SavedFaction;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");
                if (_ofPlayerField == null ||
                    _ofPlayerField.FieldType != typeof(Faction))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Harbinger tree execution faction " +
                        "context target resolution failed (ofPlayer field).");
                    return;
                }

                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_HarbingerTreeSpawnExecutionDeterminism),
                    nameof(TryExecuteWorkerPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_HarbingerTreeSpawnExecutionDeterminism),
                    nameof(TryExecuteWorkerFinalizer));

                int patched = 0;
                patched += TryPatchWorker(
                    harmony,
                    "RimWorld.IncidentWorker_SpecialTreeSpawn",
                    prefix,
                    finalizer);
                patched += TryPatchWorker(
                    harmony,
                    "RimWorld.IncidentWorker_HarbingerTreeSpawn",
                    prefix,
                    finalizer);

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Harbinger tree execution faction " +
                        "context NOT active: no special-tree TryExecuteWorker " +
                        "targets resolved.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Harbinger/special-tree execution faction " +
                    $"context active (TryExecuteWorker patched={patched}).");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Harbinger tree execution faction " +
                    "context apply failed: " + e.Message);
            }
        }

        private static int TryPatchWorker(
            Harmony harmony,
            string typeName,
            MethodInfo prefix,
            MethodInfo finalizer)
        {
            try
            {
                Type workerType = AccessTools.TypeByName(typeName);
                MethodInfo target = workerType == null
                    ? null
                    : AccessTools.Method(
                        workerType,
                        "TryExecuteWorker",
                        new[] { typeof(IncidentParms) });
                if (target == null || target.IsAbstract)
                    return 0;

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
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private static void TryExecuteWorkerPrefix(
            IncidentParms parms,
            ref TreeSpawnScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || parms == null)
                return;

            try
            {
                Map map = parms.target as Map;
                Faction contextFaction = map?.ParentFaction;
                if (contextFaction?.def?.isPlayer != true)
                {
                    contextFaction = TryGetSpectatorFaction();
                }

                FactionManager factionManager = Find.FactionManager;
                if (contextFaction == null || factionManager == null ||
                    _ofPlayerField == null)
                {
                    return;
                }

                Faction previous = _ofPlayerField.GetValue(
                    factionManager) as Faction;
                if (ReferenceEquals(previous, contextFaction))
                    return;

                _ofPlayerField.SetValue(factionManager, contextFaction);
                __state = new TreeSpawnScopeState
                {
                    Active = true,
                    SavedFaction = previous
                };

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Harbinger tree execution faction " +
                        $"context: {contextFaction.Name} " +
                        "(AnyPlayerHomeMap/proximity now peer-identical).");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Harbinger tree execution faction " +
                        "context prefix failed open: " + e.Message);
                }
            }
        }

        private static Exception TryExecuteWorkerFinalizer(
            Exception __exception,
            TreeSpawnScopeState __state)
        {
            if (__state?.Active == true && _ofPlayerField != null)
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
                    if (!_loggedFailure)
                    {
                        _loggedFailure = true;
                        Log.Warning(
                            "[MP-MeowOnlineShop] Harbinger tree execution faction " +
                            "context restore failed: " + e.Message);
                    }
                }
            }

            return __exception;
        }

        private static Faction TryGetSpectatorFaction()
        {
            try
            {
                Type multiplayerType =
                    AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
                PropertyInfo worldComp = multiplayerType == null
                    ? null
                    : AccessTools.Property(multiplayerType, "WorldComp");
                FieldInfo spectatorField = worldComp?.PropertyType == null
                    ? null
                    : AccessTools.Field(
                        worldComp.PropertyType,
                        "spectatorFaction");
                object comp = worldComp?.GetValue(null, null);
                return comp == null
                    ? null
                    : spectatorField?.GetValue(comp) as Faction;
            }
            catch
            {
                return null;
            }
        }
    }
}
