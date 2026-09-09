using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenLiquidNetworkState
    {
        private const string Name = "RavenRace.Features.RavenLiquidPipe.MapComponent_RavenLiquidNetwork";
        private static Type networkType;
        private static readonly ConditionalWeakTable<Map, NetworkSnapshot> snapshots = new ConditionalWeakTable<Map, NetworkSnapshot>();
        private static readonly string[] RegistryNames = { "registeredPipes", "registeredPorts", "registeredPumps", "registeredStorages",
            "pendingMixedAutoDisconnectPipes", "pendingStorageNotifications", "pendingStorageNotificationSet" };
        private static readonly string[] CounterNames = { "lastMixedMessageTick", "nextProductOutputBlockedMessageTick",
            "nextNetworkProcessIndex", "processCycleStartTick", "processedNetworksThisCycle", "processCycleNetworkCount" };

        internal static void Apply(Harmony harmony)
        {
            networkType = AccessTools.TypeByName(Name) ?? throw new TypeLoadException(Name);
            foreach (string field in RegistryNames.Concat(CounterNames).Concat(new[] { "pendingMixedAutoDisconnectTicks",
                "topologyDirty", "pendingStorageNotificationSet" }))
                if (AccessTools.DeclaredField(networkType, field) == null) throw new MissingFieldException(Name, field);
            harmony.Patch(AccessTools.Method(typeof(Map), "ExposeData"),
                postfix: new HarmonyMethod(typeof(RavenLiquidNetworkState), nameof(Expose)));
            harmony.Patch(AccessTools.DeclaredMethod(networkType, "RebuildNetworks"),
                postfix: new HarmonyMethod(typeof(RavenLiquidNetworkState), nameof(AfterRebuild)));
            harmony.Patch(AccessTools.DeclaredMethod(networkType, "EnsureNetworks"),
                prefix: new HarmonyMethod(typeof(RavenLiquidNetworkState), nameof(AllowTopologyWork)));
            var conveyor = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.MapComponent_RavenConveyorSystem");
            harmony.Patch(AccessTools.DeclaredMethod(conveyor, "EnsureTopology"),
                prefix: new HarmonyMethod(typeof(RavenLiquidNetworkState), nameof(AllowTopologyWork)));
        }

        // Both cache builders have simulation side effects. Defer UI reads of a dirty
        // cache until the next simulation tick/command instead of rebuilding during Draw.
        private static bool AllowTopologyWork() => !MP.InInterface;

        private static object Read(object obj, string field) => AccessTools.Field(obj.GetType(), field).GetValue(obj);
        private static void Write(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);

        private static void Expose(Map __instance)
        {
            if (!MP.enabled || networkType == null) return;
            var network = __instance.components.FirstOrDefault(c => c.GetType() == networkType);
            if (network == null) return;
            var state = snapshots.GetOrCreateValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving) state.Capture(network);
            if (!Scribe.EnterNode("meowRavenLiquidNetwork")) return;
            try { state.ExposeData(); }
            finally { Scribe.ExitNode(); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && state.present)
                state.RestoreRegistries(network);
        }

        private static void AfterRebuild(MapComponent __instance)
        {
            if (!snapshots.TryGetValue(__instance.map, out var state) || !state.restoreCycle) return;
            state.restoreCycle = false;
            for (int i = 0; i < CounterNames.Length; i++) Write(__instance, CounterNames[i], state.counters[i]);
        }

        private sealed class CompLink : IExposable
        {
            private ThingWithComps parent;
            private int index;
            public CompLink() { }
            public CompLink(ThingComp comp)
            {
                index = -1;
                if (comp?.parent == null || comp.parent.Destroyed) return;
                parent = comp.parent;
                index = parent.AllComps.IndexOf(comp);
            }
            public ThingComp Resolve()
            {
                if (index < 0) return null;
                if (parent == null || index >= parent.AllComps.Count)
                    throw new InvalidOperationException("Unresolved live Raven network component");
                return parent.AllComps[index];
            }
            public void ExposeData()
            {
                Scribe_References.Look(ref parent, "parent");
                Scribe_Values.Look(ref index, "compIndex", -1);
            }
        }

        private sealed class Registry : IExposable
        {
            public Registry() { }
            public string name;
            public List<CompLink> entries = new List<CompLink>();
            public void ExposeData()
            {
                Scribe_Values.Look(ref name, "field");
                Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            }
        }

        private sealed class NetworkSnapshot
        {
            public NetworkSnapshot() { }
            public bool present, restoreCycle;
            private bool topologyDirty;
            public List<int> counters = new List<int>();
            private List<int> pendingTicks = new List<int>();
            private List<Registry> registries = new List<Registry>();

            public void Capture(object network)
            {
                present = true;
                topologyDirty = (bool)Read(network, "topologyDirty");
                counters = CounterNames.Select(n => (int)Read(network, n)).ToList();
                pendingTicks = new List<int>((IEnumerable<int>)Read(network, "pendingMixedAutoDisconnectTicks"));
                registries = new List<Registry>();
                foreach (string name in RegistryNames)
                {
                    var registry = new Registry { name = name };
                    foreach (ThingComp comp in (IEnumerable)Read(network, name))
                        registry.entries.Add(new CompLink(comp));
                    registries.Add(registry);
                }
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref present, "present");
                Scribe_Values.Look(ref topologyDirty, "topologyDirty");
                Scribe_Collections.Look(ref counters, "counters", LookMode.Value);
                Scribe_Collections.Look(ref pendingTicks, "pendingTicks", LookMode.Value);
                Scribe_Collections.Look(ref registries, "registries", LookMode.Deep);
            }

            public void RestoreRegistries(object network)
            {
                if (counters == null || counters.Count != CounterNames.Length ||
                    registries == null || registries.Count < RegistryNames.Length - 1 || registries.Count > RegistryNames.Length ||
                    registries.Any(r => r == null || r.entries == null) ||
                    registries.Select(r => r.name).Distinct().Count() != registries.Count ||
                    RegistryNames.Take(RegistryNames.Length - 1).Any(name => !registries.Any(r => r.name == name)) ||
                    pendingTicks == null)
                    throw new InvalidOperationException("Invalid Raven network snapshot");
                foreach (var registry in registries)
                {
                    if (!RegistryNames.Contains(registry.name)) throw new InvalidOperationException("Unknown Raven registry");
                    var target = Read(network, registry.name);
                    target.GetType().GetMethod("Clear").Invoke(target, null);
                    foreach (var entry in registry.entries)
                    {
                        var comp = entry.Resolve();
                        target.GetType().GetMethod("Add").Invoke(target, new object[] { comp });
                    }
                }
                var ticks = (IList)Read(network, "pendingMixedAutoDisconnectTicks");
                ticks.Clear();
                foreach (int tick in pendingTicks) ticks.Add(tick);
                if (ticks.Count != ((IList)Read(network, "pendingMixedAutoDisconnectPipes")).Count)
                    throw new InvalidOperationException("Raven pending disconnect count mismatch");
                // Older candidate fixtures did not save the set separately. New saves
                // preserve it exactly: DeregisterStorage removes from the set but not the list.
                if (!registries.Any(r => r.name == "pendingStorageNotificationSet"))
                {
                    var notifications = Read(network, "pendingStorageNotificationSet");
                    notifications.GetType().GetMethod("Clear").Invoke(notifications, null);
                    var add = notifications.GetType().GetMethod("Add");
                    foreach (var comp in (IEnumerable)Read(network, "pendingStorageNotifications"))
                        if (comp != null) add.Invoke(notifications, new[] { comp });
                }
                // FinalizeInit rebuilds topology. Only a clean host topology has a cycle
                // that must survive that reconstruction.
                restoreCycle = !topologyDirty;
                Write(network, "topologyDirty", true);
                for (int i = 0; i < 2; i++) Write(network, CounterNames[i], counters[i]);
            }
        }
    }
}
