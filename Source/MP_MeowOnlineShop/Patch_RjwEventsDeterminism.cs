using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RJW Events chooses a psychic-drone gender and orgy partner from pawn
    /// collections whose order can differ after save loading.  Keep the original
    /// Rand calls, but feed them a Thing-ID ordered collection.
    /// </summary>
    internal static class Patch_RjwEventsDeterminism
    {
        private const string PackageId = "c0ffee.rjw.events";
        private const string ConditionTypeName = "RJW_Events.GameCondition_PsychicArouse";
        private const string OrgyTypeName = "RJW_Events.JobGiver_FindOrgyPartner";

        private static readonly MethodInfo StableRandomElementMethod =
            AccessTools.Method(typeof(Patch_RjwEventsDeterminism), nameof(StableRandomElement));
        private static readonly MethodInfo StableWeightedMethod =
            AccessTools.Method(typeof(Patch_RjwEventsDeterminism), nameof(StableTryRandomElementByWeight));

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type condition = AccessTools.TypeByName(ConditionTypeName);
            Type orgy = AccessTools.TypeByName(OrgyTypeName);
            MethodInfo randomize = condition == null ? null : AccessTools.Method(condition, "RandomizeSettings");
            MethodInfo choosePartner = orgy == null ? null : AccessTools.Method(orgy, "BestPawnForOrgyExists");
            if (randomize == null || choosePartner == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Events] selection target signature drift; stable pawn ordering was not applied.");
                return;
            }

            harmony.Patch(randomize, transpiler: new HarmonyMethod(AccessTools.Method(
                typeof(Patch_RjwEventsDeterminism), nameof(RandomElementTranspiler))));
            harmony.Patch(choosePartner, transpiler: new HarmonyMethod(AccessTools.Method(
                typeof(Patch_RjwEventsDeterminism), nameof(WeightedElementTranspiler))));
            Log.Message("[MP-MeowOnlineShop][RJW-Events] stabilized psychic-drone and orgy-partner Pawn selection.");
        }

        private static IEnumerable<CodeInstruction> RandomElementTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && IsPawnRandomElement(instruction.operand as MethodInfo))
                {
                    instruction.operand = StableRandomElementMethod;
                }
                yield return instruction;
            }
        }

        private static IEnumerable<CodeInstruction> WeightedElementTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && IsPawnWeightedRandomElement(instruction.operand as MethodInfo))
                {
                    instruction.operand = StableWeightedMethod;
                }
                yield return instruction;
            }
        }

        private static Pawn StableRandomElement(IEnumerable<Pawn> pawns)
        {
            return pawns.OrderBy(pawn => pawn?.thingIDNumber ?? int.MinValue).RandomElement();
        }

        private static bool StableTryRandomElementByWeight(
            IEnumerable<Pawn> pawns,
            Func<Pawn, float> weightSelector,
            out Pawn result)
        {
            return pawns.OrderBy(pawn => pawn?.thingIDNumber ?? int.MinValue)
                .TryRandomElementByWeight(weightSelector, out result);
        }

        private static bool IsPawnRandomElement(MethodInfo method)
        {
            return method != null && method.Name == "RandomElement" && method.ReturnType == typeof(Pawn) &&
                   method.GetParameters().Length == 1;
        }

        private static bool IsPawnWeightedRandomElement(MethodInfo method)
        {
            ParameterInfo[] parameters = method?.GetParameters();
            return method != null && method.Name == "TryRandomElementByWeight" && method.ReturnType == typeof(bool) &&
                   parameters.Length == 3 && parameters[2].IsOut;
        }
    }
}
