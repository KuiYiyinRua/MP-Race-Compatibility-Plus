using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// AsyncTimeComp.TimeToTickThrough is a runtime-only scheduler accumulator.
    /// AsyncTimeComp.ExposeData does not serialize it, so a cold rejoin starts
    /// every map/world tickable at 0 while the long-running host keeps its
    /// fractional phase. With the vanilla `while (TimeToTickThrough >= 0)`
    /// loop that phase changes how many map ticks each peer runs per shared
    /// global tick. The result is a join/rejoin-time "traces differ in amount,
    /// but the existing ones are equal" bundle followed by
    /// "Wrong random state on map X" (Desync-257, map 21).
    ///
    /// Fix: on every multiplayer scheduler pass, including join/rejoin
    /// catch-up simulation, normalize the accumulator to `1f - timePerTick`.
    /// TickPatch.DoTick has already added 1f before TickTickable runs, so
    /// this yields the phase the original patch intended before that +1: the
    /// loop runs exactly 1/timePerTick map ticks and every peer returns to
    /// the same phase. Using `-timePerTick` here made the loop see a negative
    /// accumulator and skip all map/world ticks, which froze multiplayer at
    /// TPS 0 regardless of time speed.
    /// </summary>
    internal static class Patch_AsyncTickSchedulerPhase
    {
        private static readonly Type TickPatchType =
            AccessTools.TypeByName("Multiplayer.Client.TickPatch");
        private static readonly MethodInfo TickTickableMethod =
            TickPatchType == null
                ? null
                : AccessTools.Method(TickPatchType, "TickTickable");
        private static readonly Type TickableType =
            TickTickableMethod == null ||
            TickTickableMethod.GetParameters().Length == 0
                ? null
                : TickTickableMethod.GetParameters()[0].ParameterType;
        private static readonly PropertyInfo TimeToTickThroughProperty =
            TickableType == null
                ? null
                : AccessTools.Property(TickableType, "TimeToTickThrough");
        private static readonly PropertyInfo DesiredTimeSpeedProperty =
            TickableType == null
                ? null
                : AccessTools.Property(TickableType, "DesiredTimeSpeed");
        private static readonly MethodInfo TimePerTickMethod =
            TickPatchType == null
                ? null
                : AccessTools.Method(TickPatchType, "TimePerTick");
        private static readonly PropertyInfo IsReplayProperty =
            AccessTools.Property(
                AccessTools.TypeByName("Multiplayer.Client.Multiplayer"),
                "IsReplay");

        private static bool _applied;
        private static bool _loggedFailure;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            if (TickTickableMethod == null || TickableType == null ||
                TimeToTickThroughProperty == null ||
                DesiredTimeSpeedProperty == null || TimePerTickMethod == null ||
                IsReplayProperty == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Async tick scheduler phase guard " +
                    "targets unresolved; skipped.");
                return;
            }

            try
            {
                harmony.Patch(
                    TickTickableMethod,
                    prefix: new HarmonyMethod(
                        typeof(Patch_AsyncTickSchedulerPhase),
                        nameof(TickTickablePrefix))
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Async tick scheduler phase guard " +
                    "active: TimeToTickThrough is normalized to a deterministic " +
                    "per-tick phase on every peer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Async tick scheduler phase guard " +
                    "install failed: " + e.Message);
            }
        }

        private static bool TickTickablePrefix(object[] __args)
        {
            if (!MP.IsInMultiplayer || __args == null ||
                __args.Length == 0 || __args[0] == null)
            {
                return true;
            }

            if (IsReplay())
                return true;

            try
            {
                object tickable = __args[0];
                object speed = DesiredTimeSpeedProperty.GetValue(tickable);
                float timePerTick = (float)TimePerTickMethod.Invoke(
                    null,
                    new[] { tickable, speed });
                if (timePerTick <= 0f)
                    return true;

                TimeToTickThroughProperty.SetValue(tickable, 1f - timePerTick);
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Async tick scheduler phase " +
                        "normalization failed: " + e.Message);
                }
            }

            return true;
        }

        private static bool IsReplay()
        {
            try
            {
                return IsReplayProperty != null &&
                       (bool)IsReplayProperty.GetValue(null);
            }
            catch
            {
                return true;
            }
        }
    }
}
