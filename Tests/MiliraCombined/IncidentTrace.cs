using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class IncidentTrace
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("miliraincidenttrace");
    static int lastSample = -1;
    static bool InWindow
    {
        get
        {
            try
            {
                return Mp.Client?.State == Multiplayer.Common.ConnectionStateEnum.ClientPlaying &&
                    Current.ProgramState == ProgramState.Playing && MP.IsInMultiplayer &&
                    Mp.AsyncWorldTime != null && Mp.AsyncWorldTime.worldTicks >= 149800 && Mp.AsyncWorldTime.worldTicks <= 150500;
            }
            catch { return false; }
        }
    }
    static string State()
    {
        return "server=" + TickPatch.Timer + " world=" + Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState +
            " maps=" + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState));
    }
    internal static void Init(Harmony h)
    {
        if (!enabled) return;
        var incident = AccessTools.Method(typeof(IncidentWorker), "TryExecute", new[] { typeof(IncidentParms) });
        var pawn = AccessTools.Method(typeof(PawnGenerator), "GeneratePawn", new[] { typeof(PawnGenerationRequest) });
        if (incident == null || pawn == null) throw new MissingMemberException("incident diagnostic exact targets");
        h.Patch(incident, prefix: new HarmonyMethod(typeof(IncidentTrace), nameof(IncidentBefore)),
            postfix: new HarmonyMethod(typeof(IncidentTrace), nameof(IncidentAfter)));
        h.Patch(pawn, postfix: new HarmonyMethod(typeof(IncidentTrace), nameof(PawnAfter)));
        Log.Message("MILIRA_INCIDENT_TRACE enabled window=149800..150500");
    }
    internal static void Update()
    {
        if (!enabled || !InWindow) return;
        int sample = Mp.AsyncWorldTime.worldTicks / 25;
        if (sample == lastSample) return;
        lastSample = sample;
        Log.Message("MILIRA_INCIDENT_SAMPLE " + State());
    }
    static void IncidentBefore(IncidentWorker __instance, IncidentParms parms)
    {
        if (InWindow) Log.Message("MILIRA_INCIDENT_BEGIN " + __instance.def?.defName + " target=" + parms?.target?.GetType().FullName + " " + State());
    }
    static void IncidentAfter(IncidentWorker __instance, bool __result)
    {
        if (InWindow) Log.Message("MILIRA_INCIDENT_END " + __instance.def?.defName + " result=" + __result + " " + State());
    }
    static void PawnAfter(Pawn __result)
    {
        if (InWindow && __result != null)
            Log.Message("MILIRA_INCIDENT_PAWN " + __result.ThingID + " kind=" + __result.kindDef?.defName + " faction=" + __result.Faction?.loadID + " " + State());
    }
}
