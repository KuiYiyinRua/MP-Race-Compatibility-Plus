using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenSoulAltarActions
 {
  private static MethodInfo scan;
  internal static void Apply(Harmony harmony)
  {
   var type=AccessTools.TypeByName("RavenRace.CompSoulAltar")??throw new TypeLoadException("RavenRace.CompSoulAltar");
   scan=AccessTools.DeclaredMethod(type,"ScanNetwork",Type.EmptyTypes)??throw new MissingMethodException(type.FullName,"ScanNetwork");
   MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type,"TryStartIncubation",Type.EmptyTypes)??throw new MissingMethodException(type.FullName,"TryStartIncubation"));
   harmony.Patch(scan,prefix:new HarmonyMethod(typeof(RavenSoulAltarActions),nameof(CanScan)));
   harmony.Patch(AccessTools.DeclaredMethod(type,"GetSpeedMultiplier",Type.EmptyTypes),prefix:new HarmonyMethod(typeof(RavenSoulAltarActions),nameof(RefreshSpeed)));
  }
  // Window drawing must not advance the simulation's topology cache.
  private static bool CanScan()=>!MP.IsInMultiplayer||!MP.InInterface;
  // The native caches are not saved. Rebuild at consumption so cold loading or
  // opening a dialog cannot change the incubation rate on only one peer.
  private static void RefreshSpeed(ThingComp __instance)
  {
   if(MP.IsInMultiplayer&&!MP.InInterface&&__instance.parent.Spawned)scan.Invoke(__instance,null);
  }
 }
}
