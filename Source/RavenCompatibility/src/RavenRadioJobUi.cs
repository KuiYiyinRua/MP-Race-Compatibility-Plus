using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;
using Runtime = Multiplayer.Client.Multiplayer;
namespace MP_MeowOnlineShop
{
 internal static class RavenRadioJobUi
 {
  private static readonly ConditionalWeakTable<object,HashSet<long>> requests=new ConditionalWeakTable<object,HashSet<long>>();
  internal static void Apply(Harmony harmony)
  {
   // MP's normal-priority prefix replaces interface job IDs before this prefix.
   harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.TryTakeOrderedJob)),prefix:new HarmonyMethod(typeof(RavenRadioJobUi),nameof(Ordered)){priority=Priority.Last});
   var driver=AccessTools.TypeByName("RavenRace.JobDriver_UseFusangRadio")??throw new TypeLoadException("Raven radio job");
   harmony.Patch(AccessTools.DeclaredMethod(driver,"<MakeNewToils>b__1_0",Type.EmptyTypes)??throw new MissingMethodException(driver.FullName,"radio window toil"),prefix:new HarmonyMethod(typeof(RavenRadioJobUi),nameof(OpenForIssuer)));
  }
  private static long Key(Pawn pawn,Job job)=>((long)pawn.thingIDNumber<<32)|(uint)job.loadID;
  private static void Ordered(Pawn ___pawn,Job __0)
  {
   if(!MP.IsExecutingSyncCommand||__0?.def.defName!="Raven_Job_UseFusangRadio")return;
   if(__0.loadID<0)throw new InvalidOperationException("Raven radio ordered job has no shared ID");
   if(MP.IsExecutingSyncCommandIssuedBySelf)requests.GetOrCreateValue(Runtime.session).Add(Key(___pawn,__0));
  }
  // Session-local IDs survive MP's shared save/reload without making UI ownership
  // part of the save. A newly connected process has no prior local UI request.
  private static bool OpenForIssuer(Pawn ___pawn,Job ___job)
  {
   if(!MP.IsInMultiplayer)return true;
   return Runtime.session!=null&&requests.TryGetValue(Runtime.session,out var pending)&&pending.Remove(Key(___pawn,___job));
  }
 }
}
