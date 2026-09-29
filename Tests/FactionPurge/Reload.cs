using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Meow.FactionPurge;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;

public sealed class PurgeReloadState:GameComponent
{
    public PurgeReloadState(Game game){}
    public override void GameComponentUpdate(){PurgeReload.Update();}
}
[StaticConstructorOnStartup]
public static class PurgeReload
{
    static bool enabled=GenCommandLine.CommandLineArgPassed("purgereload");
    static bool client=GenCommandLine.CommandLineArgPassed("purgeclient");
    static bool ready,frozen,failed,complete;
    static int start,checkpoint;
    static int[] mapStarts;
    static float next, nextSpeedDrive;
    static string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static PurgeReload()
    {
        if(!enabled)return;
        Log.Message("PURGERELOAD START peer="+(client?"client":"host")+" mvid="+typeof(PurgeCommand).Assembly.ManifestModule.ModuleVersionId);
        Mp.username=client?"PurgeClient":"PurgeHost";
        if(!client)LongEventHandler.ExecuteWhenFinished(()=>
        {
            Assert(GenCommandLine.TryGetCommandLineArg("purgeinput",out var file),"input missing");
            Replay.LoadReplay(new FileInfo(file),true,Host,()=>Fail(new Exception("Replay load cancelled")),"MpSimulatingServer",false);
        });
    }
    static void Host()
    {
        try
        {
            var settings=new ServerSettings{gameName="Purge reload",direct=true,directAddress="127.0.0.1:"+(GenCommandLine.TryGetCommandLineArg("purgeport",out var port)?port:"30932"),lan=false,steam=false,multifaction=true,asyncTime=!GenCommandLine.CommandLineArgPassed("purgesynctime"),syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};
            Assert((bool)AccessTools.Method(typeof(HostWindow),"TryParseEndpoints").Invoke(null,new object[]{settings}),"endpoints");
            Assert((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}),"server");
            HostUtil.HostServer(settings,true);
        }
        catch(Exception e){Fail(e);}
    }
    static void Assert(bool b,string message){if(!b)throw new Exception(message);}
    static void Fail(Exception e){failed=true;Log.Error("PURGERELOAD FAILED "+e);File.WriteAllText(Path.Combine(root,client?"client.failed":"host.failed"),e.ToString());}
    static string Baseline()=>string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>"map="+m.uniqueID+" tick="+m.AsyncTime().mapTicks+" rand="+m.AsyncTime().randState))+";world="+Mp.AsyncWorldTime.worldTicks+" rand="+Mp.AsyncWorldTime.randState;
    public static void Update()
    {
        if(!enabled||failed||Time.realtimeSinceStartup<next||LongEventHandler.AnyEventNowOrWaiting)return;
        next=Time.realtimeSinceStartup+0.2f;
        if(File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}
        if(complete)return;
        try
        {
            Assert(Time.realtimeSinceStartup<3600,"runtime timeout");
            if(Mp.Client==null||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating||Mp.IsReplay)return;
            Assert(!Mp.session.desynced,"desynced");
            if(!client&&!frozen){frozen=true;Mp.Client.Send(new ClientFreezePacket(true));return;}
            if(!client&&!ready&&!TickPatch.serverFrozen)return;
            if(client&&!ready)
            {
                ready=true;
                string baseline=Baseline();
                Current.Game.CurrentMap=Find.Maps.OrderBy(m=>m.uniqueID).Last();
                Log.Message("PURGERELOAD BASELINE "+baseline);
                var s=Current.Game.GetComponent<PurgeProbeState>();
                Assert(Find.FactionManager.GetById(s.target)==null,"deleted faction reappeared");
                Assert(Mp.WorldComp.factionData.ContainsKey(s.host),"host data lost");
                PurgeCommand.Open();
                Assert(Find.WindowStack.Windows.Any(w=>w is Dialog_MessageBox),"client permission refusal absent");
                Find.WindowStack.TryRemove(typeof(Dialog_MessageBox));
                File.WriteAllText(Path.Combine(root,"client.ready.tmp"),baseline);
                File.Move(Path.Combine(root,"client.ready.tmp"),Path.Combine(root,"client.ready"));
            }
            if(!client&&!ready&&File.Exists(Path.Combine(root,"client.ready"))&&Mp.session.players.Count==2)
            {
                Assert(Baseline()==File.ReadAllText(Path.Combine(root,"client.ready")),"BASELINE_LOAD_DRIFT");
                Log.Message("PURGERELOAD BASELINE "+Baseline());ready=true;
                Mp.Client.Send(new ClientFreezePacket(false));
                Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Ultrafast);
                foreach(var map in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)TimeSpeed.Ultrafast);
            }
            if(!ready||TickPatch.serverFrozen)return;
            Assert(Mp.session.players.Count==2,"peer lost");
            if(!client&&Time.realtimeSinceStartup>=nextSpeedDrive)
            {
                nextSpeedDrive=Time.realtimeSinceStartup+5;
                if(Mp.AsyncWorldTime.DesiredTimeSpeed==TimeSpeed.Paused)
                    Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Ultrafast);
                foreach(var map in Find.Maps)
                    if(map.AsyncTime().DesiredTimeSpeed==TimeSpeed.Paused)
                    {
                        Log.Message("PURGERELOAD RESUME map="+map.uniqueID+" tick="+map.AsyncTime().mapTicks);
                        Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)TimeSpeed.Ultrafast);
                    }
            }
            if(mapStarts==null){start=TickPatch.Timer;mapStarts=Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.AsyncTime().mapTicks).ToArray();}
            var deltas=Find.Maps.OrderBy(m=>m.uniqueID).Select((m,i)=>m.AsyncTime().mapTicks-mapStarts[i]).ToArray();
            int elapsed=deltas.Min();
            if(elapsed/10000>checkpoint){checkpoint=elapsed/10000;Log.Message("PURGERELOAD CHECKPOINT maps="+string.Join(",",deltas)+" network="+(TickPatch.Timer-start)+" desynced="+Mp.session.desynced);}
            if(elapsed>=(GenCommandLine.TryGetCommandLineArg("purgeticks",out var ticks)?int.Parse(ticks):120000))
            {
                complete=true;Log.Message("PURGERELOAD COMPLETE maps="+string.Join(",",deltas)+" network="+(TickPatch.Timer-start)+" desynced="+Mp.session.desynced);
                File.WriteAllText(Path.Combine(root,client?"client.complete":"host.complete"),Baseline());
            }
        }
        catch(Exception e){Fail(e);}
    }
}
