using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RJW Menstruation's pheromone emitter iterates AllPawnsSpawned and applies
    /// Hediffs in that process-local insertion order. Cross-PC snapshot loading
    /// can preserve the same pawns with a different list order, changing the
    /// order of Hediff callbacks and their subsequent Rand consumption.
    /// </summary>
    internal static class Patch_RjwMenstruation
    {
        private const string PackageId = "rjw.menstruation";
        private const string PheromoneCompTypeName =
            "RJW_Menstruation.HediffComp_Pheromones";

        private static int _allPawnsReplacements;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            var compType = AccessTools.TypeByName(PheromoneCompTypeName);
            var iteratorType = compType?
                .GetNestedTypes(BindingFlags.NonPublic)
                .SingleOrDefault(type =>
                    type.Name.StartsWith(
                        "<AffectedPawns>d__",
                        StringComparison.Ordinal));
            var moveNext = iteratorType == null
                ? null
                : AccessTools.Method(iteratorType, "MoveNext", Type.EmptyTypes);

            if (moveNext == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Menstruation] pheromone iterator " +
                    "signature not found; stable affected-pawn ordering was not applied.");
                return;
            }

            _allPawnsReplacements = 0;
            harmony.Patch(
                moveNext,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwMenstruation),
                        nameof(AffectedPawnsTranspiler))));

            if (_allPawnsReplacements == 1)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][RJW-Menstruation] pheromone affected-pawn " +
                    "iteration now uses stable Thing IDs.");
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Menstruation] expected one " +
                    $"AllPawnsSpawned read in AffectedPawns, found {_allPawnsReplacements}.");
            }
        }

        private static IEnumerable<CodeInstruction> AffectedPawnsTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var allPawnsGetter = AccessTools.PropertyGetter(
                typeof(MapPawns),
                nameof(MapPawns.AllPawnsSpawned));
            var stableGetter = AccessTools.Method(
                typeof(Patch_RjwMenstruation),
                nameof(StableAllPawnsSpawned));

            foreach (var instruction in instructions)
            {
                if (allPawnsGetter != null && instruction.Calls(allPawnsGetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = stableGetter;
                    _allPawnsReplacements++;
                }

                yield return instruction;
            }
        }

        private static List<Pawn> StableAllPawnsSpawned(MapPawns mapPawns)
        {
            if (mapPawns == null)
                return new List<Pawn>();

            return mapPawns.AllPawnsSpawned
                .OrderBy(pawn => pawn?.thingIDNumber ?? int.MinValue)
                .ToList();
        }
    }
}
