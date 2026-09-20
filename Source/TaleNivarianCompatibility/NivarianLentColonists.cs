using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using RimWorld;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianLentColonists
    {
        static MethodInfo predicate;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.QuestPart_LendColonistsToNivarian")
                ?? throw new TypeLoadException("Nivarian lend-colonists quest");
            predicate = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Patches.CompShuttle_ContainedColonistCount_Patch"),
                "IsFreeColonistAnyPlayerFaction", new[] { typeof(Pawn) })
                ?? throw new MissingMethodException("Multiplayer any-player colonist predicate");
            var enable = AccessTools.DeclaredMethod(type, "Enable", new[] { typeof(SignalArgs) })
                ?? throw new MissingMethodException(type.FullName, "Enable");
            harmony.Patch(enable, transpiler: new HarmonyMethod(typeof(NivarianLentColonists), nameof(Transpile)));
            Log.Message("[TaleNivarianCompat] Nivarian lent colonists use native Multiplayer faction predicate.");
        }
        static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsFreeColonist));
            var result = new List<CodeInstruction>();
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = predicate;
                    replaced++;
                }
                result.Add(instruction);
            }
            if (replaced != 1) throw new InvalidOperationException("Nivarian lend-colonists predicate sites=" + replaced);
            return result;
        }
    }
}
