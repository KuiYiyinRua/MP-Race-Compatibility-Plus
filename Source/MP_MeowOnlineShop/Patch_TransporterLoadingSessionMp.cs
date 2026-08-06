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

                Log.Message(
                    LogTag + " active: session faction stays with the " +
                    "issuing player; stale sessions are removed; session IDs " +
                    "are deterministic and do not advance the shared ID stream.");
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
                RemoveMismatchedSessions(
                    sessionManager,
                    faction,
                    transporters);

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
