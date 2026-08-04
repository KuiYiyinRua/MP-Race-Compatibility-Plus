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
    /// Ero Traders builds its readable-magazine candidates from map listers and
    /// bookcase contents, then chooses one with RandomElement.  Stable Thing-ID
    /// ordering preserves the existing Rand draw but prevents distinct local
    /// lister insertion orders from assigning different reading jobs.
    /// </summary>
    internal static class Patch_RjwEroTraderDeterminism
    {
        private const string PackageId = "shauaputa.lewdtrader";
        private const string TypeName = "EROStuff.JobGiver_ReadPorn";
        private static readonly MethodInfo StableRandomElementMethod =
            AccessTools.Method(typeof(Patch_RjwEroTraderDeterminism), nameof(StableRandomElement));

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type type = AccessTools.TypeByName(TypeName);
            MethodInfo target = type == null ? null : AccessTools.Method(
                type, "TryGetRandomPornMagazineToRead", new[] { typeof(Pawn), typeof(Book).MakeByRefType() });
            if (target == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-EroTrader] readable-magazine selector signature drift; stable ordering was not applied.");
                return;
            }

            harmony.Patch(target, transpiler: new HarmonyMethod(AccessTools.Method(
                typeof(Patch_RjwEroTraderDeterminism), nameof(Transpiler))));
            Log.Message("[MP-MeowOnlineShop][RJW-EroTrader] stabilized readable-magazine selection.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo method = instruction.operand as MethodInfo;
                if (instruction.opcode == OpCodes.Call && method != null && method.Name == "RandomElement" &&
                    method.ReturnType == typeof(Thing) && method.GetParameters().Length == 1)
                {
                    instruction.operand = StableRandomElementMethod;
                }
                yield return instruction;
            }
        }

        private static Thing StableRandomElement(IEnumerable<Thing> things)
        {
            return things.OrderBy(thing => thing?.thingIDNumber ?? int.MinValue).RandomElement();
        }
    }
}
