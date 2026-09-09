using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenConveyorPacketState
    {
        private static Type systemType;
        private static readonly string[] fields = { "LineIndex", "PositionFixed", "ExitProgressFixed", "EntrySourceLineIndex", "EntryProgressFixed", "ActiveIndex" };
        private static readonly ConditionalWeakTable<Map, Snapshot> snapshots = new ConditionalWeakTable<Map, Snapshot>();
        private static object Read(object obj, string field) => AccessTools.Field(obj.GetType(), field).GetValue(obj);
        internal static void Apply(Harmony harmony)
        {
            systemType = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.MapComponent_RavenConveyorSystem")
                ?? throw new TypeLoadException("Raven conveyor packet system");
            harmony.Patch(AccessTools.Method(typeof(Map), "ExposeData"),
                postfix: new HarmonyMethod(typeof(RavenConveyorPacketState), nameof(Expose)));
            harmony.Patch(AccessTools.DeclaredMethod(systemType, "EnsureTopology"),
                postfix: new HarmonyMethod(typeof(RavenConveyorPacketState), nameof(AfterTopology)));
        }
        private static void Expose(Map __instance)
        {
            var network = __instance.components.FirstOrDefault(c => c.GetType() == systemType);
            if (network == null) return;
            var snapshot = snapshots.GetOrCreateValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving) snapshot.Capture(network);
            if (!Scribe.EnterNode("meowRavenConveyorPacketState")) return;
            try { snapshot.ExposeData(); } finally { Scribe.ExitNode(); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) snapshot.restore = snapshot.present && !snapshot.dirty;
        }
        private static void AfterTopology(MapComponent __instance)
        {
            if (MP.InInterface || (bool)Read(__instance, "topologyDirty")) return;
            if (!snapshots.TryGetValue(__instance.map, out var snapshot) || !snapshot.restore) return;
            snapshot.restore = false;
            snapshot.Restore(__instance);
        }
        private sealed class Packet : IExposable
        {
            public List<int> values = new List<int>();
            public IntVec3 source;
            public Packet() { }
            public void ExposeData()
            {
                Scribe_Collections.Look(ref values, "values", LookMode.Value);
                Scribe_Values.Look(ref source, "source", IntVec3.Invalid);
            }
        }
        private sealed class Line : IExposable
        {
            public List<int> packets = new List<int>();
            public Line() { }
            public void ExposeData() => Scribe_Collections.Look(ref packets, "packets", LookMode.Value);
        }
        private sealed class Snapshot
        {
            public Snapshot() { }
            public bool present, dirty, restore;
            private List<Packet> packets = new List<Packet>();
            private List<Line> lines = new List<Line>();
            public void Capture(object network)
            {
                present = true;
                dirty = (bool)Read(network, "topologyDirty");
                packets = new List<Packet>();
                lines = new List<Line>();
                if (dirty) return;
                var live = (IList)Read(network, "activePackets");
                var indices = new Dictionary<object, int>();
                for (int i = 0; i < live.Count; i++)
                {
                    indices.Add(live[i], i);
                    packets.Add(new Packet { values = fields.Select(f => (int)Read(live[i], f)).ToList(),
                        source = (IntVec3)Read(live[i], "EntrySourceCell") });
                }
                foreach (var line in (IEnumerable)Read(Read(network, "topology"), "Lines"))
                {
                    var queue = Read(line, "Packets");
                    var saved = new Line();
                    int count = (int)AccessTools.Property(queue.GetType(), "Count").GetValue(queue);
                    var indexer = AccessTools.Property(queue.GetType(), "Item");
                    for (int i = 0; i < count; i++) saved.packets.Add(indices[indexer.GetValue(queue, new object[] { i })]);
                    lines.Add(saved);
                }
            }
            public void ExposeData()
            {
                Scribe_Values.Look(ref present, "present");
                Scribe_Values.Look(ref dirty, "dirty");
                Scribe_Collections.Look(ref packets, "packets", LookMode.Deep);
                Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
            }
            public void Restore(object network)
            {
                var live = (IList)Read(network, "activePackets");
                var liveLines = (IList)Read(Read(network, "topology"), "Lines");
                if (live.Count != packets.Count || liveLines.Count != lines.Count)
                    throw new InvalidOperationException("Raven clean conveyor topology changed during packet restoration");
                for (int i = 0; i < packets.Count; i++)
                {
                    if (packets[i].values.Count != fields.Length) throw new InvalidOperationException("Invalid Raven packet state");
                    for (int f = 0; f < fields.Length; f++) AccessTools.Field(live[i].GetType(), fields[f]).SetValue(live[i], packets[i].values[f]);
                    AccessTools.Field(live[i].GetType(), "EntrySourceCell").SetValue(live[i], packets[i].source);
                }
                // Preserve equal-position packet ordering instead of relying on an unstable sort after loading.
                for (int i = 0; i < lines.Count; i++)
                {
                    var queue = Read(liveLines[i], "Packets");
                    AccessTools.Method(queue.GetType(), "Clear").Invoke(queue, null);
                    foreach (int index in lines[i].packets)
                    {
                        if (index < 0 || index >= live.Count) throw new InvalidOperationException("Invalid Raven packet index");
                        AccessTools.Method(queue.GetType(), "Add").Invoke(queue, new[] { live[index] });
                    }
                }
            }
        }
    }
}
