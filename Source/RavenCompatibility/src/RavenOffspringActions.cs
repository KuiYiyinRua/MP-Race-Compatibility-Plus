using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Patches;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenOffspringActions
 {
  private static Type dialog;
  private static ISyncMethod execute;
  private static int next;
  private sealed class Pending { public WeakReference window; public bool done,success; }
  private static readonly Dictionary<int,Pending> pending=new Dictionary<int,Pending>();
  internal static void Apply(Harmony h)
  {
   dialog=AccessTools.TypeByName("RavenRace.Features.FusangOrganization.UI.Dialog_FusangOffspringProtocol");
   execute=MP.RegisterSyncMethod(typeof(RavenOffspringActions),nameof(Execute));
   foreach(var name in new[]{"FinalizeEggDelivery","FinalizePawnDelivery"})
    h.Patch(AccessTools.DeclaredMethod(dialog,name),prefix:new HarmonyMethod(typeof(RavenOffspringActions),nameof(BeforeDelivery)),transpiler:new HarmonyMethod(typeof(RavenOffspringActions),nameof(LocalClose)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"DoWindowContents"),prefix:new HarmonyMethod(typeof(RavenOffspringActions),nameof(Draw)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"RejectInvalid"),prefix:new HarmonyMethod(typeof(RavenOffspringActions),nameof(Reject)));
   h.Patch(AccessTools.DeclaredMethod(typeof(Game),"ExposeSmallComponents"),prefix:new HarmonyMethod(typeof(RavenOffspringActions),nameof(Load)));
  }
  private static void Load(){if(Scribe.mode==LoadSaveMode.LoadingVars)pending.Clear();}
  private static bool Reject()
  {
   if(!MP.InInterface)return true;
   Messages.Message("RavenRace_Fusang_OffspringProtocol_Invalid".Translate(),MessageTypeDefOf.RejectInput,false);return false;
  }
  private static bool BeforeDelivery(Window __instance,MethodBase __originalMethod)
  {
   if(!MP.InInterface)return true;
   foreach(var request in pending.Values)if(request.window.Target==__instance)return false;
   var radio=(Thing)AccessTools.Field(dialog,"radio").GetValue(__instance);
   bool pawn=__originalMethod.Name=="FinalizePawnDelivery";
   var selected=(Thing)AccessTools.Field(dialog,pawn?"selectedPawn":"selectedEgg").GetValue(__instance);
   if(radio?.Map==null||selected?.Map==null||selected.Destroyed){Reject();return false;}
   int token=++next;pending[token]=new Pending{window=new WeakReference(__instance)};
   // MP permits one command map. Cross-map colonist selection executes on the
   // colonist's map; the radio is resolved by IDs without changing that context.
   execute.DoSync(null,selected.Map,radio.Map.uniqueID,radio.thingIDNumber,selected.thingIDNumber,pawn,token);return false;
  }
  private static void Execute(Map targetMap,int radioMapId,int radioId,int targetId,bool pawn,int token)
  {
   bool success=false;
   try
   {
    var radioMap=Find.Maps.Find(m=>m.uniqueID==radioMapId);
    var radio=radioMap?.listerThings.AllThings.Find(t=>t.thingIDNumber==radioId);
    var selected=targetMap?.listerThings.AllThings.Find(t=>t.thingIDNumber==targetId);
    if(radio==null||selected==null||radio.def.defName!="Raven_FusangRadio")return;
    var context=(Window)Activator.CreateInstance(dialog,new object[]{radio});
    if(pawn&&(!(selected is Pawn p)||!(bool)AccessTools.DeclaredMethod(dialog,"CanOfferColonist").Invoke(null,new object[]{p})))return;
    AccessTools.Field(dialog,pawn?"selectedPawn":"selectedEgg").SetValue(context,selected);
    if(!pawn&&!((List<Thing>)AccessTools.DeclaredMethod(dialog,"GetEggs").Invoke(context,null)).Contains(selected))return;
    AccessTools.DeclaredMethod(dialog,pawn?"FinalizePawnDelivery":"FinalizeEggDelivery").Invoke(context,null);
    success=pawn?!selected.Spawned&&selected.ParentHolder?.GetType().FullName=="RavenRace.WorldComponent_Fusang":selected.Destroyed;
    // The native custom ThingOwner never calls WorldPawns.AddPawn.
    if(success&&pawn)TimestampFixer.FixPawn((Pawn)selected,targetMap,null);
   }
   finally
   {
    if(MP.IsExecutingSyncCommandIssuedBySelf&&pending.TryGetValue(token,out var request))
    {
     if(!(request.window.Target is Window window)||!Find.WindowStack.Windows.Contains(window))pending.Remove(token);
     else{request.done=true;request.success=success;}
    }
   }
  }
  private static bool Draw(Window __instance)
  {
   if(!MP.InInterface)return true;
   int completed=0;Pending found=null;
   foreach(var pair in pending)if(pair.Value.window.Target==__instance&&pair.Value.done){completed=pair.Key;found=pair.Value;break;}
   if(found==null)return true;pending.Remove(completed);
   if(found.success){__instance.Close(true);return false;}Reject();return true;
  }
  private static IEnumerable<CodeInstruction> LocalClose(IEnumerable<CodeInstruction> instructions)
  {
   var close=AccessTools.Method(typeof(Window),nameof(Window.Close),new[]{typeof(bool)});int count=0;
   foreach(var instruction in instructions){if(instruction.Calls(close)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(RavenOffspringActions),nameof(Close));count++;}yield return instruction;}
   if(count!=1)throw new InvalidOperationException("Raven offspring close target="+count);
  }
  private static void Close(Window window,bool sound){if(!MP.IsInMultiplayer)window.Close(sound);}
 }
}
