using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using AM.Processing;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    [HarmonyPatch(typeof(MapPawnProcessor), "GetPotentialTargets")]
    internal static class AutoTargetOrder
    {
        private static void Postfix(List<IAttackTarget> output)
        {
            if (Bootstrap.Active) output.Sort((a, b) => a.Thing.thingIDNumber.CompareTo(b.Thing.thingIDNumber));
        }
    }

    [HarmonyPatch(typeof(MapPawnProcessor), "CompileListOfAttackers")]
    internal static class AutoAttackerOrder
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.AllPawnsSpawned));
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.Calls(original))
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AutoAttackerOrder), nameof(Sort)));
            }
        }
        private static List<Pawn> Sort(List<Pawn> list) => Bootstrap.Active ? list.OrderBy(p => p.thingIDNumber).ToList() : list;
    }
}
