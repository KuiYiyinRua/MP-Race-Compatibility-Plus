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
                Log.Warning(
                    "[MP-MeowOnlineShop] Gravship landing MP guard install failed: " +
                    e);
            }
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
