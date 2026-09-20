using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Odyssey gravship landings create a map through
    /// GravshipUtility.ArriveNewMap. Multiplayer initializes every freshly
    /// generated async map as Paused and normally unpauses it in
    /// WorldComponent_GravshipController.LandingEnded. When the game is saved
    /// and loaded around that boundary the saved map speed can remain Paused,
    /// and the landing preview/designator can keep a stale map reference while
    /// the marker is spawned on the actual generated map.
    ///
    /// This patch binds the controller's landingMap to the real marker map at
    /// every arrival/spawn boundary, makes the move designator follow that map,
    /// and restores Normal map speed after map generation and landing. All
    /// changes are local state on every peer with the same replicated world, so
    /// they are deterministic and do not add new sync commands.
    /// </summary>
    internal static class Patch_GravshipLandingMp
    {
        private static readonly Type ExtensionsType =
            AccessTools.TypeByName("Multiplayer.Client.Extensions");
        private static readonly MethodInfo AsyncTimeMethod =
            AccessTools.Method(ExtensionsType, "AsyncTime", new[] { typeof(Map) });
        private static readonly MethodInfo MpCompMethod =
            AccessTools.Method(ExtensionsType, "MpComp", new[] { typeof(Map) });
        private static readonly MethodInfo SetFactionMethod = AccessTools.Method(
            AccessTools.TypeByName("Multiplayer.Client.MultiplayerMapComp"),
            "SetFaction", new[] { typeof(Faction) });
        private static readonly PropertyInfo WorldTimeProperty = AccessTools.Property(
            AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "AsyncWorldTime");
        private static readonly FieldInfo ControllerMapField =
            AccessTools.Field(typeof(WorldComponent_GravshipController), "map");
        private static readonly FieldInfo LandingMarkerField =
            AccessTools.Field(typeof(WorldComponent_GravshipController), "landingMarker");

        private static bool _applied;
        private static int _traceCountArriveNew;
        private static int _traceCountArriveExisting;
        private static int _traceCountLandingEnded;
        private static int _traceCountDesignator;
        private static int _traceCountSpawn;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            try
            {
                Patch_GravshipRitualInitialization.Apply(harmony);
                Patch_GravshipShuttleClock.Apply(harmony);
                MethodInfo arriveNewMap = AccessTools.Method(
                    typeof(GravshipUtility),
                    "ArriveNewMap",
                    new[] { typeof(Gravship) });
                MethodInfo arriveExistingMap = AccessTools.Method(
                    typeof(GravshipUtility),
                    "ArriveExistingMap",
                    new[] { typeof(Gravship) });
                MethodInfo landingEnded = AccessTools.Method(
                    typeof(WorldComponent_GravshipController),
                    "LandingEnded",
                    Type.EmptyTypes);
                MethodInfo moveDesignator = AccessTools.Method(
                    typeof(WorldComponent_GravshipController),
                    "MoveDesignator",
                    Type.EmptyTypes);
                MethodInfo markerSpawn = AccessTools.Method(
                    typeof(GravshipLandingMarker),
                    "SpawnSetup",
                    new[] { typeof(Map), typeof(bool) });
                MethodInfo removeGravship = AccessTools.Method(
                    typeof(WorldComponent_GravshipController), "RemoveGravshipFromMap",
                    new[] { typeof(Building_GravEngine) });

                if (removeGravship == null || MpCompMethod == null || SetFactionMethod == null)
                {
                    Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILED gravship takeoff faction restore.");
                    return;
                }
                harmony.Patch(removeGravship,
                    prefix: new HarmonyMethod(typeof(Patch_GravshipLandingMp), nameof(RemoveGravshipPrefix)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(Patch_GravshipLandingMp), nameof(RemoveGravshipFinalizer)) { priority = Priority.Last });
                harmony.Patch(AccessTools.Method(typeof(Building_GravEngine), nameof(Building_GravEngine.DeSpawn)),
                    prefix: new HarmonyMethod(typeof(Patch_GravshipLandingMp), nameof(EngineDespawnPrefix)),
                    finalizer: new HarmonyMethod(typeof(Patch_GravshipLandingMp), nameof(EngineDespawnFinalizer)));
                harmony.Patch(AccessTools.Method(typeof(Building_GravEngine), nameof(Building_GravEngine.SpawnSetup)),
                    postfix: new HarmonyMethod(typeof(Patch_GravshipLandingMp), nameof(EngineSpawnPostfix)));

                if (arriveNewMap != null)
                {
                    harmony.Patch(
                        arriveNewMap,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipLandingMp),
                            nameof(ArriveNewMapPostfix))
                        {
                            priority = Priority.Last
                        });
                }
                if (arriveExistingMap != null)
                {
                    harmony.Patch(
                        arriveExistingMap,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipLandingMp),
                            nameof(ArriveExistingMapPostfix))
                        {
                            priority = Priority.Last
                        });
                }
                if (landingEnded != null)
                {
                    harmony.Patch(
                        landingEnded,
                        finalizer: new HarmonyMethod(
                            typeof(Patch_GravshipLandingMp),
                            nameof(LandingEndedFinalizer))
                        {
                            priority = Priority.Last
                        });
                }
                if (moveDesignator != null)
                {
                    harmony.Patch(
                        moveDesignator,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipLandingMp),
                            nameof(MoveDesignatorPostfix))
                        {
                            priority = Priority.Last
                        });
                }
                if (markerSpawn != null)
                {
                    harmony.Patch(
                        markerSpawn,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GravshipLandingMp),
                            nameof(MarkerSpawnPostfix))
                        {
                            priority = Priority.Last
                        });
                }

                if (arriveNewMap == null || arriveExistingMap == null ||
                    landingEnded == null || moveDesignator == null ||
                    markerSpawn == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Gravship landing MP guard partially " +
                        $"installed: arriveNew={arriveNewMap != null} " +
                        $"arriveExisting={arriveExistingMap != null} " +
                        $"landingEnded={landingEnded != null} " +
                        $"moveDesignator={moveDesignator != null} " +
                        $"markerSpawn={markerSpawn != null}.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Gravship landing MP guard active: " +
                    "landingMap/designator map binding and landing map time " +
                    "speed normalization are deterministic.");
            }
            catch (Exception e)
            {
                Log.Error(
                    "[MP-MeowOnlineShop] REQUIRED_TARGET_FAILED gravship landing MP guard: " +
                    e);
            }
        }

        private sealed class TakeoffFactionState
        {
            internal Map Map;
            internal Faction Faction;
        }

        private sealed class CooldownState
        {
            internal int Remaining;
            internal int WorldTick;
        }

        internal static bool TryReadClocks(Map map, out int mapTick, out int worldTick)
        {
            mapTick = worldTick = 0;
            if (!MP.IsInMultiplayer || map == null ||
                !MpRuntimeInfo.TryGetAsyncTimeActive(out bool asyncTime) || !asyncTime)
                return false;
            object mapTime = AsyncTimeMethod?.Invoke(null, new object[] { map });
            object worldTime = WorldTimeProperty?.GetValue(null);
            if (mapTime == null || worldTime == null)
                return false;
            mapTick = (int)AccessTools.Field(mapTime.GetType(), "mapTicks").GetValue(mapTime);
            worldTick = (int)AccessTools.Field(worldTime.GetType(), "worldTicks").GetValue(worldTime);
            return true;
        }

        private static void EngineDespawnPrefix(Building_GravEngine __instance, out CooldownState __state)
        {
            __state = null;
            if (__instance.cooldownCompleteTick >= 0 &&
                TryReadClocks(__instance.Map, out int mapTick, out int worldTick))
                __state = new CooldownState { Remaining = __instance.cooldownCompleteTick - mapTick, WorldTick = worldTick };
        }

        private static Exception EngineDespawnFinalizer(Building_GravEngine __instance, CooldownState __state, Exception __exception)
        {
            if (__exception == null && __state != null && !__instance.Spawned)
                __instance.cooldownCompleteTick = __state.Remaining > 0 ? __state.WorldTick + __state.Remaining : -1;
            return __exception;
        }

        private static void EngineSpawnPostfix(Building_GravEngine __instance, Map map, bool respawningAfterLoad)
        {
            // Saved spawned engines already use their map clock. Engines in
            // transit use the world clock; elapsed travel time counts down.
            if (respawningAfterLoad || __instance.cooldownCompleteTick < 0 ||
                !TryReadClocks(map, out int mapTick, out int worldTick))
                return;
            int remaining = __instance.cooldownCompleteTick - worldTick;
            __instance.cooldownCompleteTick = remaining > 0 ? mapTick + remaining : -1;
        }

        private static void RemoveGravshipPrefix(Building_GravEngine engine, out TakeoffFactionState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || engine?.Map == null ||
                !MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) || !multifaction)
                return;
            __state = new TakeoffFactionState { Map = engine.Map, Faction = Faction.OfPlayer };
        }

        private static Exception RemoveGravshipFinalizer(TakeoffFactionState __state, Exception __exception)
        {
            // MP pops its faction scope via engine.Map. GenerateGravship has
            // already despawned the engine, so that pop restores the world but
            // cannot restore the original map's per-faction managers. Retain
            // the pre-removal map and restore it after MP's finalizer.
            if (__state?.Map != null && __state.Faction != null && Find.Maps.Contains(__state.Map))
            {
                object comp = MpCompMethod.Invoke(null, new object[] { __state.Map });
                if (comp != null)
                    SetFactionMethod.Invoke(comp, new object[] { __state.Faction });
            }
            return __exception;
        }

        private static void ArriveNewMapPostfix(Gravship gravship)
        {
            if (!MP.IsInMultiplayer || gravship == null)
                return;

            try
            {
                Map map = Find.WorldObjects.MapParentAt(gravship.destinationTile)?.Map;
                if (map == null)
                    return;

                WorldComponent_GravshipController controller = Find.GravshipController;
                if (controller != null && controller.landingMap == null)
                    controller.landingMap = map;

                SetMapTimeSpeed(map, TimeSpeed.Normal);

                if (_traceCountArriveNew < 3)
                {
                    _traceCountArriveNew++;
                    Log.Message(
                        "[MP-MeowOnlineShop] GRAVSHIP_LANDING_MAP_BOUND " +
                        $"map={map.uniqueID} tile={map.Tile} " +
                        $"tick={TickTimer()} marker={GetLandingMarker(controller) != null}");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship landing map bind failed: " +
                    e.Message);
            }
        }

        private static void ArriveExistingMapPostfix(Gravship gravship)
        {
            if (!MP.IsInMultiplayer || gravship == null)
                return;

            try
            {
                Map map = Find.WorldObjects.MapParentAt(gravship.destinationTile)?.Map;
                if (map == null)
                    return;

                WorldComponent_GravshipController controller = Find.GravshipController;
                if (controller != null && controller.landingMap == null)
                    controller.landingMap = map;

                if (_traceCountArriveExisting < 3)
                {
                    _traceCountArriveExisting++;
                    Log.Message(
                        "[MP-MeowOnlineShop] GRAVSHIP_ARRIVE_EXISTING_BOUND " +
                        $"map={map.uniqueID} tile={map.Tile} " +
                        $"tick={TickTimer()} marker={GetLandingMarker(controller) != null}");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship existing landing bind failed: " +
                    e.Message);
            }
        }

        private static Exception LandingEndedFinalizer(
            WorldComponent_GravshipController __instance,
            Exception __exception)
        {
            try
            {
                if (!MP.IsInMultiplayer || __instance == null)
                    return __exception;

                Map map = ControllerMapField?.GetValue(__instance) as Map;
                if (map != null)
                    SetMapTimeSpeed(map, TimeSpeed.Normal);
                if (__instance.landingMap != null)
                    SetMapTimeSpeed(__instance.landingMap, TimeSpeed.Normal);

                if (_traceCountLandingEnded < 3)
                {
                    _traceCountLandingEnded++;
                    Log.Message(
                        "[MP-MeowOnlineShop] GRAVSHIP_LANDING_TIME_NORMAL " +
                        $"map={map?.uniqueID ?? -1} " +
                        $"landingMap={__instance.landingMap?.uniqueID ?? -1} " +
                        $"tick={TickTimer()} exception={__exception?.GetType().Name ?? "none"}");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship landing time normalization " +
                    "failed: " + e.Message);
            }

            return __exception;
        }

        private static void MoveDesignatorPostfix(
            WorldComponent_GravshipController __instance,
            ref Designator_MoveGravship __result)
        {
            if (!MP.IsInMultiplayer || __result == null || __instance == null)
                return;

            try
            {
                GravshipLandingMarker marker = GetLandingMarker(__instance);
                Map markerMap = marker?.Map;
                Map targetMap = __instance.landingMap ?? markerMap;
                if (targetMap == null)
                    return;

                if (__result.map != targetMap)
                {
                    __result.map = targetMap;
                    if (_traceCountDesignator < 3)
                    {
                        _traceCountDesignator++;
                        Log.Warning(
                            "[MP-MeowOnlineShop] Gravship move designator map " +
                            $"rebound from={__result.map?.uniqueID ?? -1} " +
                            $"to={targetMap.uniqueID} tick={TickTimer()}.");
                    }
                }

                if (marker != null && __result.marker != marker)
                {
                    __result.marker = marker;
                    if (_traceCountDesignator < 3)
                    {
                        _traceCountDesignator++;
                        Log.Warning(
                            "[MP-MeowOnlineShop] Gravship move designator marker " +
                            "rebound to spawned marker tick=" + TickTimer() + ".");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship move designator rebind failed: " +
                    e.Message);
            }
        }

        private static void MarkerSpawnPostfix(
            GravshipLandingMarker __instance,
            Map map)
        {
            if (!MP.IsInMultiplayer || __instance == null || map == null)
                return;

            try
            {
                WorldComponent_GravshipController controller = Find.GravshipController;
                if (controller == null)
                    return;

                if (controller.landingMap == null)
                    controller.landingMap = map;

                if (_traceCountSpawn < 3)
                {
                    _traceCountSpawn++;
                    Log.Message(
                        "[MP-MeowOnlineShop] GRAVSHIP_MARKER_SPAWNED " +
                        $"map={map.uniqueID} tile={map.Tile} " +
                        $"landingMap={controller.landingMap?.uniqueID ?? -1} " +
                        $"tick={TickTimer()}.");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship marker spawn bind failed: " +
                    e.Message);
            }
        }

        private static GravshipLandingMarker GetLandingMarker(
            WorldComponent_GravshipController controller)
        {
            if (controller == null)
                return null;
            try
            {
                return LandingMarkerField?.GetValue(controller) as GravshipLandingMarker;
            }
            catch
            {
                return null;
            }
        }

        private static void SetMapTimeSpeed(Map map, TimeSpeed speed)
        {
            if (map == null || AsyncTimeMethod == null)
                return;

            try
            {
                object asyncTime = AsyncTimeMethod.Invoke(
                    null,
                    new object[] { map });
                if (asyncTime == null)
                    return;

                AccessTools.Property(asyncTime.GetType(), "DesiredTimeSpeed")
                    ?.SetValue(asyncTime, speed);
            }
            catch
            {
                // The AsyncTimeComp may not be attached yet during map setup;
                // later arrival/landing patches retry with the real component.
            }
        }

        private static int TickTimer()
        {
            try
            {
                PropertyInfo timer = AccessTools.Property(
                    AccessTools.TypeByName("Multiplayer.Client.TickPatch"),
                    "Timer");
                return timer == null ? -1 : (int)timer.GetValue(null, null);
            }
            catch
            {
                return -1;
            }
        }
    }
}
