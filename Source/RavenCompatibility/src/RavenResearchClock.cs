using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Runtime = Multiplayer.Client.Multiplayer;

namespace MP_MeowOnlineShop
{
    // Research is game-global. The currently viewed map is not its clock in MP.
    internal static class RavenResearchClock
    {
        internal static void Apply(Harmony harmony, Type systemType)
        {
            var tick = AccessTools.DeclaredMethod(systemType, "GameComponentTick")
                ?? throw new MissingMethodException(systemType.FullName, "GameComponentTick");
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(RavenResearchClock), nameof(RecoverFutureTick)),
                transpiler: new HarmonyMethod(typeof(RavenResearchClock), nameof(UseResearchClock)));
        }

        internal static int ResearchTick(TickManager manager)
        {
            // MP increments worldTicks after DoSingleTick; game components run
            // after vanilla has advanced TicksGame inside that call.
            if (MP.IsInMultiplayer && Runtime.AsyncWorldTime != null)
                return Runtime.AsyncWorldTime.worldTicks + 1;
            return manager?.TicksGame ?? 0;
        }

        private static void RecoverFutureTick(ref int ___lastResearchTick)
        {
            // Old saves retain a timestamp from a faster map. Rebase only this
            // invalid scheduler marker; progress, costs and prerequisites remain native.
            if (___lastResearchTick > ResearchTick(Find.TickManager))
                ___lastResearchTick = -1;
        }

        internal static IEnumerable<CodeInstruction> UseResearchClock(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TicksGame));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RavenResearchClock), nameof(ResearchTick));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1)
                throw new InvalidOperationException("Raven research clock getter count " + count);
        }
    }
}
