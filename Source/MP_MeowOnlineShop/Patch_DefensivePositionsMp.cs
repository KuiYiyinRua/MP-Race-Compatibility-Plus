using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// DefensivePositions ships its own Multiplayer API compatibility layer
    /// (`Compat_MultiplayerAPI.Initialize`), but that layer aborts before
    /// registering anything because the installed 0MultiplayerAPI no longer
    /// matches the mod's hard-coded API version check. Log evidence in
    /// Desync-115/116:
    ///
    /// [DefensivePositions][ERR] Failed to apply Multiplayer API compatibility
    /// layer: System.Exception: MP API version mismatch. This mod is designed
    /// to work with MPAPI version 0.1
    ///
    /// As a result, assigning/clearing a drafted pawn's defensive position,
    /// reassigning squads, and toggling advanced mode run locally on the
    /// clicking peer only. The saved position then drives drafted-pawn
    /// movement and jobs on only one peer, which shows up as movement/filth,
    /// job-ID, and map-Rand divergence while drafting pawns during combat
    /// (Desync-113 through 116).
    ///
    /// Restore the exact registrations the mod intended: the sync methods and
    /// the handler/manager/squad sync workers. `TryTakeOrderedJob` (used to
    /// send a pawn to a saved position) is already synced by Multiplayer.
    /// </summary>
    internal static class Patch_DefensivePositionsMp
    {
        private const string ManagerTypeName =
            "DefensivePositions.DefensivePositionsManager";
        private const string PawnHandlerTypeName =
            "DefensivePositions.PawnSavedPositionHandler";
        private const string SquadHandlerTypeName =
            "DefensivePositions.PawnSquadHandler";

        private static bool _applied;
        private static Type _managerType;
        private static Type _handlerType;
        private static Type _squadType;
        private static PropertyInfo _managerInstanceProperty;
        private static PropertyInfo _handlerOwnerProperty;
        private static MethodInfo _getHandlerMethod;
        private static FieldInfo _squadHandlerField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                _managerType = AccessTools.TypeByName(ManagerTypeName);
                _handlerType = AccessTools.TypeByName(PawnHandlerTypeName);
                _squadType = AccessTools.TypeByName(SquadHandlerTypeName);

                if (_managerType == null || _handlerType == null || _squadType == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] DefensivePositions sync skipped " +
                        "(target mod not active).");
                    return;
                }

                int registered = 0;
                registered += TryRegisterSyncMethod(
                    AccessTools.Method(
                        _handlerType,
                        "SetDefensivePosition",
                        new[] { typeof(int) }));
                registered += TryRegisterSyncMethod(
                    AccessTools.Method(
                        _handlerType,
                        "DiscardSavedPosition",
                        new[] { typeof(int) }));
                registered += TryRegisterSyncMethod(
                    AccessTools.Method(
                        _managerType,
                        "ToggleAdvancedMode",
                        new[] { typeof(bool) }));
                registered += TryRegisterSyncMethod(
                    AccessTools.Method(
                        _squadType,
                        "ReassignSquadMembers",
                        new[] { typeof(int), typeof(List<Thing>) }));
                registered += TryRegisterSyncMethod(
                    AccessTools.Method(
                        _squadType,
                        "ClearSquad",
                        new[] { typeof(int) }));

                _managerInstanceProperty = _managerType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static);
                _handlerOwnerProperty = _handlerType.GetProperty(
                    "Owner",
                    BindingFlags.Public | BindingFlags.Instance);
                _getHandlerMethod = _managerType.GetMethod(
                    "GetHandlerForPawn",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(Pawn) },
                    null);
                _squadHandlerField = _managerType.GetField(
                    "squadHandler",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (_managerInstanceProperty == null ||
                    _handlerOwnerProperty == null ||
                    _getHandlerMethod == null ||
                    _squadHandlerField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] DefensivePositions sync workers could " +
                        $"not resolve manager accessors; methods registered={registered}.");
                    return;
                }

                TryRegisterSyncWorker(ManagerSyncer, _managerType);
                TryRegisterSyncWorker(PawnHandlerSyncer, _handlerType);
                TryRegisterSyncWorker(SquadSyncer, _squadType);

                Log.Message(
                    "[MP-MeowOnlineShop] DefensivePositions MP compat restored: " +
                    $"methods={registered}, workers=3.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DefensivePositions MP compat restore failed: " +
                    e.Message);
            }
        }

        private static int TryRegisterSyncMethod(MethodInfo method)
        {
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DefensivePositions sync register failed on " +
                    $"{method.Name}: {e.Message}");
                return 0;
            }
        }

        private static void TryRegisterSyncWorker(
            SyncWorkerDelegate<object> worker,
            Type targetType)
        {
            try
            {
                MP.RegisterSyncWorker<object>(
                    worker,
                    targetType,
                    false,
                    false);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DefensivePositions sync worker registration " +
                    $"failed for {targetType?.Name}: {e.Message}");
            }
        }

        private static void ManagerSyncer(SyncWorker sync, ref object inst)
        {
            if (!sync.isWriting)
                inst = _managerInstanceProperty?.GetValue(null, null);
        }

        private static void PawnHandlerSyncer(SyncWorker sync, ref object inst)
        {
            Pawn pawn = inst == null || _handlerOwnerProperty == null
                ? null
                : _handlerOwnerProperty.GetValue(inst, null) as Pawn;
            sync.Bind(ref pawn);

            if (!sync.isWriting)
            {
                object manager = _managerInstanceProperty?.GetValue(null, null);
                inst = manager == null || pawn == null
                    ? null
                    : _getHandlerMethod.Invoke(manager, new object[] { pawn });
            }
        }

        private static void SquadSyncer(SyncWorker sync, ref object inst)
        {
            if (!sync.isWriting)
            {
                object manager = _managerInstanceProperty?.GetValue(null, null);
                inst = manager == null
                    ? null
                    : _squadHandlerField.GetValue(manager);
            }
        }
    }
}
