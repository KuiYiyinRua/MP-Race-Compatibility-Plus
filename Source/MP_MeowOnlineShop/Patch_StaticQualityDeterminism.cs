using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Static Quality 1.3.0 creates a new time-seeded System.Random for every
    /// crafted quality modifier. Different peers therefore obtain different
    /// qualities even after the vanilla Verse.Rand quality roll matched.
    /// Seed that narrow Random instance from Multiplayer's active deterministic
    /// Rand stream while leaving the mod's single-player behavior unchanged.
    /// </summary>
    internal static class Patch_StaticQualityDeterminism
    {
        private const string TargetTypeName =
            "StaticQualityPlus.Patch_GenerateQualityCreatedByPawn";

        private static int constructorReplacementCount;
        private static bool loggedFirstDeterministicUse;

        internal static void Apply(Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName(TargetTypeName);
            if (targetType == null)
                return;

            MethodInfo target = AccessTools.Method(
                targetType,
                "Postfix",
                new[]
                {
                    typeof(RimWorld.QualityCategory),
                    typeof(int),
                    typeof(bool)
                });
            MethodInfo transpiler = AccessTools.Method(
                typeof(Patch_StaticQualityDeterminism),
                nameof(Transpiler));

            if (target == null || !target.IsStatic ||
                target.ReturnType != typeof(RimWorld.QualityCategory) ||
                target.GetParameters().Length != 3 || transpiler == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Static Quality deterministic Random " +
                    "target signature drift; patch skipped.");
                return;
            }

            constructorReplacementCount = 0;
            harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
            if (constructorReplacementCount == 1)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Static Quality multiplayer Random fix " +
                    "active: target=Postfix(QualityCategory,Int32,Boolean), " +
                    "timeSeededConstructorsReplaced=1.");
            }
            else
            {
                Log.Error(
                    "[MP-MeowOnlineShop] Static Quality deterministic Random " +
                    "patch is NOT active: expected one time-seeded Random " +
                    $"constructor, replaced={constructorReplacementCount}.");
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConstructorInfo randomDefault = AccessTools.Constructor(
                typeof(Random),
                Type.EmptyTypes);
            MethodInfo replacement = AccessTools.Method(
                typeof(Patch_StaticQualityDeterminism),
                nameof(CreateQualityRandom));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj &&
                    Equals(instruction.operand, randomDefault))
                {
                    var call = new CodeInstruction(OpCodes.Call, replacement);
                    call.labels.AddRange(instruction.labels);
                    call.blocks.AddRange(instruction.blocks);
                    constructorReplacementCount++;
                    yield return call;
                }
                else
                {
                    yield return instruction;
                }
            }

        }

        private static Random CreateQualityRandom()
        {
            if (!MP.IsInMultiplayer)
                return new Random();

            // Both peers are inside the same per-map Rand context here. One
            // synchronized draw preserves Static Quality's random distribution
            // without depending on wall-clock time or process scheduling.
            int seed = Rand.Int;
            if (!loggedFirstDeterministicUse)
            {
                loggedFirstDeterministicUse = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Static Quality deterministic Random " +
                    "executed in multiplayer (first-use proof; seed sourced from Verse.Rand).");
            }
            return new Random(seed);
        }
    }
}
