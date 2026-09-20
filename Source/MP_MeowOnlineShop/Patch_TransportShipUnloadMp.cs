using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Replay complete world-side shuttle unloads at the destination map's
    /// MapPreTick, where pawn jobs and random state belong. The serialized
    /// queue deduplicates paused-map retries and discards stale ship jobs.
    /// Vanilla decides draft and inventory effects after the pawn has spawned,
    /// under its actual owner faction rather than the peer's viewed faction.
    /// </summary>
    internal static class Patch_TransportShipUnloadMp
    {
        private static readonly Type FactionExtensionsType =
            AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
        private static readonly MethodInfo PushFactionMethod = AccessTools.Method(
            FactionExtensionsType, "PushFaction",
            new[] { typeof(Map), typeof(Faction), typeof(bool) });
        private static readonly MethodInfo PopFactionMethod = AccessTools.Method(
            FactionExtensionsType, "PopFaction", new[] { typeof(Map) });
        private static readonly MethodInfo AsyncTimeMethod = AccessTools.Method(
            AccessTools.TypeByName("Multiplayer.Client.Extensions"), "AsyncTime", new[] { typeof(Map) });
        private static readonly FieldInfo TickingMapField = AccessTools.Field(
            AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "tickingMap");
        private static readonly PropertyInfo MapPausedProperty = AccessTools.Property(
            AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "Paused");

        private static bool IsAsyncTimeActive()
        {
            return MpRuntimeInfo.TryGetAsyncTimeActive(out bool asyncTime) &&
                   asyncTime;
        }
        internal sealed class DeferredUnloadOperation : IExposable
        {
            private static readonly FieldInfo DroppedThingsField =
                AccessTools.Field(typeof(ShipJob_Unload), "droppedThings");
            internal TransportShip Ship;
            internal Thing Thing;
            internal Map SourceMap;
            internal List<Thing> DroppedThings;
            internal bool UnforbidAll;
            internal int UnloadJobId = -1;

            public DeferredUnloadOperation() { }

            internal DeferredUnloadOperation(
                TransportShip ship,
                Thing thing,
                List<Thing> droppedThings,
                bool unforbidAll)
            {
                Ship = ship;
                Thing = thing;
                DroppedThings = droppedThings;
                UnforbidAll = unforbidAll;
                SourceMap = ship.shipThing.Map;
                if (ship.curJob is ShipJob_Unload job && droppedThings != null &&
                    ReferenceEquals(DroppedThingsField.GetValue(job), droppedThings))
                    UnloadJobId = job.loadID;
            }

            internal List<Thing> ResolveDroppedThings()
            {
                if (UnloadJobId < 0)
                    return DroppedThings;
                return ShipJobMatches() ? DroppedThingsField.GetValue(Ship.curJob) as List<Thing> : null;
            }

            internal bool ShipJobMatches() => Ship?.curJob is ShipJob_Unload job &&
                job.loadID == UnloadJobId && job.jobState != ShipJobState.Ended;

            public void ExposeData()
            {
                Scribe_References.Look(ref Ship, "ship");
                Scribe_References.Look(ref Thing, "thing");
                Scribe_References.Look(ref SourceMap, "sourceMap");
                Scribe_Values.Look(ref UnforbidAll, "unforbidAll");
                Scribe_Values.Look(ref UnloadJobId, "unloadJobId", -1);
                // A ship job owns its live droppedThings list. Rebind to that
                // job after loading instead of silently using a detached copy.
                if (UnloadJobId < 0)
                    Scribe_Collections.Look(ref DroppedThings, "droppedThings", LookMode.Reference);
            }
        }

        private static List<DeferredUnloadOperation> PendingUnloadOperations =>
            Current.Game.GetComponent<TransportShipUnloadState>().Pending;

        private static bool _applied;
        private static bool _processingDeferred;
        private sealed class NativeUnloadScope
        {
            internal Map Map;
            internal Faction Owner;
            internal NativeUnloadScope Previous;
            internal bool Pushed;
        }
        private static NativeUnloadScope nativeScope;


        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null) return;
            _applied = true;
            try
            {
                MethodInfo unload = AccessTools.Method(typeof(ShipJob_Unload),
                    nameof(ShipJob_Unload.UnloadThingFromShuttle),
                    new[] { typeof(TransportShip), typeof(Thing), typeof(List<Thing>), typeof(bool) });
                MethodInfo preTick = AccessTools.Method(typeof(Map), nameof(Map.MapPreTick));
                if (unload == null || preTick == null || PushFactionMethod == null || PopFactionMethod == null || AsyncTimeMethod == null || MapPausedProperty == null || TickingMapField == null)
                {
                    Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILED transport unload map boundary.");
                    return;
                }
                harmony.Patch(unload,
                    prefix: new HarmonyMethod(typeof(Patch_TransportShipUnloadMp), nameof(UnloadPrefix)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(Patch_TransportShipUnloadMp), nameof(UnloadFinalizer)) { priority = Priority.Last },
                    transpiler: new HarmonyMethod(typeof(Patch_TransportShipUnloadMp), nameof(UsePassengerHomeContext)));
                harmony.Patch(preTick, postfix: new HarmonyMethod(typeof(Patch_TransportShipUnloadMp), nameof(MapPreTickPostfix)) { priority = Priority.Last });
                Log.Message("[MP-MeowOnlineShop] Transport unload map boundary active: serialized, deduplicated native replay with owner faction context.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILED transport unload: " + e);
            }
        }
        private static bool UnloadPrefix(
            TransportShip ship,
            Thing thingToDrop,
            List<Thing> droppedThings,
            bool unforbidAll,
            out NativeUnloadScope __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer)
                return true;
            if (ship?.shipThing == null ||
                !ship.shipThing.Spawned || thingToDrop == null)
            {
                return true;
            }
            if (_processingDeferred || !IsAsyncTimeActive())
            {
                // World ticks use the spectator faction even with async off.
                // Keep native execution timing, but restore the passenger's
                // owner context for spawning, draft and inventory decisions.
                Faction owner = (thingToDrop as Pawn)?.Faction ?? ship.shipThing.Faction;
                if (ship.shipThing is Building_PassengerShuttle && owner != null && owner.IsPlayer)
                {
                    __state = new NativeUnloadScope { Map = ship.shipThing.Map, Owner = owner, Previous = nativeScope };
                    nativeScope = __state;
                    PushFactionMethod.Invoke(null, new object[] { __state.Map, owner, true });
                    __state.Pushed = true;
                }
                return true;
            }

            // Desync-686 showed that the TryDrop result itself can differ on
            // peers before the destination map reaches its stable pre-tick
            // boundary. Queue the complete vanilla operation so the drop,
            // Notify_ThingRemoved, and pawn side effects share one map context.
            // A paused/slower destination can receive several world-side
            // attempts before its next pre-tick. Keep one operation per item;
            // otherwise successful drops leave duplicates retrying forever.
            foreach (DeferredUnloadOperation pending in PendingUnloadOperations)
            {
                if (pending.Ship == ship && pending.Thing == thingToDrop)
                {
                    pending.UnforbidAll |= unforbidAll;
                    return false;
                }
            }
            PendingUnloadOperations.Add(
                new DeferredUnloadOperation(
                    ship,
                    thingToDrop,
                    droppedThings,
                    unforbidAll));
            return false;
        }
        private static void MapPreTickPostfix(Map __instance)
        {
            if (MP.IsInMultiplayer && !_processingDeferred && __instance != null &&
                ReferenceEquals(TickingMapField.GetValue(null), __instance))
                ReplayPendingUnloadOperations(__instance);
        }

        private static Exception UnloadFinalizer(NativeUnloadScope __state, Exception __exception)
        {
            if (__state != null)
            {
                try { if (__state.Pushed) PopFactionMethod.Invoke(null, new object[] { __state.Map }); }
                finally { nativeScope = __state.Previous; }
            }
            return __exception;
        }

        private static bool IsPassengerHome(Map map)
        {
            bool vanilla = map.IsPlayerHome;
            if (!vanilla || nativeScope == null || nativeScope.Map != map ||
                !MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) || !multifaction)
                return vanilla;
            Faction owner = nativeScope.Owner;
            if (map.ParentFaction == owner &&
                (map.Parent?.def.canBePlayerHome == true || map.wasSpawnedViaGravShipLanding))
                return true;
            // Odyssey's generic home flag accepts any grav-engine or a past
            // landing. Another player's ship/base must not count as our home.
            if (map.listerThings.ThingsOfDef(ThingDefOf.GravEngine).Any(t => t.Faction == owner))
                return true;
            var packed = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForDef(ThingDefOf.GravEngine.minifiedDef),
                packed, allowUnreal: true, null, alsoGetSpawnedThings: true);
            return packed.Any(t => t.GetInnerIfMinified()?.Faction == owner);
        }

        private static IEnumerable<CodeInstruction> UsePassengerHomeContext(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Map), nameof(Map.IsPlayerHome));
            var replacement = AccessTools.Method(typeof(Patch_TransportShipUnloadMp), nameof(IsPassengerHome));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 2) throw new InvalidOperationException("REQUIRED_TARGET_FAILED passenger home checks=" + matches);
        }
        private static void ReplayPendingUnloadOperations(Map map)
        {
            if (!MP.IsInMultiplayer || map == null || PendingUnloadOperations.Count == 0)
                return;
            // Only the real AsyncTimeComp map context may reach this replay.
            // Multiplayer LoadPatch ticks every map once to initialize caches,
            // including paused maps. That synthetic tick must not unload cargo.
            object mapTime = AsyncTimeMethod.Invoke(null, new object[] { map });
            if (mapTime != null && (bool)MapPausedProperty.GetValue(mapTime))
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
                    !operation.Ship.shipThing.Spawned ||
                    operation.Ship.shipThing.Map != operation.SourceMap ||
                    (operation.UnloadJobId >= 0 && !operation.ShipJobMatches()) ||
                    operation.Ship.TransporterComp == null ||
                    !operation.Ship.TransporterComp.innerContainer.Contains(operation.Thing))
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
                    // An earlier operation or native callback may have
                    // transferred this item since the ready list was built.
                    if (!operation.Ship.TransporterComp.innerContainer.Contains(operation.Thing))
                        continue;
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
            // Drafter/inventory components can be absent while a pawn is in
            // the shuttle and are restored by SpawnSetup. Let vanilla inspect
            // the spawned pawn, rather than replaying pre-spawn eligibility.
            // Its OfPlayer-relative checks must use the actual pawn owner,
            // including when this shuttle lands at another player's base.
            Faction owner = (thingToDrop as Pawn)?.Faction ?? ship.shipThing.Faction;
            bool pushed = false;
            try
            {
                if (owner != null && owner.IsPlayer)
                {
                    PushFactionMethod.Invoke(null, new object[] { map, owner, false });
                    pushed = true;
                }
                ShipJob_Unload.UnloadThingFromShuttle(
                    ship, thingToDrop, operation.ResolveDroppedThings(), operation.UnforbidAll);
                return !ship.TransporterComp.innerContainer.Contains(thingToDrop);
            }
            finally
            {
                if (pushed)
                    PopFactionMethod.Invoke(null, new object[] { map });
            }
        }
    }

    // Game components are included by both vanilla saves and Multiplayer's
    // ExposeSmallComponents snapshot path. Pending operations belong to this
    // game, so cold joins and loading another save cannot retain stale objects.
    public sealed class TransportShipUnloadState : GameComponent
    {
        internal List<Patch_TransportShipUnloadMp.DeferredUnloadOperation> Pending =
            new List<Patch_TransportShipUnloadMp.DeferredUnloadOperation>();

        public TransportShipUnloadState(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Pending, "pendingTransportShipUnloads", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Pending == null)
                Pending = new List<Patch_TransportShipUnloadMp.DeferredUnloadOperation>();
        }
    }
}
