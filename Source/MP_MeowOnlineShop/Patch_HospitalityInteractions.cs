using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Hospitality 1.6 builds the random-interaction candidate list from
    /// MapPawns.AllPawnsSpawned and shuffles that list without first normalizing
    /// its insertion order. A cross-PC snapshot can contain the same pawns in a
    /// different list order, so identical Rand draws produce a different
    /// permutation and may take a different number of later weighted draws.
    /// </summary>
    internal static class Patch_HospitalityInteractions
    {
        private const string ReplacementTypeName =
            "Hospitality.Patches.Pawn_InteractionsTracker_Patch+TryInteractRandomly";

        private static int _shuffleReplacements;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!IsHospitalityActive())
                return;

            var replacementType = AccessTools.TypeByName(ReplacementTypeName);
            var replacement = replacementType == null
                ? null
                : AccessTools.Method(replacementType, "Replacement");
            if (replacement == null || replacement.ReturnType != typeof(bool) ||
                !replacement.IsStatic)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][Hospitality] random-interaction replacement " +
                    "signature not found; deterministic candidate shuffle was not applied.");
                return;
            }

            _shuffleReplacements = 0;
            harmony.Patch(
                replacement,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_HospitalityInteractions),
                        nameof(StableShuffleTranspiler))));

            if (_shuffleReplacements == 1)
            {
            Log.Message(
                "[MP-MeowOnlineShop][Hospitality] stabilized random-interaction " +
                "candidate order before its existing shuffle.");
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][Hospitality] expected one pawn Shuffle call in " +
                    $"TryInteractRandomly.Replacement, found {_shuffleReplacements}.");
            }
        }

        private static bool IsHospitalityActive()
        {
            // Nagisa.Orion.Hospitality is a translation-only package with no
            // assembly or declared dependency. It must not make us assume the
            // actual Hospitality simulation mod is active.
            return ModsConfig.IsActive("Orion.Hospitality");
        }

        private static IEnumerable<CodeInstruction> StableShuffleTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var stableShuffle = AccessTools.Method(
                typeof(Patch_HospitalityInteractions),
                nameof(StableShufflePawns));

            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo called &&
                    called.Name == "Shuffle" &&
                    called.IsGenericMethod &&
                    called.GetGenericArguments().Length == 1 &&
                    called.GetGenericArguments()[0] == typeof(Pawn))
                {
                    instruction.operand = stableShuffle;
                    _shuffleReplacements++;
                }

                yield return instruction;
            }
        }

        private static void StableShufflePawns(IList<Pawn> pawns)
        {
            if (pawns == null || pawns.Count <= 1)
                return;

            var stable = new List<Pawn>(pawns);
            stable.Sort((left, right) =>
                (left?.thingIDNumber ?? int.MinValue)
                    .CompareTo(right?.thingIDNumber ?? int.MinValue));
            for (int index = 0; index < stable.Count; index++)
                pawns[index] = stable[index];

            GenList.Shuffle(pawns);
        }
    }
}
