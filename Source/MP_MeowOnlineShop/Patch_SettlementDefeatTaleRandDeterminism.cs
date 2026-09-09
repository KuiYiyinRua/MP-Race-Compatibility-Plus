using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-282/283: when an Odyssey gravship assault defeats an enemy
    /// settlement, vanilla SettlementDefeatUtility.CheckDefeated finishes with
    /// TaleRecorder.RecordTale(TaleDefOf.CaravanAssaultSuccessful,
    /// map.mapPawns.FreeColonists.RandomElement()).
    ///
    /// RandomElement consumes a synchronized world-Rand draw only when the
    /// map's FreeColonists list is non-empty. The evidence bundle shows the
    /// client list empty at the same tick ("Getting random element from empty
    /// collection"), so the client reaches TaleRecorder with null while the
    /// host first advances Rand and then records the tale; both then diverge in
    /// the world stream, and a rejoin replays the same asymmetric path.
    ///
    /// The settlement destruction, letter, goodwill, faction-defeat, and
    /// map-parent changes above that line are all deterministic simulation
    /// effects and are left untouched. Only the cosmetic victory tale and its
    /// Rand-consuming argument are removed in multiplayer.
    /// </summary>
    internal static class Patch_SettlementDefeatTaleRandDeterminism
    {
        private static readonly MethodInfo StableVictoryTaleVictimMethod =
            AccessTools.Method(
                typeof(Patch_SettlementDefeatTaleRandDeterminism),
                nameof(StableVictoryTaleVictim));

        private static readonly MethodInfo SuppressedVictoryTaleMethod =
            AccessTools.Method(
                typeof(Patch_SettlementDefeatTaleRandDeterminism),
                nameof(SuppressedVictoryTale));

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo checkDefeated = AccessTools.Method(
                    typeof(SettlementDefeatUtility),
                    nameof(SettlementDefeatUtility.CheckDefeated));
                MethodInfo transpiler = AccessTools.Method(
                    typeof(Patch_SettlementDefeatTaleRandDeterminism),
                    nameof(Transpiler));

                if (checkDefeated == null || transpiler == null ||
                    StableVictoryTaleVictimMethod == null ||
                    SuppressedVictoryTaleMethod == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Settlement defeat tale/Rand determinism " +
                        "target resolution failed; victory tale can still desync.");
                    return;
                }

                harmony.Patch(
                    checkDefeated,
                    transpiler: new HarmonyMethod(transpiler));

                Log.Message(
                    "[MP-MeowOnlineShop] Settlement defeat tale/RandomElement path is " +
                    "deterministic in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Settlement defeat tale/Rand determinism failed: " +
                    e.Message);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (IsPawnRandomElement(instruction.operand))
                {
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        StableVictoryTaleVictimMethod);
                }
                else if (IsVictoryTaleRecord(instruction.operand))
                {
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        SuppressedVictoryTaleMethod);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static bool IsPawnRandomElement(object operand)
        {
            return operand is MethodInfo method &&
                   method.Name == "RandomElement" &&
                   method.DeclaringType == typeof(GenCollection) &&
                   method.IsGenericMethod &&
                   method.GetGenericArguments().Length == 1 &&
                   method.GetGenericArguments()[0] == typeof(Pawn);
        }

        private static bool IsVictoryTaleRecord(object operand)
        {
            if (!(operand is MethodInfo method) ||
                method.Name != "RecordTale" ||
                method.DeclaringType != typeof(TaleRecorder))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 2 &&
                   parameters[0].ParameterType == typeof(TaleDef) &&
                   parameters[1].ParameterType == typeof(object[]);
        }

        private static Pawn StableVictoryTaleVictim(IEnumerable<Pawn> freeColonists)
        {
            if (MP.IsInMultiplayer)
                return null;

            return freeColonists.RandomElement();
        }

        private static Tale SuppressedVictoryTale(TaleDef def, object[] args)
        {
            if (MP.IsInMultiplayer)
                return null;

            return TaleRecorder.RecordTale(def, args);
        }
    }
}
