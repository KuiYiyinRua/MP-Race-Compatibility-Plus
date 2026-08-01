using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Milira's active drop pod ticks its contained pawn while the pod itself is
    /// still inside an incoming skyfaller. Those hidden pawn ticks may issue jobs
    /// and consume map Rand before the pod is spawned. The pod opens immediately
    /// on spawn, so multiplayer can safely omit only this pre-spawn contained tick.
    /// </summary>
    internal static class Patch_MiliraActiveDropPod
    {
        internal static void Apply(Harmony harmony)
        {
            var podType = AccessTools.TypeByName("Milira.Milira_ActiveDropPod");
            var target = AccessTools.Method(podType, "Tick");
            var transpiler = AccessTools.Method(
                typeof(Patch_MiliraActiveDropPod),
                nameof(TickTranspiler));

            if (target == null || transpiler == null)
                return;

            harmony.Patch(
                target,
                transpiler: new HarmonyMethod(transpiler)
                {
                    priority = Priority.First
                });

            Log.Message(
                "[MP-MeowOnlineShop] Milira active drop-pod MP guard active.");
        }

        private static IEnumerable<CodeInstruction> TickTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var containedTick = AccessTools.Method(typeof(ThingOwner), "DoTick");
            var guardedTick = AccessTools.Method(
                typeof(Patch_MiliraActiveDropPod),
                nameof(TickContainedThingsUnlessMultiplayer));
            bool replaced = false;

            foreach (var instruction in instructions)
            {
                if (!replaced && instruction.Calls(containedTick))
                {
                    replaced = true;
                    yield return new CodeInstruction(OpCodes.Call, guardedTick)
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction);
                    continue;
                }

                yield return instruction;
            }

            if (!replaced)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira active drop-pod contained Tick " +
                    "call was not found; compatibility guard was not injected.");
            }
        }

        private static void TickContainedThingsUnlessMultiplayer(ThingOwner contents)
        {
            if (!MP.IsInMultiplayer)
                contents?.DoTick();
        }
    }
}
