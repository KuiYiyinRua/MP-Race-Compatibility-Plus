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
    internal static class RavenConveyorNetworkState
    {
        private static Type networkType, utilityType;
        private static readonly ConditionalWeakTable<Map, Snapshot> states = new ConditionalWeakTable<Map, Snapshot>();
        private static object Read(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
        private static void Write(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        internal static void Apply(Harmony harmony)
        {
            networkType = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.MapComponent_RavenConveyorSystem");
            utilityType = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.RavenConveyorPortUtility");
            if (networkType == null || utilityType == null) throw new TypeLoadException("Raven conveyor runtime");
            foreach (string field in new[] { "registeredConveyors", "registeredPorts", "topologyDirty", "outputPorts",
                "inputPortsByLine", "outputPortProbeBuckets" })
                if (AccessTools.DeclaredField(networkType, field) == null) throw new MissingFieldException(networkType.FullName, field);
            harmony.Patch(AccessTools.Method(typeof(Map), "ExposeData"),
                postfix: new HarmonyMethod(typeof(RavenConveyorNetworkState), nameof(Expose)));
            harmony.Patch(AccessTools.DeclaredMethod(networkType, "EnsureTopology"),
                postfix: new HarmonyMethod(typeof(RavenConveyorNetworkState), nameof(AfterTopology)));
        }

        private static void Expose(Map __instance)
        {
            if (!MP.enabled) return;
            var network = __instance.components.FirstOrDefault(c => c.GetType() == networkType);
            if (network == null) return;
            var state = states.GetOrCreateValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving) state.Capture(network);
            if (!Scribe.EnterNode("meowRavenConveyorNetwork")) return;
            try { state.ExposeData(); } finally { Scribe.ExitNode(); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && state.present) state.RestoreRegistries(network);
        }

        private static void AfterTopology(MapComponent __instance)
        {
            if (MP.InInterface || (bool)Read(__instance, "topologyDirty")) return;
            if (!states.TryGetValue(__instance.map, out var state) || !state.restore) return;
            state.restore = false;
            state.RestoreBindings(__instance);
        }

        private sealed class Link : IExposable
        {
            private ThingWithComps parent;
            private int index = -1;
            public Link() { }
            public Link(ThingComp comp)
            {
                if (comp == null) return;
                parent = comp.parent;
                index = parent.AllComps.IndexOf(comp);
            }
            public ThingComp Resolve() => parent != null && index >= 0 && index < parent.AllComps.Count ? parent.AllComps[index] : null;
            public void ExposeData()
            {
                Scribe_References.Look(ref parent, "parent");
                Scribe_Values.Look(ref index, "index", -1);
            }
        }

        private sealed class Binding : IExposable
        {
            public Link port;
            public ThingWithComps target;
            public int tick;
            public bool input, hasAdapter;
            public Binding() { }
            public Binding(object source, bool input)
            {
                this.input = input;
                port = new Link((ThingComp)Read(source, "Port"));
                target = (ThingWithComps)Read(source, "Target");
                tick = (int)Read(source, input ? "NextTargetRefreshTick" : "NextProbeTick");
                hasAdapter = Read(source, input ? "Sink" : "Source") != null;
            }
            public void ExposeData()
            {
                Scribe_Deep.Look(ref port, "port");
                Scribe_References.Look(ref target, "target");
                Scribe_Values.Look(ref tick, "tick");
                Scribe_Values.Look(ref input, "input");
                Scribe_Values.Look(ref hasAdapter, "hasAdapter");
            }
            public void Restore(object binding)
            {
                Write(binding, "Target", target);
                Write(binding, input ? "NextTargetRefreshTick" : "NextProbeTick", tick);
                var adapter = hasAdapter ? AccessTools.Method(utilityType, input ? "SinkFor" : "SourceFor")
                    .Invoke(null, new object[] { target }) : null;
                Write(binding, input ? "Sink" : "Source", adapter);
            }
        }

        private sealed class Probe : IExposable
        {
            public Link port;
            public int bucket;
            public Probe() { }
            public void ExposeData()
            {
                Scribe_Deep.Look(ref port, "port");
                Scribe_Values.Look(ref bucket, "bucket");
            }
        }

        private sealed class Snapshot
        {
            public Snapshot() { }
            public bool present, restore;
            private bool dirty;
            private List<Link> conveyors = new List<Link>(), ports = new List<Link>();
            private List<Binding> bindings = new List<Binding>();
            private List<Probe> probes = new List<Probe>();
            public void Capture(object network)
            {
                present = true;
                dirty = (bool)Read(network, "topologyDirty");
                conveyors = ((IEnumerable)Read(network, "registeredConveyors")).Cast<ThingComp>().Select(c => new Link(c)).ToList();
                ports = ((IEnumerable)Read(network, "registeredPorts")).Cast<ThingComp>().Select(c => new Link(c)).ToList();
                bindings = new List<Binding>();
                probes = new List<Probe>();
                if (dirty) return;
                foreach (var binding in ((IDictionary)Read(network, "inputPortsByLine")).Values) bindings.Add(new Binding(binding, true));
                foreach (var binding in (IEnumerable)Read(network, "outputPorts")) bindings.Add(new Binding(binding, false));
                var buckets = (Array)Read(network, "outputPortProbeBuckets");
                if (buckets == null) return;
                for (int i = 0; i < buckets.Length; i++)
                    foreach (var binding in (IEnumerable)buckets.GetValue(i))
                        probes.Add(new Probe { bucket = i, port = new Link((ThingComp)Read(binding, "Port")) });
            }
            public void ExposeData()
            {
                Scribe_Values.Look(ref present, "present");
                Scribe_Values.Look(ref dirty, "dirty");
                Scribe_Collections.Look(ref conveyors, "conveyors", LookMode.Deep);
                Scribe_Collections.Look(ref ports, "ports", LookMode.Deep);
                Scribe_Collections.Look(ref bindings, "bindings", LookMode.Deep);
                Scribe_Collections.Look(ref probes, "probes", LookMode.Deep);
            }
            public void RestoreRegistries(object network)
            {
                RestoreList(network, "registeredConveyors", conveyors);
                RestoreList(network, "registeredPorts", ports);
                restore = !dirty;
                Write(network, "topologyDirty", true);
            }
            private static void RestoreList(object network, string field, List<Link> saved)
            {
                if (saved == null) throw new InvalidOperationException("Missing Raven conveyor registry");
                var list = (IList)Read(network, field);
                list.Clear();
                foreach (var link in saved) list.Add(link.Resolve());
            }
            public void RestoreBindings(object network)
            {
                var input = new Dictionary<ThingComp, object>();
                var output = new Dictionary<ThingComp, object>();
                foreach (var binding in ((IDictionary)Read(network, "inputPortsByLine")).Values)
                    input.Add((ThingComp)Read(binding, "Port"), binding);
                foreach (var binding in (IEnumerable)Read(network, "outputPorts"))
                    output.Add((ThingComp)Read(binding, "Port"), binding);
                foreach (var binding in bindings)
                {
                    var port = binding.port.Resolve();
                    if (port == null || !(binding.input ? input : output).TryGetValue(port, out var live))
                        throw new InvalidOperationException("Raven conveyor binding changed during load");
                    binding.Restore(live);
                }
                var buckets = (Array)Read(network, "outputPortProbeBuckets");
                foreach (IList bucket in buckets) bucket.Clear();
                foreach (var probe in probes)
                {
                    var port = probe.port.Resolve();
                    if (port == null || !output.TryGetValue(port, out var binding))
                        throw new InvalidOperationException("Unresolved Raven conveyor probe");
                    ((IList)buckets.GetValue(probe.bucket)).Add(binding);
                }
            }
        }
    }
}
