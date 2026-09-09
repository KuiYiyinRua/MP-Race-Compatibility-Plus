using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Verse;
using Runtime=Multiplayer.Client.Multiplayer;
namespace MP_MeowOnlineShop
{
 internal static class RavenIndustrialClocks
 {
  internal static void Apply(Harmony harmony)
  {
   Patch(harmony,"RavenRace.Features.RavenConveyor.CompRavenCuttingMachine",new[]{"get_ScheduledWorkTicksDone","PostExposeData","ReceiveCompSignal","ScheduleNextCheck","WakeIndustrialScheduler","RefreshIndustrialSchedule"});
   Patch(harmony,"RavenRace.Features.RavenLiquidPipe.CompRavenFillingMachineRecipeProcessor",new[]{"get_ScheduledWorkTicksDone","PostExposeData","ReceiveCompSignal","TryStartSelectedRecipeIfDue","ScheduleNextAutoStartCheck","WakeIndustrialScheduler","RefreshIndustrialSchedule"});
   Patch(harmony,"RavenRace.Features.RavenLiquidPipe.CompRavenRecipePowerGenerator",new[]{"get_ScheduledRemainingTicks","ReceiveCompSignal","PostExposeData","TryStartSelectedRecipeIfDue","ScheduleNextAutoStartCheck","CanOperateNow","WakeIndustrialScheduler","RefreshIndustrialSchedule"});
  }
  private static void Patch(Harmony harmony,string name,string[] methods)
  {
   var type=AccessTools.TypeByName(name)??throw new TypeLoadException(name);
   foreach(var method in methods)harmony.Patch(AccessTools.DeclaredMethod(type,method)??throw new MissingMethodException(name,method),transpiler:new HarmonyMethod(typeof(RavenIndustrialClocks),nameof(OwnerClock)));
  }
  private static int Ticks(TickManager manager,ThingComp comp)
  {
   if(!MP.IsInMultiplayer||Runtime.AsyncWorldTime==null)return manager.TicksGame;
   return comp.parent.MapHeld?.AsyncTime().mapTicks??Runtime.AsyncWorldTime.worldTicks;
  }
  private static IEnumerable<CodeInstruction> OwnerClock(IEnumerable<CodeInstruction> code)
  {
   var getter=AccessTools.PropertyGetter(typeof(TickManager),nameof(TickManager.TicksGame));int count=0;
   foreach(var ins in code)
   {
    if(ins.Calls(getter))
    {
     var owner=new CodeInstruction(OpCodes.Ldarg_0);owner.labels.AddRange(ins.labels);ins.labels.Clear();owner.blocks.AddRange(ins.blocks);ins.blocks.Clear();yield return owner;
     ins.opcode=OpCodes.Call;ins.operand=AccessTools.Method(typeof(RavenIndustrialClocks),nameof(Ticks));count++;
    }
    yield return ins;
   }
   if(count!=1)throw new InvalidOperationException("Raven industrial owner-clock getter count "+count);
  }
 }
}
