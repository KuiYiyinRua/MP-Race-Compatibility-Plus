using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Cluster Projection (`acutus.clusterprojection`) mutates simulation from
    /// a large set of local UI entry points:
    ///
    /// - Console gizmos: load/eject steel, eject fuel, start assembly, choose a
    ///   world or map landing target, and the fast-return allowlist tab.
    /// - Fast-return marker: start assembly and launch.
    /// - Infantry capsule and special carrier rack: load/unload dialogs and
    ///   gizmos.
    /// - Projection beacon: its custom CompLaunchable replacement launches the
    ///   beacon through the same world targeter as vanilla transport pods.
    ///
    /// The patch keeps targeter/window lifecycle local and synchronizes only
    /// the stable simulation executors. TryBeginLaunchSequence is wrapped by a
    /// bool-returning Harmony prefix so the local targeter still receives
    /// "accepted" and closes.
    /// </summary>
    internal static class Patch_ClusterProjectionMp
    {
        private const string PackageId = "acutus.clusterprojection";

        private const string ConsoleTypeName = "ClusterProjection.Building_ClusterProjectionConsole";
        private const string FastReturnTypeName = "ClusterProjection.Building_FastReturnMarker";
        private const string RackTypeName = "ClusterProjection.Building_SpecialCarrierRack";
        private const string CapsuleTypeName = "ClusterProjection.Building_InfantryCapsule";
        private const string BeaconCompTypeName = "ClusterProjection.CP_CompLaunchableBeacon";
        private const string InfantryDialogTypeName = "ClusterProjection.Dialog_LoadInfantryCapsule";
        private const string RackDialogTypeName = "ClusterProjection.Dialog_LoadSpecialCarrierRack";

        private static Type _consoleType;
        private static Type _fastReturnType;
        private static Type _rackType;
        private static Type _capsuleType;
        private static Type _beaconCompType;
        private static Type _infantryDialogType;
        private static Type _rackDialogType;

        private static MethodInfo _tryBeginLaunchSequence;
        private static MethodInfo _tryStartAssemblyAndLaunch;
        private static MethodInfo _loadSteelFromMap;
        private static MethodInfo _ejectSteel;
        private static MethodInfo _ejectFuel;
        private static MethodInfo _setFastReturnAllowed;
        private static MethodInfo _setAllFastReturnAllowed;
        private static MethodInfo _allowsFastReturn;
        private static MethodInfo _startReturnCountdown;
        private static MethodInfo _rackQueuePawn;
        private static MethodInfo _rackUnloadPawn;
        private static MethodInfo _capsuleUnloadAll;
        private static MethodInfo _beaconTryLaunch;

        private static MethodInfo _infantryCalculate;
        private static MethodInfo _infantrySetLoaded;
        private static MethodInfo _infantryTryAccept;
        private static MethodInfo _infantryGetSelected;
        private static FieldInfo _infantryTransferablesField;
        private static FieldInfo _infantryMapField;
        private static FieldInfo _infantryCapsuleField;

        private static MethodInfo _rackTryAccept;
        private static MethodInfo _rackGetSelected;
        private static FieldInfo _rackMapField;
        private static FieldInfo _rackRackField;
        private static PropertyInfo _rackHeldPawnProperty;

        private static ISyncMethod _syncBeginLaunch;
        private static ISyncMethod _syncSetFastReturnAllowed;
        private static ISyncMethod _syncSetAllFastReturnAllowed;
        private static ISyncMethod _syncAcceptInfantry;
        private static ISyncMethod _syncAcceptRack;

        private static int _registeredSyncMethods;
        private static int _patchedMethods;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            ResolveTargets();
            if (_consoleType == null || _tryBeginLaunchSequence == null ||
                _rackType == null || _rackQueuePawn == null || _rackUnloadPawn == null ||
                _capsuleType == null || _capsuleUnloadAll == null ||
                _beaconCompType == null || _beaconTryLaunch == null ||
                _infantryDialogType == null || _infantryTryAccept == null ||
                _rackDialogType == null || _rackTryAccept == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection target resolution incomplete; patch skipped.");
                return;
            }

            RegisterSyncMethods();
            PatchFastReturnAllowlist(harmony);
            PatchBeginLaunch(harmony);
            PatchDialogs(harmony);

            Log.Message(
                "[MP-MeowOnlineShop] Cluster Projection MP patch active: " +
                $"syncMethods={_registeredSyncMethods}, patchedMethods={_patchedMethods}.");
        }

        private static void ResolveTargets()
        {
            _consoleType = AccessTools.TypeByName(ConsoleTypeName);
            _fastReturnType = AccessTools.TypeByName(FastReturnTypeName);
            _rackType = AccessTools.TypeByName(RackTypeName);
            _capsuleType = AccessTools.TypeByName(CapsuleTypeName);
            _beaconCompType = AccessTools.TypeByName(BeaconCompTypeName);
            _infantryDialogType = AccessTools.TypeByName(InfantryDialogTypeName);
            _rackDialogType = AccessTools.TypeByName(RackDialogTypeName);

            _tryBeginLaunchSequence = AccessTools.Method(
                _consoleType,
                "TryBeginLaunchSequence",
                new[] { typeof(PlanetTile), typeof(TransportersArrivalAction) });
            _tryStartAssemblyAndLaunch = AccessTools.Method(_consoleType, "TryStartAssemblyAndLaunch", Type.EmptyTypes);
            _loadSteelFromMap = AccessTools.Method(_consoleType, "LoadSteelFromMap", Type.EmptyTypes);
            _ejectSteel = AccessTools.Method(_consoleType, "EjectSteel", Type.EmptyTypes);
            _ejectFuel = AccessTools.Method(_consoleType, "EjectFuel", Type.EmptyTypes);
            _setFastReturnAllowed = AccessTools.Method(_consoleType, "SetFastReturnAllowed", new[] { typeof(ThingDef), typeof(bool) });
            _setAllFastReturnAllowed = AccessTools.Method(_consoleType, "SetAllFastReturnAllowed", new[] { typeof(bool) });
            _allowsFastReturn = AccessTools.Method(_consoleType, "AllowsFastReturn", new[] { typeof(ThingDef) });

            _startReturnCountdown = AccessTools.Method(_fastReturnType, "StartReturnCountdown", Type.EmptyTypes);
            _rackQueuePawn = AccessTools.Method(_rackType, "QueuePawn", new[] { typeof(Pawn) });
            _rackUnloadPawn = AccessTools.Method(_rackType, "UnloadPawn", Type.EmptyTypes);
            _rackHeldPawnProperty = AccessTools.Property(_rackType, "HeldPawn");
            _capsuleUnloadAll = AccessTools.Method(_capsuleType, "UnloadAll", Type.EmptyTypes);
            _beaconTryLaunch = AccessTools.Method(
                _beaconCompType,
                "TryLaunch",
                new[] { typeof(PlanetTile), typeof(TransportersArrivalAction) });

            _infantryCalculate = AccessTools.Method(_infantryDialogType, "CalculateAndRecacheTransferables", Type.EmptyTypes);
            _infantrySetLoaded = AccessTools.Method(_infantryDialogType, "SetLoadedPawnsToLoad", Type.EmptyTypes);
            _infantryTryAccept = AccessTools.Method(_infantryDialogType, "TryAccept", Type.EmptyTypes);
            _infantryGetSelected = AccessTools.Method(_infantryDialogType, "GetSelectedPawns", Type.EmptyTypes);
            _infantryTransferablesField = AccessTools.Field(_infantryDialogType, "transferables");
            _infantryMapField = AccessTools.Field(_infantryDialogType, "map");
            _infantryCapsuleField = AccessTools.Field(_infantryDialogType, "capsule");

            _rackTryAccept = AccessTools.Method(_rackDialogType, "TryAccept", Type.EmptyTypes);
            _rackGetSelected = AccessTools.Method(_rackDialogType, "GetSelectedPawns", Type.EmptyTypes);
            _rackMapField = AccessTools.Field(_rackDialogType, "map");
            _rackRackField = AccessTools.Field(_rackDialogType, "rack");
        }

        private static void RegisterSyncMethods()
        {
            try
            {
                _syncBeginLaunch = MP.RegisterSyncMethod(
                        typeof(Patch_ClusterProjectionMp),
                        nameof(SyncBeginLaunchSequence))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull()
                    .ExposeParameter(3);
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection launch sync registration failed: " + e.Message);
            }

            TryRegisterSyncMethod(_tryStartAssemblyAndLaunch, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(_loadSteelFromMap, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(_ejectSteel, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(_ejectFuel, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(_startReturnCountdown, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(_rackQueuePawn, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(_rackUnloadPawn, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(_capsuleUnloadAll, SyncContext.CurrentMap, null);
            TryRegisterSyncMethod(
                _beaconTryLaunch,
                SyncContext.CurrentMap,
                sync => sync.ExposeParameter(1));

            try
            {
                _syncSetFastReturnAllowed = MP.RegisterSyncMethod(
                        typeof(Patch_ClusterProjectionMp),
                        nameof(SyncSetFastReturnAllowed))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection fast-return sync registration failed: " + e.Message);
            }

            try
            {
                _syncSetAllFastReturnAllowed = MP.RegisterSyncMethod(
                        typeof(Patch_ClusterProjectionMp),
                        nameof(SyncSetAllFastReturnAllowed))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection allow-all sync registration failed: " + e.Message);
            }

            try
            {
                _syncAcceptInfantry = MP.RegisterSyncMethod(
                        typeof(Patch_ClusterProjectionMp),
                        nameof(SyncAcceptInfantryCapsule))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection infantry dialog sync registration failed: " + e.Message);
            }

            try
            {
                _syncAcceptRack = MP.RegisterSyncMethod(
                        typeof(Patch_ClusterProjectionMp),
                        nameof(SyncAcceptSpecialCarrierRack))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection carrier rack dialog sync registration failed: " + e.Message);
            }
        }

        private static void TryRegisterSyncMethod(MethodInfo method, SyncContext context, Action<ISyncMethod> configure)
        {
            if (method == null)
                return;

            try
            {
                ISyncMethod sync = MP.RegisterSyncMethod(method, null);
                sync.SetContext(context);
                configure?.Invoke(sync);
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Cluster Projection sync registration failed on " +
                    (method.DeclaringType?.Name ?? "?") + "." + method.Name + ": " + e.Message);
            }
        }

        private static void PatchFastReturnAllowlist(Harmony harmony)
        {
            if (_syncSetFastReturnAllowed == null || _syncSetAllFastReturnAllowed == null)
                return;

            MethodInfo setPrefix = AccessTools.Method(
                typeof(Patch_ClusterProjectionMp),
                nameof(SetFastReturnAllowedPrefix));
            MethodInfo allPrefix = AccessTools.Method(
                typeof(Patch_ClusterProjectionMp),
                nameof(SetAllFastReturnAllowedPrefix));

            if (setPrefix != null && _setFastReturnAllowed != null)
            {
                try
                {
                    harmony.Patch(_setFastReturnAllowed, prefix: new HarmonyMethod(setPrefix));
                    _patchedMethods++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Cluster Projection fast-return setter patch failed: " + e.Message);
                }
            }

            if (allPrefix != null && _setAllFastReturnAllowed != null)
            {
                try
                {
                    harmony.Patch(_setAllFastReturnAllowed, prefix: new HarmonyMethod(allPrefix));
                    _patchedMethods++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Cluster Projection allow-all patch failed: " + e.Message);
                }
            }
        }

        private static void PatchBeginLaunch(Harmony harmony)
        {
            if (_syncBeginLaunch == null || _tryBeginLaunchSequence == null)
                return;

            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_ClusterProjectionMp),
                nameof(TryBeginLaunchSequencePrefix));
            if (prefix == null)
                return;

            try
            {
                harmony.Patch(_tryBeginLaunchSequence, prefix: new HarmonyMethod(prefix));
                _patchedMethods++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection launch boundary patch failed: " + e.Message);
            }
        }

        private static void PatchDialogs(Harmony harmony)
        {
            MethodInfo infantryPrefix = AccessTools.Method(
                typeof(Patch_ClusterProjectionMp),
                nameof(InfantryDialogTryAcceptPrefix));
            if (_syncAcceptInfantry != null && _infantryTryAccept != null && infantryPrefix != null)
            {
                try
                {
                    harmony.Patch(_infantryTryAccept, prefix: new HarmonyMethod(infantryPrefix));
                    _patchedMethods++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Cluster Projection infantry dialog patch failed: " + e.Message);
                }
            }

            MethodInfo rackPrefix = AccessTools.Method(
                typeof(Patch_ClusterProjectionMp),
                nameof(RackDialogTryAcceptPrefix));
            if (_syncAcceptRack != null && _rackTryAccept != null && rackPrefix != null)
            {
                try
                {
                    harmony.Patch(_rackTryAccept, prefix: new HarmonyMethod(rackPrefix));
                    _patchedMethods++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Cluster Projection carrier rack dialog patch failed: " + e.Message);
                }
            }
        }

        private static bool SetFastReturnAllowedPrefix(object __instance, ThingDef buildingDef, bool allowed)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncSetFastReturnAllowed == null || __instance == null || buildingDef == null ||
                !(__instance is Thing parent))
            {
                return true;
            }

            bool current = IsFastReturnAllowed(parent, buildingDef);
            if (current == allowed)
                return false;

            _syncSetFastReturnAllowed.DoSync(null, parent, buildingDef.defName, allowed);
            return false;
        }

        private static bool SetAllFastReturnAllowedPrefix(object __instance, bool allowed)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncSetAllFastReturnAllowed == null || !(__instance is Thing parent))
            {
                return true;
            }

            _syncSetAllFastReturnAllowed.DoSync(null, parent, allowed);
            return false;
        }

        private static bool TryBeginLaunchSequencePrefix(
            object __instance,
            PlanetTile destinationTile,
            TransportersArrivalAction arrivalAction,
            ref bool __result)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncBeginLaunch == null || !(__instance is Thing parent))
            {
                return true;
            }

            Map map = parent.Map;
            if (map == null)
                return true;

            try
            {
                bool sent = _syncBeginLaunch.DoSync(
                    null,
                    map.Index,
                    parent.thingIDNumber,
                    destinationTile,
                    arrivalAction);
                if (sent)
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection launch sync send failed: " + e.Message);
            }

            return true;
        }

        private static bool InfantryDialogTryAcceptPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncAcceptInfantry == null || __instance == null ||
                _infantryGetSelected == null || _infantryMapField == null || _infantryCapsuleField == null)
            {
                return true;
            }

            List<Pawn> selected = InvokeSelectedPawns(_infantryGetSelected, __instance);
            if (selected == null || selected.Count == 0)
                return true;

            Map map = _infantryMapField.GetValue(__instance) as Map;
            Thing capsule = _infantryCapsuleField.GetValue(__instance) as Thing;
            if (map == null || capsule == null)
                return true;

            _syncAcceptInfantry.DoSync(
                null,
                map.Index,
                capsule.thingIDNumber,
                selected.Select(p => p.thingIDNumber).ToList());
            return false;
        }

        private static bool RackDialogTryAcceptPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncAcceptRack == null || __instance == null ||
                _rackGetSelected == null || _rackMapField == null || _rackRackField == null)
            {
                return true;
            }

            List<Pawn> selected = InvokeSelectedPawns(_rackGetSelected, __instance);
            if (selected != null && selected.Count > 1)
                return true;

            Map map = _rackMapField.GetValue(__instance) as Map;
            Thing rack = _rackRackField.GetValue(__instance) as Thing;
            if (map == null || rack == null)
                return true;

            int selectedId = selected != null && selected.Count == 1 ? selected[0].thingIDNumber : 0;
            _syncAcceptRack.DoSync(null, map.Index, rack.thingIDNumber, selectedId);
            return false;
        }

        private static List<Pawn> InvokeSelectedPawns(MethodInfo method, object dialog)
        {
            if (method == null || dialog == null)
                return null;

            try
            {
                return method.Invoke(dialog, null) as List<Pawn>;
            }
            catch
            {
                return null;
            }
        }

        public static void SyncSetFastReturnAllowed(Thing parent, string defName, bool allowed)
        {
            if (_setFastReturnAllowed == null || parent == null || string.IsNullOrEmpty(defName))
                return;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;

            _setFastReturnAllowed.Invoke(parent, new object[] { def, allowed });
        }

        public static void SyncSetAllFastReturnAllowed(Thing parent, bool allowed)
        {
            if (_setAllFastReturnAllowed == null || parent == null)
                return;

            _setAllFastReturnAllowed.Invoke(parent, new object[] { allowed });
        }

        public static void SyncBeginLaunchSequence(
            int mapIndex,
            int thingId,
            PlanetTile destinationTile,
            TransportersArrivalAction arrivalAction)
        {
            if (_tryBeginLaunchSequence == null || arrivalAction == null)
                return;

            Thing parent = FindThingById(FindMap(mapIndex), thingId);
            if (parent == null)
                return;

            _tryBeginLaunchSequence.Invoke(parent, new object[] { destinationTile, arrivalAction });
        }

        public static void SyncAcceptInfantryCapsule(int mapIndex, int capsuleId, List<int> pawnIds)
        {
            if (_infantryDialogType == null || _infantryCalculate == null ||
                _infantrySetLoaded == null || _infantryTryAccept == null ||
                _infantryTransferablesField == null || pawnIds == null || pawnIds.Count == 0)
            {
                return;
            }

            Thing capsule = FindThingById(FindMap(mapIndex), capsuleId);
            Map map = FindMap(mapIndex);
            if (map == null || capsule == null)
                return;

            object dialog;
            try
            {
                dialog = Activator.CreateInstance(_infantryDialogType, map, capsule);
            }
            catch
            {
                return;
            }

            try
            {
                _infantryCalculate.Invoke(dialog, null);
                _infantrySetLoaded.Invoke(dialog, null);
                object transferables = _infantryTransferablesField.GetValue(dialog);
                SetTransferableCounts(transferables, pawnIds);
                _infantryTryAccept.Invoke(dialog, null);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cluster Projection infantry dialog replay failed: " + e.Message);
            }
        }

        public static void SyncAcceptSpecialCarrierRack(int mapIndex, int rackId, int selectedPawnId)
        {
            if (_rackQueuePawn == null || _rackUnloadPawn == null || _rackHeldPawnProperty == null)
                return;

            Thing rack = FindThingById(FindMap(mapIndex), rackId);
            if (rack == null)
                return;

            Pawn held = _rackHeldPawnProperty.GetValue(rack) as Pawn;
            Pawn selected = selectedPawnId == 0
                ? null
                : held != null && held.thingIDNumber == selectedPawnId
                    ? held
                    : FindPawnById(FindMap(mapIndex), selectedPawnId);

            if (selected != null)
                _rackQueuePawn.Invoke(rack, new object[] { selected });
            else if (held != null)
                _rackUnloadPawn.Invoke(rack, null);
        }

        private static bool IsFastReturnAllowed(Thing parent, ThingDef def)
        {
            if (_allowsFastReturn == null || parent == null || def == null)
                return false;

            try
            {
                return _allowsFastReturn.Invoke(parent, new object[] { def }) is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private static void SetTransferableCounts(object transferables, List<int> selectedIds)
        {
            if (!(transferables is IEnumerable enumerable))
                return;

            foreach (object transferable in enumerable)
                SetCountToTransfer(transferable, 0);

            foreach (int id in selectedIds)
            {
                foreach (object transferable in enumerable)
                {
                    if (TransferableContainsThingId(transferable, id))
                    {
                        SetCountToTransfer(transferable, 1);
                        break;
                    }
                }
            }
        }

        private static void SetCountToTransfer(object transferable, int value)
        {
            if (transferable == null)
                return;

            PropertyInfo property = AccessTools.Property(transferable.GetType(), "CountToTransfer");
            try
            {
                property?.SetValue(transferable, value, null);
            }
            catch
            {
                // Ignore a stale transferable whose thing was destroyed.
            }
        }

        private static bool TransferableContainsThingId(object transferable, int thingId)
        {
            if (transferable == null)
                return false;

            FieldInfo things = AccessTools.Field(transferable.GetType(), "things");
            if (things?.GetValue(transferable) is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item is Thing thing && thing.thingIDNumber == thingId)
                        return true;
                }
            }

            return false;
        }

        private static Map FindMap(int mapIndex)
        {
            return Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
        }

        private static Thing FindThingById(Map map, int thingId)
        {
            if (map?.listerThings?.AllThings == null)
                return null;

            for (int i = 0; i < map.listerThings.AllThings.Count; i++)
            {
                Thing thing = map.listerThings.AllThings[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;
            }

            return null;
        }

        private static Pawn FindPawnById(Map map, int thingId)
        {
            return FindThingById(map, thingId) as Pawn;
        }
    }
}
