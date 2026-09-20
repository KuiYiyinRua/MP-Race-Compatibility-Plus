using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using UnityEngine;
public static class UnloadControlProbe
{
 static ISyncMethod setup,check;static Pawn pawn;static Building_PassengerShuttle shuttle;static Building_GravEngine engine;static Map map;static int round;static bool requested,checking,observed,drafted,inventory,expectedHome,oldLanded;static float deadline;
 public static bool Enabled=>GenCommandLine.CommandLineArgPassed("opunloadcontrol");
 public static void Install(){setup=MP.RegisterSyncMethod(typeof(UnloadControlProbe),nameof(Setup));check=MP.RegisterSyncMethod(typeof(UnloadControlProbe),nameof(Check));new Harmony("meow.odyssey.unload.control").Patch(AccessTools.Method(typeof(ShipJob_Unload),nameof(ShipJob_Unload.UnloadThingFromShuttle)),postfix:new HarmonyMethod(typeof(UnloadControlProbe),nameof(Observe)));}
 public static void Setup(){
  var maps=Find.Maps.OrderBy(m=>m.uniqueID).ToArray();map=round==2?maps[1]:maps[0];var owner=round==0?maps[0].ParentFaction:maps[1].ParentFaction;expectedHome=round==0||round==2||round==4;
  oldLanded=map.wasSpawnedViaGravShipLanding;if(round==5)map.wasSpawnedViaGravShipLanding=true;
  var def=DefDatabase<ThingDef>.GetNamed("PassengerShuttle");var cell=GenRadial.RadialCellsAround(map.Center,50,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(3).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant))&&CellRect.CenteredOn(c+new IntVec3(5,0,0),1).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  if(round==3||round==4){engine=(Building_GravEngine)ThingMaker.MakeThing(ThingDefOf.GravEngine);engine.SetFaction(round==3?map.ParentFaction:owner);GenSpawn.Spawn(engine,cell+new IntVec3(5,0,0),map);}
  shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(def);shuttle.SetFaction(owner);GenSpawn.Spawn(shuttle,cell,map);
  pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,owner,forceGenerateNewPawn:true,mustBeCapableOfViolence:true));GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(cell,map,5),map);var cargo=ThingMaker.MakeThing(ThingDefOf.Steel);cargo.stackCount=7;pawn.inventory.innerContainer.TryAdd(cargo);pawn.DeSpawn();shuttle.TransporterComp.innerContainer.TryAdd(pawn,false);observed=false;
  shuttle.ShuttleComp.shipParent.ForceJob_DelayCurrent(ShipJobMaker.MakeShipJob(ShipJobDefOf.Unload));
 }
 static void Observe(Thing thingToDrop){if(!Enabled||thingToDrop!=pawn||!pawn.Spawned)return;drafted=pawn.Drafted;inventory=pawn.inventory.UnloadEverything;observed=true;Log.Message("ODYSSEY_TRACE UNLOAD_OWNER round="+round+" owner="+pawn.Faction.loadID+" context="+Faction.OfPlayer.loadID+" home="+map.IsPlayerHome+" drafted="+drafted+" inventory="+inventory+" expectedHome="+expectedHome);}
 public static void Check(){if(!observed||drafted==expectedHome||inventory!=expectedHome)throw new Exception("native unload owner behavior round="+round+" drafted="+drafted+" inventory="+inventory+" expectedHome="+expectedHome);Log.Message("ODYSSEY_UNLOAD_CONTROL COMPLETE round="+round+" expectedHome="+expectedHome+" drafted="+drafted+" inventory="+inventory);if(engine!=null){engine.Destroy(DestroyMode.Vanish);engine=null;}map.wasSpawnedViaGravShipLanding=oldLanded;round++;observed=false;requested=checking=false;if(round==6)OdysseyProbe.Complete();}
 public static void Update(){if(!GenCommandLine.CommandLineArgPassed("opclient")||round==6)return;if(!requested){requested=setup.DoSync(null);deadline=Time.realtimeSinceStartup+90;return;}if(Time.realtimeSinceStartup>deadline)throw new Exception("unload control timeout");if(observed&&!checking)checking=check.DoSync(null);}
}
