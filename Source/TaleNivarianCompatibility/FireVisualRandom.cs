using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class FireVisualRandom
    {
        internal static void Apply(Harmony harmony)
        {
            var smoke = AccessTools.DeclaredMethod(typeof(Fire), "SpawnSmokeParticles")
                ?? throw new MissingMethodException(typeof(Fire).FullName, "SpawnSmokeParticles");
            var tick = AccessTools.DeclaredMethod(typeof(Fire), "TickInterval", new[] { typeof(int) })
                ?? throw new MissingMethodException(typeof(Fire).FullName, "TickInterval");
            harmony.Patch(smoke,
                prefix: new HarmonyMethod(typeof(FireVisualRandom), nameof(BeforeSmoke)),
                finalizer: new HarmonyMethod(typeof(FireVisualRandom), nameof(AfterSmoke)));
            harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(FireVisualRandom), nameof(SparksOnly)));
            Log.Message("[TaleNivarianCompat] Fire smoke/spark visual RNG isolated; spread, damage and growth unchanged.");
        }

        // The unsaved smoke timer and particle visibility must not consume simulation RNG.
        // Scope only this visual method, never Fire.TickInterval or its spread/damage calls.
        static void BeforeSmoke(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }
        static void AfterSmoke(bool __state)
        {
            if (__state) Rand.PopState();
        }

        static IEnumerable<CodeInstruction> SparksOnly(IEnumerable<CodeInstruction> instructions)
        {
            var value = AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value));
            var replacement = AccessTools.Method(typeof(FireVisualRandom), nameof(ParticleChance));
            int count = 0;
            foreach (var instruction in instructions)
            {
                // In the installed 1.6 Fire.TickInterval this sole direct Rand.Value
                // decides ThrowMicroSparks; gameplay RNG lives in called methods.
                if (instruction.Calls(value))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Fire spark RNG target count=" + count);
        }
        static float ParticleChance()
        {
            if (!MP.IsInMultiplayer) return Rand.Value;
            Rand.PushState();
            try { return Rand.Value; }
            finally { Rand.PopState(); }
        }
    }
}
