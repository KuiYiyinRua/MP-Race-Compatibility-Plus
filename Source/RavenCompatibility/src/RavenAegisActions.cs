using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenAegisActions
 {
  private static FieldInfo consumeFluid;
  internal static void Apply(Harmony h)
  {
   var core=AccessTools.TypeByName("RavenRace.Features.MechanicalAngel.CompAegisCore");
   consumeFluid=AccessTools.Field(core,"allowConsumeFluid") ?? throw new MissingFieldException("Aegis fluid policy");
   // Exact 74AE9E1D target IL: two policy toggles, then the target callback
   // that drops a selected pawn's weapon before ordering the equip job.
   foreach(var index in new[]{1,3}) MP.RegisterSyncMethod(AccessTools.DeclaredMethod(core,"<CompGetGizmosExtra>b__13_"+index,Type.EmptyTypes) ?? throw new MissingMethodException("Aegis policy toggle"));
   MP.RegisterSyncMethod(AccessTools.DeclaredMethod(core,"<CompGetGizmosExtra>b__13_5",new[]{typeof(LocalTargetInfo)}) ?? throw new MissingMethodException("Aegis equip callback"));
   h.Patch(AccessTools.DeclaredMethod(core,"PostExposeData"),postfix:new HarmonyMethod(typeof(RavenAegisActions),nameof(ExposePolicy)));
   var wear=AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Features.MechanicalAngel.Gear.AegisGearUtility"),"TryForceWearSpecialApparel",new[]{typeof(Pawn),typeof(Apparel)}) ?? throw new MissingMethodException("Aegis special outfit executor");
   MP.RegisterSyncMethod(wear);
   h.Patch(wear,prefix:new HarmonyMethod(typeof(RavenAegisActions),nameof(ValidWear)));
  }
  private static bool ValidWear(Pawn __0,Apparel __1,ref bool __result)
  {
   if(!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)return true;
   if(__0!=null && __0.Spawned && !__0.Dead && __1!=null && __1.Spawned && __1.Map==__0.Map)return true;
   __result=false;return false;
  }
  private static void ExposePolicy(ThingComp __instance)
  {
   bool value=(bool)consumeFluid.GetValue(__instance);
   Scribe_Values.Look(ref value,"mpRavenAegisAllowConsumeFluid",true);
   consumeFluid.SetValue(__instance,value);
  }
 }
}
