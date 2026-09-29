using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class GHState : GameComponent
{
    public int stage, start, changed, starts, releases, mapStart;
    public Pawn pawn, enemy;
    public Thing item;
    public IntVec3 destination;
    public GHState(Game game) { }
    public override void ExposeData() {
        Scribe_Values.Look(ref stage,"gh_stage"); Scribe_Values.Look(ref start,"gh_start");
        Scribe_Values.Look(ref mapStart,"gh_mapStart");
        Scribe_Values.Look(ref changed,"gh_changed"); Scribe_Values.Look(ref starts,"gh_starts");
        Scribe_Values.Look(ref releases,"gh_releases"); Scribe_Values.Look(ref destination,"gh_destination");
        Scribe_References.Look(ref pawn,"gh_pawn"); Scribe_References.Look(ref enemy,"gh_enemy");
        Scribe_References.Look(ref item,"gh_item");
    }
    public override void GameComponentUpdate() { GHProbe.Update(); }
}
[StaticConstructorOnStartup]
public static class GHProbe
{
    static readonly bool enabled=GenCommandLine.CommandLineArgPassed("ghprobe");
    static readonly bool client=GenCommandLine.CommandLineArgPassed("ghclient");
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static readonly Type sync=AccessTools.TypeByName("MP_MeowOnlineShop.GodHandSync");
    static readonly Type patch=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_GodHands");
    static bool hosted,frozen,ready,failed,complete;
    static int dispatched=-1;
    static float next;
    static object controller;
    static bool requestedSnapshot, snapshotLoaded;
    static int checkpoint;
    static GHState S=>Current.Game.GetComponent<GHState>();
    static GHProbe() {
        if(!enabled)return;
        AccessTools.Method(patch,"Resolve").Invoke(null,null);
        MP.RegisterSyncMethod(typeof(GHProbe),nameof(Prepare));
        MP.RegisterSyncMethod(typeof(GHProbe),nameof(Advance));
        var h=new Harmony("meow.gh.probe");
        h.Patch(AccessTools.Method(sync,"SyncGodHandStartGrab"),postfix:new HarmonyMethod(typeof(GHProbe),nameof(Started)));
        h.Patch(AccessTools.Method(sync,"SyncGodHandRelease"),postfix:new HarmonyMethod(typeof(GHProbe),nameof(Released)));
        h.Patch(AccessTools.Method(typeof(SaveLoad),"SaveAndReload"),postfix:new HarmonyMethod(typeof(GHProbe),nameof(Reloaded)));
        // Drive only real controller boundaries; disable the physical mouse loop during scripted input.
        h.Patch(AccessTools.Method(AccessTools.TypeByName("GodHandMod.Designator_GodHand"),"SelectedUpdate"),prefix:new HarmonyMethod(typeof(GHProbe),nameof(NoPhysicalInput)));
        Set("canGrabHostile",client); Set("hasSeenHelp_GodHand",true);
        Log.Message("GHPROBE START peer="+(client?"client":"host")+" hash="+Hash(Path.Combine(root,"Game/Mods/Candidate/Assemblies/MP_MeowOnlineShop.dll"))+" mvid="+sync.Assembly.ManifestModule.ModuleVersionId);
        if(!client)LongEventHandler.ExecuteWhenFinished(()=>{
            Assert(File.Exists(GenFilePaths.FilePathForSavedGame("autostart")),"baseline missing");
            Log.Message("GHPROBE LOAD_BASELINE hash="+Hash(GenFilePaths.FilePathForSavedGame("autostart")));
            GameDataSaveLoader.LoadGame("autostart");
        });
    }
    static string Hash(string path){using(var s=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static void Set(string name,object v)=>AccessTools.Method(patch,"SetSettingsValue").Invoke(null,new[]{(object)name,v});
    static bool NoPhysicalInput()=>false;
    static void Reloaded(){if(Current.Game!=null && S.stage==10){snapshotLoaded=true;Log.Message("GHPROBE HELD_SNAPSHOT_LOADED item="+S.item?.thingIDNumber+" spawned="+S.item?.Spawned);}}
    static void Started(){if(MP.IsExecutingSyncCommand){S.starts++;Log.Message("GHPROBE START_EXEC tick="+TickPatch.Timer);}}
    static void Released(){if(MP.IsExecutingSyncCommand){S.releases++;Log.Message("GHPROBE RELEASE_EXEC tick="+TickPatch.Timer);}}
    static void Assert(bool b,string reason){if(!b)throw new Exception(reason);}
    static void Fail(Exception e){failed=true;Log.Error("GHPROBE FAILED "+e);File.WriteAllText(Path.Combine(root,client?"client.failed":"host.failed"),e.ToString());}
    static string Baseline()=>string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>"map="+m.uniqueID+" tick="+m.AsyncTime().mapTicks+" rand="+m.AsyncTime().randState))+";worldTick="+Mp.AsyncWorldTime.worldTicks+" worldRand="+Mp.AsyncWorldTime.randState;
    public static void Prepare(Map map){
        S.pawn=map.mapPawns.FreeColonists.First();
        S.pawn.drafter.Drafted=true;
        var enemyFaction=Find.FactionManager.AllFactionsListForReading.Where(f=>!f.IsPlayer && f.HostileTo(S.pawn.Faction)).OrderBy(f=>f.loadID).First();
        S.enemy=PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate,enemyFaction);
        Assert(S.enemy.HostileTo(S.pawn.Faction),"fixture is not hostile");
        GenSpawn.Spawn(S.enemy,CellFinder.RandomClosewalkCellNear(S.pawn.Position,map,7),map);
        S.enemy.mindState.mentalStateHandler.Reset();
        S.enemy.stances.stunner.StunFor(30000,null);
        S.destination=CellFinder.RandomClosewalkCellNear(S.pawn.Position,map,5);
        Assert(S.destination!=S.pawn.Position,"fixture destination must move");
        S.start=TickPatch.Timer;S.mapStart=map.AsyncTime().mapTicks;S.stage=1;S.changed=TickPatch.Timer;
        Log.Message("GHPROBE PREPARE pawn="+S.pawn.thingIDNumber+" enemy="+S.enemy.thingIDNumber+" faction="+enemyFaction.def.defName+" hostile="+S.enemy.HostileTo(S.pawn.Faction));
    }
    public static void Advance(int stage){
        try {
            if(stage==2){Assert(S.starts==1,"duplicate denied requests "+S.starts);Assert(S.enemy.CurJobDef!=JobDefOf.Wait || S.enemy.CurJob.expiryInterval!=99999,"hostile grabbed");S.enemy.Destroy();}
            if(stage==4||stage==6||stage==8){
                Assert(S.pawn.Position==S.destination,"landing "+S.pawn.Position+" != "+S.destination);
                Assert(S.pawn.CurJobDef!=JobDefOf.Wait || S.pawn.CurJob.expiryInterval!=99999,"stuck wait");
                Assert(S.starts==stage/2,"duplicate starts "+S.starts);Assert(S.releases==stage/2-1,"duplicate releases "+S.releases);
            }
            if(stage==9){S.item=ThingMaker.MakeThing(ThingDefOf.Steel);S.item.stackCount=7;GenSpawn.Spawn(S.item,CellFinder.RandomClosewalkCellNear(S.pawn.Position,S.pawn.Map,6),S.pawn.Map);}
            if(stage==10)Assert(S.item!=null&&!S.item.Spawned&&!S.item.Destroyed,"item was not grabbed");
            if(stage==12)Assert(S.item!=null&&S.item.Spawned&&S.item.Map==S.pawn.Map,"item lost after snapshot/release");
            if(stage==14||stage==16||stage==18)Assert(S.pawn.CurJobDef!=JobDefOf.Wait||S.pawn.CurJob.expiryInterval!=99999,"cancel left pawn waiting");
            S.stage=stage;S.changed=TickPatch.Timer;
            Log.Message("GHPROBE ASSERT stage="+stage+" starts="+S.starts+" releases="+S.releases+" pos="+S.pawn.Position);
        }catch(Exception e){Fail(e);throw;}
    }
    static void Select(){
        var d=(Designator)Activator.CreateInstance(AccessTools.TypeByName("GodHandMod.Designator_GodHand"));
        Find.DesignatorManager.Select(d);
        controller=AccessTools.Field(d.GetType(),"controller").GetValue(d);
    }
    static void Call(string method,params object[] args)=>AccessTools.Method(controller.GetType(),method).Invoke(controller,args);
    public static void Update(){
        if(!enabled)return;
        if(File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}
        if(failed||complete||Current.ProgramState!=ProgramState.Playing||LongEventHandler.ShouldWaitForEvent||Time.realtimeSinceStartup<next)return;
        next=Time.realtimeSinceStartup+0.1f;
        try{
            Assert(Time.realtimeSinceStartup<4000,"timeout");
            if(Find.CurrentMap==null)return;
            if(!client&&!hosted&&!MP.IsInMultiplayer){
                hosted=true;Mp.username="GHHost";
                GameDataSaveLoader.SaveGame("GHBaseline");
                Assert(HostWindow.HostProgrammatically(new ServerSettings{gameName="GodHands0911",direct=true,directAddress="127.0.0.1:30794",lan=false,steam=false,multifaction=false,asyncTime=false,syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=!GenCommandLine.CommandLineArgPassed("ghsoak")}),"host rejected");return;
            }
            if(!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying)return;
            Assert(!Mp.session.desynced,"desync");
            if(!client&&!frozen){frozen=true;Mp.Client.Send(new ClientFreezePacket(true));return;}
            if(client&&!ready){ready=true;string baseline=Baseline();Log.Message("GHPROBE BASELINE "+baseline);string marker=Path.Combine(root,"client.ready");File.WriteAllText(marker+".tmp",baseline);File.Move(marker+".tmp",marker);}
            if(!File.Exists(Path.Combine(root,"client.ready"))||Mp.session.players.Count!=2)return;
            if(!client&&!ready){string baseline=Baseline();Log.Message("GHPROBE BASELINE "+baseline);Assert(baseline==File.ReadAllText(Path.Combine(root,"client.ready")),"BASELINE_LOAD_DRIFT");ready=true;Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);}
            int elapsed=Find.CurrentMap.AsyncTime().mapTicks-S.mapStart;
            if(S.stage>0&&elapsed/10000>checkpoint){checkpoint=elapsed/10000;Log.Message("GHPROBE CHECKPOINT sharedMapTicks="+elapsed+" networkTicks="+(TickPatch.Timer-S.start)+" stage="+S.stage+" players="+Mp.session.players.Count+" desynced="+Mp.session.desynced);}
            if(S.stage==18 && elapsed>=120000){complete=true;Log.Message("GHPROBE COMPLETE desynced="+Mp.session.desynced+" sharedMapTicks="+elapsed+" networkTicks="+(TickPatch.Timer-S.start));File.WriteAllText(Path.Combine(root,client?"client.complete":"host.complete"),"complete");return;}
            if(!client&&S.stage==10&&!requestedSnapshot){requestedSnapshot=true;Mp.Client.Send(ClientChatPacket.Create("/joinpoint"));Log.Message("GHPROBE REQUEST held snapshot");}
            if(!client)return;
            if(S.stage==0&&dispatched!=0){dispatched=0;Prepare(Find.CurrentMap);return;}
            if(S.stage==1&&dispatched!=1){dispatched=1;Select();for(int i=0;i<20;i++)Call("TryStartGrab",Find.CurrentMap,S.enemy,S.enemy.Position);Log.Message("GHPROBE DISPATCH denied x20");return;}
            if(S.stage==1&&TickPatch.Timer-S.changed>120){Advance(2);return;}
            if((S.stage==2||S.stage==4||S.stage==6)&&dispatched!=S.stage){dispatched=S.stage;Select();for(int i=0;i<20;i++)Call("TryStartGrab",Find.CurrentMap,S.pawn,S.pawn.Position);Log.Message("GHPROBE DISPATCH start x20");return;}
            if((S.stage==2||S.stage==4||S.stage==6)&&TickPatch.Timer-S.changed>90){Advance(S.stage+1);return;}
            if((S.stage==3||S.stage==5||S.stage==7)&&dispatched!=S.stage){dispatched=S.stage;var position=S.pawn.Position;Call("UpdateDragged",Find.CurrentMap,S.destination,Time.time);Assert(S.pawn.Position==position,"local preview mutated simulation");for(int i=0;i<20;i++)Call("ReleaseGrab",Find.CurrentMap,S.destination,Time.time);Log.Message("GHPROBE DISPATCH release x20 previewPure=True");return;}
            if((S.stage==3||S.stage==5||S.stage==7)&&TickPatch.Timer-S.changed>90){Advance(S.stage+1);return;}
            if(S.stage==8&&dispatched!=8){dispatched=8;Advance(9);return;}
            if(S.stage==9&&dispatched!=9){dispatched=9;Select();for(int i=0;i<20;i++)Call("TryStartGrabItem",Find.CurrentMap,S.item,S.item.Position);return;}
            if(S.stage==9&&TickPatch.Timer-S.changed>120){Advance(10);return;}
            if(S.stage==10&&snapshotLoaded&&dispatched!=10){dispatched=10;Assert(S.item!=null&&!S.item.Spawned,"snapshot lost held item");Select();Call("TryStartGrabItem",Find.CurrentMap,S.item,S.destination);return;}
            if(S.stage==10&&snapshotLoaded&&dispatched==10&&TickPatch.Timer-S.changed>600){Advance(11);return;}
            if(S.stage==11&&dispatched!=11){dispatched=11;Call("UpdateDragged",Find.CurrentMap,S.destination,Time.time);for(int i=0;i<20;i++)Call("ReleaseGrab",Find.CurrentMap,S.destination,Time.time);return;}
            if(S.stage==11&&TickPatch.Timer-S.changed>120){Advance(12);return;}
            if((S.stage==12||S.stage==14||S.stage==16)&&dispatched!=S.stage){dispatched=S.stage;Select();Call("TryStartGrab",Find.CurrentMap,S.pawn,S.pawn.Position);return;}
            if((S.stage==12||S.stage==14||S.stage==16)&&TickPatch.Timer-S.changed>90){Advance(S.stage+1);return;}
            if((S.stage==13||S.stage==15||S.stage==17)&&dispatched!=S.stage){dispatched=S.stage;Find.DesignatorManager.Deselect();return;}
            if((S.stage==13||S.stage==15||S.stage==17)&&TickPatch.Timer-S.changed>120){Advance(S.stage+1);return;}
        }catch(Exception e){Fail(e);}
    }
}
