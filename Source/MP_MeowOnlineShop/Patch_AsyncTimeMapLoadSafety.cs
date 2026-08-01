using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// A rejoining client can run Pawn.PostMapInit while Multiplayer has not yet
    /// attached an AsyncTimeComp to Find.CurrentMap. Multiplayer's Paused postfix
    /// dereferences that missing component and aborts the pawn's path/job restore.
    /// Catch only that exact third-party postfix failure so the vanilla Paused
    /// result survives and PostMapInit can finish deterministically.
    /// </summary>
    internal static class Patch_AsyncTimeMapLoadSafety
    {
        private const string PausedPostfixTypeName =
            "Multiplayer.Client.AsyncTime.TickManagerPausedPatch";

        private static bool _loggedRecovery;

        internal static void Apply(Harmony harmony)
        {
            MethodInfo target = AccessTools.PropertyGetter(
                typeof(TickManager), nameof(TickManager.Paused));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_AsyncTimeMapLoadSafety), nameof(PausedFinalizer));

            if (target == null || finalizer == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Async-time map-load Paused guard skipped: " +
                    "TickManager.Paused getter/finalizer was not resolved.");
                return;
            }

            harmony.Patch(
                target,
                finalizer: new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Async-time map-load Paused guard active.");
        }

        private static Exception PausedFinalizer(
            Exception __exception,
            ref bool __result)
        {
            if (__exception == null || !MP.IsInMultiplayer ||
                !IsMissingAsyncTimePausedPostfix(__exception))
            {
                return __exception;
            }

            // The original TickManager.Paused getter completed before the MP
            // postfix failed, so retain its __result. This matches the safe
            // fallback while the per-map async component is still unavailable.
            if (!_loggedRecovery)
            {
                _loggedRecovery = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Recovered Multiplayer async-time " +
                    "TickManager.Paused lookup during map PostMapInit; retained " +
                    $"the vanilla paused result ({__result}) so pawn path/job " +
                    "restoration can complete.");
            }

            return null;
        }

        private static bool IsMissingAsyncTimePausedPostfix(Exception exception)
        {
            if (!(exception is NullReferenceException))
                return false;

            MethodBase target = exception.TargetSite;
            if (target?.DeclaringType?.FullName == PausedPostfixTypeName &&
                target.Name == "Postfix")
            {
                return true;
            }

            // Mono may omit TargetSite for exceptions crossing a dynamic Harmony
            // wrapper. Keep the fallback just as narrow as the direct check.
            string stack = exception.StackTrace;
            return stack != null &&
                   stack.IndexOf(
                       PausedPostfixTypeName + ".Postfix",
                       StringComparison.Ordinal) >= 0;
        }
    }
}
