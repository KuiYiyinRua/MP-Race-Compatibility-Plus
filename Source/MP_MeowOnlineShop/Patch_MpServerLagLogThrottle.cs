using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Bounds Multiplayer's per-tick server lag diagnostics and prevents
    /// players that have not yet sent their first keepalive from being
    /// treated as far behind.
    ///
    /// The observed user log contains hundreds of consecutive
    /// "Simulation paused because some players are too far behind" lines
    /// immediately after "Server started." and before a remote client
    /// connects. ServerPlayer.ExtrapolatedTicksBehind extrapolates from
    /// ticksBehindReceivedAt, which is still 0 for a newly created host
    /// player, so the value grows every server tick even though no keepalive
    /// has been received. The getter prefix below returns the reported
    /// value only after a keepalive exists. The Log prefix additionally
    /// throttles identical pause messages so genuine catch-up does not flood
    /// the player log.
    /// </summary>
    internal static class Patch_MpServerLagLogThrottle
    {
        private const string PauseMessagePrefix =
            "Simulation paused because some players are too far behind";
        private const int PauseMessageCooldownMs = 1000;

        private static readonly object SyncRoot = new object();
        private static bool _applied;
        private static string _lastPauseMessage;
        private static long _lastPauseMessageTimestamp;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;

            if (!OptimizationGate.IsMpServerLagThrottleEnabled)
            {
                _applied = true;
                OptimizationGate.LogOnce(
                    "mp.server.lag.throttle.disabled",
                    "[MP-MeowOnlineShop] Multiplayer server lag log throttle is disabled; " +
                    "ServerLog and ServerPlayer.ExtrapolatedTicksBehind keep vanilla behavior.");
                return;
            }

            try
            {
                bool logPatchApplied = TryPatchServerLog(harmony);
                bool getterPatchApplied = TryPatchExtrapolatedTicksBehind(harmony);
                if (!logPatchApplied && !getterPatchApplied)
                {
                    _applied = false;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Server lag log throttle could not " +
                        "resolve Multiplayer targets; repeated pause logs remain.");
                    return;
                }

                _applied = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Server lag log throttle active: " +
                    $"pauseLogThrottle={logPatchApplied}, " +
                    $"firstKeepaliveGuard={getterPatchApplied}.");
            }
            catch (Exception e)
            {
                _applied = false;
                Log.Warning(
                    "[MP-MeowOnlineShop] Server lag log throttle apply failed: " + e.Message);
            }
        }

        private static bool TryPatchServerLog(Harmony harmony)
        {
            try
            {
                Type serverLogType = AccessTools.TypeByName("Multiplayer.Common.ServerLog");
                MethodInfo log = serverLogType == null
                    ? null
                    : AccessTools.Method(serverLogType, "Log", new[] { typeof(string) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_MpServerLagLogThrottle),
                    nameof(ServerLogPrefix));
                if (log == null || prefix == null)
                    return false;

                harmony.Patch(
                    log,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryPatchExtrapolatedTicksBehind(Harmony harmony)
        {
            try
            {
                Type serverPlayerType =
                    AccessTools.TypeByName("Multiplayer.Common.ServerPlayer");
                MethodInfo getter = serverPlayerType == null
                    ? null
                    : AccessTools.PropertyGetter(
                        serverPlayerType,
                        "ExtrapolatedTicksBehind")
                      ?? AccessTools.Method(
                          serverPlayerType,
                          "get_ExtrapolatedTicksBehind");
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_MpServerLagLogThrottle),
                    nameof(ExtrapolatedTicksBehindPrefix));
                if (getter == null || prefix == null)
                    return false;

                harmony.Patch(
                    getter,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ServerLogPrefix(string s)
        {
            if (string.IsNullOrEmpty(s) ||
                !s.StartsWith(PauseMessagePrefix, StringComparison.Ordinal))
            {
                return true;
            }

            long now = Stopwatch.GetTimestamp();
            lock (SyncRoot)
            {
                long elapsedMs = _lastPauseMessageTimestamp == 0
                    ? long.MaxValue
                    : (now - _lastPauseMessageTimestamp) * 1000 /
                      Stopwatch.Frequency;
                if (elapsedMs < PauseMessageCooldownMs &&
                    string.Equals(s, _lastPauseMessage, StringComparison.Ordinal))
                {
                    return false;
                }

                _lastPauseMessage = s;
                _lastPauseMessageTimestamp = now;
            }

            return true;
        }

        private static bool ExtrapolatedTicksBehindPrefix(
            ref int __result,
            int ___ticksBehind,
            int ___ticksBehindReceivedAt)
        {
            if (___ticksBehindReceivedAt <= 0)
            {
                __result = ___ticksBehind;
                return false;
            }

            return true;
        }
    }
}
