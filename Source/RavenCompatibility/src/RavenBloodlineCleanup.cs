using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenBloodlineCleanup
 {
  private static Type bloodline;private static MethodInfo hasBloodline,remove;private static ISyncMethod cleanup;
  internal static void Apply(Harmony h)
  {
   bloodline=AccessTools.TypeByName("RavenRace.Features.Bloodline.CompBloodline");
   hasBloodline=AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.BloodlineUtility"),"HasBloodline",new[]{bloodline,typeof(string[])});
   var settings=AccessTools.TypeByName("RavenRace.Settings.Settings_Bloodline");remove=AccessTools.DeclaredMethod(settings,"RemoveHediff",new[]{typeof(Pawn),typeof(string)});
   if(hasBloodline==null||remove==null)throw new MissingMethodException("Raven bloodline cleanup helpers");
   cleanup=MP.RegisterSyncMethod(typeof(RavenBloodlineCleanup),nameof(CleanupMap));
   h.Patch(AccessTools.DeclaredMethod(settings,"CleanupLeakedHediffs",Type.EmptyTypes),prefix:new HarmonyMethod(typeof(RavenBloodlineCleanup),nameof(Before)));
  }
  private static bool Before()
  {
   if(!MP.IsInMultiplayer||!MP.InInterface)return true;
   foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))cleanup.DoSync(null,map);
   Messages.Message("血脉状态清理已提交。",MessageTypeDefOf.TaskCompletion,false);return false;
  }
  private static void CleanupMap(Map map)
  {
   if(map==null)return;
   foreach(var pawn in map.mapPawns.AllPawnsSpawned.OrderBy(p=>p.thingIDNumber))
   {
    if(pawn.def.defName!="Raven_Race"||pawn.health?.hediffSet==null)continue;
    var comp=pawn.AllComps.FirstOrDefault(c=>c.GetType()==bloodline);
    if(comp!=null&&(bool)hasBloodline.Invoke(null,new object[]{comp,new[]{"Wolfein_Race"}}))continue;
    remove.Invoke(null,new object[]{pawn,"Wolfein_NightStrength"});remove.Invoke(null,new object[]{pawn,"Wolfein_MoonStrength"});
   }
  }
 }
}
