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
    /// peer schedules the same pawns regardless of local draft state. No
    /// home-map guard is applied when the deferred setter actually runs.
    ///
    /// The setter is deferred to the pawn's actual map's next MapPreTick. All
    /// peers then run the vanilla draft side effects in the same per-map
    /// context and stable pawn-ID order before that map's tick lists execute.
    /// The queue is keyed by pawn, not by the shuttle's local map, and the
    /// home-map guard is removed so one peer cannot skip the replay while the
    /// other runs it (Desync-259).
    /// </summary>
    internal static class Patch_TransportShipUnloadMp
    {
        private static readonly FieldInfo DraftedField =
            AccessTools.Field(typeof(Pawn_DraftController), "draftedInt");
        private static readonly HashSet<Pawn> PendingDrafts =
            new HashSet<Pawn>();
        private static readonly HashSet<int> LoggedDeferredDraftMaps =
            new HashSet<int>();

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
                    mapPreTickPostfix == null || DraftedField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Transport-ship unload draft guard " +
                        $"targets unresolved; skipped unload={unloadThingFromShuttle != null}, " +
                        $"setter={draftedSetter != null}, mapPreTick={mapPreTick != null}, " +
                        $"field={DraftedField != null}.");
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

            PendingDrafts.Add(pawn);
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

            if (pawn.Destroyed || !pawn.Spawned)
                PendingDrafts.Remove(pawn);

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

            List<Pawn> valid = null;
            foreach (Pawn pawn in PendingDrafts)
            {
                if (pawn == null || pawn.Destroyed || !pawn.Spawned ||
                    pawn.Map != __instance || pawn.drafter == null ||
                    !pawn.IsPlayerControlled)
                {
                    continue;
                }

                if (valid == null)
                    valid = new List<Pawn>();
                valid.Add(pawn);
            }

            if (valid == null || valid.Count == 0)
                return;

            valid.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            for (int i = 0; i < valid.Count; i++)
                PendingDrafts.Remove(valid[i]);

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

                if (LoggedDeferredDraftMaps.Add(__instance.uniqueID))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Replayed deferred transport-ship " +
                        $"auto-draft at MapPreTick: map={__instance.uniqueID}, " +
                        $"count={valid.Count}, ids=" +
                        string.Join(",", valid.ConvertAll(p => p.ThingID)));
                }
            }
            finally
            {
                _processingDeferred = false;
            }
        }
    }
}
