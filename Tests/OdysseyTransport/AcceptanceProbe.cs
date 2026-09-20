using System;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
public static class AcceptanceProbe
{
 static Building_PassengerShuttle shuttle;static Thing cargo;static Pawn hauler;static int phase,round,groundBefore;static bool requested,ready,done,checking;static float deadline;static ISyncMethod setup,check;
 public static void Restart(){shuttle=null;cargo=null;hauler=null;phase=round=0;requested=ready=done=checking=false;}
 static TransporterLoading Session=>shuttle.Map.MpComp().sessionManager.GetFirstOfType<TransporterLoading>();
 public static void Install(){setup=MP.RegisterSyncMethod(typeof(AcceptanceProbe),nameof(Setup));check=MP.RegisterSyncMethod(typeof(AcceptanceProbe),nameof(Check));}
 static void Require(bool ok,string why){if(!ok)throw new Exception("acceptance: "+why);}
 public static void Setup(){
  var maps=Find.Maps.OrderBy(m=>m.uniqueID).ToArray();var map=maps[round%maps.Length];var owner=maps[0].ParentFaction;var def=DefDatabase<ThingDef>.GetNamed("PassengerShuttle");
  var cell=GenRadial.RadialCellsAround(map.Center,40,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(2).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(def);shuttle.SetFaction(owner);GenSpawn.Spawn(shuttle,cell,map);
  cargo=ThingMaker.MakeThing(ThingDefOf.Uranium);cargo.stackCount=31;GenSpawn.Spawn(cargo,CellFinder.RandomClosewalkCellNear(cell,map,5),map);cargo.SetForbidden(false);map.fogGrid.Unfog(cargo.Position);
  // Native home-map manifest filters items outside home/storage.
  map.PushFaction(owner,true);try{map.areaManager.Home[cargo.Position]=true;}finally{map.PopFaction();}
  hauler=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,owner,forceGenerateNewPawn:true));GenSpawn.Spawn(hauler,CellFinder.RandomClosewalkCellNear(cell,map,4),map);
  groundBefore=map.listerThings.ThingsOfDef(ThingDefOf.Uranium).Sum(t=>t.stackCount);ready=true;
 }
 public static void Check(){
  int loaded=shuttle.TransporterComp.innerContainer.Where(t=>t.def==ThingDefOf.Uranium).Sum(t=>t.stackCount);
  Require(loaded==17,"loaded amount");int groundNow=shuttle.Map.listerThings.ThingsOfDef(ThingDefOf.Uranium).Sum(t=>t.stackCount);Require(groundBefore-groundNow==17,"ground cargo conservation");Require(!shuttle.TransporterComp.AnythingLeftToLoad,"unfinished manifest");
  Require(shuttle.Faction==hauler.Faction,"shuttle owner changed");
  Log.Message("ODYSSEY_ACCEPT COMPLETE round="+round+" map="+shuttle.Map.uniqueID+" loaded="+loaded+" groundRemoved="+(groundBefore-groundNow)+" owner="+shuttle.Faction.loadID);
  round++;phase=0;requested=ready=checking=false;if(round==3)done=true;
 }
 public static bool Update(){
  if(done)return false;if(!GenCommandLine.CommandLineArgPassed("opclient"))return true;
  if(!requested){requested=setup.DoSync(null);deadline=Time.realtimeSinceStartup+120;return true;}Require(Time.realtimeSinceStartup<deadline,"timeout phase="+phase);if(!ready)return true;
  Current.Game.CurrentMap=shuttle.Map;
  switch(phase){
   case 0:Find.Selector.ClearSelection();Find.Selector.Select(shuttle);shuttle.GetGizmos().OfType<Command_LoadToTransporter>().Single().ProcessInput(null);phase++;break;
   case 1:if(Session!=null){var t=Session.transferables.FirstOrDefault(x=>x.things.Contains(cargo));Require(t!=null,"native cargo missing");MP.WatchBegin();try{var r=new MpTransferableReference(Session,t);SyncFields.SyncTradeableCount.Watch(r);r.CountToTransfer=17;}finally{MP.WatchEnd();}phase++;}break;
   case 2:if(Session.transferables.First(t=>t.things.Contains(cargo)).CountToTransfer==17){Session.TryAccept();phase++;}break;
   case 3:if(Session==null){Require(shuttle.TransporterComp.LoadingInProgressOrReadyToLaunch,"accept rejected");Find.WindowStack.WindowOfType<TransporterLoadingProxy>()?.Close(false);Require(LoadTransportersJobUtility.HasJobOnTransporter(hauler,shuttle.TransporterComp),"native haul unavailable");hauler.jobs.TryTakeOrderedJob(LoadTransportersJobUtility.JobOnTransporter(hauler,shuttle.TransporterComp),JobTag.Misc);phase++;}break;
   case 4:if(!shuttle.TransporterComp.AnythingLeftToLoad&&!checking)checking=check.DoSync(null);break;
  }
  return true;
 }
}
