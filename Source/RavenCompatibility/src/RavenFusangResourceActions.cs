using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
namespace MP_MeowOnlineShop
{
 // Candidate: donation and delivery runtime coverage tracked separately.
 internal static class RavenFusangResourceActions
 {
  private static Type support,ember,resourceType,resources;private static MethodInfo drawButton;
  internal static void Apply(Harmony harmony)
  {
   support=AccessTools.TypeByName("RavenRace.Dialog_FusangSupport");ember=AccessTools.TypeByName("RavenRace.Dialog_Mission_EmberSacrifice");resourceType=AccessTools.TypeByName("RavenRace.FusangResourceType");resources=AccessTools.TypeByName("RavenRace.FusangResourceManager");
   drawButton=AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.FusangUIStyle"),"DrawButton",new[]{typeof(Rect),typeof(string),typeof(bool)});
   MP.RegisterSyncMethod(typeof(RavenFusangResourceActions),nameof(Donate));MP.RegisterSyncMethod(typeof(RavenFusangResourceActions),nameof(Deliver));
   harmony.Patch(AccessTools.DeclaredMethod(support,"TryDonateSilver"),prefix:new HarmonyMethod(typeof(RavenFusangResourceActions),nameof(BeforeDonate)));
   harmony.Patch(AccessTools.DeclaredMethod(ember,"DoWindowContents"),transpiler:new HarmonyMethod(typeof(RavenFusangResourceActions),nameof(DeliveryButton)));
  }
  private static bool BeforeDonate(Window __instance,int __0,int __1)
  {
   if(!MP.InInterface)return true;
   var radio=(Thing)AccessTools.Field(support,"radio").GetValue(__instance);if(radio?.Map==null||Find.CurrentMap==null)return false;
   // The native donation UI quotes silver from the currently viewed map.
   // The radio may be on another map, so carry only its stable integer IDs.
   Donate(Find.CurrentMap,radio.Map.uniqueID,radio.thingIDNumber,__0,__1,Convert.ToInt32(AccessTools.Field(support,"selectedResource").GetValue(__instance)));return false;
  }
  private static void Donate(Map map,int radioMapId,int radioId,int cost,int reward,int resource)
  {
   if(map==null||resource<0||resource>3||!((cost==100&&reward==5)||(cost==500&&reward==30)||(cost==2000&&reward==150)))return;
   var radio=Find.Maps.Find(m=>m.uniqueID==radioMapId)?.listerThings.AllThings.Find(t=>t.thingIDNumber==radioId);
   if(radio?.def.defName!="Raven_FusangRadio")return;
   var window=Activator.CreateInstance(support,new object[]{radio});AccessTools.Field(support,"selectedResource").SetValue(window,Enum.ToObject(resourceType,resource));
   AccessTools.DeclaredMethod(support,"TryDonateSilver").Invoke(window,new object[]{cost,reward});
  }
  private static IEnumerable<CodeInstruction> DeliveryButton(IEnumerable<CodeInstruction> instructions)
  {
   int count=0;foreach(var instruction in instructions)
   {
    if(instruction.Calls(drawButton)&&++count==1){yield return new CodeInstruction(OpCodes.Ldarg_0);instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(RavenFusangResourceActions),nameof(BuyButton));}
    yield return instruction;
   }
   if(count!=2)throw new InvalidOperationException("Ember delivery button targets="+count);
  }
  private static bool BuyButton(Rect rect,string label,bool active,Window window)
  {
   bool clicked=(bool)drawButton.Invoke(null,new object[]{rect,label,active});if(!clicked||!MP.InInterface)return clicked;
   if(!Find.WindowStack.Windows.Contains(window))return false;
   var radio=(Thing)AccessTools.Field(ember,"radio").GetValue(window);if(radio?.Map!=null)Deliver(radio);window.Close(true);return false;
  }
  private static object Resource(string method,string name,int amount=0)
  {
   var value=Enum.Parse(resourceType,name);return AccessTools.DeclaredMethod(resources,method).Invoke(null,method=="GetAmount"?new[]{value}:new object[]{value,amount});
  }
  private static void Deliver(Thing radio)
  {
   if(radio?.Map==null||radio.def.defName!="Raven_FusangRadio"||(MP.IsInMultiplayer&&radio.Map!=Find.CurrentMap))return;
   // The original UI short-circuits two withdrawals; preflight both balances so
   // a stale second balance cannot consume Intel without delivering the item.
   if((int)Resource("GetAmount","Intel")<30||(int)Resource("GetAmount","Influence")<50)
   {Messages.Message((TaggedString)AccessTools.Method(AccessTools.TypeByName("RavenRace.RavenText"),"Key",new[]{typeof(string)}).Invoke(null,new object[]{"RavenRace_Fusang_Dialog_Mission_EmberSacrifice_Message"}),MessageTypeDefOf.RejectInput,true);return;}
   Resource("TryConsume","Intel",30);Resource("TryConsume","Influence",50);
   AccessTools.DeclaredMethod(ember,"ExecuteDelivery").Invoke(Activator.CreateInstance(ember,new object[]{radio}),null);
  }
 }
}
