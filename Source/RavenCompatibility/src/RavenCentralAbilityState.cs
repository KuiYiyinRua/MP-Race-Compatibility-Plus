using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenCentralAbilityState
    {
        private static Type systemType, abilityDefType, counterType;
        private static readonly ConditionalWeakTable<GameComponent, Snapshot> snapshots = new ConditionalWeakTable<GameComponent, Snapshot>();
        private static object Read(object obj, string field) => AccessTools.Field(obj.GetType(), field).GetValue(obj);
        private static void Write(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
        internal static void Apply(Harmony harmony)
        {
            systemType = AccessTools.TypeByName("RavenRace.Features.CentralHub.Abilities.GameComponent_RavenCentralAbilitySystem");
            abilityDefType = AccessTools.TypeByName("RavenRace.Features.CentralHub.Abilities.RavenCentralAbilityDef");
            counterType = AccessTools.TypeByName("RavenRace.Features.CentralHub.Abilities.Workers.RavenCentralBuildingCountPawnBuffAbilityWorker");
            if (systemType == null || abilityDefType == null || counterType == null) throw new TypeLoadException("Raven central ability state");
            harmony.Patch(AccessTools.DeclaredMethod(systemType, "ExposeData"),
                postfix: new HarmonyMethod(typeof(RavenCentralAbilityState), nameof(Expose)));
            harmony.Patch(AccessTools.DeclaredMethod(systemType, "FinalizeInit"),
                postfix: new HarmonyMethod(typeof(RavenCentralAbilityState), nameof(AfterFinalizeInit)));
            harmony.Patch(AccessTools.DeclaredMethod(counterType, "CachedBuildingCount"),
                prefix: new HarmonyMethod(typeof(RavenCentralAbilityState), nameof(ReadOnlyCount)));
            harmony.Patch(AccessTools.DeclaredMethod(systemType, "SetAbilityActive"),
                transpiler: new HarmonyMethod(typeof(RavenCentralAbilityState), nameof(OrderedMutations)));
            foreach (var worker in new[] { counterType, AccessTools.TypeByName("RavenRace.Features.CentralHub.Abilities.Workers.RavenCentralPawnHediffAbilityWorker") })
                foreach (var name in new[] { "RefreshMapPawns", "RemoveAbilityHediffs" })
                    harmony.Patch(AccessTools.DeclaredMethod(worker, name), transpiler: new HarmonyMethod(typeof(RavenCentralAbilityState), nameof(OrderedPawns)));
        }

        private static List<Pawn> StablePawns(List<Pawn> pawns) => MP.IsInMultiplayer ? pawns.OrderBy(p => p.thingIDNumber).ToList() : pawns;
        private static IEnumerable<CodeInstruction> OrderedPawns(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(MapPawns), "FreeColonistsSpawned");
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.Calls(getter))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RavenCentralAbilityState), nameof(StablePawns)));
                    count++;
                }
            }
            if (count != 1) throw new InvalidOperationException("Raven central pawn iteration target count " + count);
        }

        private static IEnumerable<CodeInstruction> OrderedMutations(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            Type setType = typeof(HashSet<>).MakeGenericType(abilityDefType);
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == setType &&
                    (method.Name == "Add" || method.Name == "Remove"))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RavenCentralAbilityState), method.Name == "Add" ? nameof(AddOrdered) : nameof(RemoveOrdered));
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 2) throw new InvalidOperationException("Raven ability set mutation targets " + replaced);
        }
        private static bool AddOrdered(object set, Def ability) => ChangeOrdered(set, ability, "Add");
        private static bool RemoveOrdered(object set, Def ability) => ChangeOrdered(set, ability, "Remove");
        private static bool ChangeOrdered(object set, Def ability, string operation)
        {
            bool changed = (bool)set.GetType().GetMethod(operation).Invoke(set, new object[] { ability });
            if (changed && MP.IsInMultiplayer) Canonicalize(set);
            return changed;
        }
        private static void Canonicalize(object set)
        {
            // Reconstructing only live entries loses HashSet free slots on join. Reset
            // order immediately after each mutation, before worker callbacks consume it.
            var ordered = ((IEnumerable)set).Cast<Def>().OrderBy(d => d.defName, StringComparer.Ordinal).ToArray();
            set.GetType().GetMethod("Clear").Invoke(set, null);
            var add = set.GetType().GetMethod("Add");
            foreach (var def in ordered) add.Invoke(set, new object[] { def });
        }

        private static bool ReadOnlyCount(Map __0, ref int __result)
        {
            if (!MP.InInterface) return true;
            __result = __0 == null ? 0 : (int)AccessTools.DeclaredMethod(counterType, "CountRavenBuildings").Invoke(null, new object[] { __0 });
            return false;
        }

        private static void Expose(GameComponent __instance)
        {
            var state = snapshots.GetOrCreateValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving) state.Capture(__instance);
            if (!Scribe.EnterNode("meowRavenCentralAbilityState")) return;
            try { state.ExposeData(); } finally { Scribe.ExitNode(); }
        }
        private static void AfterFinalizeInit(GameComponent __instance)
        {
            if (snapshots.TryGetValue(__instance, out var state) && state.present) state.Restore(__instance);
        }

        private sealed class Counter : IExposable
        {
            public string ability;
            public Map map;
            public int count, tick;
            public Counter() { }
            public void ExposeData()
            {
                Scribe_Values.Look(ref ability, "ability");
                Scribe_References.Look(ref map, "map");
                Scribe_Values.Look(ref count, "count");
                Scribe_Values.Look(ref tick, "tick", -1);
            }
        }

        private sealed class Snapshot
        {
            public Snapshot() { }
            public bool present;
            private int tick;
            private List<string> active = new List<string>();
            private List<Counter> counters = new List<Counter>();
            public void Capture(GameComponent system)
            {
                present = true;
                tick = (int)Read(system, "lastAbilityTick");
                active = ((IEnumerable)Read(system, "activeAbilities")).Cast<Def>().Select(d => d.defName).ToList();
                counters = new List<Counter>();
                foreach (DictionaryEntry worker in (IDictionary)Read(system, "workerCache"))
                {
                    if (worker.Value.GetType() != counterType || (int)Read(worker.Value, "lastBuildingCountTick") < 0) continue;
                    counters.Add(new Counter { ability = ((Def)worker.Key).defName, map = (Map)Read(worker.Value, "countedMap"),
                        count = (int)Read(worker.Value, "cachedBuildingCount"), tick = (int)Read(worker.Value, "lastBuildingCountTick") });
                }
            }
            public void ExposeData()
            {
                Scribe_Values.Look(ref present, "present");
                Scribe_Values.Look(ref tick, "tick", -1);
                Scribe_Collections.Look(ref active, "active", LookMode.Value);
                Scribe_Collections.Look(ref counters, "counters", LookMode.Deep);
            }
            public void Restore(GameComponent system)
            {
                Write(system, "lastAbilityTick", tick);
                var set = Read(system, "activeAbilities");
                set.GetType().GetMethod("Clear").Invoke(set, null);
                foreach (string name in active)
                    set.GetType().GetMethod("Add").Invoke(set, new[] { GenDefDatabase.GetDef(abilityDefType, name) });
                if (MP.IsInMultiplayer) Canonicalize(set);
                foreach (var counter in counters)
                {
                    var def = GenDefDatabase.GetDef(abilityDefType, counter.ability);
                    var worker = AccessTools.DeclaredMethod(systemType, "WorkerFor").Invoke(system, new object[] { def });
                    if (worker.GetType() != counterType) throw new InvalidOperationException("Raven ability counter worker mismatch");
                    Write(worker, "countedMap", counter.map);
                    Write(worker, "cachedBuildingCount", counter.count);
                    Write(worker, "lastBuildingCountTick", counter.tick);
                }
            }
        }
    }
}
