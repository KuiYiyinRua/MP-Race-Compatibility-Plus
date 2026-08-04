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
    /// Romance On The Rim keeps a System.Random instance on its wedding lord.
    /// That instance is seeded from wall-clock state and may select different
    /// ceremony outcomes on host and client.  Replace only parameterless
    /// construction in the verified runtime assembly with a Rand-seeded instance.
    /// </summary>
    internal static class Patch_RjwRomanceRandom
    {
        private const string PackageId = "telardo.RomanceOnTheRim.chillkill190.pe";
        private const string AnchorTypeName = "RomanceOnTheRim.LordJob_WeddingCeremony";

        private static readonly ConstructorInfo ParameterlessRandom =
            AccessTools.Constructor(typeof(Random), Type.EmptyTypes);
        private static readonly MethodInfo CreateRandomMethod =
            AccessTools.Method(typeof(Patch_RjwRomanceRandom), nameof(CreateDeterministicRandom));

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type anchor = AccessTools.TypeByName(AnchorTypeName);
            MethodBase target = anchor == null ? null : AccessTools.Constructor(anchor, Type.EmptyTypes);
            if (target == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Romance] wedding-lord constructor signature drift; Random isolation was not applied.");
                return;
            }

            harmony.Patch(
                target,
                transpiler: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwRomanceRandom), nameof(RandomConstructorTranspiler))));
            Log.Message("[MP-MeowOnlineShop][RJW-Romance] wedding-lord System.Random construction is deterministic in multiplayer.");
        }

        // Harmony invokes this as a transpiler based on its IEnumerable signature;
        // keeping the method name separate from the factory prevents accidental
        // reflection of the wrong overload.
        private static IEnumerable<CodeInstruction> RandomConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj &&
                    instruction.operand is ConstructorInfo constructor &&
                    constructor == ParameterlessRandom)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = CreateRandomMethod;
                }
                yield return instruction;
            }
        }

        private static Random CreateDeterministicRandom()
        {
            return MP.IsInMultiplayer ? new Random(Rand.Int) : new Random();
        }
    }
}
