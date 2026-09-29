using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;
public sealed class OdysseySnapshotState:GameComponent
{
 public int jobId=-1;public bool dropRecorded;public int loads,beforeLoad;public bool active;public Pawn pawn;public Building_PassengerShuttle shuttle;
 public OdysseySnapshotState(Game game){}
 public override void ExposeData(){if(active&&Scribe.mode==LoadSaveMode.Saving)Log.Message("ODYSSEY_TRACE SAVE pawnSpawned="+pawn.Spawned+" tick="+shuttle.Map.AsyncTime().mapTicks+" paused="+shuttle.Map.AsyncTime().Paused);Scribe_Values.Look(ref jobId,"osJobId",-1);Scribe_Values.Look(ref dropRecorded,"osDropRecorded");Scribe_Values.Look(ref loads,"osLoads");Scribe_Values.Look(ref beforeLoad,"osBeforeLoad");Scribe_Values.Look(ref active,"osActive");Scribe_References.Look(ref pawn,"osPawn");Scribe_References.Look(ref shuttle,"osShuttle");if(Scribe.mode==LoadSaveMode.PostLoadInit)loads++;}
}
public static class SnapshotProbe
{
 static ISyncMethod prepare,check;static bool requested,prepared,joinPoint,resumed,checkedState;static int resumeTick;
 static OdysseySnapshotState S=>Current.Game.GetComponent<OdysseySnapshotState>();
 internal static int Pending{get{var t=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_TransportShipUnloadMp");return ((ICollection)AccessTools.Property(t,"PendingUnloadOperations").GetValue(null)).Count;}}
 public static void RequestJoinPoint(){object server=AccessTools.Property(typeof(Mp),"LocalServer").GetValue(null);if(server==null)throw new Exception("joinpoint requires host");AccessTools.Method(server.GetType(),"Enqueue").Invoke(server,new object[]{new Action(()=>{object data=AccessTools.Field(server.GetType(),"worldData").GetValue(server);if(!(bool)AccessTools.Method(data.GetType(),"TryStartJoinPointCreation").Invoke(data,new object[]{true}))Log.Error("ODYSSEY_PROBE FAILED joinpoint creation already active");})});}
 public static void Install(){prepare=MP.RegisterSyncMethod(typeof(SnapshotProbe),nameof(Prepare));check=MP.RegisterSyncMethod(typeof(SnapshotProbe),nameof(Check));}
 public static void Prepare(){
  var map=Find.Maps.OrderBy(m=>m.uniqueID).First();
  if(!map.AsyncTime().Paused)throw new Exception("snapshot map must already be paused");
  var def=DefDatabase<ThingDef>.GetNamed("PassengerShuttle");
  var cell=GenRadial.RadialCellsAround(map.Center,50,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(2).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  S.shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(def);S.shuttle.SetFaction(map.ParentFaction);GenSpawn.Spawn(S.shuttle,cell,map);
  S.pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,map.ParentFaction,forceGenerateNewPawn:true,mustBeCapableOfViolence:true));
  GenSpawn.Spawn(S.pawn,CellFinder.RandomClosewalkCellNear(cell,map,5),map);S.pawn.DeSpawn();
  if(!S.shuttle.TransporterComp.innerContainer.TryAdd(S.pawn,false))throw new Exception("snapshot cargo seed");
  var ship=S.shuttle.ShuttleComp.shipParent;ship.ForceJob_DelayCurrent(ShipJobMaker.MakeShipJob(ShipJobDefOf.Unload));
  var job=ship.curJob as ShipJob_Unload;if(job==null)throw new Exception("snapshot native unload job missing");S.jobId=job.loadID;S.dropRecorded=false;AccessTools.Method(typeof(ShipJob_Unload),"Drop").Invoke(job,null);
  if(Pending!=1||S.pawn.Spawned)throw new Exception("snapshot expected one pending unload");
  S.beforeLoad=S.loads;S.active=true;
  Log.Message("ODYSSEY_SNAPSHOT QUEUED pawn="+S.pawn.thingIDNumber+" loads="+S.loads+" pending="+Pending+" mapTick="+map.AsyncTime().mapTicks+" paused="+map.AsyncTime().Paused);
 }
 public static void Check(){
  if(!S.dropRecorded||S.loads<=S.beforeLoad||!S.pawn.Spawned||S.pawn.Map!=S.shuttle.Map||Pending!=0)throw new Exception("snapshot lost pending unload");
  Log.Message("ODYSSEY_SNAPSHOT COMPLETE pawn="+S.pawn.thingIDNumber+" loads="+S.loads+" pending="+Pending+" map="+S.pawn.Map.uniqueID+" desynced="+Mp.session.desynced);
  OdysseyProbe.Complete();
 }
 public static void Update(){
  bool client=GenCommandLine.CommandLineArgPassed("opclient");
  if(client&&!requested&&!S.active){requested=true;Mp.Client.SendCommand(CommandType.MapTimeSpeed,Find.Maps.OrderBy(m=>m.uniqueID).First().uniqueID,(byte)TimeSpeed.Paused);return;}
  if(client&&requested&&!prepared&&!S.active&&Find.Maps.OrderBy(m=>m.uniqueID).First().AsyncTime().Paused){prepared=prepare.DoSync(null);return;}
  if(!S.active)return;
  if(!client&&!joinPoint){joinPoint=true;RequestJoinPoint();return;}
  if(S.loads<=S.beforeLoad)return;
  if(ColdProbe.Update())return;
  if(!client)return;
  if(ColdProbe.Rejoining&&MP.RealPlayerFaction!=S.shuttle.Faction){Mp.Client.Send(new ClientSetFactionPacket(Mp.session.playerId,S.shuttle.Faction.loadID));return;}
  if(!resumed){resumed=true;resumeTick=TickPatch.Timer;Mp.Client.SendCommand(CommandType.MapTimeSpeed,S.shuttle.Map.uniqueID,(byte)TimeSpeed.Normal);return;}
  if(!checkedState&&TickPatch.Timer-resumeTick>120){checkedState=true;check.DoSync(null);}
 }
}
