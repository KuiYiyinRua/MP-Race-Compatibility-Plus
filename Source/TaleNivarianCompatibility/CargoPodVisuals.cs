using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class CargoPodVisuals
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("RatkinUnderground.RKU_DrillingCargoPodBullet");
            if (type == null) return;
            var tick = AccessTools.DeclaredMethod(type, "Tick", Type.EmptyTypes)
                ?? throw new MissingMethodException(type.FullName, "Tick");
            harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(CargoPodVisuals), nameof(Dust)));
            Log.Message("[CargoPodVisuals] resolved=1 native dust branch; viewed-map pause and 2 cosmetic offsets isolated; missing=0");
        }
        static IEnumerable<CodeInstruction> Dust(IEnumerable<CodeInstruction> instructions)
        {
            var paused = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.Paused));
            var range = AccessTools.Method(typeof(Rand), nameof(Rand.Range), new[] { typeof(float), typeof(float) });
            int pauses = 0, offsets = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(paused))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(CargoPodVisuals), nameof(DustPaused));
                    pauses++;
                }
                else if (code.Calls(range))
                {
                    // Exact installed Tick has only two float ranges, both
                    // arguments to the cosmetic dust-position vector.
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(CargoPodVisuals), nameof(DustOffset));
                    offsets++;
                }
                yield return code;
            }
            if (pauses != 1 || offsets != 2)
                throw new InvalidOperationException("Cargo pod dust IL changed: " + pauses + "/" + offsets);
        }
        // Native Tick is already an authoritative simulation step. MP's Paused
        // getter consults Find.CurrentMap, which can be a different paused map.
        static bool DustPaused(TickManager manager) => !MP.IsInMultiplayer && manager.Paused;
        // These values affect only a fleck's position, never flight/cargo state.
        static float DustOffset(float min, float max) => MP.IsInMultiplayer ? UnityEngine.Random.Range(min, max) : Rand.Range(min, max);
    }
}
