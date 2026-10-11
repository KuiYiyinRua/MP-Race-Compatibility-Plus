using System;
using System.Linq;
using System.Text;
using Multiplayer.API;
using Multiplayer.Client;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class RuntimeHeartbeat
{
    static float next;
    internal static void Update()
    {
        if (Time.realtimeSinceStartup < next || !MP.IsInMultiplayer || Current.ProgramState != ProgramState.Playing) return;
        next = Time.realtimeSinceStartup + 20;
        Log.Message("MILIRA_HEARTBEAT " + State());
    }
    internal static string State()
    {
        var text = new StringBuilder("state=" + Mp.Client?.State + ";server=" + TickPatch.Timer +
            ";world=" + Mp.AsyncWorldTime?.worldTicks + ";frozen=" + TickPatch.Frozen + ";simulating=" + TickPatch.Simulating + ";worldSessions=");
        foreach (var session in Mp.WorldComp.sessionManager.AllSessions) text.Append(session.GetType().FullName).Append(',');
        foreach (var map in Find.Maps)
        {
            text.Append(";map=").Append(map.uniqueID).Append(':').Append(map.AsyncTime().mapTicks)
                .Append(':').Append(map.AsyncTime().DesiredTimeSpeed).Append(":paused=").Append(map.AsyncTime().Paused)
                .Append(":dialogs=").Append(map.MpComp().mapDialogs.Count).Append(":sessions=");
            foreach (var session in map.MpComp().sessionManager.AllSessions) text.Append(session.GetType().FullName).Append('/');
        }
        text.Append(";windows=");
        foreach (var window in Find.WindowStack.Windows) text.Append(window.GetType().FullName).Append(',');
        if (MiliraActionsProbe.TraceDiagnostic)
            text.Append(";traceEnabled=").Append(Mp.GameComp.logDesyncTraces)
                .Append(";traceNativeReady=").Append(Native.LmfPtr != 0)
                .Append(";traceCalls=").Append(Multiplayer.Client.Desyncs.DeferredStackTracing.acc);
        return text.ToString();
    }
}
