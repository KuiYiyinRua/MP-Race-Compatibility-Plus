using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
public static class UIRefuelProbe
{
 static Building_PassengerShuttle shuttle;static ISyncMethod setup,check;
 static bool requested,ready,clicked,checkedState,done;static int round,total;
 static float before;
 public static void Install(){setup=MP.RegisterSyncMethod(typeof(UIRefuelProbe),nameof(Setup));check=MP.RegisterSyncMethod(typeof(UIRefuelProbe),nameof(Check));}
 static void Require(bool ok,string why){if(!ok)throw new Exception("refuel: "+why);}
 public static void Setup(){
  var map=Find.Maps.OrderBy(m=>m.uniqueID).Last();var owner=Find.Maps.OrderBy(m=>m.uniqueID).First().ParentFaction;
  var def=DefDatabase<ThingDef>.GetNamed("PassengerShuttle");
  var cell=GenRadial.RadialCellsAround(map.Center,30,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(1).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(def);shuttle.SetFaction(owner);GenSpawn.Spawn(shuttle,cell,map);
  shuttle.RefuelableComp.ConsumeFuel(shuttle.FuelLevel);
  var fuel=ThingMaker.MakeThing(ThingDefOf.Chemfuel);fuel.stackCount=100;Require(shuttle.TransporterComp.innerContainer.TryAdd(fuel,false),"cargo seed");
  ready=true;
 }
 static Dialog_Slider Open(){
  Find.Selector.ClearSelection();Find.Selector.Select(shuttle);Current.Game.CurrentMap=shuttle.Map;
  string label="CommandRefuelShuttleFromCargo".Translate();
  var action=shuttle.GetGizmos().OfType<Command_Action>().Single(g=>g.defaultLabel==label);
  Require(!action.Disabled,"native gizmo disabled");action.action();
  var slider=Find.WindowStack.WindowOfType<Dialog_Slider>();Require(slider!=null,"slider absent");return slider;
 }
 public static void Check(){
  total+=7+round;
  Require(shuttle.FuelLevel==total,"fuel outcome");
  int cargo=shuttle.TransporterComp.innerContainer.Where(t=>t.def==ThingDefOf.Chemfuel).Sum(t=>t.stackCount);
  Require(cargo==100-total,"cargo conservation");
  Log.Message("ODYSSEY_REFUEL COMPLETE round="+round+" fuel="+shuttle.FuelLevel+" cargo="+cargo+" shuttle="+shuttle.thingIDNumber);
  round++;clicked=checkedState=false;if(round==3)done=true;
 }
 public static bool Update(){
  if(done)return false;
  if(!GenCommandLine.CommandLineArgPassed("opclient"))return true;
  if(!requested){requested=setup.DoSync(null);return true;}if(!ready)return true;
  if(!clicked){
   Require(MP.InInterface,"callback outside interface");before=shuttle.FuelLevel;
   Open().Close(false);var slider=Open();
   var callback=(Action<int>)AccessTools.Field(typeof(Dialog_Slider),"confirmAction").GetValue(slider);
   slider.Close(false);callback(7+round);
   Require(shuttle.FuelLevel==before,"unsynced immediate fuel mutation");
   Current.Game.CurrentMap=Find.Maps.FirstOrDefault(m=>m!=shuttle.Map)??shuttle.Map;clicked=true;return true;
  }
  if(!checkedState&&shuttle.FuelLevel==before+7+round)checkedState=check.DoSync(null);
  return true;
 }
}
