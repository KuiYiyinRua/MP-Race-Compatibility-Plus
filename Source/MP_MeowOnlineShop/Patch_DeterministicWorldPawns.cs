using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// WorldPawns copies HashSet contents into a temporary list and then ticks
    /// that list in enumeration order.  A large mod stack can perturb HashSet
    /// insertion history independently on a joining process even when the set
    /// contains the same pawns.  Since each pawn health tick consumes world
    /// Rand, a different order advances the synchronized stream differently.
    /// Large content stacks also make the number of Rand calls within one pawn
    /// tick sensitive to optional comps. Scope MP Pawn.TickInterval calls by the
    /// stable pawn ID and game tick so one pawn cannot perturb the next one.
    /// Do not scope every Thing: Multiplayer owns the surrounding map Rand
    /// stream, and a global Thing.DoTick scope can interfere with its snapshots.
    /// </summary>
    internal static class Patch_DeterministicWorldPawns
    {
        private static readonly MethodInfo PawnListAddRange = AccessTools.Method(
            typeof(List<Pawn>),
            nameof(List<Pawn>.AddRange),
            new[] { typeof(IEnumerable<Pawn>) });

        private static readonly MethodInfo AddRangeStableMethod = AccessTools.Method(
            typeof(Patch_DeterministicWorldPawns),
            nameof(AddRangeStable));

        private static int _replacementCount;

        internal static void Apply(Harmony harmony)
        {
            var transpiler = new HarmonyMethod(AccessTools.Method(
                typeof(Patch_DeterministicWorldPawns),
                nameof(StableWorldPawnOrderTranspiler)));

            var worldTick = AccessTools.Method(
                typeof(WorldPawns),
                nameof(WorldPawns.WorldPawnsTick));
            var mothball = AccessTools.Method(typeof(WorldPawns), "DoMothballProcessing");
            var pawnTickInterval = AccessTools.Method(typeof(Pawn), "TickInterval");

            if (PawnListAddRange == null || AddRangeStableMethod == null ||
                worldTick == null || mothball == null || pawnTickInterval == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Deterministic WorldPawns guard could not resolve its exact RimWorld targets.");
                return;
            }

            harmony.Patch(worldTick, transpiler: transpiler);
            harmony.Patch(mothball, transpiler: transpiler);
            harmony.Patch(
                pawnTickInterval,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_DeterministicWorldPawns),
                    nameof(PawnTickPrefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_DeterministicWorldPawns),
                    nameof(PawnTickFinalizer))));

            if (_replacementCount != 3)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Deterministic WorldPawns signature drift: " +
                    $"expected AddRange replacements=3, actual={_replacementCount}.");
            }
            else
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Deterministic multiplayer WorldPawns order and per-pawn Rand scope active " +
                    "(3 tick-list boundaries). ");
            }
        }

        private static IEnumerable<CodeInstruction> StableWorldPawnOrderTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(PawnListAddRange))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AddRangeStableMethod;
                    _replacementCount++;
                }

                yield return instruction;
            }
        }

        private static void AddRangeStable(List<Pawn> destination, IEnumerable<Pawn> pawns)
        {
            destination.AddRange(pawns);
            if (!MP.IsInMultiplayer || destination.Count < 2)
                return;

            destination.Sort(ComparePawnIds);
        }

        private static int ComparePawnIds(Pawn left, Pawn right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;
            return left.thingIDNumber.CompareTo(right.thingIDNumber);
        }

        private static void PawnTickPrefix(Pawn __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            // Async-time changes the order in which otherwise equivalent pawn
            // TickInterval calls reach a map stream.  A role-change ritual can
            // make that visible immediately: its attendees receive different
            // jobs, then mental, interaction, health, and path branches draw a
            // different number of values before the next pawn is processed.
            // Scope every pawn tick, including the async/multi-map path, so a
            // pawn's optional random branch cannot advance another pawn's live
            // map Rand state.  Rand.PushState/PopState preserves Multiplayer's
            // surrounding per-map context and the stable seed preserves the
            // same result for this pawn and simulation tick on every peer.
            int seed = Gen.HashCombineInt(
                __instance.thingIDNumber,
                Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            __state = true;
        }

        private static Exception PawnTickFinalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
