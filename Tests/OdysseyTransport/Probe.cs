using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class OdysseyProbeState : GameComponent
{
 public int start, stage, round;
 public Pawn passenger;
 public Building_PassengerShuttle shuttle;
 public OdysseyProbeState(Game game) { }
 public override void ExposeData() {
  Scribe_Values.Look(ref start,"opStart"); Scribe_Values.Look(ref stage,"opStage");
  Scribe_Values.Look(ref round,"opRound");
  Scribe_References.Look(ref passenger,"opPassenger"); Scribe_References.Look(ref shuttle,"opShuttle");
 }
 public override void GameComponentUpdate() { OdysseyProbe.Update(); }
}

[StaticConstructorOnStartup]
public static class OdysseyProbe
{
 static readonly bool enabled=GenCommandLine.CommandLineArgPassed("odysseyprobe");
 static readonly bool client=GenCommandLine.CommandLineArgPassed("opclient");
 static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
 static bool baselineLoading; static bool hosted, ready, failed, done, announced, sent;
 static Faction secondFaction;
 static float next, deadline;
 static ISyncMethod begin, check;
 static OdysseyProbeState S=>Current.Game.GetComponent<OdysseyProbeState>();
 static string Peer=>client?"client":"host";
 static Type Unload=>AccessTools.TypeByName("MP_MeowOnlineShop.Patch_TransportShipUnloadMp");
 static int Pending=>((ICollection)(AccessTools.Field(Unload,"PendingUnloadOperations")?.GetValue(null)??AccessTools.Property(Unload,"PendingUnloadOperations").GetValue(null))).Count;
 static string Snapshot()=>"shared="+TickPatch.Timer+";current="+Find.CurrentMap.uniqueID+";players="+Mp.session.players.Count+";"+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.ParentFaction.loadID+":"+m.IsPlayerHome+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState))+";w="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState;
 static string Hash(string path){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
 static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Fail(Exception e){failed=true;Log.Error("ODYSSEY_PROBE FAILED "+e);File.WriteAllText(Path.Combine(root,Peer+".failed"),e.ToString());}
 static void Seed(){Rand.PushState(20920621);} static void Unseed(){Rand.PopState();}
 static OdysseyProbe(){
  if(!enabled)return;
  begin=MP.RegisterSyncMethod(typeof(OdysseyProbe),nameof(Begin));
  check=MP.RegisterSyncMethod(typeof(OdysseyProbe),nameof(Check));
  var h=new Harmony("meow.odyssey.probe");ColdProbe.Install(h);
  SnapshotProbe.Install();
  UIRefuelProbe.Install(); FlightProbe.Install(); LoadingProbe.Install(); AcceptanceProbe.Install(); SoakProbe.Install(); PilotProbe.Install(); ControlProbe.Install(); RecoveryProbe.Install(); UnloadControlProbe.Install();
  h.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(OdysseyProbe),nameof(Update)));
  h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(OdysseyProbe),nameof(Seed)),finalizer:new HarmonyMethod(typeof(OdysseyProbe),nameof(Unseed)));
  h.Patch(AccessTools.Method(Unload,"ReplayDeferredUnload"),postfix:new HarmonyMethod(typeof(OdysseyProbe),nameof(AfterUnload)));
  Log.Message("ODYSSEY_PROBE START run="+root+" peer="+Peer+" candidate="+Hash(Path.Combine(root,"Candidate/MP_MeowOnlineShop.dll"))+" config="+Hash(Path.Combine(GenFilePaths.SaveDataFolderPath,"Config/ModsConfig.xml")));
 }
 public static void Begin(){
  Require(S.stage==0,"duplicate start");
  var ordered=Find.Maps.OrderBy(m=>m.uniqueID).ToArray();
  var map=ordered[S.round%ordered.Length];
  var pawnFaction=ordered[0].ParentFaction;
  var def=DefDatabase<ThingDef>.GetNamed("PassengerShuttle");
  var cell=GenRadial.RadialCellsAround(map.Center,40,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(2).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  S.shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(def);
  S.shuttle.SetFaction(pawnFaction);
  GenSpawn.Spawn(S.shuttle,cell,map);
  S.passenger=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,pawnFaction,forceGenerateNewPawn:true,mustBeCapableOfViolence:true));
  GenSpawn.Spawn(S.passenger,CellFinder.RandomClosewalkCellNear(cell,map,5),map);
  Require(S.passenger.drafter!=null,"spawned colonist must have drafter");
  S.passenger.DeSpawn();
  var cargo=ThingMaker.MakeThing(ThingDefOf.Steel);cargo.stackCount=7;
  Require(S.passenger.inventory.innerContainer.TryAdd(cargo,false),"seed unloadable cargo");
  Require(S.shuttle.TransporterComp.innerContainer.TryAdd(S.passenger,false),"add passenger");
  var dropped=new List<Thing>();
  for(int i=0;i<3;i++)ShipJob_Unload.UnloadThingFromShuttle(S.shuttle.ShuttleComp.shipParent,S.passenger,dropped,true);
  S.start=TickPatch.Timer;S.stage=1;
  Log.Message("ODYSSEY_DIAGNOSTIC QUEUED round="+S.round+" pending="+Pending+" spawned="+S.passenger.Spawned+" home="+map.IsPlayerHome+" pawn="+S.passenger.thingIDNumber+" map="+map.uniqueID+" faction="+S.passenger.Faction?.loadID+" defPlayer="+S.passenger.Faction?.def.isPlayer+" drafter="+(S.passenger.drafter!=null));
 }
 public static void Check(){
  try {
  Require(S.stage==1,"check sequence");
  Require(S.passenger.Spawned,"passenger never unloaded");
  string receipt="ODYSSEY_DIAGNOSTIC RESULT pawn="+S.passenger.thingIDNumber+" home="+S.passenger.Map.IsPlayerHome+" drafted="+S.passenger.Drafted+" inventoryUnload="+S.passenger.inventory.UnloadEverything+" pending="+Pending+" desynced="+Mp.session.desynced;
  Require(Pending==0,"pending unload queue must drain");
  Log.Message(receipt);File.AppendAllText(Path.Combine(root,Peer+".receipt"),"round="+S.round+" "+receipt+Environment.NewLine);
  var map=S.shuttle.Map;
  var guard=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_GravshipOrphanCommands");
  var foreign=Find.Maps.Select(m=>m.ParentFaction).First(f=>f!=map.ParentFaction);
  AccessTools.Field(guard,"_inTakeoffEnded").SetValue(null,true);
  AccessTools.Field(guard,"_pendingTakeoffMapId").SetValue(null,map.uniqueID);
  AccessTools.Field(guard,"_takeoffEngineFaction").SetValue(null,foreign);
  try {
   GravshipUtility.AbandonMap(map);
   bool marked=((HashSet<int>)AccessTools.Field(guard,"AbandonedMapIds").GetValue(null)).Contains(map.uniqueID);
   Require(Find.Maps.Contains(map)&&!marked,"retained foreign base must accept commands");
   string retained="ODYSSEY_DIAGNOSTIC RETAINED present="+Find.Maps.Contains(map)+" markedAbandoned="+marked+" owner="+map.ParentFaction.loadID+" engineOwner="+foreign.loadID;
   Log.Message(retained);File.AppendAllText(Path.Combine(root,Peer+".receipt"),Environment.NewLine+retained);
  } finally {
   AccessTools.Field(guard,"_inTakeoffEnded").SetValue(null,false);
   AccessTools.Field(guard,"_pendingTakeoffMapId").SetValue(null,-1);
   AccessTools.Field(guard,"_takeoffEngineFaction").SetValue(null,null);
   ((HashSet<int>)AccessTools.Field(guard,"AbandonedMapIds").GetValue(null)).Clear();
  }
  S.round++;sent=false;
  if(S.round<3){S.stage=0;return;}
  LifecycleProbe.Run();
  S.stage=2;
  }catch(Exception e){Fail(e);throw;}
 }
 public static void Update(){
  if(!enabled)return;
  if(File.Exists(Path.Combine(root,"quit"))||(client&&File.Exists(Path.Combine(root,"client.quit")))){Application.Quit();return;}
  if(!client&&!baselineLoading&&Current.ProgramState==ProgramState.Entry&&!LongEventHandler.AnyEventNowOrWaiting&&File.Exists(Path.Combine(root,"baseline"))){baselineLoading=true;LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("OdysseyInput"),"Loading baseline",false,null);return;}
  if(failed||done||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||Time.realtimeSinceStartup<next)return;
  next=Time.realtimeSinceStartup+0.1f;
  try {
   if(!client&&!hosted&&!MP.IsInMultiplayer){
    hosted=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
    if(!baselineLoading){var faction=Faction.OfPlayer;
    secondFaction=new Faction{loadID=Find.UniqueIDsManager.GetNextFactionID(),def=faction.def,Name="OdysseyProbeB"};
    secondFaction.ideos=new FactionIdeosTracker(secondFaction);secondFaction.ideos.SetPrimary(faction.ideos.PrimaryIdeo);
    foreach(var f in Find.FactionManager.AllFactionsListForReading)secondFaction.TryMakeInitialRelationsWith(f);
    Find.FactionManager.Add(secondFaction);
    var first=Find.CurrentMap;
    var neighbors=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(first.Tile,neighbors);
    var tile=neighbors.OrderBy(t=>t.tileId).First(t=>!Find.WorldObjects.AnyMapParentAt(t)&&Find.WorldPathGrid.Passable(t));
    var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=tile;parent.SetFaction(secondFaction);Find.WorldObjects.Add(parent);
    using(MpScope.PushFaction(secondFaction)){Rand.PushState(20920622);try{GetOrGenerateMapUtility.GetOrGenerateMap(tile,new IntVec3(80,1,80),WorldObjectDefOf.Settlement);}finally{Rand.PopState();}}
    Current.Game.CurrentMap=first;
    GameDataSaveLoader.SaveGame("OdysseyBaseline");}
    if(ControlProbe.Maps==1){foreach(var extra in Find.Maps.Skip(1).ToArray())Current.Game.DeinitAndRemoveMap(extra,false);}
    Mp.username="OdysseyHost";
    Require(HostWindow.HostProgrammatically(new ServerSettings{gameName="OdysseyDiagnostic",direct=true,directAddress="127.0.0.1:"+(GenCommandLine.TryGetCommandLineArg("opport",out var probePort)?probePort:"30987"),lan=false,steam=false,multifaction=ControlProbe.Maps!=1,asyncTime=ControlProbe.Maps==0,syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=true}),"host rejected");return;
   }
   if(!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
   Require(!Mp.session.desynced,"desync");
   if(!announced){
    if(client&&!ColdProbe.Rejoining&&MP.RealPlayerFaction!=Find.Maps[0].ParentFaction){Mp.Client.Send(new ClientSetFactionPacket(Mp.session.playerId,Find.Maps[0].ParentFaction.loadID));return;}
    Current.Game.CurrentMap=Find.Maps[0];announced=true;
    var t=AccessTools.TypeByName("Multiplayer.Client.Patches.VTRSync");
    if((int)AccessTools.Field(t,"lastMovedToMapId").GetValue(null)==-1)
     AccessTools.Method(t,"SendViewedMapUpdate").Invoke(null,new object[]{-1,Find.Maps[0].uniqueID});
    Log.Message("ODYSSEY_PROBE LOADED mvid="+Unload.Module.ModuleVersionId);return;
   }
   if(RecoveryProbe.Active){RecoveryProbe.Update();return;}
   if(SoakProbe.Active){SoakProbe.Update();return;}
   if(ColdProbe.Enabled&&Current.Game.GetComponent<OdysseySnapshotState>().active){SnapshotProbe.Update();return;}
   if(!client&&!ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
   if(client&&!ready){if(!TickPatch.Frozen)return;ready=true;File.WriteAllText(Path.Combine(root,"client.ready.tmp"),Snapshot());File.Move(Path.Combine(root,"client.ready.tmp"),Path.Combine(root,"client.ready"));Log.Message("ODYSSEY_PROBE CLIENT_LOADED_FROZEN "+Snapshot());}
   if(Mp.session.players.Count!=2||!File.Exists(Path.Combine(root,"client.ready")))return;
   if(!client&&!ready){Require(Snapshot()==File.ReadAllText(Path.Combine(root,"client.ready")),"BASELINE_LOAD_DRIFT");ready=true;Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)(m==Find.Maps[0]?TimeSpeed.Fast:TimeSpeed.Normal));}
   if(UnloadControlProbe.Enabled&&TickPatch.Timer>=1000){UnloadControlProbe.Update();return;}
   if(ControlProbe.Maps>0&&TickPatch.Timer>=1000){ControlProbe.Update();return;}
   if(GenCommandLine.CommandLineArgPassed("opstaleonly")&&TickPatch.Timer>=1000){if(LoadingProbe.Update())return;if(GenCommandLine.CommandLineArgPassed("oppilotonly"))PilotProbe.Update();return;}
   if(GenCommandLine.CommandLineArgPassed("oppilotonly")&&TickPatch.Timer>=1000){PilotProbe.Update();return;}
   if(GenCommandLine.CommandLineArgPassed("opsnapshotonly")&&TickPatch.Timer>=1000){SnapshotProbe.Update();return;}
   if(S.stage==2){if(UIRefuelProbe.Update())return;if(FlightProbe.Update())return;if(LoadingProbe.Update())return;if(AcceptanceProbe.Update())return;if(GenCommandLine.CommandLineArgPassed("oppilotafter")&&!PilotProbe.Done){PilotProbe.Update();return;}SnapshotProbe.Update();return;}
   if(!client)return;
   if(TickPatch.Timer<1000)return;
   if(S.stage==0&&!sent){sent=begin.DoSync(null);deadline=Time.realtimeSinceStartup+60;Log.Message("ODYSSEY_PROBE DISPATCH native unload x3");return;}
   if(S.stage==1&&TickPatch.Timer-S.start>120){check.DoSync(null);next=Time.realtimeSinceStartup+2;}
   Require(!sent||Time.realtimeSinceStartup<deadline,"action timeout");
  }catch(Exception e){Fail(e);}
 }
 static void AfterUnload(object operation,bool __result){
  if(!__result)return;
  var pawn=AccessTools.Field(operation.GetType(),"Thing").GetValue(operation) as Pawn;
  if(Current.Game.GetComponent<OdysseySnapshotState>().active&&pawn==Current.Game.GetComponent<OdysseySnapshotState>().pawn)Log.Message("ODYSSEY_TRACE REPLAY paused="+pawn.Map.AsyncTime().Paused+" mapTick="+pawn.Map.AsyncTime().mapTicks+" loads="+Current.Game.GetComponent<OdysseySnapshotState>().loads+" stack="+Environment.StackTrace);
  var snapshot=Current.Game.GetComponent<OdysseySnapshotState>();if(snapshot.active&&pawn==snapshot.pawn){var job=snapshot.shuttle.ShuttleComp.shipParent.curJob as ShipJob_Unload;snapshot.dropRecorded=job!=null&&job.loadID==snapshot.jobId&&((System.Collections.Generic.List<Thing>)AccessTools.Field(typeof(ShipJob_Unload),"droppedThings").GetValue(job)).Contains(pawn);Require(snapshot.dropRecorded,"snapshot native droppedThings list not rebound");}
  if(pawn!=S.passenger)return;
  bool home=pawn.Map.ParentFaction==pawn.Faction;
  Require(pawn.Drafted==!home,"native auto-draft mismatch after spawn");
  Require(pawn.inventory.UnloadEverything==home,"native inventory unload mismatch after spawn");
  Log.Message("ODYSSEY_EFFECT round="+S.round+" home="+home+" drafted="+pawn.Drafted+" inventoryUnload="+pawn.inventory.UnloadEverything+" pawn="+pawn.thingIDNumber);
 }
 public static void Complete(){if(RecoveryProbe.Cycles>0&&!RecoveryProbe.Finished){RecoveryProbe.Begin();return;}if(SoakProbe.Duration>0){SoakProbe.Begin();return;}FinishRun();}
 public static void FinishRun(){done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"combined assertions and snapshot complete");}
}
