using System;
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
    /// Vanilla ShipJob_Unload drops pawns and immediately sets
    /// pawn.drafter.Drafted = true while the transport ship is ticking in the
    /// world time domain. In Multiplayer async time the map can be at a
    /// different tick; the world-tick setter then ends the pawn's job and
    /// creates its next job under the world Rand context on one peer, while the
    /// same pawn's job tracker ends naturally under the map Rand context on the
    /// other peer. That cross-domain ordering makes job IDs and Rand states
    /// diverge exactly after a shuttle landing.
    ///
    /// Desync-259 reproduced the same ordering failure with Odyssey passenger
    /// shuttles. The Drafted setter is not a reliable queue boundary: it is
    /// skipped when the pawn is already drafted or when the local map is
    /// reported as a home map, so one peer defers the draft while the other
    /// lets the pawn tick through its job naturally. Queueing now happens at
    /// UnloadThingFromShuttle itself, from the method's own arguments, so every
    /// peer schedules the same pawns regardless of local draft state. The home
    /// map guard is applied when the deferred setter actually runs.
    ///
    /// The setter is deferred to the destination map's next MapPreTick. All
    /// peers then run the vanilla draft side effects in the same per-map
    /// context and stable pawn-ID order before that map's tick lists execute.
    /// </summary>
    internal static class Patch_TransportShipUnloadMp
    {
        private const string AsyncTimeTypeName = "Multiplayer.Client.AsyncTimeComp";

        private static readonly FieldInfo DraftedField =
            AccessTools.Field(typeof(Pawn_DraftController), "draftedInt");
        private static readonly FieldInfo AsyncTickingMapField =
            AccessTools.Field(AccessTools.TypeByName(AsyncTimeTypeName), "tickingMap");

        private static readonly Dictionary<int, List<Pawn>> PendingDraftsByMap =
            new Dictionary<int, List<Pawn>>();

        private static bool _applied;
        private static bool _processingDeferred;
        private static int _shipUnloadDepth;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo unloadThingFromShuttle = AccessTools.Method(
                    typeof(ShipJob_Unload),
                    nameof(ShipJob_Unload.UnloadThingFromShuttle),
                    new[]
                    {
                        typeof(TransportShip),
                        typeof(Thing),
                        typeof(List<Thing>),
                        typeof(bool)
                    });
                MethodInfo draftedSetter = AccessTools.PropertySetter(
                    typeof(Pawn_DraftController),
                    nameof(Pawn_DraftController.Drafted));
                MethodInfo mapPreTick = AccessTools.Method(
                    typeof(Map),
                    nameof(Map.MapPreTick));

                MethodInfo unloadPrefix = AccessTools.Method(
                    typeof(Patch_TransportShipUnloadMp),
                    nameof(UnloadPrefix));
                MethodInfo unloadFinalizer = AccessTools.Method(
                    typeof(Patch_TransportShipUnloadMp),
                    nameof(UnloadFinalizer));
                MethodInfo draftedPrefix = AccessTools.Method(
                    typeof(Patch_TransportShipUnloadMp),
                    nameof(DraftedPrefix));
                MethodInfo mapPreTickPostfix = AccessTools.Method(
                    typeof(Patch_TransportShipUnloadMp),
                    nameof(MapPreTickPostfix));

                if (unloadThingFromShuttle == null || draftedSetter == null ||
                    mapPreTick == null || unloadPrefix == null ||
                    unloadFinalizer == null || draftedPrefix == null ||
                    mapPreTickPostfix == null || DraftedField == null ||
                    AsyncTickingMapField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Transport-ship unload draft guard " +
                        $"targets unresolved; skipped unload={unloadThingFromShuttle != null}, " +
                        $"setter={draftedSetter != null}, mapPreTick={mapPreTick != null}, " +
                        $"fields={DraftedField != null}/{AsyncTickingMapField != null}.");
                    return;
                }

                harmony.Patch(
                    unloadThingFromShuttle,
                    prefix: new HarmonyMethod(unloadPrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(unloadFinalizer)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(
                    draftedSetter,
                    prefix: new HarmonyMethod(draftedPrefix)
                    {
                        priority = Priority.First
                    });
                harmony.Patch(
                    mapPreTick,
                    postfix: new HarmonyMethod(mapPreTickPostfix)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Transport-ship unload draft guard active: " +
                    "world-tick auto-drafts are replayed at the destination " +
                    "map's next MapPreTick.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Transport-ship unload draft guard " +
                    "install failed: " + e);
            }
        }

        private static void UnloadPrefix(
            TransportShip ship,
            Thing thingToDrop)
        {
            if (!MP.IsInMultiplayer)
                return;

            _shipUnloadDepth++;

            if (!(thingToDrop is Pawn pawn) || pawn == null ||
                pawn.drafter == null || !pawn.IsPlayerControlled)
            {
                return;
            }

            Map map = ship?.shipThing?.Map;
            if (map == null)
                return;

            int mapId = map.uniqueID;
            if (!PendingDraftsByMap.TryGetValue(mapId, out List<Pawn> pending))
            {
                pending = new List<Pawn>();
                PendingDraftsByMap[mapId] = pending;
            }
            if (!pending.Contains(pawn))
                pending.Add(pawn);
        }

        private static Exception UnloadFinalizer(
            TransportShip ship,
            Thing thingToDrop,
            Exception __exception)
        {
            if (_shipUnloadDepth > 0)
                _shipUnloadDepth--;

            if (!MP.IsInMultiplayer ||
                !(thingToDrop is Pawn pawn) || pawn == null)
            {
                return __exception;
            }

            Map map = ship?.shipThing?.Map;
            if (map == null || pawn.Destroyed || !pawn.Spawned ||
                pawn.Map != map)
            {
                RemovePendingDraft(map, pawn);
            }

            return __exception;
        }

        private static bool DraftedPrefix(
            Pawn_DraftController __instance,
            bool value)
        {
            if (!MP.IsInMultiplayer || !value || _processingDeferred ||
                _shipUnloadDepth <= 0)
            {
                return true;
            }

            if (!MpRuntimeInfo.TryGetAsyncTimeActive(out bool asyncTime) ||
                !asyncTime)
            {
                return true;
            }

            Pawn pawn = __instance?.pawn;
            if (pawn == null || pawn.Destroyed || !pawn.Spawned ||
                pawn.Map == null)
            {
                return true;
            }

            // Mirror the resulting Drafted value immediately, but postpone the
            // full setter side effects (queued-job clear, current-job end, and
            // next-job selection) until the map owns the Rand/UniqueID context.
            if (!__instance.Drafted)
                DraftedField.SetValue(__instance, true);
            return false;
        }

        private static void MapPreTickPostfix(Map __instance)
        {
            if (!MP.IsInMultiplayer || _processingDeferred ||
                __instance == null)
            {
                return;
            }

            object tickingObject = AsyncTickingMapField?.GetValue(null);
            if (!(tickingObject is Map tickingMap) ||
                tickingMap != __instance)
            {
                return;
            }

            if (!PendingDraftsByMap.TryGetValue(
                    __instance.uniqueID,
                    out List<Pawn> pending) ||
                pending.Count == 0)
            {
                return;
            }

            PendingDraftsByMap.Remove(__instance.uniqueID);
            List<Pawn> valid = pending
                .Where(pawn =>
                    pawn != null && !pawn.Destroyed && pawn.Spawned &&
                    pawn.Map == __instance && pawn.drafter != null &&
                    pawn.IsPlayerControlled)
                .OrderBy(pawn => pawn.thingIDNumber)
                .ToList();
            if (valid.Count == 0)
                return;

            if (__instance.IsPlayerHome)
                return;

            _processingDeferred = true;
            try
            {
                foreach (Pawn pawn in valid)
                {
                    // The prefix already mirrored the value; reset it so the
                    // vanilla setter performs its normal job transitions.
                    DraftedField.SetValue(pawn.drafter, false);
                    pawn.drafter.Drafted = true;
                }
            }
            finally
            {
                _processingDeferred = false;
            }
        }

        private static void RemovePendingDraft(Map map, Pawn pawn)
        {
            if (map == null || pawn == null)
                return;

            if (!PendingDraftsByMap.TryGetValue(
                    map.uniqueID,
                    out List<Pawn> pending))
            {
                return;
            }

            pending.Remove(pawn);
            if (pending.Count == 0)
                PendingDraftsByMap.Remove(map.uniqueID);
        }
    }
}
