using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
 internal static class RavenDroneOrder
 {
  private static FieldInfo stations;
  private static MethodInfo fill;
  internal static void Apply(Harmony h)
  {
   var network=AccessTools.TypeByName("RavenRace.Features.Drone.Buildings.MapComponent_DroneStationNetwork");
   stations=AccessTools.DeclaredField(network,"stations")??throw new MissingFieldException("Drone stations");
   fill=AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Features.Drone.SpatialGrid.MapComponent_DroneSpatialGrid"),"FillObjectsInRadius",new[]{typeof(IntVec3),typeof(int),typeof(List<object>)});
   if(fill==null)throw new MissingMethodException("Drone spatial radius query");
   h.Patch(AccessTools.DeclaredMethod(network,"RebuildNetworks"),prefix:new HarmonyMethod(typeof(RavenDroneOrder),nameof(OrderStations)));
   h.Patch(AccessTools.DeclaredMethod(network,"EnqueueLinkedStations"),transpiler:new HarmonyMethod(typeof(RavenDroneOrder),nameof(OrderNeighbours)));
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Features.Drone.Medical.MapComponent_RavenMedicalDroneManager"),"TryFindBestBedOfKind"),transpiler:new HarmonyMethod(typeof(RavenDroneOrder),nameof(OrderBeds)));
  }
  private static void OrderStations(object __instance)
  {
   if(!MP.IsInMultiplayer)return;
   var list=(IList)stations.GetValue(__instance);
   var ordered=list.Cast<Thing>().OrderBy(t=>t?.thingIDNumber??int.MaxValue).ToArray();
   for(int i=0;i<ordered.Length;i++)list[i]=ordered[i];
  }
  private static void FillOrdered(object grid,IntVec3 center,int radius,List<object> results)
  {
   fill.Invoke(grid,new object[]{center,radius,results});
   // This wrapper is used only by the station graph builder. Non-station
   // entries are ignored by its native loop; preserve their relative order.
   if(MP.IsInMultiplayer&&results!=null)
   {
    var ordered=results.OrderBy(t=>(t as Thing)?.thingIDNumber??int.MaxValue).ToArray();
    results.Clear();results.AddRange(ordered);
   }
  }
  private static IEnumerable<CodeInstruction> OrderNeighbours(IEnumerable<CodeInstruction> code)
  {
   int count=0;
   foreach(var ins in code)
   {
    if(ins.Calls(fill)){ins.opcode=OpCodes.Call;ins.operand=AccessTools.Method(typeof(RavenDroneOrder),nameof(FillOrdered));count++;}
    yield return ins;
   }
   if(count!=1)throw new InvalidOperationException("Drone neighbour query count "+count);
  }
  private static List<Building> SortedBeds(List<Building> list)=>MP.IsInMultiplayer?list.OrderBy(b=>b.thingIDNumber).ToList():list;
  private static IEnumerable<CodeInstruction> OrderBeds(IEnumerable<CodeInstruction> code)
  {
   var field=AccessTools.Field(typeof(ListerBuildings),"allBuildingsColonist");int count=0;
   foreach(var ins in code)
   {
    yield return ins;
    if(ins.opcode==OpCodes.Ldfld&&Equals(ins.operand,field)){yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(RavenDroneOrder),nameof(SortedBeds)));count++;}
   }
   if(count!=1)throw new InvalidOperationException("Medical drone bed lookup count "+count);
  }
 }
}
