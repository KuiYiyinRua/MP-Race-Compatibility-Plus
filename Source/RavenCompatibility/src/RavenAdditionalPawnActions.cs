using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Patches;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenAdditionalPawnActions
 {
  private static MethodInfo complete;
  private static FieldInfo treasureTick;
  private static ISyncMethod choose;
  internal static void Apply(Harmony h)
  {
   var treasure = AccessTools.TypeByName("RavenRace.Features.Creatures.GreatRaven.CompRavenArchonTreasure");
   var absorb = AccessTools.DeclaredMethod(treasure, "AbsorbTarget", new[] { typeof(Thing) }) ?? throw new MissingMethodException("Raven gold absorption");
   MP.RegisterSyncMethod(absorb);
   treasureTick = AccessTools.Field(treasure, "lastTreasureTick") ?? throw new MissingFieldException("Raven treasure clock");
   h.Patch(AccessTools.DeclaredMethod(treasure, "PostExposeData"), prefix: new HarmonyMethod(typeof(RavenAdditionalPawnActions), nameof(TransferTreasureClock)));
   var degradation = AccessTools.TypeByName("RavenRace.Features.DegradationCharm.Hediffs.Hediff_Degradation");
   complete = AccessTools.DeclaredMethod(degradation, "CompleteTransformation", new[] { typeof(bool) }) ?? throw new MissingMethodException("Raven final choice executor");
   choose = MP.RegisterSyncMethod(typeof(RavenAdditionalPawnActions), nameof(Choose));
   h.Patch(complete, prefix: new HarmonyMethod(typeof(RavenAdditionalPawnActions), nameof(BeforeChoose)));
   var dialog = AccessTools.TypeByName("RavenRace.Features.DegradationCharm.UI.Dialog_DegradationGenderChoice");
   h.Patch(AccessTools.DeclaredMethod(dialog, "DoWindowContents"), prefix: new HarmonyMethod(typeof(RavenAdditionalPawnActions), nameof(CloseResolvedChoice)));
  }
  private static void TransferTreasureClock(ThingComp __instance)
  {
   if (!TimestampFixer.currentOffset.HasValue) return;
   int tick = (int)treasureTick.GetValue(__instance);
   if (tick >= 0) treasureTick.SetValue(__instance, tick + TimestampFixer.currentOffset.Value);
  }
  private static bool BeforeChoose(Hediff __instance, bool __0)
  {
   if (!MP.InInterface) return true;
   choose.DoSync(null, __instance.pawn, __instance.loadID, __0); return false;
  }
  private static void Choose(Pawn pawn, int id, bool change)
  {
   var hediff = pawn?.health?.hediffSet?.hediffs.FirstOrDefault(h => h.loadID == id && h.GetType() == complete.DeclaringType);
   if (hediff != null) complete.Invoke(hediff, new object[] { change });
  }
  private static bool CloseResolvedChoice(Window __instance)
  {
   if (!MP.IsInMultiplayer) return true;
   var action = (Delegate)AccessTools.Field(__instance.GetType(), "resolvedAction").GetValue(__instance);
   if (!(action?.Target is Hediff hediff) || hediff.pawn?.health?.hediffSet?.hediffs.Contains(hediff) == true) return true;
   // The first synchronized choice removes the hediff on every peer. Close other
   // local copies without invoking their now-stale callbacks a second time.
   __instance.Close(false); return false;
  }
 }
}
