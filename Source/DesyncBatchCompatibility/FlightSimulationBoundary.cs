using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class FlightSimulationBoundary
    {
        static MethodInfo unfog;
        static FieldInfo active;

        internal static void Apply(Harmony harmony)
        {
            var flight = Bootstrap.Type("ChezhouLib.ClThingComp.ThingComp_RaceFly");
            var visual = Bootstrap.Type("ChezhouLib.Patch.Patch_FlyingVisualEffects+Patch_FlyingDrawPos");
            active = Bootstrap.Field(flight, "isActionFiy", typeof(bool));
            unfog = Bootstrap.Method(visual, "UnfogAround", typeof(Map), typeof(IntVec3), flight);
            var draw = Bootstrap.Method(visual, "Postfix", typeof(Pawn), typeof(UnityEngine.Vector3).MakeByRefType());
            harmony.Patch(draw,
                prefix: new HarmonyMethod(typeof(FlightSimulationBoundary), nameof(VisualOnly)),
                transpiler: new HarmonyMethod(typeof(FlightSimulationBoundary), nameof(NoVisualUnfog)));
            harmony.Patch(Bootstrap.Method(flight, "CompTick"),
                postfix: new HarmonyMethod(typeof(FlightSimulationBoundary), nameof(TickUnfog)));
        }

        // MP stabilizes PawnTweener, but this later postfix adds realtime Sin()
        // to z. Verb_LaunchProjectile consumes Pawn.DrawPos as the actual origin.
        // Leave the underlying deterministic position intact during simulation.
        internal static bool VisualOnly() => !MP.IsInMultiplayer || MP.InInterface;

        internal static IEnumerable<CodeInstruction> NoVisualUnfog(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int count = 0;
            foreach (var instruction in code)
                if (instruction.Calls(unfog))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(FlightSimulationBoundary), nameof(UnfogInSinglePlayer));
                    count++;
                }
            if (count != 1) throw new InvalidOperationException("Flight visual unfog call count: " + count);
            return code;
        }

        static void UnfogInSinglePlayer(Map map, IntVec3 cell, ThingComp comp)
        {
            if (!MP.IsInMultiplayer) unfog.Invoke(null, new object[] { map, cell, comp });
        }

        internal static void TickUnfog(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer || MP.InInterface || !(bool)active.GetValue(__instance)) return;
            var pawn = __instance.parent as Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null) return;
            // The native radius uses serialized curVisualHeight. Re-evaluate while
            // climbing even without changing cells; do not consult the UI cache.
            unfog.Invoke(null, new object[] { pawn.Map, pawn.Position, __instance });
        }
    }
}
