using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Keeps Multiplayer's synchronized time-control decision authoritative by
    /// preventing vanilla/modded threat notifications from installing a local
    /// forced-normal-speed deadline. Pauses and Multiplayer's host/lowest-wins
    /// policy remain owned by Multiplayer.
    /// </summary>
    internal static class Patch_MpThreatSpeedUnlock
    {
        private static bool _applied;
        private static bool _loggedSuppression;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;

            _applied = true;
            MethodInfo standard = null;
            MethodInfo shortSignal = null;
            try
            {
                standard = AccessTools.DeclaredMethod(
                    typeof(TimeSlower),
                    nameof(TimeSlower.SignalForceNormalSpeed),
                    Type.EmptyTypes);
                shortSignal = AccessTools.DeclaredMethod(
                    typeof(TimeSlower),
                    nameof(TimeSlower.SignalForceNormalSpeedShort),
                    Type.EmptyTypes);
                MethodInfo standardPrefix = AccessTools.Method(
                    typeof(Patch_MpThreatSpeedUnlock),
                    nameof(SignalForceNormalSpeed_Prefix));
                MethodInfo shortPrefix = AccessTools.Method(
                    typeof(Patch_MpThreatSpeedUnlock),
                    nameof(SignalForceNormalSpeedShort_Prefix));

                if (!IsExpectedTarget(standard) || !IsExpectedTarget(shortSignal) ||
                    standardPrefix == null || shortPrefix == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] MP threat-speed unlock target shape mismatch; " +
                        "feature disabled and forced-normal-speed behavior preserved.");
                    return;
                }

                harmony.Patch(
                    standard,
                    prefix: new HarmonyMethod(standardPrefix) { priority = Priority.First });
                harmony.Patch(
                    shortSignal,
                    prefix: new HarmonyMethod(shortPrefix) { priority = Priority.First });

                Log.Message(
                    "[MP-MeowOnlineShop] MP threat-speed unlock initialized: " +
                    "Verse.TimeSlower.SignalForceNormalSpeed() and " +
                    "SignalForceNormalSpeedShort() resolved; forced-normal-speed signals " +
                    "will be suppressed only during multiplayer sessions.");
            }
            catch (Exception e)
            {
                bool rollbackSucceeded = TryRemovePrefix(harmony, standard) &
                                         TryRemovePrefix(harmony, shortSignal);
                Log.Warning(
                    "[MP-MeowOnlineShop] MP threat-speed unlock initialization failed; " +
                    (rollbackSucceeded
                        ? "installed prefixes were removed and forced-normal-speed behavior was preserved: "
                        : "prefix rollback was incomplete; a partial threat-speed patch may remain active: ") +
                    e);
            }
        }

        private static bool TryRemovePrefix(Harmony harmony, MethodInfo target)
        {
            if (harmony == null || target == null)
                return true;

            try
            {
                harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExpectedTarget(MethodInfo method)
        {
            return method != null &&
                   method.DeclaringType == typeof(TimeSlower) &&
                   !method.IsStatic &&
                   method.ReturnType == typeof(void) &&
                   method.GetParameters().Length == 0;
        }

        private static bool SignalForceNormalSpeed_Prefix()
        {
            return ShouldRunOriginal(nameof(TimeSlower.SignalForceNormalSpeed));
        }

        private static bool SignalForceNormalSpeedShort_Prefix()
        {
            return ShouldRunOriginal(nameof(TimeSlower.SignalForceNormalSpeedShort));
        }

        private static bool ShouldRunOriginal(string signalName)
        {
            if (!MP.IsInMultiplayer)
                return true;

            if (Prefs.DevMode && !_loggedSuppression)
            {
                _loggedSuppression = true;
                Log.Message(
                    "[MP-MeowOnlineShop][Debug] Suppressed multiplayer forced-normal-speed " +
                    "signal=" + signalName + "; synchronized time-control selection remains active.");
            }

            return false;
        }
    }
}
