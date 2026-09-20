using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianDroneSearchState
    {
        public sealed class Targets : IExposable
        {
            public string field;
            public List<Thing> things;
            public void ExposeData()
            {
                Scribe_Values.Look(ref field, "field");
                Scribe_Collections.Look(ref things, "things", LookMode.Reference);
            }
        }
        public sealed class Entry : IExposable
        {
            public string worker, payload;
            public bool fixNonFaction;
            public int factionId = -1, tick = -1;
            public List<Targets> targets;
            public void ExposeData()
            {
                Scribe_Values.Look(ref worker, "worker");
                Scribe_Values.Look(ref payload, "payload");
                Scribe_Values.Look(ref fixNonFaction, "fixNonFaction");
                Scribe_Values.Look(ref factionId, "factionId", -1);
                Scribe_Values.Look(ref tick, "tick", -1);
                Scribe_Collections.Look(ref targets, "targets", LookMode.Deep);
            }
        }
        public sealed class Claim : IExposable
        {
            public string worker;
            public Thing target, drone;
            public void ExposeData()
            {
                Scribe_Values.Look(ref worker, "worker");
                Scribe_References.Look(ref target, "target");
                Scribe_References.Look(ref drone, "drone");
            }
        }
        sealed class Snapshot { internal List<Entry> entries; internal List<Claim> claims; }
        static readonly ConditionalWeakTable<object, Snapshot> snapshots = new ConditionalWeakTable<object, Snapshot>();
        static readonly Dictionary<string, Type> payloads = new Dictionary<string, Type>();
        static Type component, keyType, entryType;
        static FieldInfo entriesField, claimsField, tickField, payloadField;
        static PropertyInfo workerProperty, fixProperty, factionProperty;
        static MethodInfo claimTarget;
        const string Namespace = "Nivarian.NivarianDrones.NivarianDroneTargetValidator.";

        internal static void Apply(Harmony harmony)
        {
            component = RequiredType("Nivarian.MapComp_NivarianDroneTargetSearch");
            keyType = RequiredType(Namespace + "DroneSearchCacheKey");
            entryType = RequiredType(Namespace + "DroneSearchCacheEntry");
            entriesField = RequiredField(component, "_entries"); claimsField = RequiredField(component, "_targetClaims");
            tickField = RequiredField(entryType, "LastRefreshTick"); payloadField = RequiredField(entryType, "Payload");
            workerProperty = AccessTools.Property(keyType, "WorkerType");
            fixProperty = AccessTools.Property(keyType, "FixNonFaction");
            factionProperty = AccessTools.Property(keyType, "FactionLoadId");
            claimTarget = AccessTools.DeclaredMethod(component, "ClaimTarget") ?? throw new MissingMethodException(component.FullName, "ClaimTarget");
            foreach (var name in new[] { "EnemyDroneSearchCachePayload", "EngineeringDroneSearchCachePayload", "HealDroneSearchCachePayload" })
            {
                var type = RequiredType(Namespace + name);
                payloads[type.FullName] = type;
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (!typeof(IList).IsAssignableFrom(field.FieldType)) throw new InvalidOperationException("Unsupported drone payload field " + field);
            }
            harmony.Patch(AccessTools.DeclaredMethod(typeof(MapComponent), "ExposeData"), postfix: Hook(nameof(Expose)));
            harmony.Patch(AccessTools.DeclaredMethod(component, "FinalizeInit"), postfix: Hook(nameof(AfterFinalize)));
            Log.Message("[TaleNivarianCompat] drone search cache snapshots and target claims persistence installed.");
        }
        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(NivarianDroneSearchState), name);
        static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        static FieldInfo RequiredField(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        // Packed/carried things may respawn before the next cache refresh. Keep their
        // references too; filtering merely despawned objects changes the host snapshot.
        static bool Persistent(Thing thing) => thing != null && !thing.Destroyed;

        static void Capture(object instance, Snapshot state)
        {
            state.entries = new List<Entry>(); state.claims = new List<Claim>();
            foreach (DictionaryEntry pair in (IDictionary)entriesField.GetValue(instance))
            {
                var data = new Entry { worker = ((Type)workerProperty.GetValue(pair.Key)).FullName,
                    fixNonFaction = (bool)fixProperty.GetValue(pair.Key), factionId = (int?)factionProperty.GetValue(pair.Key) ?? -1,
                    tick = (int)tickField.GetValue(pair.Value), targets = new List<Targets>() };
                var payload = payloadField.GetValue(pair.Value);
                if (payload != null)
                {
                    data.payload = payload.GetType().FullName;
                    if (!payloads.ContainsKey(data.payload)) throw new InvalidOperationException("Unsupported drone cache payload " + data.payload);
                    foreach (var field in payload.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.Name, StringComparer.Ordinal))
                        data.targets.Add(new Targets { field = field.Name, things = ((IEnumerable)field.GetValue(payload)).Cast<Thing>().Where(Persistent).ToList() });
                }
                state.entries.Add(data);
            }
            foreach (DictionaryEntry group in (IDictionary)claimsField.GetValue(instance))
                foreach (DictionaryEntry pair in (IDictionary)group.Value)
                    if (Persistent(pair.Key as Thing) && Persistent(pair.Value as Thing))
                        state.claims.Add(new Claim { worker = ((Type)group.Key).FullName, target = (Thing)pair.Key, drone = (Thing)pair.Value });
        }
        static void Expose(object __instance)
        {
            if (!component.IsInstanceOfType(__instance)) return;
            var state = snapshots.GetValue(__instance, key => new Snapshot());
            if (Scribe.mode == LoadSaveMode.Saving) Capture(__instance, state);
            Scribe_Collections.Look(ref state.entries, "meowDroneSearchEntries", LookMode.Deep);
            Scribe_Collections.Look(ref state.claims, "meowDroneSearchClaims", LookMode.Deep);
        }
        static void AfterFinalize(object __instance)
        {
            if (!snapshots.TryGetValue(__instance, out var state) || state.entries == null) return;
            // FinalizeInit normally refreshes caches. Restore afterwards so a joining peer
            // retains the host's exact 60-tick snapshot, including candidate order and age.
            var entries = (IDictionary)entriesField.GetValue(__instance); entries.Clear();
            foreach (var data in state.entries)
            {
                var worker = RequiredType(data.worker);
                var faction = data.factionId < 0 ? null : Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == data.factionId);
                if (data.factionId >= 0 && faction == null) continue;
                var key = Activator.CreateInstance(keyType, worker, data.fixNonFaction, faction);
                var entry = Activator.CreateInstance(entryType); tickField.SetValue(entry, data.tick);
                if (data.payload != null)
                {
                    if (!payloads.TryGetValue(data.payload, out var payloadType)) throw new InvalidOperationException("Unsupported saved drone payload " + data.payload);
                    var payload = Activator.CreateInstance(payloadType);
                    foreach (var targets in data.targets ?? new List<Targets>())
                    {
                        var list = (IList)RequiredField(payloadType, targets.field).GetValue(payload);
                        foreach (var thing in targets.things ?? new List<Thing>()) if (Persistent(thing)) list.Add(thing);
                    }
                    payloadField.SetValue(entry, payload);
                }
                entries.Add(key, entry);
            }
            ((IDictionary)claimsField.GetValue(__instance)).Clear();
            foreach (var claim in state.claims ?? new List<Claim>())
                if (Persistent(claim.target) && Persistent(claim.drone))
                    claimTarget.Invoke(__instance, new object[] { RequiredType(claim.worker), claim.target, claim.drone });
            snapshots.Remove(__instance);
        }
    }
}
