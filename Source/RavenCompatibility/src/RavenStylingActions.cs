using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenStylingActions
 {
  private sealed class Local { public bool hide, pending; public Dictionary<string,bool> parts; public int result; }
  private sealed class Scope { public object comp; public bool hide; public Dictionary<string,bool> parts; }
  private static ConditionalWeakTable<Window, Local> locals = new ConditionalWeakTable<Window, Local>();
  private static readonly Dictionary<int, WeakReference> requests = new Dictionary<int, WeakReference>();
  private static Type dialog, compType, hairUtility, selectionType;
  private static FieldInfo hideField, partsField;
  private static MethodInfo refresh;
  private static int next;
  private static ISyncMethod submit;
  private static object Read(object o, string n) => AccessTools.Field(o.GetType(), n).GetValue(o);
  private static void Write(object o, string n, object v) => AccessTools.Field(o.GetType(), n).SetValue(o,v);
  private static object Call(string n, params object[] args) => AccessTools.DeclaredMethod(hairUtility,n).Invoke(null,args);
  private static Scope Capture(Pawn pawn)
  {
   var comp = pawn.AllComps.Single(c => c.GetType() == compType);
   return new Scope { comp=comp, hide=(bool)hideField.GetValue(comp), parts=(Dictionary<string,bool>)partsField.GetValue(comp) };
  }
  private static void Restore(Scope s) { if(s != null) {hideField.SetValue(s.comp,s.hide); partsField.SetValue(s.comp,s.parts);} }
  internal static void Apply(Harmony h)
  {
   dialog=AccessTools.TypeByName("RavenRace.Features.ClStyling.Dialog_RavenClStylingStation");
   compType=AccessTools.TypeByName("RavenRace.Race.CompRavenRace");
   hairUtility=AccessTools.TypeByName("ChezhouLib.ClHair.ClHairStyleUtility");
   selectionType=AccessTools.TypeByName("ChezhouLib.ClHair.ClAppearanceSelection");
   hideField=AccessTools.Field(compType,"hideEarFeathers"); partsField=AccessTools.Field(compType,"partToggles");
   if(dialog==null || selectionType==null || hideField==null || partsField==null) throw new MissingMemberException("Raven styling targets");
   submit=MP.RegisterSyncMethod(typeof(RavenStylingActions),nameof(Submit));
   refresh=AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Features.ClStyling.RavenClStylingUtility"),"RefreshBloodlinePartState"); MP.RegisterSyncMethod(refresh);
   h.Patch(AccessTools.Constructor(dialog,new[]{typeof(Pawn)}),prefix:new HarmonyMethod(typeof(RavenStylingActions),nameof(BeforeCtor)),finalizer:new HarmonyMethod(typeof(RavenStylingActions),nameof(AfterCtor)),transpiler:new HarmonyMethod(typeof(RavenStylingActions),nameof(ConstructorRefresh)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"DoWindowContents"),prefix:new HarmonyMethod(typeof(RavenStylingActions),nameof(BeforeDraw)),finalizer:new HarmonyMethod(typeof(RavenStylingActions),nameof(AfterDraw)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"PostClose"),prefix:new HarmonyMethod(typeof(RavenStylingActions),nameof(BeforeClose)),finalizer:new HarmonyMethod(typeof(RavenStylingActions),nameof(AfterClose)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"Accept"),prefix:new HarmonyMethod(typeof(RavenStylingActions),nameof(BeforeAccept)));
   h.Patch(AccessTools.DeclaredMethod(typeof(Game),"ExposeSmallComponents"),prefix:new HarmonyMethod(typeof(RavenStylingActions),nameof(BeforeLoad)));
  }
  private static void BeforeLoad() { if(Scribe.mode==LoadSaveMode.LoadingVars){requests.Clear(); locals=new ConditionalWeakTable<Window,Local>();} }
  private static IEnumerable<CodeInstruction> ConstructorRefresh(IEnumerable<CodeInstruction> instructions)
  {
   int count=0;
   foreach(var instruction in instructions)
   {
    if(instruction.Calls(refresh)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(RavenStylingActions),nameof(SinglePlayerRefresh));count++;}
    yield return instruction;
   }
   if(count!=1)throw new InvalidOperationException("Styling constructor refresh call count="+count);
  }
  private static void SinglePlayerRefresh(Pawn pawn){if(!MP.IsInMultiplayer)refresh.Invoke(null,new object[]{pawn});}
  private static void BeforeCtor(Pawn __0, out Scope __state)
  {
   __state=null;if(!MP.IsInMultiplayer)return;
   // Commit the actual bloodline refresh before entering the local preview scope.
   // In interface context the registered executor queues it for every peer.
   refresh.Invoke(null,new object[]{__0});__state=Capture(__0);
   partsField.SetValue(__state.comp,new Dictionary<string,bool>(__state.parts));
  }
  private static void AfterCtor(Window __instance, Scope __state)
  {
   if(__state==null) return;
   try { var p=Capture((Pawn)Read(__instance,"pawn")); locals.Add(__instance,new Local{hide=p.hide,parts=new Dictionary<string,bool>(p.parts)}); }
   finally {Restore(__state);}
  }
  private static bool BeforeDraw(Window __instance, out Scope __state)
  {
   __state=null;
   if(!MP.InInterface || !locals.TryGetValue(__instance,out var local)) return true;
   if(local.result==1){__instance.Close(true);return false;}
   if(local.result==2){local.result=0;Messages.Message("NotEnoughDye".Translate(),MessageTypeDefOf.RejectInput,false);}
   if(local.result==3){local.result=0;Messages.Message("造型目标已变化，请重新打开造型台。",MessageTypeDefOf.RejectInput,false);}
   __state=Capture((Pawn)Read(__instance,"pawn"));
   hideField.SetValue(__state.comp,local.hide);partsField.SetValue(__state.comp,new Dictionary<string,bool>(local.parts));
   return true;
  }
  private static void AfterDraw(Window __instance, Scope __state)
  {
   if(__state==null) return;
   try { if(locals.TryGetValue(__instance,out var local)){local.hide=(bool)hideField.GetValue(__state.comp);local.parts=new Dictionary<string,bool>((Dictionary<string,bool>)partsField.GetValue(__state.comp));} }
   finally {Restore(__state);}
  }
  private static void BeforeClose(Window __instance,out Scope __state)
  {
   __state=MP.IsInMultiplayer?Capture((Pawn)Read(__instance,"pawn")):null;
   if(__state!=null)partsField.SetValue(__state.comp,new Dictionary<string,bool>(__state.parts));
  }
  private static void AfterClose(Window __instance,Scope __state){Restore(__state);locals.Remove(__instance);}
  private static bool BeforeAccept(Window __instance)
  {
   if(!MP.InInterface)return true;
   if(!locals.TryGetValue(__instance,out var local) || local.pending)return false;
   var pawn=(Pawn)Read(__instance,"pawn");var selection=Read(__instance,"currentSelection");
   if(pawn==null||pawn.Dead||!pawn.Spawned||!pawn.AllComps.Any(c=>c.GetType()==compType)){local.result=3;return false;}
   var identifiers=new List<string>{(Read(selection,"hairCatalog") as Def)?.defName,Read(selection,"hair")==null?null:(string)Read(Read(selection,"hair"),"hairId"),(Read(selection,"skinColorDef") as Def)?.defName};
   var choices=(IDictionary)Read(selection,"appearanceOptions");
   foreach(string key in choices.Keys.Cast<string>().OrderBy(k=>k,StringComparer.Ordinal)){if(choices[key]!=null){identifiers.Add(key);identifiers.Add((string)Read(choices[key],"id"));}}
   var snapshot=Capture(pawn);var keys=snapshot.parts.Keys.OrderBy(k=>k,StringComparer.Ordinal).ToArray();
   var flags=keys.Select(k=>snapshot.parts[k]).Concat(new[]{(bool)Read(__instance,"hideEarFeathers")}).ToArray();
   var hair=(Color)Read(selection,"hairColor");var skin=(Color)Read(selection,"skinColor");
   int token=++next;local.pending=true;requests[token]=new WeakReference(__instance);
   submit.DoSync(null,pawn,identifiers.ToArray(),keys,flags,new[]{hair.r,hair.g,hair.b,skin.r,skin.g,skin.b},token);
   return false;
  }
  private static void Submit(Pawn pawn,string[] ids,string[] keys,bool[] flags,float[] colors,int token)
  {
   bool success=false; int failure=3;
   try
   {
    if(pawn==null || pawn.Dead || !pawn.Spawned || !pawn.AllComps.Any(c=>c.GetType()==compType) || ids==null || ids.Length<3 || ids.Length%2!=1 || keys==null || flags==null || flags.Length!=keys.Length+1 || colors==null || colors.Length!=6 || colors.Any(c=>float.IsNaN(c)||float.IsInfinity(c)||c<0||c>1))return;
    var catalog=Call("GetHairCatalog",pawn);var skinDef=Call("GetSkinColorDef",pawn);
    if((catalog as Def)?.defName!=ids[0] || (skinDef as Def)?.defName!=ids[2])return;
    if(ids[1]!=null && catalog==null)return;
    var hair=ids[1]==null?null:AccessTools.Method(catalog?.GetType(),"FindStyle").Invoke(catalog,new object[]{ids[1]});
    if(ids[1]!=null && hair==null)return;
    var defs=Call("GetAppearanceDefs",pawn);var selection=Activator.CreateInstance(selectionType);
    var options=(IDictionary)Read(selection,"appearanceOptions");
    for(int i=3;i<ids.Length;i+=2){var def=((IEnumerable)defs).Cast<Def>().SingleOrDefault(d=>d.defName==ids[i]);if(def==null || options.Contains(ids[i]))return;var option=AccessTools.Method(def.GetType(),"FindOption").Invoke(def,new object[]{ids[i+1]});if(option==null)return;options.Add(ids[i],option);}
    var partValues=new Dictionary<string,bool>();for(int i=0;i<keys.Length;i++){if(string.IsNullOrEmpty(keys[i]) || partValues.ContainsKey(keys[i]))return;partValues.Add(keys[i],flags[i]);}
    Color hc=new Color(colors[0],colors[1],colors[2],1),sc=new Color(colors[3],colors[4],colors[5],1);
    // Match the original confirmation's dye transaction; replay never reads local PreviewStates.
    if(pawn.story!=null && (!(bool)AccessTools.DeclaredMethod(dialog,"ColorsEqual").Invoke(null,new object[]{hc,pawn.story.HairColor}) || !(bool)AccessTools.DeclaredMethod(dialog,"ColorsEqual").Invoke(null,new object[]{sc,pawn.story.SkinColor})) && !(bool)Call("TryConsumeDye",pawn,1)){failure=2;return;}
    Write(selection,"hairCatalog",catalog);Write(selection,"hair",hair);Write(selection,"appearanceDefs",defs);Write(selection,"skinColorDef",skinDef);Write(selection,"hairColor",hc);Write(selection,"skinColor",sc);
    Call("ApplySelection",pawn,selection);var target=Capture(pawn);var merged=new Dictionary<string,bool>(target.parts);foreach(var part in partValues)merged[part.Key]=part.Value;hideField.SetValue(target.comp,flags[flags.Length-1]);partsField.SetValue(target.comp,merged);pawn.Drawer.renderer.SetAllGraphicsDirty();success=true;
   }
   finally
   {
    if(MP.IsExecutingSyncCommandIssuedBySelf && requests.TryGetValue(token,out var pending))
    {requests.Remove(token);if(pending.Target is Window w && locals.TryGetValue(w,out var local)){local.pending=false;local.result=success?1:failure;}}
   }
  }
 }
}
