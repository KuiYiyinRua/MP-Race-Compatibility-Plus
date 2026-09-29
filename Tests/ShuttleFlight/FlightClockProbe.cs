using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Multiplayer.Client;
public static class FlightClockProbe {
  static HashSet<int> logged=new HashSet<int>();
 static bool requested; static int loaded;
 public static void ExposeReferences(){if(!GenCommandLine.CommandLineArgPassed("opflightsnapshot"))return;Scribe_References.Look(ref FlightProbe.shuttle,"flightProbeShuttle");Scribe_References.Look(ref FlightProbe.pilot,"flightProbePilot");Scribe_References.Look(ref FlightProbe.target,"flightProbeTarget");}
 public static void Update(){if(requested||!GenCommandLine.CommandLineArgPassed("opflightsnapshot")||GenCommandLine.CommandLineArgPassed("opclient"))return;foreach(var ship in Find.WorldObjects.TravellingTransporters){if(ship.def.defName!="PassengerShuttle")continue;float p=(float)AccessTools.Field(typeof(TravellingTransporters),"traveledPct").GetValue(ship);if(p>0.15f&&p<0.6f){requested=true;Log.Message("FLIGHT_SNAPSHOT REQUEST progress="+p);SnapshotProbe.RequestJoinPoint();return;}}}
 static void Saved(TravellingTransporters __instance){if(!GenCommandLine.CommandLineArgPassed("opflightsnapshot")||Scribe.mode!=LoadSaveMode.PostLoadInit||__instance.def.defName!="PassengerShuttle")return;var type=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_PassengerShuttleFlightClock");var state=AccessTools.Method(type,"State").Invoke(null,new object[]{__instance});int duration=(int)AccessTools.Field(state.GetType(),"Duration").GetValue(state);int elapsed=(int)AccessTools.Field(state.GetType(),"Elapsed").GetValue(state);if(duration<=0||elapsed<=0||elapsed>=duration)throw new Exception("flight snapshot lost in-flight plan");loaded++;Log.Message("FLIGHT_SNAPSHOT LOADED id="+__instance.ID+" duration="+duration+" elapsed="+elapsed);}
 public static void Install(){if(!GenCommandLine.CommandLineArgPassed("opflightguard"))return;var h=new Harmony("meow.flightclock.fault");h.Patch(AccessTools.Method(typeof(TravellingTransporters),"ExposeData"),postfix:new HarmonyMethod(typeof(FlightClockProbe),nameof(Saved)){priority=Priority.Last});h.Patch(AccessTools.PropertyGetter(typeof(TravellingTransporters),"TraveledPctStepPerTick"),postfix:new HarmonyMethod(typeof(FlightClockProbe),nameof(Rate)));h.Patch(AccessTools.Method(typeof(TravellingTransporters),"Arrived"),prefix:new HarmonyMethod(typeof(FlightClockProbe),nameof(Arrived)));}
 static void Rate(TravellingTransporters __instance,ref float __result){if(__instance.def.defName!="PassengerShuttle")return;bool client=GenCommandLine.CommandLineArgPassed("opclient");if(GenCommandLine.CommandLineArgPassed("opflightsnapshot"))__result*=0.02f;if(client)__result*=0.5f;if(logged.Add(__instance.ID))Log.Message("FLIGHT_FAULT rate="+__result.ToString("R")+" world="+Multiplayer.Client.Multiplayer.AsyncWorldTime.worldTicks+" id="+__instance.ID+" client="+client);}
 static void Arrived(TravellingTransporters __instance){if(__instance.def.defName=="PassengerShuttle")Log.Message("FLIGHT_ARRIVED id="+__instance.ID+" world="+Multiplayer.Client.Multiplayer.AsyncWorldTime.worldTicks+" shared="+TickPatch.Timer);}
}
