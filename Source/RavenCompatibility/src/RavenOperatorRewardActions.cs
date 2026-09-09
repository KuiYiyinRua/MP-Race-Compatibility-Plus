using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace MP_MeowOnlineShop
{
 // The original claim method only reads its Def argument and
 // world/map state; construct an unshown native dialog to preserve its effects.
 internal static class RavenOperatorRewardActions
 {
  private static Type dialog,manager;private static ISyncMethod execute;
  internal static void Apply(Harmony h)
  {
   dialog=AccessTools.TypeByName("RavenRace.Features.Operator.UI.Dialog_RequestReward");manager=AccessTools.TypeByName("RavenRace.Features.Operator.WorldComponent_OperatorManager");
   execute=MP.RegisterSyncMethod(typeof(RavenOperatorRewardActions),nameof(Execute));
   h.Patch(AccessTools.DeclaredMethod(dialog,"ClaimReward"),prefix:new HarmonyMethod(typeof(RavenOperatorRewardActions),nameof(BeforeClaim)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"DoWindowContents"),transpiler:new HarmonyMethod(typeof(RavenOperatorRewardActions),nameof(BalanceListing)));
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Features.FusangOrganization.UI.FusangComm_UIPanels"),"DrawStatusAndGalgamePanel"),transpiler:new HarmonyMethod(typeof(RavenOperatorRewardActions),nameof(LocalReject)));
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Dialog_FusangMissionBoard"),"CheckSurrogateProgress"),transpiler:new HarmonyMethod(typeof(RavenOperatorRewardActions),nameof(LocalReject)));
  }
  private static IEnumerable<CodeInstruction> BalanceListing(IEnumerable<CodeInstruction> instructions)
  {
   var code=instructions.ToList();var end=AccessTools.Method(typeof(Listing),nameof(Listing.End));
   int endIndex=code.FindIndex(i=>i.Calls(end));
   if(endIndex<1||!code[endIndex-1].IsLdloc())throw new InvalidOperationException("Raven reward listing end target");
   var claim=AccessTools.DeclaredMethod(dialog,"ClaimReward");int count=0;
   foreach(var instruction in code)
   {
    if(instruction.Calls(claim))
    {
     yield return new CodeInstruction(code[endIndex-1].opcode,code[endIndex-1].operand);
     yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(RavenOperatorRewardActions),nameof(EndOnClaim)));count++;
    }
    yield return instruction;
   }
   if(count!=1)throw new InvalidOperationException("Raven reward claim branch target="+count);
  }
  private static void EndOnClaim(Listing listing){if(MP.IsInMultiplayer)listing.End();}
  private static IEnumerable<CodeInstruction> LocalReject(IEnumerable<CodeInstruction> instructions)
  {
   var message=AccessTools.Method(typeof(Messages),nameof(Messages.Message),new[]{typeof(string),typeof(MessageTypeDef),typeof(bool)});int count=0;
   foreach(var instruction in instructions){if(instruction.Calls(message)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(RavenOperatorRewardActions),nameof(Report));count++;}yield return instruction;}
   if(count!=1)throw new InvalidOperationException("Raven local empty-list message target="+count);
  }
  private static void Report(string text,MessageTypeDef type,bool historical)=>Messages.Message(text,type,historical&&!MP.IsInMultiplayer);
  private static bool BeforeClaim(Def __0)
  {
   if(!MP.InInterface)return true;
   if(Find.CurrentMap!=null&&__0!=null)execute.DoSync(null,Find.CurrentMap,__0);
   return false;
  }
  private static void Execute(Map map,Def reward)
  {
   if(map==null||reward==null||reward.GetType().FullName!="RavenRace.Features.Operator.Rewards.RewardDef")return;
   var component=Find.World.components.Find(c=>c.GetType()==manager);
   if(component==null)return;
   var unlocked=(HashSet<string>)AccessTools.Field(manager,"unlockedRewardDefs").GetValue(component);
   if(!unlocked.Contains(reward.defName))return;
   var rewards=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(reward.GetType()));rewards.Add(reward);
   var context=Activator.CreateInstance(dialog,new object[]{rewards});
   AccessTools.DeclaredMethod(dialog,"ClaimReward").Invoke(context,new object[]{reward});
  }
 }
}
