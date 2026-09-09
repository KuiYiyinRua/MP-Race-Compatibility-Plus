using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Hardens the caravan reform/leave path against Multiplayer +
    /// Vehicle Framework state leaks seen in the 2026-08-12 log:
    /// 1. A map can be deinitialized while its AsyncTimeComp is still mid-tick,
    ///    so MapPostTick and the later manager updates throw inside
    ///    TempTerrainManager.Tick.
    /// 2. Vehicle Framework's DetachedMapComponentCache can be missing a
    ///    VehiclePositionManager for a freshly generated site map, which makes
    ///    ColonistBar.CheckRecacheEntries throw KeyNotFoundException. The
    ///    recovery path must not mutate the bar's lists while Multiplayer's
    ///    time-control UI is enumerating them, and restores the last coherent
    ///    cache if a non-enumerated recache fails after vanilla clears it.
    /// 3. SmashTools clears the detached VehiclePositionManager during
    ///    MapDeiniter.Deinit, before the map leaves Find.Maps; the GetComponent
    ///    repair re-creates it on demand so UI/alerts do not throw for the
    ///    remaining removal frames.
    ///
    /// These patches are local safety/repair only and add no sync commands.
    /// </summary>
    internal static class Patch_CaravanMapRemovalSafety
    {
        private sealed class RemovingMapMarker
        {
        }

        // Keep the marker attached to the Map object rather than only to its
        // uniqueID. A removed map can remain reachable from a third-party
        // cache for a few frames, while a future map must never inherit the
        // old map's removal state.
        private static readonly ConditionalWeakTable<Map, RemovingMapMarker> _removingMaps =
            new ConditionalWeakTable<Map, RemovingMapMarker>();
        private static readonly Dictionary<int, int> _removalExpiryByMapId =
            new Dictionary<int, int>();
        // DeinitAndRemoveMap can run while Multiplayer is enumerating its
        // ITickable/map-comp lists. Defer list mutation until TickPatch has
        // finished its current update.
        private static readonly List<Map> _pendingRemovedMaps = new List<Map>();
        // The removal boundary must be based on the shared simulation clock.
        // A DateTime-based grace period can expire on one peer before another
        // peer and turn an otherwise local safety guard into a desync source.
        private const int RemovalScanGraceTicks = 2;
        // ColonistBarTimeControl draws bar.Entries from the ColonistBarOnGUI
        // prefix/postfix.  The actual map-removal command can run between
        // those two callbacks, so using the shared game tick as a UI guard is
        // insufficient: one Unity frame can contain both the removal and the
        // enumeration.  This is presentation-only state and deliberately uses
        // a local frame boundary rather than simulation time.
        private const int ColonistBarRecacheGraceFrames = 2;
        private static int _suspendColonistBarRecacheUntilFrame = -1;

        private static Type _asyncCompType;
        private static AccessTools.FieldRef<TempTerrainManager, Map> _tempTerrainMapRef;
        private static FieldInfo _asyncMapField;
        private static FieldInfo _asyncTickingMapField;
        private static FieldInfo _asyncExecutingCmdMapField;
        private static Type _extensionsType;
        private static MethodInfo _asyncTimeMethod;
        private static Type _multiplayerType;
        private static PropertyInfo _multiplayerGameProperty;
        private static Type _colonistBarTimeControlType;
        private static int _drawButtonsDepth;

        private static Type _vehiclePositionManagerType;
        private static Type _vehicleCacheType;
        private static FieldInfo _vehicleCacheMapCompsField;
        private static IDictionary _vehicleCacheMapComps;
        private static MethodInfo _vehicleCacheAddMethod;

        private static FieldInfo _colonistEntriesDirtyField;
        private static FieldInfo _colonistCachedEntriesField;
        private static FieldInfo _colonistCachedDrawLocsField;
        private static FieldInfo _colonistCachedGroupsField;
        private static FieldInfo _colonistCachedScaleField;
        private static bool _loggedMapGuard;
        private static bool _loggedVehicleCacheRepair;
        private static bool _loggedUiRecovery;
        private static bool _loggedMissingAsyncTimeGuard;
        private static bool _loggedRemovalBoundary;
        private static bool _loggedStaleStoryteller;
        private static bool _loggedAsyncTickSkip;
        private static bool _loggedDrawButtonsGuard;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            ResolveSymbols();

            // This is an optional Vehicle Framework compatibility layer.  A
            // Harmony wrapper rebuild can fail when VF changes one of its own
            // patches; never let that prevent unrelated Ideology/ritual and
            // Multiplayer compatibility patches from being registered.
            InstallSafely("Vehicle detached map component repair",
                () => InstallVehicleDetachedCacheRepair(harmony));
            InstallSafely("map-removal lifecycle guards",
                () => InstallMapRemovalLifecycleGuards(harmony));
            InstallSafely("stale-map async tick guards",
                () => InstallStaleMapTickGuards(harmony));
            InstallSafely("ColonistBar cache recovery",
                () => InstallColonistBarRecovery(harmony));
            InstallSafely("ColonistBar time-control enumeration guard",
                () => InstallColonistBarEnumerationGuard(harmony));
            InstallSafely("missing AsyncTime UI guards",
                () => InstallMissingAsyncTimeGuards(harmony));
        }

        private static void InstallSafely(string feature, Action install)
        {
            try
            {
                install();
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan map-removal safety subpatch failed; " +
                    "continuing startup: " + feature + ". " + e.Message);
            }
        }

        private static void ResolveSymbols()
        {
            _asyncCompType = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
            _asyncMapField = AccessTools.Field(_asyncCompType, "map");
            _asyncTickingMapField = AccessTools.Field(_asyncCompType, "tickingMap");
            _asyncExecutingCmdMapField = AccessTools.Field(_asyncCompType, "executingCmdMap");
            _extensionsType = AccessTools.TypeByName("Multiplayer.Client.Extensions");
            _asyncTimeMethod = AccessTools.Method(
                _extensionsType, "AsyncTime", new[] { typeof(Map) });
            _multiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            _multiplayerGameProperty = AccessTools.Property(
                _multiplayerType, "game");
            _colonistBarTimeControlType = AccessTools.TypeByName(
                "Multiplayer.Client.AsyncTime.ColonistBarTimeControl");
            try
            {
                _tempTerrainMapRef =
                    AccessTools.FieldRefAccess<TempTerrainManager, Map>("map");
            }
            catch
            {
                _tempTerrainMapRef = null;
            }

            _vehiclePositionManagerType = AccessTools.TypeByName("Vehicles.VehiclePositionManager");
            Type detachedCacheGeneric = AccessTools.TypeByName("SmashTools.DetachedMapComponentCache`1");
            if (_vehiclePositionManagerType != null && detachedCacheGeneric != null)
            {
                _vehicleCacheType = detachedCacheGeneric.MakeGenericType(_vehiclePositionManagerType);
                _vehicleCacheMapCompsField = AccessTools.Field(_vehicleCacheType, "MapComps");
                _vehicleCacheAddMethod = AccessTools.Method(
                    _vehicleCacheType,
                    "AddComponent",
                    new[] { typeof(Map) });
            }

            _colonistEntriesDirtyField = AccessTools.Field(typeof(ColonistBar), "entriesDirty");
            _colonistCachedEntriesField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedEntries");
            _colonistCachedDrawLocsField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedDrawLocs");
            _colonistCachedGroupsField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedReorderableGroups");
            _colonistCachedScaleField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedScale");
        }

        private static void InstallMapRemovalLifecycleGuards(Harmony harmony)
        {
            int patched = 0;

            MethodInfo deinitAndRemoveMap = AccessTools.Method(
                typeof(Game),
                "DeinitAndRemoveMap",
                new[] { typeof(Map), typeof(bool) });
            MethodInfo removalPrefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(DeinitAndRemoveMapPrefix));
            MethodInfo removalPostfix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(DeinitAndRemoveMapPostfix));
            if (deinitAndRemoveMap != null && removalPrefix != null)
            {
                harmony.Patch(
                    deinitAndRemoveMap,
                    prefix: new HarmonyMethod(removalPrefix)
                    {
                        priority = Priority.First
                    },
                    postfix: removalPostfix == null ? null : new HarmonyMethod(removalPostfix)
                    {
                        priority = Priority.Last
                    });
                patched++;
            }

            if (_asyncCompType != null)
            {
                MethodInfo asyncTick = AccessTools.Method(_asyncCompType, "Tick");
                MethodInfo asyncTickPrefix = AccessTools.Method(
                    typeof(Patch_CaravanMapRemovalSafety), nameof(AsyncTimeTickPrefix));
                MethodInfo asyncTickFinalizer = AccessTools.Method(
                    typeof(Patch_CaravanMapRemovalSafety), nameof(AsyncTimeTickFinalizer));
                if (asyncTick != null && asyncTickPrefix != null && asyncTickFinalizer != null)
                {
                    harmony.Patch(
                        asyncTick,
                        prefix: new HarmonyMethod(asyncTickPrefix)
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(asyncTickFinalizer)
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }
            }

            PatchStaleStorytellerEntry(harmony, typeof(Storyteller), "StorytellerTick");
            PatchStaleStorytellerEntry(harmony, typeof(StoryWatcher), "StoryWatcherTick");

            Log.Message(
                "[MP-MeowOnlineShop] Map removal lifecycle guards active: " +
                $"{patched} targets; removed maps cannot re-enter AsyncTime, Storyteller, " +
                "or stale Multiplayer per-map registries.");
        }

        private static void PatchStaleStorytellerEntry(
            Harmony harmony,
            Type owner,
            string methodName)
        {
            MethodInfo target = AccessTools.Method(owner, methodName);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(StaleStorytellerPrefix));
            if (target == null || prefix == null)
                return;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });
        }

        private static bool DeinitAndRemoveMapPrefix(Map map)
        {
            MarkMapRemoving(map);
            return true;
        }

        private static void DeinitAndRemoveMapPostfix(Map map)
        {
            if (map == null)
                return;

            // Game.DeinitAndRemoveMap removes the map from Find.Maps, but
            // Multiplayer's per-map registries are maintained separately.
            // Leaving either entry alive makes Extensions.AsyncTime(map),
            // TickPatch and save-time map-component traversal retain a dead
            // Map object.  That is the source of the Map_XX "not deep-saved"
            // and "Could not resolve reference" errors seen after gravship
            // takeoff.
            QueueMapCleanup(map);
            ClearStaleAsyncContext(map);
            ClearVehiclePositionManager(map);
        }

        private static void QueueMapCleanup(Map removedMap)
        {
            try
            {
                if (!_pendingRemovedMaps.Contains(removedMap))
                    _pendingRemovedMaps.Add(removedMap);
            }
            catch
            {
                // Best-effort queue only; the removal boundary remains active.
            }
        }

        private static void FlushPendingMapCleanup()
        {
            if (_pendingRemovedMaps.Count == 0)
                return;

            Map[] pending = _pendingRemovedMaps.ToArray();
            _pendingRemovedMaps.Clear();
            foreach (Map removedMap in pending)
            {
                if (removedMap == null)
                    continue;

                RemoveMapFromMultiplayerRegistries(removedMap);
                ClearStaleAsyncContext(removedMap);
            }
        }

        private static void RemoveMapFromMultiplayerRegistries(Map removedMap)
        {
            if (!MP.IsInMultiplayer || _multiplayerGameProperty == null)
                return;

            try
            {
                object multiplayerGame = _multiplayerGameProperty.GetValue(null, null);
                if (multiplayerGame == null)
                    return;

                int removedMapComps = RemoveMapEntries(multiplayerGame, "mapComps", removedMap);
                int removedAsyncComps = RemoveMapEntries(
                    multiplayerGame, "asyncTimeComps", removedMap);

                if (removedMapComps != 0 || removedAsyncComps != 0)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Removed dead map from Multiplayer registries: " +
                        "map=" + removedMap.uniqueID + ", mapComps=" + removedMapComps +
                        ", asyncTimeComps=" + removedAsyncComps + ".");
                }
            }
            catch
            {
                // Cleanup is best-effort and must never turn successful map
                // removal into a second exception.
            }
        }

        private static int RemoveMapEntries(object multiplayerGame, string fieldName, Map removedMap)
        {
            FieldInfo field = AccessTools.Field(multiplayerGame.GetType(), fieldName);
            if (field == null || !(field.GetValue(multiplayerGame) is IList entries))
                return 0;

            int removed = 0;
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                object entry = entries[index];
                if (GetEntryMap(entry) != removedMap)
                    continue;

                entries.RemoveAt(index);
                removed++;
            }

            return removed;
        }

        private static Map GetEntryMap(object entry)
        {
            if (entry == null)
                return null;

            try
            {
                FieldInfo field = AccessTools.Field(entry.GetType(), "map");
                if (field != null)
                    return field.GetValue(entry) as Map;

                PropertyInfo property = AccessTools.Property(entry.GetType(), "map");
                return property?.GetValue(entry, null) as Map;
            }
            catch
            {
                return null;
            }
        }

        private static bool AsyncTimeTickPrefix(object __instance)
        {
            Map map = GetAsyncMap(__instance);
            if (!IsMapRemoving(map))
                return true;

            if (!_loggedAsyncTickSkip)
            {
                _loggedAsyncTickSkip = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Prevented AsyncTimeComp.Tick from entering " +
                    "PreContext for a map already being removed: map=" +
                    (map?.uniqueID.ToString() ?? "null") + ".");
            }

            ClearStaleAsyncContext(map);
            return false;
        }

        private static Exception AsyncTimeTickFinalizer(
            Exception __exception,
            object __instance)
        {
            Map map = GetAsyncMap(__instance);
            if (!IsMapRemoving(map))
                return __exception;

            // Deinit can be requested by the reform-caravan command while an
            // async tick is unwinding. If a third-party MapPostTick/manager
            // throws after the removal boundary, do not leave
            // AsyncTimeComp.tickingMap pointing at the dead map.
            ClearStaleAsyncContext(map);
            return __exception == null ? null : null;
        }

        private static bool StaleStorytellerPrefix()
        {
            Map context = GetAsyncStaticMap(_asyncTickingMapField) ??
                          GetAsyncStaticMap(_asyncExecutingCmdMapField);
            if (!IsMapRemoving(context))
                return true;

            if (!_loggedStaleStoryteller)
            {
                _loggedStaleStoryteller = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Suppressed Storyteller/StoryWatcher " +
                    "execution with a removed map context: map=" + context.uniqueID + ".");
            }

            if (context != null)
                ClearStaleAsyncContext(context);
            return false;
        }

        private static void MarkMapRemoving(Map map)
        {
            if (map == null)
                return;

            try
            {
                _removingMaps.GetValue(map, _ => new RemovingMapMarker());
                _removalExpiryByMapId[map.uniqueID] =
                    CurrentGameTick() + RemovalScanGraceTicks;
                _suspendColonistBarRecacheUntilFrame = Math.Max(
                    _suspendColonistBarRecacheUntilFrame,
                    UnityEngine.Time.frameCount + ColonistBarRecacheGraceFrames);
                if (!_loggedRemovalBoundary)
                {
                    _loggedRemovalBoundary = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Entered map removal boundary: map=" +
                        map.uniqueID + ".");
                }
            }
            catch
            {
                // The guard must never make vanilla map removal fail.
            }
        }

        private static bool IsMapRemoving(Map map)
        {
            if (map == null)
                return false;

            if (map.Disposed)
                return true;

            try
            {
                return _removingMaps.TryGetValue(map, out _);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasMapRemovalPending()
        {
            if (_removalExpiryByMapId.Count == 0)
                return false;

            int now = CurrentGameTick();
            foreach (int mapId in _removalExpiryByMapId
                         .Where(pair => pair.Value <= now)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _removalExpiryByMapId.Remove(mapId);
            }

            return _removalExpiryByMapId.Count != 0;
        }

        private static Map GetAsyncMap(object asyncInstance)
        {
            try
            {
                return _asyncMapField?.GetValue(asyncInstance) as Map;
            }
            catch
            {
                return null;
            }
        }

        private static Map GetAsyncStaticMap(FieldInfo field)
        {
            try
            {
                return field?.GetValue(null) as Map;
            }
            catch
            {
                return null;
            }
        }

        private static void ClearStaleAsyncContext(Map removedMap)
        {
            try
            {
                if (_asyncTickingMapField != null &&
                    GetAsyncStaticMap(_asyncTickingMapField) == removedMap)
                {
                    _asyncTickingMapField.SetValue(null, null);
                }
            }
            catch
            {
                // Best-effort cleanup only; never turn a removal guard into a
                // second exception during Unity's update loop. The command
                // context is intentionally not cleared here: it may still be
                // unwinding the shared map-removal command.
            }
        }

        private static void InstallVehicleDetachedCacheRepair(Harmony harmony)
        {
            int patched = 0;

            if (_vehicleCacheType != null &&
                _vehicleCacheAddMethod != null &&
                _vehicleCacheMapCompsField != null)
            {
                MethodInfo getComponent = AccessTools.Method(
                    _vehicleCacheType, "GetComponent", new[] { typeof(Map) });
                MethodInfo getComponentPrefix = AccessTools.Method(
                    typeof(Patch_CaravanMapRemovalSafety),
                    nameof(VehicleGetComponentPrefix));
                if (getComponent != null && getComponentPrefix != null)
                {
                    harmony.Patch(
                        getComponent,
                        prefix: new HarmonyMethod(getComponentPrefix)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                MethodInfo addMap = AccessTools.Method(
                    typeof(Game), "AddMap", new[] { typeof(Map) });
                MethodInfo addMapPostfix = AccessTools.Method(
                    typeof(Patch_CaravanMapRemovalSafety), nameof(AddMapPostfix));
                if (addMap != null && addMapPostfix != null)
                {
                    harmony.Patch(
                        addMap,
                        postfix: new HarmonyMethod(addMapPostfix)
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                MethodInfo finalizeLoading = AccessTools.Method(typeof(Map), "FinalizeLoading");
                MethodInfo finalizeLoadingPostfix = AccessTools.Method(
                    typeof(Patch_CaravanMapRemovalSafety), nameof(FinalizeLoadingPostfix));
                if (finalizeLoading != null && finalizeLoadingPostfix != null)
                {
                    harmony.Patch(
                        finalizeLoading,
                        postfix: new HarmonyMethod(finalizeLoadingPostfix)
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }
            }

            if (patched > 0)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Vehicle detached map component repair active: " +
                    patched + "/3 hooks.");
            }
            else
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Vehicle detached map component repair " +
                    "not required: Vehicle Framework targets not resolved.");
            }
        }

        private static void InstallStaleMapTickGuards(Harmony harmony)
        {
            MethodInfo mapPostTick = AccessTools.Method(typeof(Map), "MapPostTick");
            MethodInfo mapPostTickPrefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(MapPostTickGuardPrefix));
            if (mapPostTick != null && mapPostTickPrefix != null)
            {
                harmony.Patch(
                    mapPostTick,
                    prefix: new HarmonyMethod(mapPostTickPrefix)
                    {
                        priority = Priority.Last
                    });
            }

            if (_asyncCompType == null)
                return;

            PatchAsyncTickGuard(harmony, _asyncCompType, "UpdateManagers");
            PatchAsyncTickGuard(harmony, _asyncCompType, "CacheNothingHappening");
            PatchAsyncTickGuard(harmony, _asyncCompType, "QuestManagerTickAsyncTime");

            Log.Message(
                "[MP-MeowOnlineShop] Stale-map async tick guards active: disposed " +
                "maps cannot run MapPostTick or AsyncTimeComp manager updates.");
        }

        private static void PatchAsyncTickGuard(Harmony harmony, Type owner, string methodName)
        {
            MethodInfo target = AccessTools.Method(owner, methodName);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(AsyncTickMemberGuardPrefix));
            if (target == null || prefix == null)
                return;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.Last
                });
        }

        private static void InstallColonistBarRecovery(Harmony harmony)
        {
            MethodInfo checkRecacheEntries = AccessTools.Method(
                typeof(ColonistBar), "CheckRecacheEntries");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(CheckRecacheEntriesPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(CheckRecacheEntriesFinalizer));
            if (checkRecacheEntries == null || prefix == null || finalizer == null)
                return;

            harmony.Patch(
                checkRecacheEntries,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] ColonistBar cache recovery active: map removal " +
                "recaches are deferred without mutating the active UI list.");
        }

        private static void InstallColonistBarEnumerationGuard(Harmony harmony)
        {
            MethodInfo drawButtons = _colonistBarTimeControlType == null
                ? null
                : AccessTools.Method(_colonistBarTimeControlType, "DrawButtons");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(DrawButtonsPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(DrawButtonsFinalizer));
            if (drawButtons == null || prefix == null || finalizer == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] ColonistBar time-control enumeration " +
                    "guard skipped: Multiplayer.ColonistBarTimeControl.DrawButtons " +
                    "was not resolved.");
                return;
            }

            harmony.Patch(
                drawButtons,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] ColonistBar time-control enumeration guard " +
                "active: re-entrant recaches are deferred while Multiplayer " +
                "iterates bar.Entries.");
        }

        private static bool DrawButtonsPrefix()
        {
            _drawButtonsDepth++;
            return true;
        }

        private static void DrawButtonsFinalizer(Exception __exception)
        {
            if (_drawButtonsDepth > 0)
                _drawButtonsDepth--;
        }

        private static bool IsDrawButtonsActive()
        {
            return _drawButtonsDepth > 0;
        }

        private static void InstallMissingAsyncTimeGuards(Harmony harmony)
        {
            int patched = 0;

            Type tickPatch = AccessTools.TypeByName("Multiplayer.Client.TickPatch");
            MethodInfo tickPatchPostfix = AccessTools.Method(tickPatch, "Postfix");
            MethodInfo tickPatchPrefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(TickPatchPostfixPrefix));
            MethodInfo tickPatchCleanupPostfix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety), nameof(TickPatchCleanupPostfix));
            if (tickPatchPostfix != null && tickPatchPrefix != null &&
                tickPatchCleanupPostfix != null)
            {
                harmony.Patch(
                    tickPatchPostfix,
                    prefix: new HarmonyMethod(tickPatchPrefix)
                    {
                        priority = Priority.Last
                    },
                    postfix: new HarmonyMethod(tickPatchCleanupPostfix)
                    {
                        priority = Priority.Last
                    });
                patched++;
            }

            Type asyncWorldComp = AccessTools.TypeByName(
                "Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
            MethodInfo desiredTimeSpeed = AccessTools.PropertyGetter(
                asyncWorldComp, "DesiredTimeSpeed");
            MethodInfo desiredTimeSpeedPrefix = AccessTools.Method(
                typeof(Patch_CaravanMapRemovalSafety),
                nameof(AsyncWorldTimeDesiredSpeedPrefix));
            if (desiredTimeSpeed != null && desiredTimeSpeedPrefix != null)
            {
                harmony.Patch(
                    desiredTimeSpeed,
                    prefix: new HarmonyMethod(desiredTimeSpeedPrefix)
                    {
                        priority = Priority.Last
                    });
                patched++;
            }

            if (patched > 0)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Missing AsyncTime UI guards active: " +
                    patched + "/2 hooks.");
            }
            else
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Missing AsyncTime UI guards not " +
                    "required: Multiplayer UI targets not resolved.");
            }
        }

        private static bool TickPatchPostfixPrefix()
        {
            if (!(MP.IsInMultiplayer &&
                  Find.CurrentMap != null &&
                  MapAsyncTimeMissing(Find.CurrentMap)))
            {
                return true;
            }

            // The original postfix would dereference the missing async
            // component.  It can be reached in the same UI frame as a map
            // removal, so flush only Multiplayer-owned, presentation-neutral
            // registries before bypassing that dereference.
            FlushPendingMapCleanup();
            return false;
        }

        private static void TickPatchCleanupPostfix()
        {
            // TickPatch.Postfix runs after RunCmds/DoUpdate have finished
            // enumerating Multiplayer's tickables, so list removal here cannot
            // invalidate ColonistBarTimeControl.DrawButtons or TickPatch.AllTickables.
            FlushPendingMapCleanup();
        }

        private static bool AsyncWorldTimeDesiredSpeedPrefix(ref TimeSpeed __result)
        {
            if (!MP.IsInMultiplayer || Find.Maps == null)
                return true;

            foreach (Map map in Find.Maps)
            {
                if (!MapAsyncTimeMissing(map))
                    continue;

                __result = TimeSpeed.Paused;
                LogMissingAsyncTimeGuardOnce(map);
                return false;
            }

            return true;
        }

        private static bool MapAsyncTimeMissing(Map map)
        {
            if (_asyncTimeMethod == null)
                return false;

            try
            {
                return _asyncTimeMethod.Invoke(null, new object[] { map }) == null;
            }
            catch
            {
                return false;
            }
        }

        private static void LogMissingAsyncTimeGuardOnce(Map map)
        {
            if (_loggedMissingAsyncTimeGuard)
                return;
            _loggedMissingAsyncTimeGuard = true;
            Log.Warning(
                "[MP-MeowOnlineShop] Suppressed a Multiplayer UI/update path " +
                "while map=" + (map?.uniqueID.ToString() ?? "null") +
                " has no AsyncTimeComp.");
        }

        private static void AddMapPostfix(Map map)
        {
            EnsureVehiclePositionManager(map);
        }

        private static void FinalizeLoadingPostfix(Map __instance)
        {
            EnsureVehiclePositionManager(__instance);
        }

        private static bool VehicleGetComponentPrefix(Map map)
        {
            if (!MP.IsInMultiplayer)
                return true;

            EnsureVehiclePositionManager(map);
            return true;
        }

        private static int CurrentGameTick()
        {
            try
            {
                return Find.TickManager?.TicksGame ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void EnsureVehiclePositionManager(Map map)
        {
            if (map == null || map.Disposed ||
                _vehicleCacheType == null ||
                _vehicleCacheAddMethod == null ||
                _vehicleCacheMapCompsField == null)
            {
                return;
            }

            try
            {
                if (_vehicleCacheMapComps == null &&
                    _vehicleCacheMapCompsField != null)
                {
                    _vehicleCacheMapComps =
                        _vehicleCacheMapCompsField.GetValue(null) as IDictionary;
                }

                if (_vehicleCacheMapComps == null ||
                    _vehicleCacheMapComps.Contains(map.uniqueID))
                {
                    return;
                }

                if (!IsMapStillActive(map))
                    return;

                _vehicleCacheAddMethod.Invoke(null, new object[] { map });
                if (!_loggedVehicleCacheRepair)
                {
                    _loggedVehicleCacheRepair = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Re-created missing Vehicle " +
                        "Framework detached map component for map=" + map.uniqueID +
                        " after a site-map lifecycle transition.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedVehicleCacheRepair)
                {
                    _loggedVehicleCacheRepair = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Vehicle detached map component " +
                        "repair failed: " + e.Message);
                }
            }
        }

        private static bool IsMapStillActive(Map map)
        {
            if (map == null || map.Disposed)
                return false;

            try
            {
                return Find.Maps != null && Find.Maps.Contains(map);
            }
            catch
            {
                return false;
            }
        }

        private static void ClearVehiclePositionManager(Map map)
        {
            if (map == null || _vehicleCacheMapCompsField == null)
                return;

            try
            {
                IDictionary comps =
                    _vehicleCacheMapCompsField.GetValue(null) as IDictionary;
                comps?.Remove(map.uniqueID);
            }
            catch
            {
                // Best-effort cleanup only; a stale detached component for a
                // dead map is harmless and the GetComponent repair above will
                // not resurrect it once the map leaves Find.Maps.
            }
        }

        private static bool MapPostTickGuardPrefix(Map __instance)
        {
            if (__instance != null &&
                !IsMapRemoving(__instance) &&
                !HasBrokenTempTerrain(__instance))
            {
                return true;
            }

            LogMapGuardOnce(__instance);
            return false;
        }

        private static bool HasBrokenTempTerrain(Map map)
        {
            if (_tempTerrainMapRef == null)
                return false;
            if (map.tempTerrain == null)
                return true;

            try
            {
                return _tempTerrainMapRef(map.tempTerrain) == null;
            }
            catch
            {
                return false;
            }
        }

        private static bool AsyncTickMemberGuardPrefix(object __instance)
        {
            Map map = null;
            try
            {
                map = _asyncMapField?.GetValue(__instance) as Map;
            }
            catch
            {
                // Treat unresolved state as unsafe.
            }

            if (map != null && !IsMapRemoving(map))
                return true;

            LogMapGuardOnce(map);
            return false;
        }

        private static void LogMapGuardOnce(Map map)
        {
            if (_loggedMapGuard)
                return;
            _loggedMapGuard = true;
            Log.Warning(
                "[MP-MeowOnlineShop] Skipped async tick work for a disposed/removed " +
                "map=" + (map?.uniqueID.ToString() ?? "null") + ".");
        }

        private sealed class ColonistBarCacheSnapshot
        {
            internal List<ColonistBar.Entry> Entries;
            internal List<UnityEngine.Vector2> DrawLocs;
            internal List<int> Groups;
            internal float Scale;
        }

        private static bool CheckRecacheEntriesPrefix(
            object __instance,
            ref ColonistBarCacheSnapshot __state)
        {
            __state = null;
            if (__instance == null || !IsColonistBarDirty(__instance))
                return true;

            // Multiplayer's ColonistBarTimeControl.DrawButtons iterates
            // bar.Entries and re-reads it through GroupFrameRect inside the
            // loop. A recache in that window clears cachedEntries and
            // invalidates the active enumerator, which wedges OnGUI. Defer the
            // whole recache for the duration of that presentation-only loop.
            if (IsDrawButtonsActive())
            {
                if (!_loggedDrawButtonsGuard)
                {
                    _loggedDrawButtonsGuard = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Deferred a ColonistBar recache " +
                        "while Multiplayer time-control UI was enumerating Entries.");
                }

                return false;
            }

            // Vanilla clears cachedEntries before Vehicle Framework's
            // transpiler has finished enumerating all maps. If that patched
            // recache throws, the old dirty-flag repair leaves cachedDrawLocs
            // paired with an empty cachedEntries list, so ColonistBarOnGUI
            // either draws nothing or indexes past the list and disappears.
            // Snapshot the last coherent presentation cache before allowing
            // the recache to run; the finalizer restores it only on failure.
            __state = CaptureColonistBarCache(__instance);

            // During Game.DeinitAndRemoveMap, Vehicle Framework can still see
            // the removed map through its detached component cache. Do not
            // rebuild the UI from that transient set; keep the last valid
            // entries until the shared tick boundary has passed.
            if (HasMapRemovalPending() || IsColonistBarRecacheSuspended())
            {
                if (!_loggedUiRecovery)
                {
                    _loggedUiRecovery = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Deferred ColonistBar recache while a " +
                        "map-removal boundary is active; keeping the previous entries.");
                }

                return false;
            }

            return true;
        }

        private static Exception CheckRecacheEntriesFinalizer(
            Exception __exception,
            object __instance,
            ColonistBarCacheSnapshot __state)
        {
            if (__exception == null || __instance == null)
                return __exception;

            RestoreColonistBarCache(__instance, __state);

            // CheckRecacheEntries clears entriesDirty before touching
            // cachedEntries, so a mid-body exception must re-mark the bar
            // dirty or vanilla would never retry. The snapshot restore above
            // is safe here because the enumeration guard prevents this
            // recache path from running while Multiplayer's time-control UI
            // is iterating bar.Entries; the dirty flag still requests a later
            // vanilla retry after the short UI-only guard expires.
            MarkColonistBarDirtyForRetry(__instance);
            if (!IsColonistBarRecacheSuspended())
                return __exception;

            if (!_loggedUiRecovery)
            {
                _loggedUiRecovery = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Suppressed a transient ColonistBar " +
                    "recache exception during map removal; retry is deferred " +
                    "without mutating the active UI list.");
            }

            return null;
        }

        private static ColonistBarCacheSnapshot CaptureColonistBarCache(
            object instance)
        {
            try
            {
                List<ColonistBar.Entry> entries =
                    _colonistCachedEntriesField?.GetValue(instance)
                    as List<ColonistBar.Entry>;
                List<UnityEngine.Vector2> drawLocs =
                    _colonistCachedDrawLocsField?.GetValue(instance)
                    as List<UnityEngine.Vector2>;
                List<int> groups =
                    _colonistCachedGroupsField?.GetValue(instance)
                    as List<int>;
                if (entries == null || drawLocs == null || groups == null)
                    return null;

                return new ColonistBarCacheSnapshot
                {
                    Entries = new List<ColonistBar.Entry>(entries),
                    DrawLocs = new List<UnityEngine.Vector2>(drawLocs),
                    Groups = new List<int>(groups),
                    Scale = _colonistCachedScaleField?.GetValue(instance) is float scale
                        ? scale
                        : 1f
                };
            }
            catch
            {
                return null;
            }
        }

        private static void RestoreColonistBarCache(
            object instance,
            ColonistBarCacheSnapshot snapshot)
        {
            if (snapshot == null || IsDrawButtonsActive())
                return;

            try
            {
                List<ColonistBar.Entry> entries =
                    _colonistCachedEntriesField?.GetValue(instance)
                    as List<ColonistBar.Entry>;
                List<UnityEngine.Vector2> drawLocs =
                    _colonistCachedDrawLocsField?.GetValue(instance)
                    as List<UnityEngine.Vector2>;
                List<int> groups =
                    _colonistCachedGroupsField?.GetValue(instance)
                    as List<int>;
                if (entries == null || drawLocs == null || groups == null)
                    return;

                entries.Clear();
                entries.AddRange(snapshot.Entries);
                drawLocs.Clear();
                drawLocs.AddRange(snapshot.DrawLocs);
                groups.Clear();
                groups.AddRange(snapshot.Groups);
                _colonistCachedScaleField?.SetValue(instance, snapshot.Scale);
            }
            catch
            {
                // The dirty flag below still requests a later vanilla retry.
            }
        }

        private static void MarkColonistBarDirtyForRetry(object instance)
        {
            try
            {
                _colonistEntriesDirtyField?.SetValue(instance, true);
            }
            catch
            {
                // Best-effort repair; the bar will be rebuilt on the next
                // explicit MarkColonistsDirty call if reflection fails.
            }
        }

        private static bool IsColonistBarDirty(object instance)
        {
            try
            {
                return _colonistEntriesDirtyField?.GetValue(instance) as bool? == true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsColonistBarRecacheSuspended()
        {
            try
            {
                return UnityEngine.Time.frameCount <= _suspendColonistBarRecacheUntilFrame;
            }
            catch
            {
                return false;
            }
        }

    }
}
