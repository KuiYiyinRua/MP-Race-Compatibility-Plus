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
    /// Multiplayer keeps every map-context Sync command in its global command
    /// queue (consistentCommandOrder=true). During the Odyssey takeoff the
    /// cutscene freezes the shared timer; when it resumes, commands queued at
    /// the freeze tick may become due only after TakeoffEnded has abandoned
    /// the old map. TickPatch.RunCmds then logs
    /// "!!! Tickable of {mapId} not found!" and silently drops the command,
    /// leaving the issuing side out of step with the peer that executed it
    /// (Desync-108/109: gravship takeoff then "Wrong random state").
    ///
    /// The fix defers AbandonMap by at most one tick when due commands for
    /// that map are still queued, so the pending command executes while the
    /// old map still exists. The same check runs on every peer, so the extra
    /// tick is deterministic.
    /// </summary>
    internal static class Patch_GravshipAbandonQueue
    {
        private const string TickPatchTypeName = "Multiplayer.Client.TickPatch";
        private const string MultiplayerStaticTypeName = "Multiplayer.Client.Multiplayer";
        private const string SyncTypeName = "Multiplayer.Client.Sync";
        private const string ScheduledCommandTypeName = "Multiplayer.Common.ScheduledCommand";
        private const string CommandTypeName = "Multiplayer.Common.CommandType";

        private static readonly Type TickPatchType = AccessTools.TypeByName(TickPatchTypeName);
        private static readonly Type MultiplayerStaticType = AccessTools.TypeByName(MultiplayerStaticTypeName);
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
        private static Map _deferredMap;
        private static int _deferredAtTick = -1;
        private static int _diagnosticWindowUntilTick = int.MinValue;
        private static int _orphanDiagnosticCount;
        private static int _drainedOrphanCount;
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
                        "[MP-MeowOnlineShop] Gravship abandon queue target " +
                        $"resolution failed; patch skipped takeoff={takeoffEnded != null} " +
                        $"abandon={abandonMap != null} runCmds={runCmds != null}.");
                    return;
                }

                harmony.Patch(
                    takeoffEnded,
                    prefix: new HarmonyMethod(
                        typeof(Patch_GravshipAbandonQueue),
                        nameof(TakeoffEndedPrefix))
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(
                        typeof(Patch_GravshipAbandonQueue),
                        nameof(TakeoffEndedFinalizer))
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(
                    abandonMap,
                    prefix: new HarmonyMethod(
                        typeof(Patch_GravshipAbandonQueue),
                        nameof(AbandonMapPrefix))
                    {
                        priority = Priority.First
                    });
                harmony.Patch(
                    runCmds,
                    prefix: new HarmonyMethod(
                        typeof(Patch_GravshipAbandonQueue),
                        nameof(RunCmdsPrefix))
                    {
                        priority = Priority.First
                    },
                    postfix: new HarmonyMethod(
                        typeof(Patch_GravshipAbandonQueue),
                        nameof(RunCmdsPostfix))
                    {
                        priority = Priority.Last
                    });

                if (initiateTakeoff != null)
                {
                    harmony.Patch(
                        initiateTakeoff,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipAbandonQueue),
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
                            typeof(Patch_GravshipAbandonQueue),
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
                            typeof(Patch_GravshipAbandonQueue),
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
                            typeof(Patch_GravshipAbandonQueue),
                            nameof(LandingEndedPostfix))
                        {
                            priority = Priority.Last
                        });
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Gravship abandon queue guard active: " +
                    "old-map commands are drained before the takeoff map is abandoned.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship abandon queue guard install failed: " +
                    e);
            }
        }

        private static void TakeoffEndedPrefix()
        {
            _inTakeoffEnded = true;
            int timer = ReadTimer();
            if (timer >= 0)
                _diagnosticWindowUntilTick = timer + 2000;
        }

        private static Exception TakeoffEndedFinalizer(Exception __exception)
        {
            _inTakeoffEnded = false;
            return __exception;
        }

        private static void InitiateTakeoffPostfix(
            Building_GravEngine engine,
            PlanetTile targetTile)
        {
            if (!MP.IsInMultiplayer || engine == null ||
                _traceCountTakeoff >= 3)
                return;

            _traceCountTakeoff++;
            Log.Message(
                "[MP-MeowOnlineShop] GRAVSHIP_TAKEOFF engine=" +
                engine.thingIDNumber + " map=" + engine.Map?.uniqueID +
                " from=" + engine.Map?.Tile + " to=" + targetTile +
                " tick=" + ReadTimer() + " maps=[" +
                MapsSummary() + "]");
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
            if (!MP.IsInMultiplayer || _traceCountLandingEnded >= 3)
                return;

            _traceCountLandingEnded++;
            Log.Message(
                "[MP-MeowOnlineShop] GRAVSHIP_LANDING_ENDED tick=" +
                ReadTimer() + " maps=[" + MapsSummary() + "]");
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

        private static bool AbandonMapPrefix(Map map)
        {
            if (!_inTakeoffEnded || map == null || !MP.IsInMultiplayer)
                return true;

            if (!HasPendingOrStaleCommandFor(map))
                return true;

            if (_deferredMap != null && _deferredMap != map)
            {
                // A second takeoff before the first deferred map drained is not
                // expected; abandon the older map immediately to avoid leaking
                // maps and log once so the next bundle can be triaged.
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship abandon queue saw a second " +
                    $"takeoff before deferred map={_deferredMap.uniqueID} drained; " +
                    $"new map={map.uniqueID}.");
                return true;
            }

            _deferredMap = map;
            _deferredAtTick = ReadTimer();
            Log.Message(
                "[MP-MeowOnlineShop] Gravship abandon deferred one tick while " +
                $"old-map sync commands drain: map={map.uniqueID} " +
                $"tick={_deferredAtTick}.");
            return false;
        }

        private static void RunCmdsPrefix()
        {
            if (!MP.IsInMultiplayer)
                return;

            int timer = ReadTimer();
            if (timer < 0 || timer > _diagnosticWindowUntilTick ||
                _orphanDiagnosticCount >= 3)
                return;

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

                    object cmds = tickable.GetType()
                        .GetProperty("Cmds")
                        ?.GetValue(tickable, null);
                    object head = PeekHead(cmds);
                    if (head == null)
                        continue;

                    if (ReadInt(CmdTicksField, head) != timer ||
                        !IsSyncCommand(head))
                    {
                        continue;
                    }

                    int mapId = ReadInt(CmdMapIdField, head);
                    object target = TickableByIdMethod?.Invoke(
                        null,
                        new object[] { mapId });
                    if (target != null)
                        continue;

                    _orphanDiagnosticCount++;
                    Log.Error(
                        "[MP-MeowOnlineShop] Gravship orphaned map Sync command " +
                        $"handler={ResolveHandlerName(head)} map={mapId} " +
                        $"tick={timer} tickableQueue={tickable.GetType().Name} " +
                        $"count={_orphanDiagnosticCount}.");
                    if (_orphanDiagnosticCount >= 3)
                        return;
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship orphan diagnostic probe " +
                    "failed: " + e.Message);
            }
        }

        private static void RunCmdsPostfix()
        {
            if (_deferredMap == null)
                return;

            Map map = _deferredMap;
            int timer = ReadTimer();

            try
            {
                object pending = PeekPendingOrStaleCommandFor(map);
                if (pending != null)
                {
                    int commandTick = ReadInt(CmdTicksField, pending);
                    if (commandTick < timer)
                    {
                        // A command that is already behind the local timer can
                        // never run (TickPatch uses strict equality). Remove
                        // every remaining command for this map deterministically
                        // on both peers, then let the next pass abandon.
                        int removed = RemoveAllCommandsForMap(map);
                        Log.Warning(
                            "[MP-MeowOnlineShop] Gravship drained stale old-map " +
                            $"commands map={map.uniqueID} tick={commandTick} " +
                            $"timer={timer} removed={removed}.");
                    }
                    return;
                }

                _deferredMap = null;
                _deferredAtTick = -1;
                if (map == null || map.Parent == null)
                    return;

                GravshipUtility.AbandonMap(map);
                DrainOrphanedCommands(map.uniqueID);
                Log.Message(
                    "[MP-MeowOnlineShop] Gravship deferred abandon executed: " +
                    $"map={map.uniqueID} tick={timer}.");
            }
            catch (Exception e)
            {
                _deferredMap = null;
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship deferred abandon failed: " +
                    e.Message);
            }
        }

        private static bool HasPendingOrStaleCommandFor(Map map)
        {
            object head = PeekPendingOrStaleCommandFor(map);
            return head != null;
        }

        private static object PeekPendingOrStaleCommandFor(Map map)
        {
            if (map == null)
                return null;

            object worldCmds = GetCmds(AsyncWorldTimeProperty?.GetValue(null, null));
            object hit = FindAnyCommand(worldCmds, map.uniqueID);
            if (hit != null)
                return hit;

            object mapAsync = GetMapAsyncTime(map);
            object mapCmds = GetCmds(mapAsync);
            return FindAnyCommand(mapCmds, map.uniqueID);
        }

        private static object FindAnyCommand(object cmds, int mapId)
        {
            if (!(cmds is IEnumerable enumerable))
                return null;

            foreach (object command in enumerable)
            {
                if (IsCommandForMap(command, mapId))
                    return command;
            }
            return null;
        }

        private static bool IsCommandForMap(object command, int mapId)
        {
            return command != null &&
                   ReadInt(CmdMapIdField, command) == mapId;
        }

        private static int RemoveAllCommandsForMap(Map map)
        {
            if (map == null)
                return 0;

            int removed = 0;
            object worldCmds = GetCmds(AsyncWorldTimeProperty?.GetValue(null, null));
            if (worldCmds != null)
            {
                removed += RemoveCommandsForQueue(worldCmds, map.uniqueID);
            }

            object mapAsync = GetMapAsyncTime(map);
            object mapCmds = GetCmds(mapAsync);
            if (mapCmds != null)
            {
                removed += RemoveCommandsForQueue(mapCmds, map.uniqueID);
            }

            return removed;
        }

        private static void DrainOrphanedCommands(int mapId)
        {
            int removed = 0;
            object worldCmds = GetCmds(AsyncWorldTimeProperty?.GetValue(null, null));
            if (worldCmds != null)
                removed += RemoveCommandsForQueue(worldCmds, mapId);

            if (removed <= 0 || _drainedOrphanCount >= 3)
                return;

            _drainedOrphanCount++;
            Log.Warning(
                "[MP-MeowOnlineShop] Drained orphaned commands after map " +
                $"abandonment: map={mapId} removed={removed}.");
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
                    if (IsCommandForMap(command, mapId))
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

        private static object GetMapAsyncTime(Map map)
        {
            try
            {
                MethodInfo asyncTime = AccessTools.Method(
                    AccessTools.TypeByName("Multiplayer.Client.Extensions"),
                    "AsyncTime",
                    new[] { typeof(Map) });
                return asyncTime?.Invoke(null, new object[] { map });
            }
            catch
            {
                return null;
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

        private static bool IsSyncCommand(object command)
        {
            if (command == null || CmdTypeField == null || CommandType == null)
                return false;
            try
            {
                object value = CmdTypeField.GetValue(command);
                return value != null &&
                       string.Equals(value.ToString(), "Sync", StringComparison.Ordinal);
            }
            catch
            {
                return false;
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
