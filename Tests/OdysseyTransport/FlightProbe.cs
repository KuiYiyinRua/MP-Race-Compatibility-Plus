using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
public static class FlightProbe
{
 static Building_PassengerShuttle shuttle;static Pawn pilot;static Map target;
 static IntVec3 targetCell;
 static ISyncMethod setup,check;static bool requested,ready,launched,checking,done;static int round;static float deadline;
 public static void Restart(){shuttle=null;pilot=null;round=0;requested=ready=launched=checking=done=false;}
 public static void Install(){setup=MP.RegisterSyncMethod(typeof(FlightProbe),nameof(Setup));check=MP.RegisterSyncMethod(typeof(FlightProbe),nameof(Check));}
 static void Require(bool ok,string why){if(!ok)throw new Exception("flight: "+why);}
 public static void Setup(){
  var maps=Find.Maps.OrderBy(m=>m.uniqueID).ToArray();var map=maps[round%2];target=maps.First(m=>m!=map);
  if(shuttle==null){
   var def=DefDatabase<ThingDef>.GetNamed("PassengerShuttle");
   var cell=GenRadial.RadialCellsAround(map.Center,35,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(1).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
   shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(def);shuttle.SetFaction(maps[0].ParentFaction);GenSpawn.Spawn(shuttle,cell,map);
   pilot=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,maps[0].ParentFaction,forceGenerateNewPawn:true));
   GenSpawn.Spawn(pilot,CellFinder.RandomClosewalkCellNear(cell,map,5),map);
  }
  Require(shuttle.Map==map,"wrong origin");Require(pilot.Spawned,"pilot missing before load");
  pilot.DeSpawn();Require(shuttle.TransporterComp.innerContainer.TryAdd(pilot,false),"fixture boarding");
  shuttle.RefuelableComp.Refuel(400);shuttle.TransporterComp.groupID=Find.UniqueIDsManager.GetNextTransporterGroupID();
  shuttle.TryGetComp<CompLaunchable>().lastLaunchTick=-1;
  targetCell=GenRadial.RadialCellsAround(target.Center,35,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,shuttle.def.size).ExpandedBy(1).Cells.All(p=>p.InBounds(target)&&p.Standable(target)&&p.GetThingList(target).All(t=>t is Plant)));
  ready=true;
 }
 public static void Check(){
  Require(shuttle.Spawned&&shuttle.Map==target,"arrival map");Require(pilot.Spawned&&pilot.Map==target,"passenger arrival");
  Require(shuttle.Faction==Find.Maps.OrderBy(m=>m.uniqueID).First().ParentFaction,"owner changed");
  Require(shuttle.FuelLevel==350,"fuel conservation");
  var launch=shuttle.TryGetComp<CompLaunchable>();int elapsed=target.AsyncTime().mapTicks-launch.lastLaunchTick;
  Require(elapsed>=0&&elapsed<1000,"arrival cooldown wrong clock: "+elapsed);
  Log.Message("ODYSSEY_FLIGHT COMPLETE round="+round+" map="+target.uniqueID+" fuel="+shuttle.FuelLevel+" elapsed="+elapsed+" pawn="+pilot.thingIDNumber);
  round++;requested=ready=launched=checking=false;if(round==3)done=true;
 }
 public static bool Update(){
  if(done)return false;if(!GenCommandLine.CommandLineArgPassed("opclient"))return true;
  if(!requested){requested=setup.DoSync(null);deadline=Time.realtimeSinceStartup+120;return true;}
  Require(Time.realtimeSinceStartup<deadline,"timeout");if(!ready)return true;
  if(!launched){
   Current.Game.CurrentMap=shuttle.Map;var launch=shuttle.TryGetComp<CompLaunchable>();
   Require(launch.CanLaunch(),"native launch blocked: "+launch.CanLaunch().Reason);
   launch.TryLaunch(target.Tile,new TransportersArrivalAction_LandInSpecificCell(target.Parent,targetCell,Rot4.North,true));
   Require(shuttle.Spawned,"unsynced launch mutation");launched=true;Current.Game.CurrentMap=target;return true;
  }
  if(!checking&&shuttle.Spawned&&shuttle.Map==target&&pilot.Spawned)checking=check.DoSync(null);
  return true;
 }
}
