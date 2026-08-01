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
        private static bool _loggedComplexMapPawnScopeBypass;

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

            if (MpRuntimeInfo.RequiresVanillaPerMapPipelines(out string reason))
            {
                if (!_loggedComplexMapPawnScopeBypass)
                {
                    _loggedComplexMapPawnScopeBypass = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Per-pawn Rand scope bypassed: " +
                        $"Multiplayer vanilla per-map Rand context retained ({reason}).");
                }
                return;
            }

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
