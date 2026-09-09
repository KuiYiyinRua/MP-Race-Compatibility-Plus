using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 // Capture the native transfer selection, commit once, then
 // display the local operator reply outside command replay.
 internal static class RavenGiftActions
 {
  private sealed class Local {public Map map;public bool pending,done;public string reply,error;}
  private static ConditionalWeakTable<Window,Local> locals=new ConditionalWeakTable<Window,Local>();
  private static readonly Dictionary<int,WeakReference> requests=new Dictionary<int,WeakReference>();
  private static Type dialog,gifting,manager,trader;private static int next;private static ISyncMethod submit;
  internal static void Apply(Harmony h)
  {
   dialog=AccessTools.TypeByName("RavenRace.Features.Operator.UI.Dialog_GiftGiving");gifting=AccessTools.TypeByName("RavenRace.Features.Operator.Gifting.GiftingManager");manager=AccessTools.TypeByName("RavenRace.Features.Operator.WorldComponent_OperatorManager");trader=AccessTools.Inner(dialog,"DummyTrader");
   if(trader==null)throw new MissingMemberException("Raven gift trader");
   submit=MP.RegisterSyncMethod(typeof(RavenGiftActions),nameof(Submit));
   h.Patch(AccessTools.Constructor(dialog,Type.EmptyTypes),postfix:new HarmonyMethod(typeof(RavenGiftActions),nameof(Constructed)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"DoGift"),prefix:new HarmonyMethod(typeof(RavenGiftActions),nameof(BeforeGift)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"DoWindowContents"),prefix:new HarmonyMethod(typeof(RavenGiftActions),nameof(Draw)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"PostClose"),postfix:new HarmonyMethod(typeof(RavenGiftActions),nameof(Closed)));
   h.Patch(AccessTools.DeclaredMethod(typeof(Game),"ExposeSmallComponents"),prefix:new HarmonyMethod(typeof(RavenGiftActions),nameof(BeforeLoad)));
  }
  private static void Constructed(Window __instance){if(MP.IsInMultiplayer)locals.Add(__instance,new Local{map=Find.CurrentMap});}
  private static void Closed(Window __instance){locals.Remove(__instance);}
  private static void BeforeLoad(){if(Scribe.mode==LoadSaveMode.LoadingVars){requests.Clear();locals=new ConditionalWeakTable<Window,Local>();}}
  private static bool Draw(Window __instance)
  {
   if(!MP.InInterface||!locals.TryGetValue(__instance,out var local))return true;
   if(local.error!=null){Messages.Message(local.error,MessageTypeDefOf.RejectInput,false);local.error=null;}
   if(!local.done)return true;
   var previous=Current.Game.CurrentMap;
   try
   {
    Current.Game.CurrentMap=local.map;
    AccessTools.Field(manager,"PostGiftMessage").SetValue(null,local.reply);
    __instance.Close(true);
   }
   finally{Current.Game.CurrentMap=previous;}
   return false;
  }
  private static bool BeforeGift(Window __instance)
  {
   if(!MP.InInterface)return true;
   if(!locals.TryGetValue(__instance,out var local)||local.pending)return false;
   var entries=(List<TransferableOneWay>)AccessTools.Field(dialog,"transferables").GetValue(__instance);
   var ids=new List<int>();var counts=new List<int>();
   foreach(var entry in entries)
   {
    int remaining=entry.CountToTransfer;
    foreach(var thing in entry.things)
    {
     if(remaining<=0)break;if(thing.Destroyed||thing.stackCount<=0)continue;
     int amount=Math.Min(remaining,thing.stackCount);ids.Add(thing.thingIDNumber);counts.Add(amount);remaining-=amount;
    }
   }
   if(ids.Count==0)return true;
   int token=++next;local.pending=true;requests[token]=new WeakReference(__instance);
   submit.DoSync(null,local.map,ids.ToArray(),counts.ToArray(),token);return false;
  }
  private static void Submit(Map map,int[] ids,int[] counts,int token)
  {
   bool success=false;string reply=null;
   var message=AccessTools.Field(manager,"PostGiftMessage");var previous=message.GetValue(null);
   try
   {
    if(map==null||ids==null||counts==null||ids.Length==0||ids.Length!=counts.Length||ids.Distinct().Count()!=ids.Length)return;
    var available=TradeUtility.AllLaunchableThingsForTrade(map,(ITrader)Activator.CreateInstance(trader,true)).ToDictionary(t=>t.thingIDNumber);
    for(int i=0;i<ids.Length;i++)if(counts[i]<=0||!available.TryGetValue(ids[i],out var item)||item.Destroyed||item.stackCount<counts[i])return;
    int total=0;
    for(int i=0;i<ids.Length;i++)
    {
     var item=available[ids[i]];var args=new object[]{item,counts[i],0};
     AccessTools.DeclaredMethod(gifting,"HandleGift").Invoke(null,args);total+=(int)args[2];item.SplitOff(counts[i]).Destroy();
    }
    reply=(string)message.GetValue(null)+"\n\n（好感度变化: "+GenText.ToStringWithSign(total)+"）";success=true;
   }
   finally
   {
    message.SetValue(null,previous);
    if(MP.IsExecutingSyncCommandIssuedBySelf&&requests.TryGetValue(token,out var request))
    {
     requests.Remove(token);
     if(request.Target is Window window&&locals.TryGetValue(window,out var local)){local.pending=false;local.done=success;local.reply=reply;if(!success)local.error="赠送物品已变化，请重新选择。";}
    }
   }
  }
 }
}
