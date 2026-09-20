using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-248: after a reconnect the transport-pod loading window can be
    /// created on only one peer. Multiplayer's DialogLoadTransportersCtorPatch
    /// calls CreateTransporterLoadingSession(Faction.OfPlayer, transporters);
    /// the local player faction can differ in multifaction, and an old session
    /// left on one peer makes that peer reuse it while the other peer creates a
    /// new session and consumes another session unique ID.
    ///
    /// Fix: keep the session faction that Multiplayer passes in. The call
    /// happens inside the synced Command_LoadToTransporter.ProcessInput, so
    /// Faction.OfPlayer there is the issuing player's faction on every peer.
    /// Pinning it to the transport map's parent faction instead hands loading
    /// control to the map owner in multifaction and disables the shuttle
    /// owner's UI when the shuttle stands on another player's map. Before
    /// creating/reusing a session, remove any existing TransporterLoading
    /// session that is invalid, belongs to another faction, or targets a
    /// different transporter set. The session ID is derived deterministically
    /// from map/faction/transporter IDs and does not advance the shared
    /// unique-ID stream, so one-sided create/reuse cannot shift later IDs.
    /// Passenger shuttles use Multiplayer's serialized session ID allocator:
    /// reopening their manifest must not alias the cancelled session's ID.
    /// </summary>
    internal static class Patch_TransporterLoadingSessionMp
    {
        private const string LogTag = "[MP-MeowOnlineShop] TransporterLoadingSessionMp";
        private const string TransporterLoadingTypeName =
            "Multiplayer.Client.TransporterLoading";

        private static Type _mapCompType;
        private static FieldInfo _mapField;
        private static FieldInfo _sessionManagerField;
        private static Type _sessionManagerStaticType;
        private static Type _sessionManagerType;
        private static FieldInfo _allSessionsField;
        private static MethodInfo _removeSessionMethod;
        private static Type _transporterLoadingType;
        private static FieldInfo _transporterLoadingMapField;
        private static FieldInfo _transporterLoadingFactionField;
        private static FieldInfo _transporterLoadingTransferablesField;
        private static FieldInfo _ofPlayerField;
        private static MethodInfo _getTransferableByThingIdTarget;
        private static readonly HashSet<int> RepairedTransferableThingIds =
            new HashSet<int>();
        private static int _pendingTransporterLoadingKey;
        private static int _addSessionNoCheckDepth;
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            _mapCompType = AccessTools.TypeByName(
                "Multiplayer.Client.MultiplayerMapComp");
            MethodInfo target = _mapCompType == null
                ? null
                : AccessTools.Method(
                    _mapCompType,
                    "CreateTransporterLoadingSession",
                    new[]
                    {
                        typeof(Faction),
                        typeof(List<CompTransporter>)
                    });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(CreateSessionPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(CreateSessionFinalizer));
            MethodInfo uniqueIdsPrefix = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(UniqueIdsGetNextIdPrefix));
            MethodInfo uniqueIdsTarget = AccessTools.Method(
                typeof(UniqueIDsManager),
                "GetNextID",
                new[] { typeof(int).MakeByRefType() });
            _sessionManagerStaticType = AccessTools.TypeByName(
                "Multiplayer.Client.Persistent.SessionManager");
            _transporterLoadingType = AccessTools.TypeByName(
                TransporterLoadingTypeName);
            _getTransferableByThingIdTarget = _transporterLoadingType == null
                ? null
                : AccessTools.Method(
                    _transporterLoadingType,
                    "GetTransferableByThingId",
                    new[] { typeof(int) });
            MethodInfo tryAcceptTarget = _transporterLoadingType == null
                ? null
                : AccessTools.Method(
                    _transporterLoadingType,
                    "TryAccept",
                    Type.EmptyTypes);
            MethodInfo tryAcceptPrefix = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(TryAcceptFactionContextPrefix));
            MethodInfo tryAcceptFinalizer = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(TryAcceptFactionContextFinalizer));
            MethodInfo makeLordsTarget = AccessTools.Method(
                typeof(TransporterUtility),
                "MakeLordsAsAppropriate",
                new[]
                {
                    typeof(List<Pawn>),
                    typeof(List<CompTransporter>),
                    typeof(Map)
                });
            MethodInfo makeLordsPrefix = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(NormalizeMakeLordsAsAppropriatePrefix));
            MethodInfo addSessionTarget = _sessionManagerStaticType == null
                ? null
                : AccessTools.Method(
                    _sessionManagerStaticType,
                    "AddSessionNoCheck");
            MethodInfo addSessionPrefix = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(AddSessionNoCheckPrefix));
            MethodInfo addSessionFinalizer = AccessTools.Method(
                typeof(Patch_TransporterLoadingSessionMp),
                nameof(AddSessionNoCheckFinalizer));

            if (target == null || prefix == null || finalizer == null ||
                uniqueIdsTarget == null || uniqueIdsPrefix == null ||
                addSessionTarget == null || addSessionPrefix == null ||
                addSessionFinalizer == null)
            {
                Log.Warning(
                    LogTag + " target resolution failed; transporter loading " +
                    "sessions can still desync.");
                return;
            }

            _mapField = AccessTools.Field(_mapCompType, "map");
            _sessionManagerField = AccessTools.Field(
                _mapCompType,
                "sessionManager");
            _transporterLoadingMapField = _transporterLoadingType == null
                ? null
                : AccessTools.Field(
                    _transporterLoadingType,
                    "map");
            _transporterLoadingFactionField = _transporterLoadingType == null
                ? null
                : AccessTools.Field(
                    _transporterLoadingType,
                    "faction");
            _transporterLoadingTransferablesField =
                _transporterLoadingType == null
                    ? null
                    : AccessTools.Field(
                        _transporterLoadingType,
                        "transferables");
            _ofPlayerField = AccessTools.Field(
                typeof(FactionManager),
                "ofPlayer");

            if (_mapField == null || _sessionManagerField == null)
            {
                Log.Warning(
                    LogTag + " session state fields unresolved; " +
                    "transport loading can still desync.");
                return;
            }

            try
            {
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
                harmony.Patch(
                    uniqueIdsTarget,
                    prefix: new HarmonyMethod(uniqueIdsPrefix)
                    {
                        priority = Priority.First
                    });
                harmony.Patch(
                    addSessionTarget,
                    prefix: new HarmonyMethod(addSessionPrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(addSessionFinalizer)
                    {
                        priority = Priority.Last
                    });
                if (_getTransferableByThingIdTarget != null &&
                    _transporterLoadingMapField != null &&
                    _transporterLoadingTransferablesField != null)
                {
                    MethodInfo getTransferablePrefix = AccessTools.Method(
                        typeof(Patch_TransporterLoadingSessionMp),
                        nameof(GetTransferableByThingIdPrefix));
                    harmony.Patch(
                        _getTransferableByThingIdTarget,
                        prefix: new HarmonyMethod(getTransferablePrefix)
                        {
                            priority = Priority.First
                        });
                }
                else
                {
                    Log.Warning(
                        LogTag + " GetTransferableByThingId repair target " +
                        "or session fields unresolved; missing load items " +
                        "can still desync.");
                }

                if (tryAcceptTarget != null &&
                    tryAcceptPrefix != null &&
                    tryAcceptFinalizer != null &&
                    _transporterLoadingFactionField != null &&
                    _ofPlayerField != null)
                {
                    harmony.Patch(
                        tryAcceptTarget,
                        prefix: new HarmonyMethod(tryAcceptPrefix)
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(tryAcceptFinalizer)
                        {
                            priority = Priority.Last
                        });
                }
                else
                {
                    Log.Warning(
                        LogTag + " TryAccept faction-context target or fields " +
                        "unresolved; multifaction transporter lord creation " +
                        "can still desync.");
                }

                if (makeLordsTarget != null && makeLordsPrefix != null)
                {
                    harmony.Patch(
                        makeLordsTarget,
                        prefix: new HarmonyMethod(makeLordsPrefix)
                        {
                            priority = Priority.First
                        });
                }
                else
                {
                    Log.Warning(
                        LogTag + " MakeLordsAsAppropriate target unresolved; " +
                        "transporter pawn order can still diverge.");
                }
                Log.Message(
                    LogTag + " active: session faction stays with the " +
                    "issuing player; stale sessions are removed; session IDs " +
                    "use native generations for passenger shuttles and " +
                    "deterministic keys for other transporters; TryAccept uses the session faction while " +
                    "creating transport lords; transporter and pawn order " +
                    "and missing references are repaired canonically.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " apply failed: " + e.Message);
            }
        }

        private static void CreateSessionPrefix(
            object __instance,
            ref Faction faction,
            List<CompTransporter> transporters,
            ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Map map = _mapField?.GetValue(__instance) as Map;

            // Keep Multiplayer's faction argument. It is Faction.OfPlayer under
            // the synced ProcessInput command, i.e. the issuing player's
            // faction; the map's parent faction is only the map owner and must
            // not replace it in multifaction.

            object sessionManager = _sessionManagerField?.GetValue(__instance);
            if (sessionManager == null || faction == null ||
                transporters == null)
            {
                return;
            }

            try
            {
                NormalizeTransporterOrder(transporters);
                RemoveMismatchedSessions(
                    sessionManager,
                    faction,
                    transporters);

                // Native ProcessInput constructs this session in a synchronized
                // command. Preserve an existing session's identity, and let
                // SessionManager allocate a fresh serialized ID on each reopen.
                // A map/faction/transporter hash aliases cancelled manifests,
                // allowing a late buffered count/reset to hit the next one.
                if (transporters.Any(comp => comp?.parent is Building_PassengerShuttle))
                    return;

                object existing = FindExistingTransporterLoading(
                    sessionManager,
                    faction,
                    transporters);
                if (existing != null)
                {
                    SetSessionId(existing, ComputeSessionKey(map, faction, transporters));
                    return;
                }

                _pendingTransporterLoadingKey =
                    ComputeSessionKey(map, faction, transporters);
                __state = _pendingTransporterLoadingKey;
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " stale-session cleanup failed: " + e.Message);
                __state = 0;
            }
        }

        private static Exception CreateSessionFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                _pendingTransporterLoadingKey = 0;
            return __exception;
        }

        private sealed class FactionContextState
        {
            internal readonly FactionManager Manager;
            internal readonly Faction Previous;

            internal FactionContextState(
                FactionManager manager,
                Faction previous)
            {
                Manager = manager;
                Previous = previous;
            }
        }

        private static void TryAcceptFactionContextPrefix(
            object __instance,
            ref FactionContextState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer ||
                !MP.IsExecutingSyncCommand ||
                __instance == null ||
                _transporterLoadingFactionField == null ||
                _ofPlayerField == null)
            {
                return;
            }

            try
            {
                FactionManager factionManager = Find.FactionManager;
                Faction sessionFaction =
                    _transporterLoadingFactionField.GetValue(__instance)
                        as Faction;
                if (factionManager == null ||
                    sessionFaction == null ||
                    !sessionFaction.IsPlayer)
                {
                    return;
                }

                Faction previous = _ofPlayerField.GetValue(factionManager)
                    as Faction;
                if (ReferenceEquals(previous, sessionFaction))
                {
                    return;
                }

                _ofPlayerField.SetValue(factionManager, sessionFaction);
                __state = new FactionContextState(
                    factionManager,
                    previous);
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " failed to set TryAccept faction context: " +
                    e.Message);
            }
        }

        private static Exception TryAcceptFactionContextFinalizer(
            Exception __exception,
            FactionContextState __state)
        {
            if (__state == null ||
                __state.Manager == null ||
                _ofPlayerField == null)
            {
                return __exception;
            }

            try
            {
                _ofPlayerField.SetValue(
                    __state.Manager,
                    __state.Previous);
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " failed to restore TryAccept faction context: " +
                    e.Message);
            }

            return __exception;
        }

        private static void NormalizeMakeLordsAsAppropriatePrefix(
            List<Pawn> pawns,
            List<CompTransporter> transporters)
        {
            if (!MP.IsInMultiplayer)
            {
                return;
            }

            NormalizePawnOrder(pawns);
            NormalizeTransporterOrder(transporters);
        }

        private static void NormalizePawnOrder(List<Pawn> pawns)
        {
            if (pawns == null || pawns.Count < 2)
            {
                return;
            }

            pawns.Sort(delegate(Pawn left, Pawn right)
            {
                int leftId = left?.thingIDNumber ?? int.MinValue;
                int rightId = right?.thingIDNumber ?? int.MinValue;
                return leftId.CompareTo(rightId);
            });
        }

        /// <summary>
        /// Multiplayer's transporter session normally resolves a synced
        /// MpTransferableReference only by looking for the referenced
        /// thingID in its local List&lt;TransferableOneWay&gt;. The official
        /// Dialog_LoadTransporters can add a haulable item on one peer before
        /// the other peer has the same local grouping, which leaves the
        /// reference wrapper non-null but its transferable null. The next
        /// CountToTransfer setter then throws in SyncField.Handle.
        ///
        /// Recover the missing entry from the authoritative shared Thing ID
        /// table and use the same vanilla matching routine as the loading
        /// dialog. This keeps the repair at the session-reference boundary;
        /// it does not invent a load count or bypass the synced command.
        /// </summary>
        private static bool GetTransferableByThingIdPrefix(
            object __instance,
            int thingId,
            ref Transferable __result)
        {
            if (!MP.IsInMultiplayer ||
                __instance == null ||
                thingId <= 0)
            {
                return true;
            }

            try
            {
                List<TransferableOneWay> transferables =
                    _transporterLoadingTransferablesField.GetValue(__instance)
                        as List<TransferableOneWay>;
                if (transferables == null)
                    return true;

                for (int i = 0; i < transferables.Count; i++)
                {
                    TransferableOneWay transferable = transferables[i];
                    if (transferable?.things == null)
                        continue;

                    for (int j = 0; j < transferable.things.Count; j++)
                    {
                        if (transferable.things[j]?.thingIDNumber != thingId)
                            continue;

                        __result = transferable;
                        return false;
                    }
                }

                // UI lookups must not add session members on only one peer.
                // Recovery belongs to synced reference deserialization, where
                // every peer handles the same referenced thing ID.
                if (MP.InInterface)
                    return true;

                Thing thing = MP.GetThingById(thingId) as Thing;
                Map map = _transporterLoadingMapField.GetValue(__instance)
                    as Map;
                if (thing == null ||
                    (map != null && thing.MapHeld != map))
                {
                    return true;
                }

                TransferableOneWay repaired =
                    TransferableUtility.TransferableMatchingDesperate(
                        thing,
                        transferables,
                        TransferAsOneMode.PodsOrCaravanPacking);
                if (repaired == null)
                {
                    repaired = new TransferableOneWay();
                    transferables.Add(repaired);
                }

                if (!repaired.things.Contains(thing))
                    repaired.things.Add(thing);

                __result = repaired;
                if (RepairedTransferableThingIds.Add(thingId))
                {
                    Log.Warning(
                        LogTag + " repaired missing transferable for thing " +
                        thingId + " (" + thing.def?.defName + ").");
                }
                return false;
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " missing transferable repair failed for thing " +
                    thingId + ": " + e.Message);
                return true;
            }
        }

        private static void NormalizeTransporterOrder(
            List<CompTransporter> transporters)
        {
            if (transporters == null || transporters.Count < 2)
                return;

            transporters.Sort(delegate(CompTransporter left,
                CompTransporter right)
            {
                int leftId = left?.parent?.thingIDNumber ?? int.MinValue;
                int rightId = right?.parent?.thingIDNumber ?? int.MinValue;
                return leftId.CompareTo(rightId);
            });
        }

        private static bool UniqueIdsGetNextIdPrefix(ref int __result)
        {
            if (!MP.IsInMultiplayer ||
                _pendingTransporterLoadingKey == 0 ||
                _addSessionNoCheckDepth <= 0)
            {
                return true;
            }

            __result = _pendingTransporterLoadingKey;
            return false;
        }

        private static void AddSessionNoCheckPrefix()
        {
            _addSessionNoCheckDepth++;
        }

        private static Exception AddSessionNoCheckFinalizer(
            Exception __exception)
        {
            if (_addSessionNoCheckDepth > 0)
                _addSessionNoCheckDepth--;
            return __exception;
        }

        private static int RemoveMismatchedSessions(
            object sessionManager,
            Faction faction,
            List<CompTransporter> transporters)
        {
            if (_sessionManagerType == null ||
                !ReferenceEquals(
                    _sessionManagerType,
                    sessionManager.GetType()))
            {
                _sessionManagerType = sessionManager.GetType();
                _allSessionsField = AccessTools.Field(
                    _sessionManagerType,
                    "allSessions");
                _removeSessionMethod = _sessionManagerType.GetMethod(
                    "RemoveSession",
                    BindingFlags.Instance | BindingFlags.Public);
            }

            if (_allSessionsField == null || _removeSessionMethod == null)
                return 0;

            IEnumerable allSessions =
                _allSessionsField.GetValue(sessionManager) as IEnumerable;
            if (allSessions == null)
                return 0;

            List<object> stale = new List<object>();
            foreach (object session in allSessions)
            {
                if (session == null ||
                    session.GetType().FullName != TransporterLoadingTypeName)
                {
                    continue;
                }

                if (IsMismatched(session, faction, transporters))
                    stale.Add(session);
            }

            foreach (object session in stale)
            {
                try
                {
                    _removeSessionMethod.Invoke(
                        sessionManager,
                        new[] { session });
                }
                catch (Exception e)
                {
                    Log.Warning(
                        LogTag + " failed to remove stale session: " +
                        e.Message);
                }
            }

            return stale.Count;
        }

        private static object FindExistingTransporterLoading(
            object sessionManager,
            Faction faction,
            List<CompTransporter> transporters)
        {
            if (_sessionManagerType == null ||
                !ReferenceEquals(
                    _sessionManagerType,
                    sessionManager.GetType()))
            {
                _sessionManagerType = sessionManager.GetType();
                _allSessionsField = AccessTools.Field(
                    _sessionManagerType,
                    "allSessions");
            }

            IEnumerable allSessions =
                _allSessionsField?.GetValue(sessionManager) as IEnumerable;
            if (allSessions == null)
                return null;

            foreach (object session in allSessions)
            {
                if (session == null ||
                    session.GetType().FullName != TransporterLoadingTypeName)
                {
                    continue;
                }

                if (!IsMismatched(session, faction, transporters))
                    return session;
            }

            return null;
        }

        private static int ComputeSessionKey(
            Map map,
            Faction faction,
            List<CompTransporter> transporters)
        {
            int key = Gen.HashCombineInt(
                map?.uniqueID ?? 0,
                0x54524C);
            key = Gen.HashCombineInt(key, faction.loadID);
            int[] ids = requestedTransporterIds(transporters);
            for (int i = 0; i < ids.Length; i++)
                key = Gen.HashCombineInt(key, ids[i]);
            return key;
        }

        private static void SetSessionId(object session, int id)
        {
            try
            {
                PropertyInfo property = AccessTools.Property(
                    session.GetType(),
                    "SessionId");
                property?.SetValue(session, id);
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " failed to set deterministic session ID: " +
                    e.Message);
            }
        }

        private static int[] requestedTransporterIds(
            List<CompTransporter> transporters)
        {
            if (transporters == null)
                return Array.Empty<int>();
            return transporters
                .Where(comp => comp != null && comp.parent != null)
                .Select(comp => comp.parent.thingIDNumber)
                .OrderBy(id => id)
                .ToArray();
        }

        private static bool IsMismatched(
            object session,
            Faction faction,
            List<CompTransporter> transporters)
        {
            Type sessionType = session.GetType();

            try
            {
                PropertyInfo validProperty = AccessTools.Property(
                    sessionType,
                    "IsSessionValid");
                if (validProperty != null &&
                    validProperty.GetValue(session) is bool valid &&
                    !valid)
                {
                    return true;
                }
            }
            catch
            {
                // Treat unresolved validity as valid so cleanup stays conservative.
            }

            try
            {
                FieldInfo factionField = AccessTools.Field(
                    sessionType,
                    "faction");
                Faction sessionFaction =
                    factionField?.GetValue(session) as Faction;
                if (!ReferenceEquals(sessionFaction, faction))
                    return true;
            }
            catch
            {
                return true;
            }

            try
            {
                FieldInfo transportersField = AccessTools.Field(
                    sessionType,
                    "transporters");
                IList existing = transportersField?.GetValue(session) as IList;
                if (!SameTransporterSet(existing, transporters))
                    return true;
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool SameTransporterSet(
            IList existing,
            List<CompTransporter> requested)
        {
            if (existing == null || requested == null)
                return false;

            int[] existingIds = TransporterIds(existing);
            int[] requestedIds = requested
                .Where(comp => comp != null && comp.parent != null)
                .Select(comp => comp.parent.thingIDNumber)
                .OrderBy(id => id)
                .ToArray();

            return existingIds.Length == requestedIds.Length &&
                   existingIds.SequenceEqual(requestedIds);
        }

        private static int[] TransporterIds(IList transporters)
        {
            List<int> ids = new List<int>();
            foreach (object comp in transporters)
            {
                if (comp == null)
                    continue;

                object parent = comp.GetType()
                    .GetProperty("parent")?
                    .GetValue(comp);
                if (parent is Thing thing)
                    ids.Add(thing.thingIDNumber);
            }

            ids.Sort();
            return ids.ToArray();
        }
    }
}
