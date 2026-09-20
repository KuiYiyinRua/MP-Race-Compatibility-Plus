using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility {
 internal static class NivarianMultiblock {
  static Type controller,recipeType; static FieldInfo selected; static ISyncMethod select;
  internal static void Apply(Harmony harmony){
   controller=AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.Comp_MultiBlockController")??throw new TypeLoadException("Niva multiblock");
   selected=AccessTools.Field(controller,"_selectedPreviewRecipe")??throw new MissingFieldException("multiblock selected recipe");recipeType=selected.FieldType;
   select=MP.RegisterSyncMethod(typeof(NivarianMultiblock),nameof(Select));
   int count=0;
   foreach(var type in new[]{controller}.Concat(controller.GetNestedTypes(AccessTools.all)))
    foreach(var method in AccessTools.GetDeclaredMethods(type).Where(m=>m.Name.Contains("CompGetGizmosExtra")&&m.ReturnType==typeof(void)))
     if(PatchProcessor.GetOriginalInstructions(method).Any(i=>i.opcode==OpCodes.Stfld&&Equals(i.operand,selected))){
      harmony.Patch(method,prefix:new HarmonyMethod(typeof(NivarianMultiblock),nameof(Before)));count++;
     }
   if(count!=2)throw new InvalidOperationException("Multiblock selection callbacks changed: "+count);
   var expose=AccessTools.Method(typeof(NivarianMultiblock),nameof(Expose)).MakeGenericMethod(recipeType);
   harmony.Patch(AccessTools.DeclaredMethod(controller,"PostExposeData"),postfix:new HarmonyMethod(expose));
   Log.Message("[TaleNivarianCompat] multiblock recipe priority synchronized and persisted (2 selection callbacks).");
  }
  static bool Before(object __instance){
   if(!MP.IsInMultiplayer||!MP.InInterface)return true;
   var fields=AccessTools.GetDeclaredFields(__instance.GetType());
   var comp=__instance as ThingComp ?? (ThingComp)fields.Single(f=>f.FieldType==controller).GetValue(__instance);
   var recipeField=fields.SingleOrDefault(f=>f.FieldType==recipeType);
   var recipe=__instance is ThingComp ? null : recipeField?.GetValue(__instance) as Def;
   select.DoSync(null,comp,recipe);return false;
  }
  static void Select(ThingComp comp,Def recipe){
   if(comp==null||!controller.IsInstanceOfType(comp)||comp.parent.Destroyed)return;
   if(recipe!=null){
    if(!recipeType.IsInstanceOfType(recipe))return;
    var possible=(IEnumerable)AccessTools.Property(controller,"PossibleRecipes").GetValue(comp);
    if(possible==null||!possible.Cast<object>().Contains(recipe))return;
   }
   selected.SetValue(comp,recipe);
  }
  static void Expose<T>(ThingComp __instance) where T:Def,new() {
   T recipe=selected.GetValue(__instance) as T;
   Scribe_Defs.Look(ref recipe,"mpNivarianSelectedRecipe");
   selected.SetValue(__instance,recipe);
  }
 }
}
