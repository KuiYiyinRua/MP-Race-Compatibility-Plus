using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianFireworks
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.NivarianGameCondition.GameCondition_OrbitalFireworks")
                ?? throw new TypeLoadException("Nivarian orbital fireworks");
            var pulse = AccessTools.DeclaredMethod(type, "ApplyOutdoorColonyMoodPulse", Type.EmptyTypes)
                ?? throw new MissingMethodException(type.FullName, "ApplyOutdoorColonyMoodPulse");
            harmony.Patch(pulse, transpiler: new HarmonyMethod(typeof(NivarianFireworks), nameof(ReplaceRecipients)));
            Log.Message("[TaleNivarianCompat] Orbital fireworks mood pulse uses all player factions; original pulse timing and outdoor checks retained.");
        }

        static IEnumerable<CodeInstruction> ReplaceRecipients(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonistsAndPrisonersSpawned));
            var replacement = AccessTools.Method(typeof(NivarianFireworks), nameof(Recipients));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Orbital fireworks recipient target count=" + count);
        }

        // Called only by the original mood pulse (default once per 60,001 ticks),
        // not by drawing or every-tick checks. Pawn.IsColonist is contextual in MP;
        // use the original FreeHumanlikesSpawnedOfFaction eligibility explicitly.
        static List<Pawn> Recipients(MapPawns mapPawns)
        {
            if (!MP.IsInMultiplayer) return mapPawns.FreeColonistsAndPrisonersSpawned;
            var result = new List<Pawn>();
            var pawns = mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                bool free = pawn.Faction != null && pawn.Faction.IsPlayer && pawn.HostFaction == null
                    && pawn.RaceProps.Humanlike && (!ModsConfig.AnomalyActive || !pawn.IsSubhuman);
                bool prisoner = pawn.guest != null && pawn.guest.IsPrisoner
                    && pawn.HostFaction != null && pawn.HostFaction.IsPlayer;
                if (free || prisoner) result.Add(pawn);
            }
            result.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            return result;
        }
    }
}
