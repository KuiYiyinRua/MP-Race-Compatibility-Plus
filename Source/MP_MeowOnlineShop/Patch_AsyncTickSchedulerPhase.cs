using System;
using System.Linq.Expressions;
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
        private static readonly Func<object, object> DesiredTimeSpeedGetter =
            TryCompilePropertyGetter(DesiredTimeSpeedProperty);
        private static readonly Action<object, float> TimeToTickThroughSetter =
            TryCompilePropertySetter(TimeToTickThroughProperty);
        private static readonly Func<object, object, float> TimePerTickFunc =
            TryCompileTimePerTick();
        private static readonly Func<bool> IsReplayFunc =
            TryCompileIsReplay();

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

            if (IsReplayFunc != null ? IsReplayFunc() : IsReplay())
                return true;

            try
            {
                object tickable = __args[0];
                object speed = DesiredTimeSpeedGetter != null
                    ? DesiredTimeSpeedGetter(tickable)
                    : DesiredTimeSpeedProperty.GetValue(tickable);
                float timePerTick = TimePerTickFunc != null
                    ? TimePerTickFunc(tickable, speed)
                    : (float)TimePerTickMethod.Invoke(null, new[] { tickable, speed });
                if (timePerTick <= 0f)
                    return true;

                if (TimeToTickThroughSetter != null)
                    TimeToTickThroughSetter(tickable, 1f - timePerTick);
                else
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

        private static Func<object, object> TryCompilePropertyGetter(PropertyInfo property)
        {
            if (property == null)
                return null;
            var getter = property.GetGetMethod(true);
            if (getter == null)
                return null;
            try
            {
                var instance = Expression.Parameter(typeof(object), "instance");
                var body = Expression.Convert(
                    Expression.Call(Expression.Convert(instance, property.DeclaringType), getter),
                    typeof(object));
                return Expression.Lambda<Func<object, object>>(body, instance).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Action<object, float> TryCompilePropertySetter(PropertyInfo property)
        {
            if (property == null)
                return null;
            var setter = property.GetSetMethod(true);
            if (setter == null)
                return null;
            try
            {
                var instance = Expression.Parameter(typeof(object), "instance");
                var value = Expression.Parameter(typeof(float), "value");
                var body = Expression.Call(
                    Expression.Convert(instance, property.DeclaringType),
                    setter,
                    Expression.Convert(value, property.PropertyType));
                return Expression.Lambda<Action<object, float>>(body, instance, value).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Func<object, object, float> TryCompileTimePerTick()
        {
            if (TimePerTickMethod == null)
                return null;
            var parameters = TimePerTickMethod.GetParameters();
            if (parameters.Length != 2)
                return null;
            try
            {
                var tickable = Expression.Parameter(typeof(object), "tickable");
                var speed = Expression.Parameter(typeof(object), "speed");
                var body = Expression.Call(
                    TimePerTickMethod,
                    Expression.Convert(tickable, parameters[0].ParameterType),
                    Expression.Convert(speed, parameters[1].ParameterType));
                return Expression.Lambda<Func<object, object, float>>(body, tickable, speed).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Func<bool> TryCompileIsReplay()
        {
            if (IsReplayProperty == null)
                return null;
            var getter = IsReplayProperty.GetGetMethod(true);
            if (getter == null)
                return null;
            try
            {
                return Expression.Lambda<Func<bool>>(Expression.Call(getter)).Compile();
            }
            catch
            {
                return null;
            }
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
