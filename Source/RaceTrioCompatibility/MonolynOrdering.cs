using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynOrdering
    {
        internal static void Apply(Harmony harmony)
        {
            foreach (var site in new[] {
                "ASEL.ASELComponent|UpdateConnections", "ASEL.CompLightDistributor|PostMapInit",
                "ASEL.CompLightDistributor|loopDetecter", "ASEL.CompLightDistributor|PostDeSpawn",
                "ASEL.CompLightDistributor|UnregisterAllReceivers", "ASEL.MNPunishment|Tick" })
                harmony.Patch(Required(site), transpiler: new HarmonyMethod(typeof(MonolynOrdering), nameof(SetIteration)));
            foreach (var method in new[] { "FindMaterial", "FindBestMaterial" })
                harmony.Patch(Required("ASEL.WorkGiver_DeliverMonolynMaterials|" + method),
                    transpiler: new HarmonyMethod(typeof(MonolynOrdering), nameof(MaterialDistance)));
            harmony.Patch(Required("ASEL.WorkGiver_DeliverMonolynMaterials|PotentialWorkThingsGlobal"),
                postfix: new HarmonyMethod(typeof(MonolynOrdering), nameof(WorkOrder)));
            harmony.Patch(Required("ASEL.CompLightReceiver|AddDistributor"),
                postfix: new HarmonyMethod(typeof(MonolynOrdering), nameof(ReceiverOrder)));
            harmony.Patch(Required("ASEL.CompLightReceiver|UnregisterFromLightDistributors"),
                transpiler: new HarmonyMethod(typeof(MonolynOrdering), nameof(DisconnectEvent)));
            harmony.Patch(Required("ASEL.PillarOfSalt|Breaking"),
                transpiler: new HarmonyMethod(typeof(MonolynOrdering), nameof(PillarOrder)));
            foreach (var bill in new[] { "ASEL.Bill_ResurrectFont", "ASEL.Bill_ResurrectMech" })
                harmony.Patch(Required(bill + "|FindClosestCorpse"),
                    transpiler: new HarmonyMethod(typeof(MonolynOrdering), nameof(CorpseOrder)));
            harmony.Patch(Required("ASEL.RustFilth|Tick"),
                transpiler: new HarmonyMethod(typeof(MonolynOrdering), nameof(RustOrder)));
            foreach (var site in new[] {
                "ASEL.CompPhoneBooth|DownRandomColonist", "ASEL.CompPhoneBooth|FrenzyRandomColonist",
                "ASEL.CompPhoneBooth|DoubtRandomPawn", "ASEL.CompPhoneBooth|ConsumeRandomPawn",
                "ASEL.GameCondition_TheRust|GameConditionTick", "ASEL.ASELComponent|DeliveranceRandomPawn",
                "ASEL.CompSightSign|GetRandomPawnVisibleToCamera", "ASEL.CompAbilityEffect_DistributorBeam|CompTick",
                "ASEL.HediffComp_LightDamage|ApplyLightDamage", "ASEL.TrailProjectileNew|FindEnemy",
                "ASEL.TrailProjectileNew|FindClosestAlly" })
                harmony.Patch(Required(site), transpiler: new HarmonyMethod(typeof(MonolynOrdering), nameof(PawnChoice)));
            Log.Message("[MonolynOrdering] resolved=6 set traversals, 2 material ties, 11 pawn choices, pillar damage snapshot, 2 corpse ties, rust target ties, work/receiver order, disconnect context; missing=0");
        }
        static List<Thing> PillarThings(ListerThings lister) => MP.IsInMultiplayer ? lister.AllThings.OrderBy(t => t?.thingIDNumber ?? int.MinValue).ToList() : lister.AllThings;
        static List<Thing> CorpseThings(ListerThings lister, ThingRequestGroup group)
        {
            var things = lister.ThingsInGroup(group);
            return MP.IsInMultiplayer ? things.OrderBy(t => t?.thingIDNumber ?? int.MinValue).ToList() : things;
        }
        static IReadOnlyList<Pawn> RustPawns(MapPawns mapPawns) => MP.IsInMultiplayer ? mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber).ToList() : mapPawns.AllPawnsSpawned;
        static IEnumerable<Thing> RustInfectionThings(IntVec3 center, Map map, float radius, bool useCenter)
        {
            var things = GenRadial.RadialDistinctThingsAround(center, map, radius, useCenter);
            // Keep native nearest-cell priority; only equal-distance identities
            // need a stable tie break after things are respawned during a join.
            return MP.IsInMultiplayer ? things.OrderBy(t => (t.Position - center).LengthHorizontalSquared).ThenBy(t => t.thingIDNumber) : things;
        }
        static IEnumerable<CodeInstruction> PillarOrder(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo getter && getter.DeclaringType == typeof(ListerThings) && getter.Name == "get_AllThings")
                {
                    instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MonolynOrdering), nameof(PillarThings)); count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one native pillar list, found " + count);
        }
        static IEnumerable<CodeInstruction> CorpseOrder(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(ListerThings) && method.Name == "ThingsInGroup" &&
                    method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(ThingRequestGroup) }))
                {
                    instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MonolynOrdering), nameof(CorpseThings)); count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one native corpse group, found " + count);
        }
        static IEnumerable<CodeInstruction> RustOrder(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            int pawnReads = 0, infectionReads = 0;
            var snapshot = generator.DeclareLocal(typeof(IReadOnlyList<Pawn>));
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(MapPawns) && method.Name == "get_AllPawnsSpawned")
                {
                    if (pawnReads++ == 0)
                    {
                        instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MonolynOrdering), nameof(RustPawns));
                        yield return instruction;
                        yield return new CodeInstruction(OpCodes.Dup);
                        yield return new CodeInstruction(OpCodes.Stloc, snapshot);
                    }
                    else
                    {
                        // Replace the second getter while consuming its MapPawns
                        // receiver. Count and indexing use the same local snapshot.
                        instruction.opcode = OpCodes.Pop; instruction.operand = null;
                        yield return instruction;
                        yield return new CodeInstruction(OpCodes.Ldloc, snapshot);
                    }
                    continue;
                }
                if (instruction.operand is MethodInfo radial && radial.DeclaringType == typeof(GenRadial) && radial.Name == "RadialDistinctThingsAround" &&
                    radial.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(IntVec3), typeof(Map), typeof(float), typeof(bool) }))
                {
                    instruction.operand = AccessTools.Method(typeof(MonolynOrdering), nameof(RustInfectionThings)); infectionReads++;
                }
                yield return instruction;
            }
            if (pawnReads != 2 || infectionReads != 1) throw new InvalidOperationException("Native rust selector shape changed: " + pawnReads + "/" + infectionReads);
        }
        static Pawn ChoosePawn(IEnumerable<Pawn> source) =>
            (MP.IsInMultiplayer ? source.OrderBy(p => p == null ? int.MinValue : p.thingIDNumber) : source).RandomElement();
        static IEnumerable<CodeInstruction> PawnChoice(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(GenCollection) &&
                    method.Name == "RandomElement" && method.IsGenericMethod && method.GetParameters().Length == 1 &&
                    method.GetGenericArguments().SequenceEqual(new[] { typeof(Pawn) }))
                {
                    instruction.operand = AccessTools.Method(typeof(MonolynOrdering), nameof(ChoosePawn));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException(__originalMethod + " expected one random pawn choice, found " + count);
        }
        static MethodInfo Required(string site)
        {
            var parts = site.Split('|');
            var type = AccessTools.TypeByName(parts[0]) ?? throw new TypeLoadException(parts[0]);
            return AccessTools.DeclaredMethod(type, parts[1]) ?? throw new MissingMethodException(site);
        }
        static int Id(object value) => value is Thing thing ? thing.thingIDNumber :
            value is ThingComp comp ? comp.parent.thingIDNumber : throw new InvalidOperationException("Unexpected ordered Monolyn object");
        static HashSet<T>.Enumerator Enumerator<T>(HashSet<T> set) => new HashSet<T>(set.OrderBy(v => Id(v))).GetEnumerator();
        static List<T> SortedList<T>(IEnumerable<T> source) => source.OrderBy(v => Id(v)).ToList();
        static IEnumerable<CodeInstruction> SetIteration(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method)
                {
                    MethodInfo replacement = null;
                    if (method.Name == "GetEnumerator" && method.DeclaringType.IsGenericType &&
                        method.DeclaringType.GetGenericTypeDefinition() == typeof(HashSet<>))
                        replacement = AccessTools.Method(typeof(MonolynOrdering), nameof(Enumerator)).MakeGenericMethod(method.DeclaringType.GetGenericArguments());
                    else if (method.DeclaringType == typeof(Enumerable) && method.Name == "ToList" && method.IsGenericMethod)
                        replacement = AccessTools.Method(typeof(MonolynOrdering), nameof(SortedList)).MakeGenericMethod(method.GetGenericArguments());
                    if (replacement != null) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException(__originalMethod + " expected one set traversal, found " + count);
        }
        static IOrderedEnumerable<Thing> ByDistance(IEnumerable<Thing> source, Func<Thing, float> distance) => source.OrderBy(distance).ThenBy(t => t.thingIDNumber);
        static IEnumerable<CodeInstruction> MaterialDistance(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Enumerable) &&
                    method.Name == "OrderBy" && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(Thing), typeof(float) }) && method.GetParameters().Length == 2)
                { instruction.operand = AccessTools.Method(typeof(MonolynOrdering), nameof(ByDistance)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one material distance ordering, found " + count);
        }
        static void WorkOrder(ref IEnumerable<Thing> __result) => __result = __result.OrderBy(t => t.thingIDNumber);
        static void ReceiverOrder(ThingComp __instance)
        {
            var list = (IList)AccessTools.Field(__instance.GetType(), "cachedLightDistributors").GetValue(__instance);
            var ordered = list.Cast<object>().OrderBy(Id).ToArray();
            for (int i = 0; i < ordered.Length; i++) list[i] = ordered[i];
        }
        static IEnumerable<CodeInstruction> DisconnectEvent(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(LongEventHandler) && method.Name == "QueueLongEvent")
                { instruction.operand = AccessTools.Method(typeof(MonolynOrdering), nameof(Disconnect)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one deferred Monolyn disconnect, found " + count);
        }
        static void Disconnect(Action action, string text, bool asynchronous, Action<Exception> handler, bool extra, bool force, Action callback)
        {
            // Keep graph mutations in the command/map tick that despawned the receiver.
            if (MP.IsInMultiplayer) { action(); callback?.Invoke(); }
            else LongEventHandler.QueueLongEvent(action, text, asynchronous, handler, extra, force, callback);
        }
    }
}
