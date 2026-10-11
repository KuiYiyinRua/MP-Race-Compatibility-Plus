using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    // Movement bucket insertion order and the bounded cargo FIFO are gameplay
    // state. Rebuilding from the drone list changes both timing and cargo IDs.
    internal static class RavenPendingMovement
    {
        internal sealed class Snapshot : IExposable
        {
            public List<int> Events = new List<int>(), Cargo = new List<int>();
            public List<float> Tuning = new List<float>();
            public List<int> Docked = new List<int>(), ReleaseCells = new List<int>();
            public List<Thing> ReleaseBoxes = new List<Thing>();
            public bool Rebuild, Replan, Compact;
            public Snapshot() { }
            public void ExposeData()
            {
                Scribe_Collections.Look(ref Events, "events", LookMode.Value);
                Scribe_Collections.Look(ref Cargo, "cargo", LookMode.Value);
                Scribe_Collections.Look(ref Tuning, "tuning", LookMode.Value);
                Scribe_Collections.Look(ref Docked, "docked", LookMode.Value);
                Scribe_Collections.Look(ref ReleaseCells, "releaseCells", LookMode.Value);
                Scribe_Collections.Look(ref ReleaseBoxes, "releaseBoxes", LookMode.Reference);
                Scribe_Values.Look(ref Rebuild, "rebuild");
                Scribe_Values.Look(ref Replan, "replan");
                Scribe_Values.Look(ref Compact, "compact");
            }
        }
        sealed class Pending { public Pending() { } internal Snapshot Value; internal bool NeedsNormalize; }
        static readonly ConditionalWeakTable<object, Pending> pending = new ConditionalWeakTable<object, Pending>();
        static FieldInfo drones, scheduled, cargo, buckets, arrivals, rebuild, tuningRevision, id, movementRevision;
        static FieldInfo eventDrone, eventTick, eventKind, eventRevision, cargoDrone, cargoKind, currentRevision;
        static MethodInfo enqueueEvent, enqueueCargo, clearEvent, clearCargo, tuningCurrent;
        static FieldInfo[] tuningValues;
        static FieldInfo docked, dockedSet, compact, releases, releaseBox, releaseCell;
        static MethodInfo addDocked, clearDockedSet, clearReleases, addRelease;
        internal static void Apply(Harmony harmony)
        {
            var manager = Bootstrap.Type("RavenRace.Features.Drone.Hauling.MapComponent_DroneManager");
            var drone = Bootstrap.Type("RavenRace.Features.Drone.Hauling.RavenHaulDrone");
            drones = AccessTools.DeclaredField(manager, "drones");
            scheduled = AccessTools.DeclaredField(manager, "scheduledMovementEvents");
            cargo = AccessTools.DeclaredField(manager, "cargoArrivalQueue");
            rebuild = Bootstrap.Field(manager, "scheduledMovementNeedsRebuild", typeof(bool));
            tuningRevision = Bootstrap.Field(manager, "scheduledMovementTuningRevision", typeof(int));
            id = Bootstrap.Field(drone, "Id", typeof(int));
            movementRevision = Bootstrap.Field(drone, "MovementRevision", typeof(int));
            if (drones == null || scheduled == null || cargo == null) throw new MissingFieldException("Raven movement queues");
            buckets = AccessTools.DeclaredField(scheduled.FieldType, "buckets");
            arrivals = AccessTools.DeclaredField(cargo.FieldType, "arrivals");
            if (buckets == null || arrivals == null) throw new MissingFieldException("Raven movement queue storage");
            var ev = buckets.FieldType.GetElementType().GetGenericArguments().Single();
            var ca = arrivals.FieldType.GetGenericArguments().Single();
            eventDrone = Bootstrap.Field(ev, "Drone", drone);
            eventTick = Bootstrap.Field(ev, "DueTick", typeof(int));
            eventKind = Bootstrap.Field(ev, "Kind", typeof(int));
            eventRevision = Bootstrap.Field(ev, "Revision", typeof(int));
            cargoDrone = Bootstrap.Field(ca, "Drone", drone);
            cargoKind = Bootstrap.Field(ca, "Kind", typeof(int));
            enqueueEvent = Bootstrap.Method(scheduled.FieldType, "Enqueue", drone, typeof(int), typeof(int), typeof(int));
            enqueueCargo = Bootstrap.Method(cargo.FieldType, "Enqueue", drone, typeof(int));
            clearEvent = Bootstrap.Method(scheduled.FieldType, "Clear");
            clearCargo = Bootstrap.Method(cargo.FieldType, "Clear");
            tuningCurrent = AccessTools.PropertyGetter(Bootstrap.Type("RavenRace.Features.RavenLiquidPipe.RavenIndustrialRuntimeTuning"), "Current")
                ?? throw new MissingMemberException("RavenIndustrialRuntimeTuning.Current");
            currentRevision = Bootstrap.Field(tuningCurrent.ReturnType, "Revision", typeof(int));
            tuningValues = AccessTools.GetDeclaredFields(tuningCurrent.ReturnType).Where(f => f.FieldType == typeof(float) && !f.IsStatic)
                .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
            if (tuningValues.Length != 10) throw new MissingFieldException("Raven runtime tuning snapshot layout changed");
            docked = AccessTools.DeclaredField(manager, "pendingDockedDrones");
            dockedSet = AccessTools.DeclaredField(manager, "pendingDockedDroneSet");
            releases = AccessTools.DeclaredField(manager, "pendingAutomaticClaimReleases");
            compact = Bootstrap.Field(manager, "droneListNeedsCompaction", typeof(bool));
            if (docked == null || dockedSet == null || releases == null) throw new MissingFieldException("Raven pending dock/release state");
            addDocked = Bootstrap.Method(dockedSet.FieldType, "Add", drone);
            clearDockedSet = Bootstrap.Method(dockedSet.FieldType, "Clear");
            clearReleases = Bootstrap.Method(releases.FieldType, "Clear");
            var release = releases.FieldType.GetGenericArguments().Single();
            releaseBox = AccessTools.DeclaredField(release, "Box");
            releaseCell = Bootstrap.Field(release, "CellId", typeof(int));
            if (releaseBox == null || !typeof(Thing).IsAssignableFrom(releaseBox.FieldType)) throw new MissingFieldException("Raven release Box");
            addRelease = Bootstrap.Method(manager, "QueueAutomaticClaimRelease", releaseBox.FieldType, typeof(int));
            harmony.Patch(Bootstrap.Method(manager, "ExposeData"),
                prefix: new HarmonyMethod(typeof(RavenPendingMovement), nameof(BeforeSave)),
                postfix: new HarmonyMethod(typeof(RavenPendingMovement), nameof(AfterLoad)));
            harmony.Patch(Bootstrap.Method(manager, "TickScheduledDroneMovement"),
                prefix: new HarmonyMethod(typeof(RavenPendingMovement), nameof(BeforeMovement)));
        }
        static List<float> Values(object tuning) => tuningValues.Select(f => (float)f.GetValue(tuning)).ToList();
        static Dictionary<int, object> DroneIndex(object manager) =>
            ((IEnumerable)drones.GetValue(manager)).Cast<object>().Where(d => d != null).ToDictionary(d => (int)id.GetValue(d));
        static Snapshot Capture(object manager)
        {
            var index = DroneIndex(manager);
            object tuning = tuningCurrent.Invoke(null, null);
            bool normalize = pending.TryGetValue(manager, out var previous) && previous.NeedsNormalize;
            var values = Values(tuning);
            // Commands can change tuning between loading and the first map
            // tick. Preserve that change as a native replan, rather than hiding
            // it when normalizing the process-local revision counter.
            bool tuningChanged = normalize && (previous.Value.Tuning == null || !previous.Value.Tuning.SequenceEqual(values));
            bool replan = normalize ? previous.Value.Replan || tuningChanged : (int)tuningRevision.GetValue(manager) != (int)currentRevision.GetValue(tuning);
            var result = new Snapshot { Rebuild = (bool)rebuild.GetValue(manager), Replan = replan, Tuning = values,
                Compact = (bool)compact.GetValue(manager) };
            foreach (var bucket in (Array)buckets.GetValue(scheduled.GetValue(manager)))
            {
                if (bucket == null) continue;
                foreach (var entry in (IEnumerable)bucket)
                {
                    var drone = eventDrone.GetValue(entry);
                    if (drone == null || (int)eventRevision.GetValue(entry) != (int)movementRevision.GetValue(drone)) continue;
                    int key = (int)id.GetValue(drone);
                    if (!index.TryGetValue(key, out var live) || !ReferenceEquals(live, drone))
                        throw new InvalidOperationException("Scheduled Raven drone absent from saved manager: " + key);
                    result.Events.Add(key); result.Events.Add((int)eventTick.GetValue(entry)); result.Events.Add((int)eventKind.GetValue(entry));
                }
            }
            foreach (var entry in (IEnumerable)arrivals.GetValue(cargo.GetValue(manager)))
            {
                var drone = cargoDrone.GetValue(entry);
                int key = drone == null ? -1 : (int)id.GetValue(drone);
                if (!index.TryGetValue(key, out var live) || !ReferenceEquals(live, drone))
                    throw new InvalidOperationException("Queued Raven drone absent from saved manager: " + key);
                result.Cargo.Add(key); result.Cargo.Add((int)cargoKind.GetValue(entry));
            }
            foreach (var drone in (IEnumerable)docked.GetValue(manager))
            {
                if (drone == null) continue; // Native processor ignores null entries.
                int key = (int)id.GetValue(drone);
                if (!index.TryGetValue(key, out var live) || !ReferenceEquals(live, drone))
                    throw new InvalidOperationException("Docking Raven drone absent from saved manager: " + key);
                result.Docked.Add(key);
            }
            foreach (var entry in (IEnumerable)releases.GetValue(manager))
            {
                result.ReleaseBoxes.Add((Thing)releaseBox.GetValue(entry));
                result.ReleaseCells.Add((int)releaseCell.GetValue(entry));
            }
            return result;
        }
        static void BeforeSave(object __instance)
        {
            if (!MP.IsInMultiplayer) return;
            var state = pending.GetOrCreateValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving) state.Value = Capture(__instance);
            Scribe_Deep.Look(ref state.Value, "mpMeowRavenMovement");
        }
        static void AfterLoad(object __instance)
        {
            if (!MP.IsInMultiplayer) return;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (!pending.GetOrCreateValue(__instance).NeedsNormalize) pending.Remove(__instance);
                return;
            }
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            var state = pending.GetOrCreateValue(__instance).Value;
            // Legacy snapshots cannot recover queue ordering. Retain native
            // migration rather than inventing event timestamps or cargo IDs.
            if (state == null) { pending.Remove(__instance); return; }
            if (state.Events == null || state.Events.Count % 3 != 0 || state.Cargo == null || state.Cargo.Count % 2 != 0)
                throw new InvalidOperationException("Malformed Raven pending-movement snapshot");
            var index = DroneIndex(__instance);
            object events = scheduled.GetValue(__instance), fifo = cargo.GetValue(__instance);
            clearEvent.Invoke(events, null); clearCargo.Invoke(fifo, null);
            for (int i = 0; i < state.Events.Count; i += 3)
            {
                var drone = index[state.Events[i]];
                enqueueEvent.Invoke(events, new[] { drone, state.Events[i + 1], state.Events[i + 2], movementRevision.GetValue(drone) });
            }
            for (int i = 0; i < state.Cargo.Count; i += 2)
                if (!(bool)enqueueCargo.Invoke(fifo, new[] { index[state.Cargo[i]], state.Cargo[i + 1] }))
                    throw new InvalidOperationException("Duplicate or invalid Raven cargo snapshot entry");
            var docking = (IList)docked.GetValue(__instance);
            docking.Clear(); clearDockedSet.Invoke(dockedSet.GetValue(__instance), null);
            if (state.Docked != null)
                foreach (int key in state.Docked)
                {
                    var drone = index[key];
                    if (!(bool)addDocked.Invoke(dockedSet.GetValue(__instance), new[] { drone }))
                        throw new InvalidOperationException("Duplicate Raven docking snapshot entry");
                    docking.Add(drone);
                }
            compact.SetValue(__instance, state.Docked == null || state.Compact);
            clearReleases.Invoke(releases.GetValue(__instance), null);
            if (state.ReleaseBoxes != null || state.ReleaseCells != null)
            {
                if (state.ReleaseBoxes == null || state.ReleaseCells == null || state.ReleaseBoxes.Count != state.ReleaseCells.Count)
                    throw new InvalidOperationException("Malformed Raven claim-release snapshot");
                for (int i = 0; i < state.ReleaseCells.Count; i++)
                    addRelease.Invoke(__instance, new object[] { state.ReleaseBoxes[i], state.ReleaseCells[i] });
            }
            rebuild.SetValue(__instance, state.Rebuild);
            pending.GetOrCreateValue(__instance).NeedsNormalize = true;
            // Runtime tuning revisions are process-local. Normalize only after
            // all post-load initialization, preserving a pending native replan.
        }
        static void BeforeMovement(object __instance)
        {
            if (!MP.IsInMultiplayer || !pending.TryGetValue(__instance, out var state) || !state.NeedsNormalize || state.Value == null) return;
            object tuning = tuningCurrent.Invoke(null, null);
            int revision = (int)currentRevision.GetValue(tuning);
            bool replan = state.Value.Replan || state.Value.Tuning == null || !state.Value.Tuning.SequenceEqual(Values(tuning));
            tuningRevision.SetValue(__instance, replan ? unchecked(revision - 1) : revision);
            pending.Remove(__instance);
        }
    }
}
