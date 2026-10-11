using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using RimWorld;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianLentColonists
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.QuestPart_LendColonistsToNivarian")
                ?? throw new TypeLoadException("Nivarian lend-colonists quest");
            var collect = AccessTools.DeclaredMethod(type, "TryRememberLent", new[] { typeof(Thing) })
                ?? throw new MissingMethodException(type.FullName, "TryRememberLent");
            harmony.Patch(collect, transpiler: new HarmonyMethod(typeof(NivarianLentColonists), nameof(Transpile)));
            Log.Message("[TaleNivarianCompat] Nivarian lent-colonist collection recognizes every player faction; native duplicate and humanlike checks retained.");
        }
        static bool PlayerPawn(Pawn pawn) => MP.IsInMultiplayer ? pawn.Faction?.IsPlayer == true : pawn.Faction == Faction.OfPlayer;
        static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var faction = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Faction));
            var player = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer));
            int count = 0;
            for (int i = 0; i + 2 < result.Count; i++)
                if (result[i].Calls(faction) && result[i + 1].Calls(player)
                    && (result[i + 2].opcode == OpCodes.Bne_Un || result[i + 2].opcode == OpCodes.Bne_Un_S))
                {
                    result[i].opcode = OpCodes.Call;
                    result[i].operand = AccessTools.Method(typeof(NivarianLentColonists), nameof(PlayerPawn));
                    result[i + 1].opcode = OpCodes.Nop; result[i + 1].operand = null;
                    result[i + 2].opcode = OpCodes.Brfalse;
                    count++;
                }
            if (count != 1) throw new InvalidOperationException("Nivarian lend-colonists faction sites=" + count);
            return result;
        }
    }
}
