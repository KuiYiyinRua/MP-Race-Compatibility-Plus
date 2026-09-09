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
    internal static class RavenOrderedResources
    {
        internal static void Apply(Harmony harmony)
        {
            foreach (var target in new[] {
                new[] { "RavenRace.Features.DefenseHub.Building_RavenDefenseHub", "ScanAndLinkUnmanagedTurrets" },
                new[] { "RavenRace.Features.DefenseHub.DefenseHubBuildCostCalculator", "TryConsumeBuildCost" },
                new[] { "RavenRace.Features.NanoConstruction.RavenNanoConstructionResourceUtility", "TryCreatePlan" },
                new[] { "RavenRace.Features.CustomPawn.Ui.SpecialPawnWorker.SpecialPawnWorker_ConsumeItem", "TryUnlockAndConsume" }
            })
            {
                var method = AccessTools.DeclaredMethod(AccessTools.TypeByName(target[0]), target[1]) ?? throw new MissingMethodException(target[0], target[1]);
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(RavenOrderedResources), nameof(OrderLists)));
            }
        }
        private static List<Thing> Ordered(List<Thing> things) => MP.IsInMultiplayer
            ? things.OrderBy(t => t.thingIDNumber).ToList() : things;
        private static IEnumerable<CodeInstruction> OrderLists(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var lookup = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef));
            int found = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.Calls(lookup))
                {
                    found++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RavenOrderedResources), nameof(Ordered)));
                }
            }
            if (found != 1) throw new InvalidOperationException("Raven stable resource order target count " + __originalMethod + ": " + found);
        }
    }
}
