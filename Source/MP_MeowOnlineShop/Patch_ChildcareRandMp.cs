using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Large race stacks can reach urgent autofeed job evaluation through
    /// different surrounding Rand histories.  Keep the vanilla probability and
    /// decision logic, but give this per-pawn decision a stable MP Rand scope.
    /// </summary>
    internal static class Patch_ChildcareRandMp
    {
        internal static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(
                typeof(ChildcareUtility),
                nameof(ChildcareUtility.ShouldWakeUpToAutofeedUrgent),
                new[] { typeof(Pawn) });
            if (target == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Childcare urgent-autofeed Rand guard target was not found.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_ChildcareRandMp),
                    nameof(Prefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_ChildcareRandMp),
                    nameof(Finalizer))));

            Log.Message(
                "[MP-MeowOnlineShop] Deterministic MP childcare urgent-autofeed Rand scope active.");
        }

        private static void Prefix(Pawn feeder, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || feeder == null)
                return;

            int seed = Gen.HashCombineInt(
                feeder.thingIDNumber,
                Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            __state = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
