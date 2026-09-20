using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace Meow.TaleNivarianCompatibility {
 internal static class NivarianComponentCaches {
  static Type monument; static System.Reflection.FieldInfo cache;
  internal static void Apply(Harmony harmony){
   monument=AccessTools.TypeByName("Nivarian.GameComp_NivarianRebirthMonument")??throw new TypeLoadException("Niva monument component");
   cache=AccessTools.Field(monument,"_gameComp")??throw new MissingFieldException(monument.FullName,"_gameComp");
   harmony.Patch(AccessTools.PropertyGetter(monument,"GameComp"),prefix:new HarmonyMethod(typeof(NivarianComponentCaches),nameof(CurrentMonument)));
   // Both original load callbacks only rebuild the already-saved count and
   // reapply saved hediffs under the local faction; base callbacks are empty.
   foreach(var name in new[]{"FinalizeInit","LoadedGame"})
    harmony.Patch(AccessTools.DeclaredMethod(monument,name)??throw new MissingMethodException(monument.FullName,name),prefix:new HarmonyMethod(typeof(NivarianComponentCaches),nameof(PreserveSnapshot)));
   Log.Message("[TaleNivarianCompat] monument cache uses current game; MP loading preserves saved count and effects.");
  }
  static bool PreserveSnapshot()=>!MP.IsInMultiplayer;
  static void CurrentMonument(){
   if(!MP.IsInMultiplayer)return;
   var current=Current.Game?.components.FirstOrDefault(c=>monument.IsInstanceOfType(c));
   if(!ReferenceEquals(cache.GetValue(null),current))cache.SetValue(null,current);
  }
 }
}
