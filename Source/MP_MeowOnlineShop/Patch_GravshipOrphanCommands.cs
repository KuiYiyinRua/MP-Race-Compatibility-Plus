using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer runs every command through the global AsyncWorldTime queue
    /// (consistentCommandOrder=true), so map-context commands that were queued
    /// before an Odyssey takeoff remain in the world queue after
    /// GravshipUtility.AbandonMap removes the old map. TickPatch.RunCmds then
    /// logs "!!! Tickable of {mapId} not found!" for each command and drops it.
    ///
    /// Desync-215/216 proved that deferring AbandonMap until those commands
    /// drain can leave per-map TickList membership divergent while the old map
    /// is still ticking. The shared takeoff boundary is therefore the correct
    /// place to finish map removal, and stale commands are removed
    /// deterministically:
    ///   1. The synchronized InitiateTakeoff command marks the old map as
    ///      abandoned immediately (unless it retains a grav anchor), so the
    ///      purge window starts at the same shared tick on every peer instead
    ///      of waiting for each peer's local cutscene end.
    ///   2. After vanilla AbandonMap, purge every queued command for that map
    ///      from the world/map queues and from the join data snapshot.
    ///   3. For a bounded window after takeoff, purge any command that still
    ///      references an abandoned map before TickPatch.RunCmds executes it.
    /// Both steps run on every peer with the same replicated queue contents,
    /// so no command can execute on one side only.
    /// </summary>
    internal static class Patch_GravshipOrphanCommands
    {
        private const string TickPatchTypeName = "Multiplayer.Client.TickPatch";
        private const string MultiplayerStaticTypeName = "Multiplayer.Client.Multiplayer";
        private const string MultiplayerSessionTypeName = "Multiplayer.Client.MultiplayerSession";
        private const string GameDataSnapshotTypeName = "Multiplayer.Client.GameDataSnapshot";
        private const string SyncTypeName = "Multiplayer.Client.Sync";
        private const string ScheduledCommandTypeName = "Multiplayer.Common.ScheduledCommand";
        private const string CommandTypeName = "Multiplayer.Common.CommandType";

        private static readonly Type TickPatchType = AccessTools.TypeByName(TickPatchTypeName);
        private static readonly Type MultiplayerStaticType = AccessTools.TypeByName(MultiplayerStaticTypeName);
        private static readonly Type MultiplayerSessionType = AccessTools.TypeByName(MultiplayerSessionTypeName);
        private static readonly Type GameDataSnapshotType = AccessTools.TypeByName(GameDataSnapshotTypeName);
        private static readonly Type SyncType = AccessTools.TypeByName(SyncTypeName);
        private static readonly Type ScheduledCommandType = AccessTools.TypeByName(ScheduledCommandTypeName);
        private static readonly Type CommandType = AccessTools.TypeByName(CommandTypeName);

        private static readonly PropertyInfo TimerProperty =
            AccessTools.Property(TickPatchType, "Timer");
        private static readonly PropertyInfo AllTickablesProperty =
            AccessTools.Property(TickPatchType, "AllTickables");
        private static readonly MethodInfo TickableByIdMethod =
            AccessTools.Method(TickPatchType, "TickableById");
        private static readonly PropertyInfo AsyncWorldTimeProperty =
            AccessTools.Property(MultiplayerStaticType, "AsyncWorldTime");
        private static readonly FieldInfo SessionField =
            AccessTools.Field(MultiplayerStaticType, "session");
        private static readonly FieldInfo DataSnapshotField =
            AccessTools.Field(MultiplayerSessionType, "dataSnapshot");
        private static readonly PropertyInfo MapCmdsProperty =
            AccessTools.Property(GameDataSnapshotType, "MapCmds");
        private static readonly FieldInfo TakeoffMapField =
            AccessTools.Field(typeof(WorldComponent_GravshipController), "map");
        private static readonly FieldInfo MapHasGravAnchorField =
            AccessTools.Field(typeof(WorldComponent_GravshipController), "mapHasGravAnchor");
        private static readonly FieldInfo SyncHandlersField =
            AccessTools.Field(SyncType, "handlers");

        private static readonly FieldInfo CmdTypeField =
            AccessTools.Field(ScheduledCommandType, "type");
        private static readonly FieldInfo CmdTicksField =
            AccessTools.Field(ScheduledCommandType, "ticks");
        private static readonly FieldInfo CmdMapIdField =
            AccessTools.Field(ScheduledCommandType, "mapId");
        private static readonly FieldInfo CmdDataField =
            AccessTools.Field(ScheduledCommandType, "data");

        private static bool _applied;
        private static bool _inTakeoffEnded;
        private static int _pendingTakeoffMapId = -1;
        private static int _abandonWindowUntilTick = int.MinValue;
        private static readonly HashSet<int> AbandonedMapIds =
            new HashSet<int>();
        private static int _drainedMapCount;
        private static int _droppedOrphanCount;
        private static int _traceCountTakeoff;
        private static int _traceCountTravel;
        private static int _traceCountArriveNewMap;
        private static int _traceCountLandingEnded;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo takeoffEnded = AccessTools.Method(
                    typeof(WorldComponent_GravshipController),
                    "TakeoffEnded",
                    Type.EmptyTypes);
                MethodInfo abandonMap = AccessTools.Method(
                    typeof(GravshipUtility),
                    "AbandonMap",
                    new[] { typeof(Map) });
                MethodInfo runCmds = AccessTools.Method(
                    TickPatchType,
                    "RunCmds");
                MethodInfo initiateTakeoff = AccessTools.Method(
                    typeof(WorldComponent_GravshipController),
                    "InitiateTakeoff",
                    new[] { typeof(Building_GravEngine), typeof(PlanetTile) });
                MethodInfo travelTo = AccessTools.Method(
                    typeof(GravshipUtility),
                    "TravelTo",
                    new[] { typeof(Gravship), typeof(PlanetTile), typeof(PlanetTile) });
                MethodInfo arriveNewMap = AccessTools.Method(
                    typeof(GravshipUtility),
                    "ArriveNewMap",
                    new[] { typeof(Gravship) });
                MethodInfo landingEnded = AccessTools.Method(
                    typeof(WorldComponent_GravshipController),
                    "LandingEnded",
                    Type.EmptyTypes);

                if (takeoffEnded == null || abandonMap == null || runCmds == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Gravship orphan command guard skipped: " +
                        $"takeoff={takeoffEnded != null} abandon={abandonMap != null} " +
                        $"runCmds={runCmds != null}.");
                    return;
                }

                harmony.Patch(
                    takeoffEnded,
                    prefix: new HarmonyMethod(
                        typeof(Patch_GravshipOrphanCommands),
                        nameof(TakeoffEndedPrefix))
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(
                        typeof(Patch_GravshipOrphanCommands),
                        nameof(TakeoffEndedFinalizer))
                    {
                        priority = Priority.Last
                    });

                harmony.Patch(
                    abandonMap,
                    postfix: new HarmonyMethod(
                        typeof(Patch_GravshipOrphanCommands),
                        nameof(AbandonMapPostfix))
                    {
                        priority = Priority.Last
                    });

                harmony.Patch(
                    runCmds,
                    prefix: new HarmonyMethod(
                        typeof(Patch_GravshipOrphanCommands),
                        nameof(RunCmdsPrefix))
                    {
                        priority = Priority.First
                    });

                if (initiateTakeoff != null)
                {
                    harmony.Patch(
                        initiateTakeoff,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipOrphanCommands),
                            nameof(InitiateTakeoffPostfix))
                        {
                            priority = Priority.Last
                        });
                }
                if (travelTo != null)
                {
                    harmony.Patch(
                        travelTo,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipOrphanCommands),
                            nameof(TravelToPostfix))
                        {
                            priority = Priority.Last
                        });
                }
                if (arriveNewMap != null)
                {
                    harmony.Patch(
                        arriveNewMap,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipOrphanCommands),
                            nameof(ArriveNewMapPostfix))
                        {
                            priority = Priority.Last
                        });
                }
                if (landingEnded != null)
                {
                    harmony.Patch(
                        landingEnded,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipOrphanCommands),
                            nameof(LandingEndedPostfix))
                        {
                            priority = Priority.Last
                        });
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Gravship orphan command guard active: " +
                    "abandoned-map commands are drained at the shared takeoff " +
                    "boundary and TickLists are rebuilt deterministically.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship orphan command guard install failed: " +
                    e);
            }
        }

        private static void TakeoffEndedPrefix(
            WorldComponent_GravshipController __instance)
        {
            _inTakeoffEnded = true;
            _pendingTakeoffMapId = (TakeoffMapField?.GetValue(__instance) as Map)
                ?.uniqueID ?? -1;
            int timer = ReadTimer();
            if (timer >= 0)
                _abandonWindowUntilTick = Math.Max(
                    _abandonWindowUntilTick,
                    timer + 5000);
        }

        private static Exception TakeoffEndedFinalizer(Exception __exception)
        {
            try
            {
                if (MP.IsInMultiplayer && __exception == null)
                {
                    int rebuilt = Patch_DeterministicTickList
                        .RebuildAllAsyncTickListsForStableMapLifecycle(
                            "gravship-takeoff");
                    if (_traceCountTakeoff < 3)
                    {
                        _traceCountTakeoff++;
                        Log.Message(
                            "[MP-MeowOnlineShop] GRAVSHIP_TAKEOFF_TICKLIST_REBUILD " +
                            "maps=" + rebuilt + " tick=" + ReadTimer() + ".");
                    }

                    int mapId = _pendingTakeoffMapId;
                    if (mapId >= 0 && !IsMapPresent(mapId))
                    {
                        MarkAbandoned(mapId);
                        DrainCommandsForMap(mapId, "gravship-takeoff");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship takeoff TickList/command " +
                    "cleanup failed open: " + e.Message);
            }
            finally
            {
                _inTakeoffEnded = false;
                _pendingTakeoffMapId = -1;
            }
            return __exception;
        }

        private static void AbandonMapPostfix(Map map)
        {
            if (!MP.IsInMultiplayer || !_inTakeoffEnded || map == null)
                return;

            MarkAbandoned(map.uniqueID);
            DrainCommandsForMap(map.uniqueID, "gravship-abandon");
        }

        private static void RunCmdsPrefix()
        {
            if (!MP.IsInMultiplayer || AbandonedMapIds.Count == 0)
                return;

            int timer = ReadTimer();
            if (timer > _abandonWindowUntilTick)
            {
                AbandonedMapIds.Clear();
                return;
            }

            try
            {
                var allTickables = AllTickablesProperty?.GetValue(null, null)
                    as IEnumerable;
                if (allTickables == null)
                    return;

                foreach (object tickable in allTickables)
                {
                    if (tickable == null)
                        continue;

                    object cmds = GetCmds(tickable);
                    if (cmds == null)
                        continue;

                    DrainAbandonedHeadCommands(cmds, timer);
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship orphan command cleanup " +
                    "failed: " + e.Message);
            }
        }

        private static void DrainAbandonedHeadCommands(object cmds, int timer)
        {
            int drained = 0;
            while (drained < 256)
            {
                object head = PeekHead(cmds);
                if (head == null || !IsCommandForAnyAbandonedMap(head))
                    return;

                if (_droppedOrphanCount < 3)
                {
                    _droppedOrphanCount++;
                    int mapId = ReadInt(CmdMapIdField, head);
                    Log.Error(
                        "[MP-MeowOnlineShop] Gravship orphaned map command " +
                        $"dropped handler={ResolveHandlerName(head)} map={mapId} " +
                        $"tick={ReadInt(CmdTicksField, head)} timer={timer} " +
                        $"count={_droppedOrphanCount}.");
                }

                DequeueCommand(cmds);
                drained++;
            }
        }

        private static bool IsCommandForAnyAbandonedMap(object command)
        {
            if (command == null)
                return false;

            int mapId = ReadInt(CmdMapIdField, command);
            return AbandonedMapIds.Contains(mapId);
        }

        private static void DequeueCommand(object cmds)
        {
            try
            {
                cmds.GetType()
                    .GetMethod("Dequeue", Type.EmptyTypes)
                    ?.Invoke(cmds, null);
            }
            catch
            {
                // Best-effort; RunCmds will handle the command normally if we
                // cannot remove it here.
            }
        }

        private static void InitiateTakeoffPostfix(
            Building_GravEngine engine,
            PlanetTile targetTile)
        {
            if (!MP.IsInMultiplayer || engine == null)
                return;

            Map map = engine.Map;
            if (map != null && !KeepsMapAfterTakeoff())
            {
                MarkAbandoned(map.uniqueID);
            }

            if (_traceCountTakeoff < 3)
            {
                _traceCountTakeoff++;
                Log.Message(
                    "[MP-MeowOnlineShop] GRAVSHIP_TAKEOFF engine=" +
                    engine.thingIDNumber + " map=" + map?.uniqueID +
                    " from=" + map?.Tile + " to=" + targetTile +
                    " tick=" + ReadTimer() + " maps=[" +
                    MapsSummary() + "]");
            }
        }

        private static bool KeepsMapAfterTakeoff()
        {
            try
            {
                WorldComponent_GravshipController controller =
                    Find.GravshipController;
                if (controller == null)
                    return false;
                return MapHasGravAnchorField != null &&
                       (bool)(MapHasGravAnchorField.GetValue(controller) ?? false);
            }
            catch
            {
                return false;
            }
        }

        private static void TravelToPostfix(
            Gravship gravship,
            PlanetTile oldTile,
            PlanetTile newTile)
        {
            if (!MP.IsInMultiplayer || gravship == null ||
                _traceCountTravel >= 3)
                return;

            _traceCountTravel++;
            Log.Message(
                "[MP-MeowOnlineShop] GRAVSHIP_TRAVEL id=" + gravship.ID +
                " from=" + oldTile + " to=" + newTile +
                " tick=" + ReadTimer());
        }

        private static void ArriveNewMapPostfix(Gravship gravship)
        {
            if (!MP.IsInMultiplayer || gravship == null ||
                _traceCountArriveNewMap >= 3)
                return;

            _traceCountArriveNewMap++;
            Log.Message(
                "[MP-MeowOnlineShop] GRAVSHIP_ARRIVE_NEW_MAP id=" +
                gravship.ID + " dest=" + gravship.destinationTile +
                " tick=" + ReadTimer() + " mapsBefore=[" +
                MapsSummary() + "]");
        }

        private static void LandingEndedPostfix()
        {
            if (MP.IsInMultiplayer)
            {
                int timer = ReadTimer();
                if (timer >= 0)
                    _abandonWindowUntilTick = Math.Max(
                        _abandonWindowUntilTick,
                        timer + 2000);
            }

            if (!MP.IsInMultiplayer || _traceCountLandingEnded >= 3)
                return;

            _traceCountLandingEnded++;
            Log.Message(
                "[MP-MeowOnlineShop] GRAVSHIP_LANDING_ENDED tick=" +
                ReadTimer() + " maps=[" + MapsSummary() + "]");
        }

        private static void MarkAbandoned(int mapId)
        {
            if (mapId < 0)
                return;

            AbandonedMapIds.Add(mapId);
            int timer = ReadTimer();
            if (timer >= 0)
                _abandonWindowUntilTick = Math.Max(
                    _abandonWindowUntilTick,
                    timer + 5000);
        }

        private static void DrainCommandsForMap(int mapId, string reason)
        {
            if (mapId < 0)
                return;

            int removed = 0;
            var allTickables = AllTickablesProperty?.GetValue(null, null)
                as IEnumerable;
            if (allTickables != null)
            {
                foreach (object tickable in allTickables)
                {
                    if (tickable == null)
                        continue;

                    object cmds = GetCmds(tickable);
                    if (cmds != null)
                        removed += RemoveCommandsForQueue(cmds, mapId);
                }
            }

            RemoveCommandsFromDataSnapshot(mapId);

            if (removed > 0 && _drainedMapCount < 5)
            {
                _drainedMapCount++;
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship drained orphaned commands " +
                    $"after {reason}: map={mapId} removed={removed}.");
            }
        }

        private static bool IsMapPresent(int mapId)
        {
            if (mapId < 0)
                return false;

            foreach (Map map in Find.Maps)
            {
                if (map != null && map.uniqueID == mapId)
                    return true;
            }

            return false;
        }

        private static void RemoveCommandsFromDataSnapshot(int mapId)
        {
            try
            {
                object session = SessionField?.GetValue(null);
                if (session == null)
                    return;

                object snapshot = DataSnapshotField?.GetValue(session);
                if (snapshot == null)
                    return;

                var mapCmds = MapCmdsProperty?.GetValue(snapshot) as IDictionary;
                if (mapCmds != null)
                    mapCmds.Remove(mapId);
            }
            catch
            {
                // Snapshot cleanup is best-effort; the live queue cleanup is
                // the part that prevents TickPatch.RunCmds from dropping work.
            }
        }

        private static string MapsSummary()
        {
            try
            {
                if (Find.Maps == null)
                    return string.Empty;

                string[] ids = new string[Find.Maps.Count];
                for (int i = 0; i < Find.Maps.Count; i++)
                    ids[i] = Find.Maps[i]?.uniqueID.ToString() ?? "<null>";
                return string.Join(",", ids);
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static object PeekHead(object cmds)
        {
            if (cmds == null)
                return null;

            try
            {
                PropertyInfo count = cmds.GetType().GetProperty("Count");
                if (count == null || (int)count.GetValue(cmds, null) == 0)
                    return null;

                return cmds.GetType()
                    .GetMethod("Peek", Type.EmptyTypes)
                    ?.Invoke(cmds, null);
            }
            catch
            {
                return null;
            }
        }

        private static int RemoveCommandsForQueue(object cmds, int mapId)
        {
            if (cmds == null)
                return 0;

            try
            {
                PropertyInfo count = cmds.GetType().GetProperty("Count");
                MethodInfo dequeue = cmds.GetType()
                    .GetMethod("Dequeue", Type.EmptyTypes);
                MethodInfo enqueue = cmds.GetType()
                    .GetMethod("Enqueue", new[] { cmds.GetType().GetGenericArguments()[0] });
                if (count == null || dequeue == null || enqueue == null)
                    return 0;

                int total = (int)count.GetValue(cmds, null);
                if (total <= 0)
                    return 0;

                var kept = new List<object>(total);
                int removed = 0;
                for (int i = 0; i < total; i++)
                {
                    object command = dequeue.Invoke(cmds, null);
                    if (ReadInt(CmdMapIdField, command) == mapId)
                        removed++;
                    else
                        kept.Add(command);
                }

                for (int i = 0; i < kept.Count; i++)
                    enqueue.Invoke(cmds, new[] { kept[i] });

                return removed;
            }
            catch
            {
                return 0;
            }
        }

        private static object GetCmds(object tickable)
        {
            if (tickable == null)
                return null;

            try
            {
                return tickable.GetType()
                    .GetProperty("Cmds")
                    ?.GetValue(tickable, null);
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveHandlerName(object command)
        {
            try
            {
                byte[] data = CmdDataField?.GetValue(command) as byte[];
                if (data == null || data.Length < 4 || SyncHandlersField == null)
                    return "noData";

                int id = BitConverter.ToInt32(data, 0);
                var handlers = SyncHandlersField.GetValue(null) as IList;
                if (handlers != null && id >= 0 && id < handlers.Count)
                    return handlers[id]?.ToString() ?? $"hash={id}";
                return $"hash={id}";
            }
            catch (Exception e)
            {
                return "resolveFailed:" + e.Message;
            }
        }

        private static int ReadInt(FieldInfo field, object instance)
        {
            try
            {
                return (int)(field?.GetValue(instance) ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        private static int ReadTimer()
        {
            try
            {
                return TimerProperty == null
                    ? -1
                    : (int)TimerProperty.GetValue(null, null);
            }
            catch
            {
                return -1;
            }
        }
    }
}
