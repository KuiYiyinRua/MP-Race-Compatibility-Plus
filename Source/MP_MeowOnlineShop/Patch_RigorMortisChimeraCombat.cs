using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Keeps Rigor Mortis' Chilly Wind combat selection deterministic when an
    /// Anomaly Chimera is present in the target set.
    ///
    /// The original code consumes map Rand while iterating MapListerThings.AllThings
    /// and while choosing a weighted body part. Those collections are simulation
    /// inputs, so a different insertion order changes the number/order of Rand
    /// consumers during the same combat tick.
    /// </summary>
    internal static class Patch_RigorMortisChimeraCombat
    {
        private const string LogTag = "[MP-MeowOnlineShop] RigorMortis Chimera combat";

        private static readonly Type ChillyWindType =
            AccessTools.TypeByName("RigorMortis.HediffAbility_ChillyWind");
        private static readonly Type RmUtilityType =
            AccessTools.TypeByName("RigorMortis.RMUtility");

        private static int _targetOrderingReplacements;
        private static int _bodyPartOrderingReplacements;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            try
            {
                MethodInfo targetMethod = AccessTools.Method(ChillyWindType, "ApplyClawAttack");
                MethodInfo targetTranspiler = AccessTools.Method(
                    typeof(Patch_RigorMortisChimeraCombat),
                    nameof(ChillyWindTargetOrderingTranspiler));
                if (targetMethod != null && targetTranspiler != null)
                    harmony.Patch(targetMethod, transpiler: new HarmonyMethod(targetTranspiler));
                else
                    Log.Warning($"{LogTag}: ChillyWind target-order patch skipped; method not found.");

                MethodInfo damageMethod = AccessTools.Method(RmUtilityType, "DamageVictim");
                MethodInfo damageTranspiler = AccessTools.Method(
                    typeof(Patch_RigorMortisChimeraCombat),
                    nameof(DamageVictimBodyPartOrderingTranspiler));
                if (damageMethod != null && damageTranspiler != null)
                    harmony.Patch(damageMethod, transpiler: new HarmonyMethod(damageTranspiler));
                else
                    Log.Warning($"{LogTag}: RMUtility.DamageVictim body-part patch skipped; method not found.");

                Log.Message(
                    $"{LogTag}: targetOrder={_targetOrderingReplacements}, " +
                    $"bodyPartOrder={_bodyPartOrderingReplacements}.");
            }
            catch (Exception e)
            {
                Log.Warning($"{LogTag}: setup failed: {e.Message}");
            }
        }

        public static IEnumerable<CodeInstruction> ChillyWindTargetOrderingTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo stableFindAll = AccessTools.Method(
                typeof(Patch_RigorMortisChimeraCombat), nameof(StableFindAll));
            if (stableFindAll == null)
            {
                foreach (CodeInstruction instruction in instructions)
                    yield return instruction;
                yield break;
            }

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo called && IsThingFindAll(called))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = stableFindAll;
                    _targetOrderingReplacements++;
                }

                yield return instruction;
            }
        }

        public static IEnumerable<CodeInstruction> DamageVictimBodyPartOrderingTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo stableWeighted = AccessTools.Method(
                typeof(Patch_RigorMortisChimeraCombat),
                nameof(StableBodyPartRandomElementByWeight));
            if (stableWeighted == null)
            {
                foreach (CodeInstruction instruction in instructions)
                    yield return instruction;
                yield break;
            }

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo called &&
                    called.Name == "RandomElementByWeight" &&
                    called.IsGenericMethod &&
                    called.GetGenericArguments().Length == 1 &&
                    called.GetGenericArguments()[0] == typeof(BodyPartRecord))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = stableWeighted;
                    _bodyPartOrderingReplacements++;
                }

                yield return instruction;
            }
        }

        private static bool IsThingFindAll(MethodInfo method)
        {
            if (method == null || method.Name != nameof(List<Thing>.FindAll))
                return false;
            if (method.DeclaringType != typeof(List<Thing>))
                return false;

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(Predicate<Thing>);
        }

        private static List<Thing> StableFindAll(List<Thing> source, Predicate<Thing> predicate)
        {
            if (source == null)
                return new List<Thing>();

            List<Thing> result = source.FindAll(predicate);
            if (!MP.enabled || !MP.IsInMultiplayer || result.Count < 2)
                return result;

            return result
                .OrderBy(thing => thing?.thingIDNumber ?? int.MaxValue)
                .ThenBy(thing => thing?.def?.defName ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private static BodyPartRecord StableBodyPartRandomElementByWeight(
            IEnumerable<BodyPartRecord> source,
            Func<BodyPartRecord, float> weightSelector)
        {
            if (source == null)
                return null;

            IEnumerable<BodyPartRecord> ordered = source;
            if (MP.enabled && MP.IsInMultiplayer)
            {
                ordered = source
                    .OrderBy(GetBodyPartIndex)
                    .ThenBy(part => part?.def?.defName ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(part => part?.customLabel ?? string.Empty, StringComparer.Ordinal);
            }

            return ordered.RandomElementByWeight(weightSelector);
        }

        private static int GetBodyPartIndex(BodyPartRecord part)
        {
            if (part == null)
                return int.MaxValue;

            try
            {
                return part.Index;
            }
            catch
            {
                return int.MaxValue;
            }
        }
    }
}
