using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI.Group;
namespace MP_MeowOnlineShop
{
 // MP already persists the job-created DiaNode dialog and synchronizes options.
 // Only the issuing player should open the follow-up targeter; its eventual
 // confirmation is a separate command with a fresh validity check.
 internal static class RavenRequestActions
 {
  private static Type jobType;private static ISyncMethod execute;
  internal static void Apply(Harmony h)
  {
   var provider=AccessTools.TypeByName("RavenRace.Features.Events.ReproductionRequest.FloatMenuOptionProvider_ReproductionRequest");
   var closure=AccessTools.Inner(provider,"<>c__DisplayClass7_0")??throw new MissingMemberException("Raven request targeter closure");
   h.Patch(AccessTools.DeclaredMethod(closure,"<OpenReproductionDialog>b__0"),prefix:new HarmonyMethod(typeof(RavenRequestActions),nameof(Targeter)));
   jobType=AccessTools.TypeByName("RavenRace.Features.Events.ReproductionRequest.LordJob_ReproductionRequest");
   execute=MP.RegisterSyncMethod(typeof(RavenRequestActions),nameof(Execute));
   h.Patch(AccessTools.DeclaredMethod(jobType,"AcceptAndStartQueue"),prefix:new HarmonyMethod(typeof(RavenRequestActions),nameof(Accept)));
   h.Patch(AccessTools.DeclaredMethod(jobType,"RejectRequest"),prefix:new HarmonyMethod(typeof(RavenRequestActions),nameof(Reject)));
  }
  private static bool Targeter()=>!MP.IsInMultiplayer||MP.InInterface||MP.IsExecutingSyncCommandIssuedBySelf;
  private static bool Waiting(LordJob job)=>job!=null&&job.GetType()==jobType&&job.lord!=null&&job.Map!=null&&(bool)AccessTools.Field(jobType,"isWaitingForDialog").GetValue(job);
  private static bool Valid(LordJob job,Pawn pawn)=>Waiting(job)&&pawn!=null&&pawn.Spawned&&!pawn.Dead&&!pawn.Downed&&pawn.IsColonist&&pawn.gender==Gender.Male&&pawn.Map==job.Map;
  private static bool Accept(LordJob __instance,Pawn __0)
  {
   if(!MP.IsInMultiplayer)return true;
   if(!Valid(__instance,__0))return false;
   if(!MP.InInterface)return true;
   execute.DoSync(null,__instance.lord,__0.thingIDNumber,true);return false;
  }
  private static bool Reject(LordJob __instance)
  {
   if(!MP.IsInMultiplayer)return true;
   if(!Waiting(__instance))return false;
   if(!MP.InInterface)return true;
   execute.DoSync(null,__instance.lord,0,false);return false;
  }
  private static void Execute(Lord lord,int pawnId,bool accept)
  {
   var job=lord?.LordJob;if(!Waiting(job))return;
   if(!accept){AccessTools.DeclaredMethod(jobType,"RejectRequest").Invoke(job,null);return;}
   var pawn=lord.Map.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.thingIDNumber==pawnId);
   if(Valid(job,pawn))AccessTools.DeclaredMethod(jobType,"AcceptAndStartQueue").Invoke(job,new object[]{pawn});
  }
 }
}
