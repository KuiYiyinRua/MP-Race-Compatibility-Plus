using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-648/651/652: when a shuttle enters a new Site, vanilla
    /// TransportersArrivalAction_VisitSite asks TravellingTransporters to run
    /// the whole map-generation and pawn-drop path as a LongEvent. That path
    /// is entered from the synchronized world tick, but the queued LongEvent
    /// executes after that tick has left Multiplayer's world/map context.
    ///
    /// The map contents themselves are identical in the desync bundles. The
    /// first divergence happens afterwards as different map tick/Job ID order
    /// is observed on the two peers. Keep this particular arrival synchronous
    /// in multiplayer so MapSetup, map post-generation, pawn arrival and the
    /// first scheduler boundary are completed in one shared world-tick call.
    /// Existing Site maps already return false from the vanilla method, so
    /// this only changes newly generated Site maps.
    /// </summary>
    internal static class Patch_TransportVisitSiteMp
    {
        private static bool _applied;
        private static bool _loggedActive;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(TransportersArrivalAction_VisitSite),
                    "ShouldUseLongEvent",
                    new[]
                    {
                        typeof(List<ActiveTransporterInfo>),
                        typeof(PlanetTile)
                    });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_TransportVisitSiteMp),
                    nameof(ShouldUseLongEventPrefix));

                if (target == null || prefix == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Transport visit-site map " +
                        "generation boundary target resolution failed; skipped.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Transport visit-site map generation " +
                    "boundary active: new Site arrival runs inside the " +
                    "multiplayer world-tick context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Transport visit-site map generation " +
                    "boundary apply failed: " + e.Message);
            }
        }

        private static bool ShouldUseLongEventPrefix(ref bool __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            // TravellingTransporters.Arrived invokes this before queuing the
            // LongEvent. Returning false keeps the entire VisitSite.Arrived
            // call on the shared world-tick stack. For a Site that already
            // has a map, vanilla already returns false, so there is no
            // behavioral change on that path.
            __result = false;

            if (!_loggedActive)
            {
                _loggedActive = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Synchronous VisitSite arrival " +
                    "enabled for multiplayer new-map generation.");
            }

            return false;
        }
    }
}
