using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-135: the Oberonia Aurea "Birthday Wishes" event
    /// (`OberoniaAurea.IncidentWorker_BirthdayWishes`) fired at tick 1550653 on
    /// both peers, and the first divergent map-8 draw followed immediately in
    /// `JobDriver_ConstructFinishFrame` with different Rand values on the same
    /// pawn. The incident's `CanFireNowSub`/`ResolveParms` use
    /// `Find.RandomPlayerHomeMap` (Rand + player-relative map selection) and
    /// `map.mapPawns.FreeColonists` (player-relative pawn list); in a
    /// multifaction session these can select/consume differently per peer.
    ///
    /// Fix (same pattern as the Harbinger CanFireNowSub isolation):
    /// - Scope `SpecialGlobalEventManager.CheckBirthdayWishes` and
    ///   `IncidentWorker_BirthdayWishes.CanFireNowSub` in an unconditional
    ///   deterministic Rand scope (ignoring the local performance gate).
    /// - Enforce Multiplayer's spectator faction context while the event runs
    ///   so `RandomPlayerHomeMap` and `FreeColonists` evaluate the same set on
    ///   every peer.
    /// - `TryExecuteWorker` also gets the spectator context (its Rand is
    ///   already covered by the shared incident stabilizer).
    /// Singleplayer is untouched.
    /// </summary>
    internal static class Patch_OberoniaBirthdayMp
    {
        private const string ManagerTypeName =
            "OberoniaAurea.SpecialGlobalEventManager";
        private const string WorkerTypeName =
            "OberoniaAurea.IncidentWorker_BirthdayWishes";
        private const int BirthdaySeedOffset = 0x42495254;

        private static bool _applied;
        private static Type _managerType;
        private static Type _workerType;
        private static FieldInfo _ofPlayerField;
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;

        [ThreadStatic]
        private static Map _mapForRandPop;

        private sealed class OberoniaScopeState
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
                _managerType = AccessTools.TypeByName(ManagerTypeName);
                _workerType = AccessTools.TypeByName(WorkerTypeName);
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

                if (_managerType == null || _workerType == null ||
                    _ofPlayerField == null || _worldCompProperty == null ||
                    _spectatorFactionField == null ||
                    _spectatorFactionField.FieldType != typeof(Faction))
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Oberonia birthday MP patch skipped: " +
                        $"manager={_managerType != null}, worker={_workerType != null}, " +
                        $"factionCtx={_ofPlayerField != null && _spectatorFactionField != null}.");
                    return;
                }

                int patched = 0;
                patched += TryPatch(
                    harmony,
                    _managerType,
                    "CheckBirthdayWishes",
                    Type.EmptyTypes,
                    "CheckBirthdayWishes");
                patched += TryPatch(
                    harmony,
                    _workerType,
                    "CanFireNowSub",
                    new[] { typeof(IncidentParms) },
                    "CanFireNowSub");
                patched += TryPatch(
                    harmony,
                    _workerType,
                    "TryExecuteWorker",
                    new[] { typeof(IncidentParms) },
                    "TryExecuteWorker");

                Log.Message(
                    "[MP-MeowOnlineShop] Oberonia birthday MP determinism active: " +
                    $"targets patched={patched}/3.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Oberonia birthday MP patch failed: " +
                    e.Message);
            }
        }

        private static int TryPatch(
            Harmony harmony,
            Type type,
            string methodName,
            Type[] parameterTypes,
            string label)
        {
            try
            {
                MethodInfo target = AccessTools.Method(
                    type,
                    methodName,
                    parameterTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_OberoniaBirthdayMp),
                    nameof(ScopePrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_OberoniaBirthdayMp),
                    nameof(ScopeFinalizer));
                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Oberonia birthday " + label +
                        " target resolution failed.");
                    return 0;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Oberonia birthday " + label +
                    " patch failed: " + e.Message);
                return 0;
            }
        }

        private static void ScopePrefix(
            ref OberoniaScopeState __state)
        {
            __state = new OberoniaScopeState();
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
                    BirthdaySeedOffset,
                    Find.TickManager?.TicksAbs ?? 0);
                int state = 0;
                DeterministicRandScope.Begin(
                    null,
                    seed,
                    BirthdaySeedOffset + 1,
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
                        "[MP-MeowOnlineShop] Oberonia birthday scope prefix " +
                        "failed open: " + e.Message);
                }
            }
        }

        private static Exception ScopeFinalizer(
            Exception __exception,
            OberoniaScopeState __state)
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
                                "[MP-MeowOnlineShop] Oberonia birthday faction " +
                                "restore failed: " + e.Message);
                        }
                    }
                }
            }

            return __exception;
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
