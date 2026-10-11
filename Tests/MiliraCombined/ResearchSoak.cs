using System;
using System.IO;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Client;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class ResearchSoakProbe
{
    static readonly bool client=GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    internal static int WorldElapsed=>started?Mp.AsyncWorldTime.worldTicks-worldStart:0;
    static ISyncMethod begin;
    static Map map;
    static int worldStart, mapStart, serverStart, next=10000;
    static bool requested, started;
    internal static void Init()
    {
        if(GenCommandLine.CommandLineArgPassed("miliraresearchlong"))begin=MP.RegisterSyncMethod(typeof(ResearchSoakProbe),nameof(Begin));
    }
    public static void Begin()
    {
        map=Find.Maps.First(m=>m.IsPlayerHome); worldStart=Mp.AsyncWorldTime.worldTicks; mapStart=map.AsyncTime().mapTicks; serverStart=TickPatch.Timer; started=true;
        Log.Message("RESEARCH_LONG START world="+worldStart+" map="+map.uniqueID+":"+mapStart+" server="+serverStart+" traceMode="+Mp.settings.desyncTracingMode);
    }
    internal static bool Update()
    {
        if(!started){if(client&&!requested)requested=begin.DoSync(null);return false;}
        var world=Mp.AsyncWorldTime.worldTicks-worldStart;var ticks=map.AsyncTime().mapTicks-mapStart;
        if(world>=next){Log.Message("RESEARCH_LONG CHECKPOINT world="+world+" mapTicks="+ticks+" serverTicks="+(TickPatch.Timer-serverStart)+" desynced="+Mp.session.desynced);next+=10000;}
        if(world<120000||ticks<120000||TickPatch.Timer-serverStart<10000)return false;
        var receipt="worldGate=120000;mapGate=120000;serverGate=10000;map="+map.uniqueID+";desynced=False;players=2";
        var peer=client?"client":"host";var path=Path.Combine(root,peer+".researchlong");
        if(!File.Exists(path)){File.WriteAllText(path+".tmp",receipt);File.Move(path+".tmp",path);Log.Message("RESEARCH_LONG PASS world="+world+" mapTicks="+ticks+" serverTicks="+(TickPatch.Timer-serverStart));}
        return true;
    }
}
