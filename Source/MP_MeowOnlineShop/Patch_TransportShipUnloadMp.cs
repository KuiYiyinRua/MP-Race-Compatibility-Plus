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
    ///
    /// Desync-513/514/515 (2026-08-20, faction trade): the same job-ID
    /// signature returned. The deferral queue was gated on
    /// pawn.IsPlayerControlled (a Faction.OfPlayer-relative check), and in
    /// multifaction/async sessions Faction.OfPlayer can be the spectator
    /// faction on one peer while the unload runs. One peer then queues the
    /// pawn (defers the auto-draft to MapPreTick) while the other never does,
    /// leaving the pawn drafted on one peer only. Its think tree starts
    /// JobGiver_Orders Wait_Combat / Wait_MaintainPosture jobs and consumes
    /// UniqueIDsManager job IDs on that peer only (first divergent trace).
    /// The queue decision and the mirror step are now deterministic
    /// (Faction.def.isPlayer + drafter), independent of Faction.OfPlayer.
    ///
    /// Desync-588 (2026-08-21): the same world-side unload also executes
    /// `pawn.inventory.UnloadEverything = true` for a colonist pawn arriving
    /// at a home map. The client then entered JobDriver_UnloadYourInventory
    /// while the host was already ticking a TunnelHiveSpawner. That branch is
    /// deferred through the same MapPreTick boundary; its queue is populated
    /// from the unload arguments and uses map-parent faction identity instead
    /// of the peer-local Faction.OfPlayer/Map.IsPlayerHome context.
    /// </summary>
    internal static class Patch_TransportShipUnloadMp
    {
        private static readonly FieldInfo DraftedField =
            AccessTools.Field(typeof(Pawn_DraftController), "draftedInt");

        private static readonly FieldInfo InventoryPawnField =
            AccessTools.Field(typeof(Pawn_InventoryTracker), "pawn");

        private static bool IsDeferredPlayerPawn(Pawn pawn)
        {
            return pawn != null &&
                   pawn.drafter != null &&
                   pawn.Faction != null &&
                   pawn.Faction.def != null &&
                   pawn.Faction.def.isPlayer;
        }

        private static bool IsAsyncTimeActive()
        {
            return MpRuntimeInfo.TryGetAsyncTimeActive(out bool asyncTime) &&
                   asyncTime;
        }
        private static readonly HashSet<Pawn> PendingDrafts =
            new HashSet<Pawn>();
        private static readonly HashSet<Pawn> PendingInventoryUnloads =
            new HashSet<Pawn>();
        private static readonly HashSet<int> LoggedDeferredDraftMaps =
            new HashSet<int>();
        private static readonly HashSet<int> LoggedDeferredInventoryMaps =
            new HashSet<int>();
        private sealed class DeferredUnloadOperation
        {
            internal readonly TransportShip Ship;
            internal readonly Thing Thing;
            internal readonly List<Thing> DroppedThings;
            internal readonly bool UnforbidAll;
            internal readonly bool DeferDraft;
            internal readonly bool DeferInventoryUnload;

            internal DeferredUnloadOperation(
                TransportShip ship,
                Thing thing,
                List<Thing> droppedThings,
                bool unforbidAll,
                bool deferDraft,
                bool deferInventoryUnload)
            {
                Ship = ship;
                Thing = thing;
                DroppedThings = droppedThings;
                UnforbidAll = unforbidAll;
                DeferDraft = deferDraft;
                DeferInventoryUnload = deferInventoryUnload;
            }
        }

        private static readonly List<DeferredUnloadOperation>
            PendingUnloadOperations =
                new List<DeferredUnloadOperation>();

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
                MethodInfo unloadEverythingSetter = AccessTools.PropertySetter(
                    typeof(Pawn_InventoryTracker),
                    nameof(Pawn_InventoryTracker.UnloadEverything));
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
                MethodInfo unloadEverythingPrefix = AccessTools.Method(
                    typeof(Patch_TransportShipUnloadMp),
                    nameof(UnloadEverythingPrefix));
                MethodInfo mapPreTickPostfix = AccessTools.Method(
                    typeof(Patch_TransportShipUnloadMp),
                    nameof(MapPreTickPostfix));

                if (unloadThingFromShuttle == null || draftedSetter == null ||
                    mapPreTick == null || unloadPrefix == null ||
                    unloadFinalizer == null || draftedPrefix == null ||
                    unloadEverythingPrefix == null || mapPreTickPostfix == null ||
                    DraftedField == null)
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
                bool inventoryUnloadPatched = false;
                if (unloadEverythingSetter != null &&
                    unloadEverythingPrefix != null &&
                    InventoryPawnField != null)
                {
                    harmony.Patch(
                        unloadEverythingSetter,
                        prefix: new HarmonyMethod(unloadEverythingPrefix)
                        {
                            priority = Priority.First
                        });
                    inventoryUnloadPatched = true;
                }
                harmony.Patch(
                    mapPreTick,
                    postfix: new HarmonyMethod(mapPreTickPostfix)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Transport-ship unload draft guard active: " +
                    "world-tick drops and pawn side effects are replayed at " +
                    "the destination map's next MapPreTick; inventory unload " +
                    "deferral=" +
                    inventoryUnloadPatched + ".");

                if (!inventoryUnloadPatched)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Transport-ship inventory unload " +
                        "deferral target unresolved; vanilla UnloadEverything " +
                        "side effects remain immediate.");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Transport-ship unload draft guard " +
                    "install failed: " + e);
            }
        }

        private static bool UnloadPrefix(
            TransportShip ship,
            Thing thingToDrop,
            List<Thing> droppedThings,
            bool unforbidAll)
        {
            if (!MP.IsInMultiplayer || _processingDeferred)
                return true;

            _shipUnloadDepth++;

            // With async time off there is no world/map ordering boundary to
            // repair; preserve vanilla execution in that mode.
            if (!IsAsyncTimeActive() || ship?.shipThing == null ||
                !ship.shipThing.Spawned || thingToDrop == null)
            {
                return true;
            }

            // Desync-686 showed that the TryDrop result itself can differ on
            // peers before the destination map reaches its stable pre-tick
            // boundary. Queue the complete vanilla operation so the drop,
            // Notify_ThingRemoved, and pawn side effects share one map context.
            Pawn pawn = thingToDrop as Pawn;
            Map unloadMap = ship.shipThing.Map;
            PendingUnloadOperations.Add(
                new DeferredUnloadOperation(
                    ship,
                    thingToDrop,
                    droppedThings,
                    unforbidAll,
                    IsDeferredPlayerPawn(pawn),
                    ShouldDeferInventoryUnload(pawn, unloadMap)));
            return false;
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

            if (__exception != null)
            {
                PendingDrafts.Remove(pawn);
                PendingInventoryUnloads.Remove(pawn);
                return __exception;
            }

            if (pawn.Destroyed || !pawn.Spawned)
            {
                PendingDrafts.Remove(pawn);
                PendingInventoryUnloads.Remove(pawn);
                return __exception;
            }

            // Deterministic mirror: every peer marks a queued player-faction
            // pawn drafted at the drop point, regardless of the vanilla
            // IsPlayerControlled / IsPlayerHome gates (both are
            // Faction.OfPlayer-relative and can differ between peers). The
            // full setter side effects still run once at the destination
            // map's next MapPreTick, in the map's Rand/UniqueID context.
            if (PendingDrafts.Contains(pawn) && pawn.drafter != null &&
                !pawn.drafter.Drafted)
            {
                DraftedField.SetValue(pawn.drafter, true);
            }

            return __exception;
        }

        private static bool UnloadEverythingPrefix(
            Pawn_InventoryTracker __instance,
            bool value)
        {
            if (!MP.IsInMultiplayer || !value || _processingDeferred ||
                _shipUnloadDepth <= 0 || !IsAsyncTimeActive())
            {
                return true;
            }

            Pawn pawn = GetInventoryPawn(__instance);
            // The queue decision was captured from ShipJob_Unload.UnloadThingFromShuttle.
            // Do not re-evaluate the live map/faction state here: that state can already
            // differ while vanilla is unwinding the same unload call on different peers.
            if (!PendingInventoryUnloads.Contains(pawn))
                return true;

            // ShipJob_Unload invokes this setter while the transport ship is
            // still on the world tick. Keep the flag false until MapPreTick so
            // JobDriver_UnloadYourInventory is created in the destination map
            // context on every peer. The queue is populated from the unload
            // method arguments, so this remains symmetric even if the vanilla
            // IsColonist/IsPlayerHome checks differ under multifaction.
            return false;
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

            ReplayPendingUnloadOperations(__instance);

            List<Pawn> valid = null;
            HashSet<Pawn> validDrafts = new HashSet<Pawn>();
            HashSet<Pawn> validInventoryUnloads = new HashSet<Pawn>();
            foreach (Pawn pawn in PendingDrafts)
            {
                if (pawn == null || pawn.Destroyed || !pawn.Spawned ||
                    pawn.Map != __instance || !IsDeferredPlayerPawn(pawn))
                {
                    continue;
                }

                if (valid == null)
                    valid = new List<Pawn>();
                if (validDrafts.Add(pawn))
                    valid.Add(pawn);
            }

            foreach (Pawn pawn in PendingInventoryUnloads)
            {
                // PendingInventoryUnloads is authoritative for this unload operation.
                // Re-checking the current home/parent state here could make one peer
                // replay UnloadEverything while another peer leaves it deferred.
                if (pawn == null || pawn.Destroyed || !pawn.Spawned ||
                    pawn.Map != __instance || pawn.inventory == null)
                {
                    continue;
                }

                if (valid == null)
                    valid = new List<Pawn>();
                if (validInventoryUnloads.Add(pawn) &&
                    !validDrafts.Contains(pawn))
                {
                    valid.Add(pawn);
                }
            }

            if (valid == null || valid.Count == 0)
                return;

            valid.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            for (int i = 0; i < valid.Count; i++)
            {
                PendingDrafts.Remove(valid[i]);
                PendingInventoryUnloads.Remove(valid[i]);
            }

            _processingDeferred = true;
            try
            {
                foreach (Pawn pawn in valid)
                {
                    if (validDrafts.Contains(pawn))
                    {
                        // The prefix already mirrored the value; reset it so
                        // the vanilla setter performs its normal job
                        // transitions.
                        DraftedField.SetValue(pawn.drafter, false);
                        pawn.drafter.Drafted = true;
                    }

                    if (validInventoryUnloads.Contains(pawn) &&
                        pawn.inventory != null)
                    {
                        pawn.inventory.UnloadEverything = true;
                    }
                }

                if (validDrafts.Count > 0 &&
                    LoggedDeferredDraftMaps.Add(__instance.uniqueID))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Replayed deferred transport-ship " +
                        $"auto-draft at MapPreTick: map={__instance.uniqueID}, " +
                        $"count={validDrafts.Count}, ids=" +
                        FormatPawnIds(valid, validDrafts));
                }

                if (validInventoryUnloads.Count > 0 &&
                    LoggedDeferredInventoryMaps.Add(__instance.uniqueID))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Replayed deferred transport-ship " +
                        "inventory unload at MapPreTick: map=" +
                        __instance.uniqueID + ", count=" +
                        validInventoryUnloads.Count + ", ids=" +
                        FormatPawnIds(valid, validInventoryUnloads));
                }
            }
            finally
            {
                _processingDeferred = false;
            }
        }

        private static void ReplayPendingUnloadOperations(Map map)
        {
            if (!MP.IsInMultiplayer || map == null)
                return;

            List<DeferredUnloadOperation> ready =
                new List<DeferredUnloadOperation>();
            List<DeferredUnloadOperation> stale =
                new List<DeferredUnloadOperation>();
            foreach (DeferredUnloadOperation operation in
                     PendingUnloadOperations)
            {
                if (operation == null || operation.Ship == null ||
                    operation.Thing == null ||
                    operation.Ship.shipThing == null ||
                    operation.Ship.shipThing.Destroyed ||
                    !operation.Ship.shipThing.Spawned)
                {
                    stale.Add(operation);
                }
                else if (operation.Ship.shipThing.Map == map)
                {
                    ready.Add(operation);
                }
            }

            foreach (DeferredUnloadOperation operation in stale)
                PendingUnloadOperations.Remove(operation);
            if (ready.Count == 0)
                return;

            ready.Sort((a, b) =>
            {
                int result = a.Ship.shipThing.thingIDNumber.CompareTo(
                    b.Ship.shipThing.thingIDNumber);
                return result != 0
                    ? result
                    : a.Thing.thingIDNumber.CompareTo(b.Thing.thingIDNumber);
            });
            foreach (DeferredUnloadOperation operation in ready)
                PendingUnloadOperations.Remove(operation);

            List<DeferredUnloadOperation> retry =
                new List<DeferredUnloadOperation>();
            _processingDeferred = true;
            try
            {
                foreach (DeferredUnloadOperation operation in ready)
                {
                    if (!ReplayDeferredUnload(operation))
                        retry.Add(operation);
                }
            }
            finally
            {
                _processingDeferred = false;
            }

            foreach (DeferredUnloadOperation operation in retry)
            {
                if (!PendingUnloadOperations.Contains(operation))
                    PendingUnloadOperations.Add(operation);
            }
        }
        private static bool ReplayDeferredUnload(
            DeferredUnloadOperation operation)
        {
            TransportShip ship = operation?.Ship;
            Thing thingToDrop = operation?.Thing;
            if (ship == null || thingToDrop == null ||
                ship.shipThing == null || !ship.shipThing.Spawned ||
                ship.TransporterComp == null)
            {
                return false;
            }

            Map map = ship.shipThing.Map;
            IntVec3 interactionCell = ship.shipThing.InteractionCell;
            if (!ship.TransporterComp.innerContainer.TryDrop(
                thingToDrop,
                interactionCell,
                map,
                ThingPlaceMode.Near,
                out Thing _,
                null,
                delegate(IntVec3 c)
                {
                    if (c.Fogged(map))
                        return false;
                    return (!(thingToDrop is Pawn { Downed: not false }) ||
                            c.GetFirstPawn(map) == null)
                        ? true
                        : false;
                },
                !(thingToDrop is Pawn)))
            {
                return false;
            }

            ship.TransporterComp.Notify_ThingRemoved(thingToDrop);
            operation.DroppedThings?.Add(thingToDrop);
            if (operation.UnforbidAll)
                thingToDrop.SetForbidden(false, false);

            if (thingToDrop is Pawn pawn)
            {
                if (operation.DeferDraft && pawn.drafter != null &&
                    pawn.Spawned)
                {
                    DraftedField.SetValue(pawn.drafter, false);
                    pawn.drafter.Drafted = true;
                }

                Pawn_GuestTracker guest = pawn.guest;
                if (guest != null && guest.IsPrisoner)
                    guest.WaitInsteadOfEscapingForDefaultTicks();

                if (operation.DeferInventoryUnload &&
                    pawn.inventory != null)
                {
                    pawn.inventory.UnloadEverything = true;
                }
            }

            return true;
        }
        private static Pawn GetInventoryPawn(Pawn_InventoryTracker tracker)
        {
            if (tracker == null || InventoryPawnField == null)
                return null;

            try
            {
                return InventoryPawnField.GetValue(tracker) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatPawnIds(
            List<Pawn> pawns,
            HashSet<Pawn> selected)
        {
            List<string> ids = new List<string>();
            if (pawns != null && selected != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (selected.Contains(pawns[i]))
                        ids.Add(pawns[i].ThingID);
                }
            }

            return string.Join(",", ids);
        }

        private static bool ShouldDeferInventoryUnload(Pawn pawn, Map map)
        {
            if (pawn == null || pawn.inventory == null || map == null ||
                pawn.Faction == null || pawn.Faction.def == null ||
                !pawn.Faction.def.isPlayer)
            {
                return false;
            }

            Faction parentFaction = map.ParentFaction;
            if (parentFaction != null)
            {
                return parentFaction == pawn.Faction && map.Parent != null &&
                       map.Parent.def != null &&
                       map.Parent.def.canBePlayerHome;
            }

            // Gravship-created maps can have no parent faction. Preserve the
            // vanilla fallback for that rare shape; normal faction maps use
            // the deterministic parent-faction branch above.
            return map.IsPlayerHome;
        }
    }
}
