using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-530 (2026-08-20, faction settlement trade + shuttle launch):
    /// "Wrong random state for the world". The first divergent trace shows the
    /// LOCAL running a world tick (GameComponent_Anomaly.GameComponentTick ->
    /// Rand.Range) at the same MP tick where the HOST only ticks maps - the
    /// host's async world tickable had rate 0 (paused) while the local's ran.
    /// AsyncWorldTimeComp.TickRateMultiplier returns 0 when
    /// Multiplayer.WorldComp.sessionManager.IsAnySessionCurrentlyPausing(null)
    /// is true, i.e. when a session (MpTradeSession with a world/caravan
    /// negotiator, CaravanSplittingSession, PauseLockSession) pauses the world.
    /// If that session exists on one peer only, only that peer's world stops,
    /// and the world Rand states diverge.
    ///
    /// This env-gated recorder (MP_WORLD_PAUSE_DIAG=1, bounded) logs every time
    /// the async world tick is paused by a session, with the session list and
    /// each session's IsCurrentlyPausing(null) result. Comparing the two peers'
    /// logs directly shows which session pauses the world on one side only.
    /// No-op unless enabled; adds no sync commands; fail-open.
    /// </summary>
    internal static class Patch_WorldPauseDiagnostic
    {
        private const string EnvVarName = "MP_WORLD_PAUSE_DIAG";
        private const int MaxRecords = 8;

        internal static readonly bool Enabled =
            string.Equals(
                Environment.GetEnvironmentVariable(EnvVarName),
                "1",
                StringComparison.OrdinalIgnoreCase);

        private static bool _installed;
        private static int _recordCount;

        private static Type _worldCompType;
        private static PropertyInfo _worldCompProperty;
        private static PropertyInfo _sessionManagerProperty;
        private static FieldInfo _allSessionsField;
        private static MethodInfo _isCurrentlyPausingMethod;
        private static MethodInfo _isAnySessionCurrentlyPausingMethod;
        private static MethodInfo _sessionMapGetter;

        internal static void Apply(Harmony harmony)
        {
            if (!Enabled || _installed || harmony == null)
                return;

            _installed = true;
            try
            {
                Type asyncWorldType = AccessTools.TypeByName(
                    "Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
                if (asyncWorldType == null)
                    return;

                _worldCompType =
                    AccessTools.TypeByName("Multiplayer.Client.MultiplayerWorldComp");
                _worldCompProperty = AccessTools.Property(
                    AccessTools.TypeByName("Multiplayer.Client.Multiplayer"),
                    "WorldComp");
                _sessionManagerProperty = _worldCompType == null
                    ? null
                    : AccessTools.Property(_worldCompType, "sessionManager");
                _allSessionsField = AccessTools.Field(
                    AccessTools.TypeByName(
                        "Multiplayer.Client.Persistent.SessionManager"),
                    "allSessions");
                _isCurrentlyPausingMethod = AccessTools.Method(
                    typeof(Session),
                    "IsCurrentlyPausing",
                    new[] { typeof(Map) });
                _sessionMapGetter = AccessTools.PropertyGetter(
                    typeof(Session),
                    "Map");
                _isAnySessionCurrentlyPausingMethod = _sessionManagerProperty == null
                    ? null
                    : AccessTools.Method(
                        _sessionManagerProperty.PropertyType,
                        "IsAnySessionCurrentlyPausing",
                        new[] { typeof(Map) });

                harmony.Patch(
                    AccessTools.Method(
                        asyncWorldType,
                        "TickRateMultiplier",
                        new[] { typeof(TimeSpeed) }),
                    prefix: new HarmonyMethod(
                        typeof(Patch_WorldPauseDiagnostic),
                        nameof(TickRateMultiplierPrefix))
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] World-pause diagnostic active " +
                    "(MP_WORLD_PAUSE_DIAG=1).");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] World-pause diagnostic install " +
                    "failed: " + e.Message);
            }
        }

        private static void TickRateMultiplierPrefix(TimeSpeed speed)
        {
            if (!MP.IsInMultiplayer || _recordCount >= MaxRecords)
                return;

            try
            {
                bool paused = speed == TimeSpeed.Paused;
                if (!paused && _isAnySessionCurrentlyPausingMethod != null &&
                    _sessionManagerProperty != null && _worldCompProperty != null)
                {
                    object worldComp = _worldCompProperty.GetValue(null, null);
                    object sessionManager = worldComp == null
                        ? null
                        : _sessionManagerProperty.GetValue(worldComp);
                    paused = sessionManager != null &&
                             (bool)_isAnySessionCurrentlyPausingMethod.Invoke(
                                 sessionManager, new object[] { null });
                }

                if (!paused)
                    return;

                _recordCount++;
                var builder = new System.Text.StringBuilder();
                builder.Append(
                    "[MP-WORLD-PAUSE-DIAG] worldPaused=true speed=" + speed);

                if (_worldCompProperty != null && _sessionManagerProperty != null &&
                    _allSessionsField != null)
                {
                    object worldComp = _worldCompProperty.GetValue(null, null);
                    object sessionManager = worldComp == null
                        ? null
                        : _sessionManagerProperty.GetValue(worldComp);
                    IEnumerable sessions = sessionManager == null
                        ? null
                        : _allSessionsField.GetValue(sessionManager) as IEnumerable;
                    if (sessions == null)
                    {
                        builder.Append(" sessions=null");
                    }
                    else
                    {
                        int n = 0;
                        foreach (object session in sessions)
                        {
                            if (session == null || n >= 8)
                                continue;
                            n++;
                            builder.Append(" | session=");
                            builder.Append(session.GetType().Name);
                            if (_isCurrentlyPausingMethod != null)
                            {
                                try
                                {
                                    builder.Append(
                                        " pausesWorld=" +
                                        _isCurrentlyPausingMethod.Invoke(
                                            session, new object[] { null }));
                                    if (_sessionMapGetter != null)
                                    {
                                        try
                                        {
                                            Map sessionMap =
                                                _sessionMapGetter.Invoke(
                                                    session, null) as Map;
                                            builder.Append(
                                                " map=" +
                                                (sessionMap != null
                                                    ? sessionMap.uniqueID.ToString()
                                                    : "world"));
                                        }
                                        catch
                                        {
                                            builder.Append(" map=?");
                                        }
                                    }
                                }
                                catch
                                {
                                    builder.Append(" pausesWorld=?");
                                }
                            }
                        }
                        builder.Append(" | count=" + n);
                    }
                }

                Log.Warning(builder.ToString());
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] World-pause diagnostic failed: " +
                    e.Message);
            }
        }
    }
}
