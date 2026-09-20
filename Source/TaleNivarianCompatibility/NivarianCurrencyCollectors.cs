using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // MotherShip owns one shared currency balance, and native collectors select
    // one active building of each def per map across all player factions.
    internal static class NivarianCurrencyCollectors
    {
        internal static void Apply(Harmony harmony)
        {
            const string ns = "Nivarian_Race.Code.Comps.BuildingComps.";
            var collector = AccessTools.TypeByName(ns + "CompColonistPowerCollector") ?? throw new TypeLoadException(ns);
            var gate = AccessTools.TypeByName(ns + "MothershipSupportCurrencyGate") ?? throw new TypeLoadException(ns);
            var sole = AccessTools.TypeByName(ns + "SoleActiveBuildingHelper") ?? throw new TypeLoadException(ns);
            harmony.Patch(AccessTools.DeclaredMethod(collector, "CollectAndAddCurrency") ?? throw new MissingMethodException(collector.FullName, "CollectAndAddCurrency"),
                transpiler: new HarmonyMethod(typeof(NivarianCurrencyCollectors), nameof(ColonistRead)));
            harmony.Patch(AccessTools.PropertyGetter(gate, "IsUnlocked") ?? throw new MissingMethodException(gate.FullName, "get_IsUnlocked"),
                transpiler: new HarmonyMethod(typeof(NivarianCurrencyCollectors), nameof(ResearchRead)));
            harmony.Patch(AccessTools.DeclaredMethod(sole, "IsSoleActive") ?? throw new MissingMethodException(sole.FullName, "IsSoleActive"),
                prefix: new HarmonyMethod(typeof(NivarianCurrencyCollectors), nameof(SoleActive)));
            Log.Message("[TaleNivarianCompat] shared currency collectors pool registered players; spectator colonists, research and collectors excluded.");
        }

        static IEnumerable<CodeInstruction> ColonistRead(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonistsSpawned));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianCurrencyCollectors), nameof(Colonists));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Currency collector colonist reads changed: " + count);
        }

        static List<Pawn> Colonists(MapPawns pawns)
        {
            if (!MP.IsInMultiplayer) return pawns.FreeColonistsSpawned;
            return NivarianMetricInputs.Players().SelectMany(pawns.FreeHumanlikesSpawnedOfFaction)
                .Distinct().OrderBy(p => p.thingIDNumber).ToList();
        }

        static IEnumerable<CodeInstruction> ResearchRead(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(ResearchProjectDef), nameof(ResearchProjectDef.IsFinished));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianProgressOwnership), nameof(NivarianProgressOwnership.ResearchFinished));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Currency collector research reads changed: " + count);
        }

        static bool SoleActive(Thing parent, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = false;
            if (parent?.Map == null) return false;
            var players = NivarianMetricInputs.Players();
            // Preserve native building-list order and one-per-map behavior.
            __result = parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .FirstOrDefault(b => b.def == parent.def && players.Contains(b.Faction)) == parent;
            return false;
        }
    }
}
