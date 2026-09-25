using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.PerformanceCompatibility
{
    internal static class KingfisherCompatibility
    {
        internal static void Install()
        {
            if (!ModsConfig.IsActive("Vortex.Kingfisher")) return;
            var t = typeof(KingfisherCompatibility);
            // Patch helper boundaries, not Harmony-unpatching live game ticks.
            PerformanceCompatibilityMod.Prefix(PerformanceCompatibilityMod.Require("Kingfisher.Features.ShamblerTargetSearchPatch", "DeferIdleSearch"), t, nameof(AllowSinglePlayer));
            PerformanceCompatibilityMod.Prefix(PerformanceCompatibilityMod.Require("Kingfisher.Features.ShamblerTargetSearchPatch", "NotifyAlertScheduled"), t, nameof(AllowSinglePlayer));
            PerformanceCompatibilityMod.Prefix(PerformanceCompatibilityMod.Require("Kingfisher.Features.RefuelWorkCandidates", "CandidatesFor"), t, nameof(LiveCandidates));
            Log.Message("[Meow.Performance] Kingfisher: live ordered refuel candidates; MP shambler delay bypassed; building queries use versioned index; other rewrites retained.");
        }

        private static bool AllowSinglePlayer() => !MP.IsInMultiplayer;

        private static bool LiveCandidates(Map map, ref List<Thing> __result)
        {
            if (!MP.IsInMultiplayer) return true;
            var result = new List<Thing>();
            // No query-time expiry, caller-owned result, authoritative list order.
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable))
            {
                if (thing is Building_Turret || !thing.Spawned || thing.Fogged()) continue;
                var comp = thing.TryGetComp<CompRefuelable>();
                if (comp == null || !comp.allowAutoRefuel) continue;
                if (comp.FuelPercentOfMax > 0f && !comp.Props.allowRefuelIfNotEmpty) continue;
                if (comp.ShouldAutoRefuelNow) result.Add(thing);
            }
            __result = result;
            return false;
        }
    }
}
