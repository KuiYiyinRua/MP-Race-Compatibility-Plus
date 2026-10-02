using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    // PitGate's collapse camera shake is checked only for Find.CurrentMap.
    // Different peer camera maps must not advance the shared map Rand stream.
    internal static class Patch_PitGateVisualRandom
    {
        private const int SeedSalt = 0x50495447;
        private static readonly MethodInfo MtbMethod = AccessTools.Method(typeof(Rand),
            nameof(Rand.MTBEventOccurs), new[] { typeof(float), typeof(float), typeof(float) });
        private static readonly MethodInfo VisualMtbMethod = AccessTools.Method(
            typeof(Patch_PitGateVisualRandom), nameof(VisualMtb));

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;
            try
            {
                MethodInfo tick = AccessTools.DeclaredMethod(typeof(PitGate), "Tick");
                if (tick == null || MtbMethod == null || VisualMtbMethod == null)
                    throw new MissingMethodException("PitGate.Tick visual MTB boundary");
                harmony.Patch(tick, transpiler: new HarmonyMethod(
                    typeof(Patch_PitGateVisualRandom), nameof(Transpiler)));
                Log.Message("[MP-MeowOnlineShop] PitGate collapse camera-shake Rand isolated.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILURE PitGate visual Rand: " + e);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = instructions.ToList();
            int replacements = 0;
            for (int i = 3; i < result.Count; i++)
            {
                if (result[i].opcode != OpCodes.Call || !Equals(result[i].operand, MtbMethod) ||
                    !IsFloat(result[i - 3], 2f) || !IsFloat(result[i - 2], 60f) ||
                    !IsFloat(result[i - 1], 1f))
                    continue;

                result.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                i++;
                result[i].operand = VisualMtbMethod;
                replacements++;
            }
            if (replacements != 1)
                throw new InvalidOperationException("Expected one PitGate visual MTB call, found " + replacements);
            return result;
        }

        private static bool IsFloat(CodeInstruction instruction, float value) =>
            instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float number && number == value;

        private static bool VisualMtb(float mtb, float mtbUnit, float checkDuration, PitGate gate)
        {
            if (!MP.IsInMultiplayer || gate?.Map == null)
                return Rand.MTBEventOccurs(mtb, mtbUnit, checkDuration);

            int seed = Gen.HashCombineInt(SeedSalt, gate.thingIDNumber);
            seed = Gen.HashCombineInt(seed, Find.TickManager.TicksGame);
            int state = 0;
            Map mapForPop;
            DeterministicRandScope.Begin(gate.Map, seed, SeedSalt + 1,
                ref state, out mapForPop, ignoreGate: true);
            try
            {
                return Rand.MTBEventOccurs(mtb, mtbUnit, checkDuration);
            }
            finally
            {
                DeterministicRandScope.End(state, mapForPop);
            }
        }
    }
}
