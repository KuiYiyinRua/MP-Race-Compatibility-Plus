using System;
using System.Collections.Generic;
using System.Reflection;
using AM;
using AM.Idle;
using AM.Outcome;
using AM.Events.Workers;
using AM.RendererWorkers;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    [HarmonyPatch]
    internal static class VisualRandom
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(IdleControllerComp), "TickActive");
            yield return AccessTools.Method(typeof(IdleControllerComp), nameof(IdleControllerComp.PreDraw));
            yield return AccessTools.Method(typeof(IdleControllerComp), nameof(IdleControllerComp.NotifyPawnDidMeleeAttack));
            yield return AccessTools.Method(typeof(MoteWorker), nameof(MoteWorker.Run));
            yield return AccessTools.Method(typeof(DamageEffectWorker), nameof(DamageEffectWorker.Run));
            yield return AccessTools.Method(typeof(TextMoteWorker), nameof(TextMoteWorker.Run));
            yield return AccessTools.Method(typeof(AudioWorker), nameof(AudioWorker.Run));
            yield return AccessTools.Method(typeof(ClashAudioWorker), nameof(ClashAudioWorker.Run));
            yield return AccessTools.Method(typeof(GilgameshRendererWorker), nameof(GilgameshRendererWorker.SetupRenderer));
        }

        private static void Prefix(out bool __state)
        {
            __state = Bootstrap.Active;
            if (__state) Rand.PushState();
        }

        private static void Finalizer(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(OutcomeUtility), nameof(OutcomeUtility.GenerateRandomOutcome),
        new[] { typeof(Pawn), typeof(Pawn), typeof(bool), typeof(OutcomeUtility.ProbabilityReport) })]
    internal static class ProbabilityPreview
    {
        private static void Prefix(OutcomeUtility.ProbabilityReport report, out bool __state)
        {
            __state = Bootstrap.Active && report != null;
            if (__state) Rand.PushState();
        }
        private static void Finalizer(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }
}
