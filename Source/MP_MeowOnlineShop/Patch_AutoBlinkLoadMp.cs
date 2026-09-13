using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace MP_MeowOnlineShop {
 internal static class Patch_AutoBlinkLoadMp {
  private static FieldInfo[] fields;
  private static readonly string[] names={"lastBlinkTick","scheduledBlinkTick","scheduledFarBlink","scheduledBlinkTarget","scheduledResumeDest","scheduledOriginalDest","nextBlinkCheckTick","nextLinkedHediffCheckTick"};
  internal static void Apply(Harmony harmony){
   if(harmony==null||!MP.enabled||!ModsConfig.IsActive("rabiosus.autoblink"))return;
   try{
    Type type=AccessTools.TypeByName("AutoBlink.CompAutoBlink");
    fields=names.Select(n=>AccessTools.DeclaredField(type,n)).ToArray();
    Type[] types={typeof(int),typeof(int),typeof(bool),typeof(IntVec3),typeof(IntVec3),typeof(IntVec3),typeof(int),typeof(int)};
    for(int i=0;i<fields.Length;i++)if(fields[i]==null||fields[i].FieldType!=types[i])throw new InvalidOperationException("Field shape "+names[i]);
    var spawn=AccessTools.DeclaredMethod(type,"PostSpawnSetup",new[]{typeof(bool)});
    var expose=AccessTools.DeclaredMethod(type,"PostExposeData",Type.EmptyTypes);
    if(spawn==null||expose==null)throw new InvalidOperationException("AutoBlink load methods missing");
    harmony.Patch(spawn,prefix:new HarmonyMethod(typeof(Patch_AutoBlinkLoadMp),nameof(BeforeSpawn)),finalizer:new HarmonyMethod(typeof(Patch_AutoBlinkLoadMp),nameof(AfterSpawn)));
    harmony.Patch(expose,postfix:new HarmonyMethod(typeof(Patch_AutoBlinkLoadMp),nameof(ExposeTimers)));
    Log.Message("[MP-MeowOnlineShop] AutoBlink load-state patch active: preserve=8, extraSavedTimers=2.");
   }catch(Exception e){Log.Error("[MP-MeowOnlineShop] REQUIRED TARGET FAILED AutoBlink load-state: "+e);}
  }
  private static void BeforeSpawn(object __instance,bool respawningAfterLoad,ref object[] __state){
   if(MP.IsInMultiplayer&&respawningAfterLoad)__state=fields.Select(f=>f.GetValue(__instance)).ToArray();
  }
  private static Exception AfterSpawn(object __instance,object[] __state,Exception __exception){
   if(__state!=null)for(int i=0;i<fields.Length;i++)fields[i].SetValue(__instance,__state[i]);
   return __exception;
  }
  private static void ExposeTimers(object __instance){
   // Prefix keys avoid changing the mod's existing save format; legacy defaults
   // are zero, matching original construction and single-player initialization.
   for(int i=6;i<8;i++){
    int value=(int)fields[i].GetValue(__instance);
    Scribe_Values.Look(ref value,"mpMeow_"+names[i],0);
    if(Scribe.mode==LoadSaveMode.LoadingVars)fields[i].SetValue(__instance,value);
   }
  }
 }
}
