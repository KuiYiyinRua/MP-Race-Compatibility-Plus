using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenConveyorConstruction
 {
  private static Type conveyorType;
  internal static void Apply(Harmony harmony)
  {
   conveyorType = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.CompRavenConveyor") ?? throw new TypeLoadException("Raven conveyor component");
   foreach (string name in new[] { "FailConstruction" })
    harmony.Patch(AccessTools.DeclaredMethod(typeof(Frame), name, new[] { typeof(Pawn) }), transpiler: new HarmonyMethod(typeof(RavenConveyorConstruction), nameof(CarryLayer)));
  }
  private static Thing PreserveLayer(Thing result, Frame frame)
  {
   // Failed construction must preserve the layer on its replacement blueprint.
   if (MP.IsInMultiplayer && frame.BuildDef != null && frame.BuildDef.HasComp(conveyorType))
    result.overrideGraphicIndex = frame.overrideGraphicIndex.GetValueOrDefault();
   return result;
  }
  private static IEnumerable<CodeInstruction> CarryLayer(IEnumerable<CodeInstruction> instructions)
  {
   var make = AccessTools.DeclaredMethod(typeof(ThingMaker), nameof(ThingMaker.MakeThing), new[] { typeof(ThingDef), typeof(ThingDef) });
   int count = 0;
   foreach (var instruction in instructions)
   {
    yield return instruction;
    if (!instruction.Calls(make)) continue;
    count++;
    yield return new CodeInstruction(OpCodes.Ldarg_0);
    yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(RavenConveyorConstruction), nameof(PreserveLayer)));
   }
   if (count != 1) throw new InvalidOperationException("Raven frame MakeThing boundary mismatch: " + count);
  }
 }
}
