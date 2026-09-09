using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenConfessionActions
 {
  private static Type boothType;private static ISyncMethod execute;
  internal static void Apply(Harmony h)
  {
   boothType=AccessTools.TypeByName("RavenRace.Features.MiscSmallFeatures.ConfessionBooth.Building_ConfessionBooth");
   execute=MP.RegisterSyncMethod(typeof(RavenConfessionActions),nameof(Execute));
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Features.MiscSmallFeatures.ConfessionBooth.CompAssignableToPawn_Nun"),"CanAssignTo"),prefix:new HarmonyMethod(typeof(RavenConfessionActions),nameof(AdultAssignment)));
   h.Patch(AccessTools.DeclaredMethod(boothType,"TryStartConfession"),prefix:new HarmonyMethod(typeof(RavenConfessionActions),nameof(Start)));
   h.Patch(AccessTools.DeclaredMethod(boothType,"CanAcceptPawn"),prefix:new HarmonyMethod(typeof(RavenConfessionActions),nameof(AdultAssignment)));
   h.Patch(AccessTools.DeclaredMethod(boothType,"TryAcceptPawn"),prefix:new HarmonyMethod(typeof(RavenConfessionActions),nameof(AdultEntry)));
   h.Patch(AccessTools.DeclaredMethod(boothType,"EjectAll"),prefix:new HarmonyMethod(typeof(RavenConfessionActions),nameof(Eject)));
  }
  private static bool AdultAssignment(Pawn __0,ref AcceptanceReport __result){if(!MP.IsInMultiplayer||(__0!=null&&__0.ageTracker.AgeBiologicalYears>=18))return true;__result="仅限成年角色（18岁及以上）。";return false;}
  private static bool AdultEntry(Pawn __0)=>!MP.IsInMultiplayer||(__0!=null&&__0.ageTracker.AgeBiologicalYears>=18);
  private static bool Available(Pawn pawn,Map map)=>pawn!=null&&pawn.ageTracker.AgeBiologicalYears>=18&&pawn.Spawned&&pawn.Map==map&&!pawn.Dead&&!pawn.Downed&&!pawn.InMentalState&&!pawn.Drafted;
  private static bool Valid(ThingWithComps booth,Pawn believer,Pawn nun)
  {
   if(booth?.Map==null||booth.GetType()!=boothType||believer==nun||!Available(believer,booth.Map)||!Available(nun,booth.Map))return false;
   if(!believer.IsColonist&&!believer.IsPrisonerOfColony&&!believer.IsSlaveOfColony)return false;
   var assigned=booth.AllComps.OfType<CompAssignableToPawn>().FirstOrDefault(c=>c.GetType().FullName=="RavenRace.Features.MiscSmallFeatures.ConfessionBooth.CompAssignableToPawn_Nun");
   return assigned?.AssignedPawnsForReading.FirstOrDefault()==nun&&((IThingHolder)booth).GetDirectlyHeldThings().Count==0;
  }
  private static bool Start(ThingWithComps __instance,Pawn __0,Pawn __1)
  {
   if(!MP.IsInMultiplayer)return true;if(!Valid(__instance,__0,__1))return false;if(!MP.InInterface)return true;
   execute.DoSync(null,__instance.Map,__instance.thingIDNumber,__0.thingIDNumber,__1.thingIDNumber,null);return false;
  }
  private static bool Eject(ThingWithComps __instance,string __0)
  {
   if(!MP.IsInMultiplayer||!MP.InInterface)return true;if(__instance.Map!=null)execute.DoSync(null,__instance.Map,__instance.thingIDNumber,0,0,__0??string.Empty);return false;
  }
  private static void Execute(Map map,int buildingId,int believerId,int nunId,string reason)
  {
   var booth=map?.listerThings.AllThings.Find(t=>t.thingIDNumber==buildingId) as ThingWithComps;if(booth==null||booth.GetType()!=boothType)return;
   if(reason!=null){AccessTools.DeclaredMethod(boothType,"EjectAll").Invoke(booth,new object[]{reason});return;}
   var believer=map.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.thingIDNumber==believerId);var nun=map.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.thingIDNumber==nunId);
   if(Valid(booth,believer,nun))AccessTools.DeclaredMethod(boothType,"TryStartConfession").Invoke(booth,new object[]{believer,nun});
  }
 }
}
