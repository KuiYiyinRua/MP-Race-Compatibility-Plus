using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Verse;
using UnityEngine;
using Mp=Multiplayer.Client.Multiplayer;

public sealed class Replay325State : GameComponent
{
    public int end=-1;
    public Replay325State(Game game) { }
    public override void ExposeData() { Scribe_Values.Look(ref end,"replay325End",-1); }
    public override void GameComponentUpdate() => Replay325Probe.Update();
}

[StaticConstructorOnStartup]
public static class Replay325Probe
{
    static readonly bool enabled=GenCommandLine.CommandLineArgPassed("desync310probe");
    static readonly bool client=GenCommandLine.CommandLineArgPassed("probeclient");
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static bool loading, loaded, hosting, ready, sent, failed, done;
    static int start, checkpoint;
    static string Peer => client?"client":"host";
    static ISyncMethod begin;
    static Replay325State State=>Current.Game.GetComponent<Replay325State>();
    static Replay325Probe()
    {
        if(!enabled)return;
        begin=MP.RegisterSyncMethod(typeof(Replay325Probe),nameof(Begin));
        new Harmony("local.meow.replay325").Patch(AccessTools.Method(typeof(Root_Entry),"Update"),
            postfix:new HarmonyMethod(typeof(Replay325Probe),nameof(Update)));
        var type=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_MpConfigHotSync");
        Log.Message("REPLAY325 RUN peer="+Peer+" label="+Path.GetFileName(root)+" hash="+File.ReadAllText(Path.Combine(root,"candidate.sha256")).Trim()+" version="+type.Assembly.GetName().Version+" mvid="+type.Module.ModuleVersionId);
    }
    static string Snapshot()=>Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";"+
        string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState));
    static void Check(bool value,string reason) {if(!value)throw new Exception(reason);}
    public static void Begin(int end) {State.end=end;start=TickPatch.Timer;checkpoint=start+1000;Log.Message("REPLAY325 BEGIN tick="+start+" target="+end+" maps="+Find.Maps.Count);}
    public static void Update()
    {
        if(!enabled)return;
        if(File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}
        if(done||failed||LongEventHandler.AnyEventNowOrWaiting)return;
        try
        {
            if(!client&&!loading&&Current.ProgramState==ProgramState.Entry)
            {
                loading=true;Mp.username="Replay325Host";
                ClientUtil.DoubleLongEvent(()=>Replay.LoadReplay(new FileInfo(Path.Combine(root,"Input.zip")),true,
                    ()=>{loaded=true;Log.Message("REPLAY325 LOADED "+Snapshot());},()=>{throw new Exception("Replay cancelled");}),"MpLoading");return;
            }
            if(!client&&loaded&&!hosting&&Current.ProgramState==ProgramState.Playing&&!TickPatch.Simulating)
            {
                hosting=true;
                Check(Mp.WorldComp.spectatorFaction!=null,"BASELINE_INVALID missing serialized spectator faction; cannot host this replay");
                Mp.AsyncWorldTime.SetTimeEverywhere(TimeSpeed.Paused);
                var settings=new ServerSettings {gameName="Replay325",direct=true,directAddress="127.0.0.1:31036",lan=false,steam=false,
                    multifaction=false,asyncTime=false,syncConfigs=true,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,
                    desyncTraces=!GenCommandLine.CommandLineArgPassed("probefullsoak")};
                Check((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}),"Host pre-init failed");
                Check(Mp.LocalServer!=null,"Host pre-init did not retain server");
                HostUtil.HostServer(settings,true);return;
            }
            if(Current.ProgramState!=ProgramState.Playing||!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            Check(!Mp.session.desynced,"desync");
            if(!client&&!ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
            if(client&&!ready)
            {
                if(!TickPatch.Frozen)return;ready=true;
                File.WriteAllText(Path.Combine(root,"client.ready"),Snapshot());Log.Message("REPLAY325 CLIENT_LOADED_FROZEN "+Snapshot());
            }
            if(Mp.session.players.Count!=2||!File.Exists(Path.Combine(root,"client.ready")))return;
            if(!client&&!ready)
            {
                Check(Snapshot()==File.ReadAllText(Path.Combine(root,"client.ready")),"BASELINE_LOAD_DRIFT");
                ready=true;Log.Message("REPLAY325 HOST_FROZEN "+Snapshot());Mp.Client.Send(new ClientFreezePacket(false));
                Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Normal);
            }
            if(TickPatch.Frozen)return;
            if(State.end<0){if(client&&!sent)sent=begin.DoSync(null,TickPatch.Timer+(GenCommandLine.CommandLineArgPassed("probefullsoak")?120100:10100));return;}
            if(TickPatch.Timer>=checkpoint){Log.Message("REPLAY325 CHECKPOINT tick="+TickPatch.Timer+" players=2 desynced=False "+Snapshot());checkpoint=TickPatch.Timer+1000;}
            if(TickPatch.Timer>=State.end){done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"PASS "+TickPatch.Timer);Log.Message("REPLAY325 COMPLETE tick="+TickPatch.Timer+" desynced=False");}
        }
        catch(Exception e){failed=true;File.WriteAllText(Path.Combine(root,Peer+".failed"),e.ToString());Log.Error("REPLAY325 FAILED "+e);}
    }
}
