using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenGlobalTuningActions
 {
  private static readonly string[] names={"thingIngredientCostMultiplier","liquidInputCostMultiplier","thingProductYieldMultiplier","liquidOutputYieldMultiplier","workSpeedMultiplier","powerOutputMultiplier","conveyorSpeedMultiplier","conveyorDensityCapacityMultiplier","droneSpeedMultiplier","droneCarryCapacityMultiplier"};
  private static FieldInfo[] fields;private static PropertyInfo current;private static MethodInfo normalize;private static ISyncMethod set;
  internal static void Apply(Harmony h)
  {
   const string ns="RavenRace.Features.RavenLiquidPipe.";
   var type=AccessTools.TypeByName(ns+"GameComponent_RavenIndustrialRecipeTuning")??throw new TypeLoadException("Industrial tuning");
   fields=names.Select(n=>AccessTools.DeclaredField(type,n)??throw new MissingFieldException(type.FullName,n)).ToArray();
   current=AccessTools.DeclaredProperty(type,"Current");normalize=AccessTools.DeclaredMethod(type,"NormalizeValues");set=MP.RegisterSyncMethod(typeof(RavenGlobalTuningActions),nameof(Set));
   // The exact installed DLL exposes this dialog only through a DebugAction.
   set.SetDebugOnly();
   var dialog=AccessTools.TypeByName(ns+"Dialog_RavenIndustrialGlobalTuning");
   h.Patch(AccessTools.DeclaredMethod(dialog,"ApplyValues"),prefix:new HarmonyMethod(typeof(RavenGlobalTuningActions),nameof(ApplyValues)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"ResetValues"),prefix:new HarmonyMethod(typeof(RavenGlobalTuningActions),nameof(ResetValues)));
  }
  private static bool ApplyValues(IList ___fields)
  {
   if(!MP.IsInMultiplayer||!MP.InInterface)return true;
   if(___fields.Count!=names.Length)throw new InvalidOperationException("Industrial tuning UI field count");
   var values=new float[names.Length];
   for(int i=0;i<values.Length;i++)
   {
    var field=___fields[i];var args=new object[]{0f};
    if(!(bool)AccessTools.DeclaredMethod(field.GetType(),"TryReadBuffer").Invoke(field,args)){Messages.Message("工业倍率无效，请检查输入。",MessageTypeDefOf.RejectInput,false);return false;}
    values[i]=(float)args[0];
   }
   Submit(___fields,values);return false;
  }
  private static bool ResetValues(IList ___fields)
  {
   if(!MP.IsInMultiplayer||!MP.InInterface)return true;Submit(___fields,Enumerable.Repeat(1f,names.Length).ToArray());return false;
  }
  private static void Submit(IList ui,float[] values)
  {
   set.DoSync(null,(object)values);
   for(int i=0;i<values.Length;i++)AccessTools.DeclaredMethod(ui[i].GetType(),"SetPendingValue").Invoke(ui[i],new object[]{values[i]});
   Messages.Message("工业全局倍率已提交。",MessageTypeDefOf.TaskCompletion,false);
  }
  private static void Set(float[] values)
  {
   if(Current.Game==null||values==null||values.Length!=names.Length)return;
   for(int i=0;i<values.Length;i++)if(float.IsNaN(values[i])||float.IsInfinity(values[i])||values[i]<((i==4||i>=6)?0.01f:0f))return;
   var tuning=current.GetValue(null);for(int i=0;i<values.Length;i++)fields[i].SetValue(tuning,values[i]);normalize.Invoke(tuning,null);
  }
 }
}
