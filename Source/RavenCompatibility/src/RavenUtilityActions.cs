using System;
using HarmonyLib;
using Multiplayer.API;
namespace MP_MeowOnlineShop
{
 internal static class RavenUtilityActions
 {
  internal static void Apply(Harmony harmony)
  {
   Register("RavenRace.Features.RavenReliquaryStand.Building_RavenReliquaryStand", "TryRemoveThing");
   Register("RavenRace.Features.UniqueEquipment.LiquidShield.CompRavenLiquidShield", "ToggleShield");
   // Verified against the exact installed IL: auto-load, three altar layout
   // blueprints, and egg ejection. Dev instant-build/hatch are separate paths.
   for (int i = 1; i <= 5; i++) Register("RavenRace.Building_Cradle", "<GetGizmos>b__15_" + i);
  }
  private static void Register(string type, string method)
  {
   var target = AccessTools.DeclaredMethod(AccessTools.TypeByName(type), method, Type.EmptyTypes) ?? throw new MissingMethodException(type, method);
   MP.RegisterSyncMethod(target);
  }
 }
}
