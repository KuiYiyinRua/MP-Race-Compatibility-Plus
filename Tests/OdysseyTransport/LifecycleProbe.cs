using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Factions;
using RimWorld;
using RimWorld.Planet;
using Verse;
public static class LifecycleProbe
{
 public static void Run(){
  var maps=Find.Maps.OrderBy(m=>m.uniqueID).ToArray();
  var map=maps[0];var destination=maps[1];var owner=destination.ParentFaction;
  var center=GenRadial.RadialCellsAround(map.Center,55,true).First(c=>CellRect.CenteredOn(c,6).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  foreach(var c in CellRect.CenteredOn(center,5).Cells)map.terrainGrid.SetFoundation(c,DefDatabase<TerrainDef>.GetNamed("Substructure"));
  var engine=(Building_GravEngine)ThingMaker.MakeThing(ThingDefOf.GravEngine);engine.SetFaction(owner);GenSpawn.Spawn(engine,center,map);
  var console=(Building)ThingMaker.MakeThing(ThingDefOf.PilotConsole);console.SetFaction(owner);GenSpawn.Spawn(console,center+new IntVec3(0,0,3),map);
  var carried=(Building_PassengerShuttle)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PassengerShuttle"));carried.SetFaction(owner);GenSpawn.Spawn(carried,center+new IntVec3(-4,0,0),map);
  engine.ForceSubstructureDirty();
  if(engine.ValidSubstructure.Count<20)throw new Exception("insufficient gravship fixture floor");
  var controller=Find.GravshipController;
  for(int round=0;round<6;round++){
  map=engine.Map;destination=maps.First(m=>m!=map);
  engine.cooldownCompleteTick=map.AsyncTime().mapTicks+6000;var launch=carried.TryGetComp<CompLaunchable>();
  int expected=round<3?3650:(round<5?3750-destination.AsyncTime().mapTicks-100:-100);
  launch.lastLaunchTick=map.AsyncTime().mapTicks+expected-3750;
  map.PushFaction(map.ParentFaction,true);
  var zone=map.zoneManager;
  Gravship gravship;
  try {
   gravship=(Gravship)AccessTools.Method(typeof(WorldComponent_GravshipController),"RemoveGravshipFromMap").Invoke(controller,new object[]{engine});
   if(gravship==null||engine.Spawned)throw new Exception("native gravship removal failed");
   Log.Message("ODYSSEY_LIFECYCLE REMOVE round="+round+" restoredMapFaction="+ReferenceEquals(zone,map.zoneManager)+" restoredGlobalFaction="+(Faction.OfPlayer==map.ParentFaction)+" engine="+engine.thingIDNumber);
   if(!ReferenceEquals(zone,map.zoneManager)||Faction.OfPlayer!=map.ParentFaction)throw new Exception("gravship faction restore failed");
  } finally {map.PopFaction();}
  // Native placement handles rocks/trees; an empty 15x15 square is not a
  // gameplay prerequisite. This fixed cell is inside the isolated 80x80 map.
  var target=destination==maps[0]?center:new IntVec3(20,0,20);
  AccessTools.Method(typeof(WorldComponent_GravshipController),"PlaceGravship").Invoke(controller,new object[]{gravship,target,destination});
  if(engine.Map!=destination)throw new Exception("native gravship placement failed");
  Log.Message("ODYSSEY_LIFECYCLE CLOCK round="+round+" source="+map.AsyncTime().mapTicks+" destination="+destination.AsyncTime().mapTicks+" remaining="+(engine.cooldownCompleteTick-destination.AsyncTime().mapTicks)+" expected=6000");
  if(engine.cooldownCompleteTick-destination.AsyncTime().mapTicks!=6000)throw new Exception("gravship cooldown changed on transfer");
  if(carried.Map!=destination)throw new Exception("carried shuttle absent from gravship");
  int actual=launch.lastLaunchTick+3750-destination.AsyncTime().mapTicks;
  if(expected>0&&actual!=expected)throw new Exception("carried shuttle cooldown changed");
  if(expected<=0&&launch.lastLaunchTick!=-1)throw new Exception("expired carried cooldown revived");
  if(round==3||round==4){
   if(launch.lastLaunchTick!=-100)throw new Exception("negative clock fixture wrong");
   carried.TransporterComp.groupID=Find.UniqueIDsManager.GetNextTransporterGroupID();carried.RefuelableComp.Refuel(400);
   var report=launch.CanLaunch();if(report.Accepted||!report.Reason.Contains("CommandLaunchGroupCooldown".Translate()))throw new Exception("native launch ignored negative valid timestamp: "+report.Reason);
  }
  Log.Message("ODYSSEY_CARRIED CLOCK round="+round+" remaining="+actual+" expected="+expected+" timestamp="+launch.lastLaunchTick);
  Current.Game.Gravship=null;
  }
 }
}
