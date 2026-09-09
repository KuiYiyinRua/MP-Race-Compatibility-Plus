using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
 internal static class RavenIndustrialTelemetry
 {
  private static FieldInfo buildings, refreshTick, activeCount;
  private static readonly ConditionalWeakTable<GameComponent, Snapshot> states = new ConditionalWeakTable<GameComponent, Snapshot>();
  private sealed class Snapshot
  {
   public Snapshot() { }
   public bool present;
   public int tick = -1, count;
   public List<Building> items = new List<Building>();
   public void Capture(object owner)
   {
    present = true; tick = (int)refreshTick.GetValue(owner); count = (int)activeCount.GetValue(owner);
    items = new List<Building>((List<Building>)buildings.GetValue(owner));
   }
   public void Restore(object owner)
   {
    var list = (List<Building>)buildings.GetValue(owner); list.Clear(); list.AddRange(items ?? new List<Building>());
    refreshTick.SetValue(owner, tick); activeCount.SetValue(owner, count);
   }
  }
  internal static void Apply(Harmony h)
  {
   var type = AccessTools.TypeByName("RavenRace.Features.CentralHub.Industrial.GameComponent_RavenIndustrialTelemetry");
   buildings = AccessTools.DeclaredField(type, "cachedIndustrialBuildings");
   refreshTick = AccessTools.DeclaredField(type, "lastMachineCacheRefreshTick");
   activeCount = AccessTools.DeclaredField(type, "activeMachineCount");
   if (buildings == null || refreshTick == null || activeCount == null) throw new MissingFieldException("Raven telemetry cache");
   MP.RegisterSyncMethod(type, "SetSampleIntervalTicks");
   h.Patch(AccessTools.DeclaredMethod(type, "RefreshExpectedOutputs"), prefix: new HarmonyMethod(typeof(RavenIndustrialTelemetry), nameof(BeforePreview)), finalizer: new HarmonyMethod(typeof(RavenIndustrialTelemetry), nameof(AfterPreview)));
   h.Patch(AccessTools.DeclaredMethod(type, "RefreshIndustrialBuildingCache"), postfix: new HarmonyMethod(typeof(RavenIndustrialTelemetry), nameof(SortCache)));
   h.Patch(AccessTools.DeclaredMethod(type, "ExposeData"), postfix: new HarmonyMethod(typeof(RavenIndustrialTelemetry), nameof(Expose)));
   h.Patch(AccessTools.DeclaredMethod(type, "FinalizeInit"), postfix: new HarmonyMethod(typeof(RavenIndustrialTelemetry), nameof(AfterInit)));
  }
  private static void BeforePreview(object __instance, out Snapshot __state)
  {
   __state = null;
   if (!MP.InInterface) return;
   __state = new Snapshot(); __state.Capture(__instance);
  }
  private static void AfterPreview(object __instance, Snapshot __state) => __state?.Restore(__instance);
  private static void SortCache(object __instance)
  {
   if (!MP.IsInMultiplayer) return;
   var list = (List<Building>)buildings.GetValue(__instance);
   var ordered = list.OrderBy(b => b?.Map?.uniqueID ?? -1).ThenBy(b => b?.thingIDNumber ?? -1).ToArray();
   list.Clear(); list.AddRange(ordered);
  }
  private static void Expose(GameComponent __instance)
  {
   var state = states.GetOrCreateValue(__instance);
   if (Scribe.mode == LoadSaveMode.Saving) state.Capture(__instance);
   if (!Scribe.EnterNode("mpRavenTelemetryCache")) return;
   try
   {
    Scribe_Values.Look(ref state.present, "present");
    Scribe_Values.Look(ref state.tick, "tick", -1);
    Scribe_Values.Look(ref state.count, "activeCount");
    Scribe_Collections.Look(ref state.items, "buildings", LookMode.Reference);
   }
   finally { Scribe.ExitNode(); }
  }
  private static void AfterInit(GameComponent __instance)
  {
   if (states.TryGetValue(__instance, out var state) && state.present) state.Restore(__instance);
  }
 }
}
