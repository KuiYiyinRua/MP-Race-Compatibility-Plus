using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-412/413/417: two vanilla world-side inventory helpers are not
    /// deterministic under Multiplayer.
    ///
    /// CaravanShuttleUtility.ConsumeFuelFromCaravanInventory enumerates the
    /// shared static CaravanInventoryUtility.AllInventoryItems list while
    /// SplitOff mutates pawn inventories. On the replaying peer the static
    /// list is cleared during enumeration and the synchronized refuel command
    /// throws "Collection was modified". The patch snapshots the inventory
    /// into a private, sorted list before consuming fuel.
    ///
    /// CaravanInventoryUtility.FindPawnToMoveInventoryTo performs three
    /// TryRandomElement calls over the caller's candidate list. When a trade
    /// or shuttle arrival executes on both peers, the candidate order can
    /// differ and the first divergent trace lands exactly in this method. In
    /// multiplayer we hand the original method a stable sorted copy so the
    /// same index is selected on every peer.
    ///
    /// Desync-563: faction-trade cargo reached TransportersArrivalActionUtility
    /// with a different inventory/container order on the two peers. The local
    /// trace was still in ThingOwner.TryTransferToContainer -> Thing.SplitOff
    /// while the host had already reached TransportShip.ArriveAt ->
    /// ShipJobMaker.MakeShipJob. Normalize the source inventories before
    /// LoadCaravanItemsIntoContainer and the active transporter before
    /// DropShuttle so stack splitting and subsequent ship-job allocation use
    /// the same deterministic order. The sort key deliberately does not start
    /// with thingIDNumber, because the ID stream is the state being protected.
    /// </summary>
    internal static class Patch_CaravanShuttleMp
    {
        private static bool _applied;

        private static readonly StringComparer SortComparer =
            StringComparer.Ordinal;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo consumeFuel = AccessTools.Method(
                    typeof(CaravanShuttleUtility),
                    "ConsumeFuelFromCaravanInventory",
                    new[] { typeof(Caravan), typeof(int) });
                MethodInfo findPawn = AccessTools.Method(
                    typeof(CaravanInventoryUtility),
                    "FindPawnToMoveInventoryTo",
                    new[]
                    {
                        typeof(Thing),
                        typeof(List<Pawn>),
                        typeof(List<Pawn>),
                        typeof(Pawn)
                    });
                MethodInfo loadCaravanItems = AccessTools.Method(
                    typeof(CaravanShuttleUtility),
                    "LoadCaravanItemsIntoContainer",
                    new[] { typeof(IEnumerable<Pawn>), typeof(ThingOwner) });
                MethodInfo dropShuttle = AccessTools.Method(
                    typeof(TransportersArrivalActionUtility),
                    "DropShuttle",
                    new[]
                    {
                        typeof(ActiveTransporterInfo),
                        typeof(Map),
                        typeof(IntVec3),
                        typeof(Rot4?),
                        typeof(Faction)
                    });
                MethodInfo consumePrefix = AccessTools.Method(
                    typeof(Patch_CaravanShuttleMp),
                    nameof(ConsumeFuelPrefix));
                MethodInfo findPrefix = AccessTools.Method(
                    typeof(Patch_CaravanShuttleMp),
                    nameof(FindPawnToMoveInventoryToPrefix));
                MethodInfo loadPrefix = AccessTools.Method(
                    typeof(Patch_CaravanShuttleMp),
                    nameof(LoadCaravanItemsIntoContainerPrefix));
                MethodInfo dropPrefix = AccessTools.Method(
                    typeof(Patch_CaravanShuttleMp),
                    nameof(DropShuttlePrefix));

                int patched = 0;
                if (consumeFuel != null && consumePrefix != null)
                {
                    harmony.Patch(
                        consumeFuel,
                        prefix: new HarmonyMethod(consumePrefix)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                if (findPawn != null && findPrefix != null)
                {
                    harmony.Patch(
                        findPawn,
                        prefix: new HarmonyMethod(findPrefix)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                if (loadCaravanItems != null && loadPrefix != null)
                {
                    harmony.Patch(
                        loadCaravanItems,
                        prefix: new HarmonyMethod(loadPrefix)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                if (dropShuttle != null && dropPrefix != null)
                {
                    harmony.Patch(
                        dropShuttle,
                        prefix: new HarmonyMethod(dropPrefix)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Caravan shuttle determinism " +
                        "targets were not resolved; skipped.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Caravan shuttle determinism active: " +
                    "fuel consumption uses a snapshot and caravan inventory " +
                    "placement uses stable candidate/container order (boundaries=" +
                    patched + ").");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan shuttle determinism apply " +
                    "failed: " + e.Message);
            }
        }

        private static bool ConsumeFuelPrefix(Caravan caravan, int fuelAmount)
        {
            if (!MP.IsInMultiplayer)
                return true;

            if (caravan == null || fuelAmount <= 0)
                return false;
            if (caravan.Shuttle == null ||
                caravan.Shuttle.RefuelableComp == null)
            {
                return false;
            }

            try
            {
                int remaining = fuelAmount;
                List<Thing> snapshot =
                    new List<Thing>(CaravanInventoryUtility.AllInventoryItems(caravan));
                snapshot.Sort((a, b) =>
                    (a?.thingIDNumber ?? int.MaxValue).CompareTo(
                        b?.thingIDNumber ?? int.MaxValue));

                for (int i = 0; i < snapshot.Count && remaining > 0; i++)
                {
                    Thing item = snapshot[i];
                    if (item == null || item.Destroyed ||
                        !caravan.Shuttle.RefuelableComp.Props.fuelFilter.Allows(item))
                    {
                        continue;
                    }

                    Thing split = item.SplitOff(
                        Math.Min(remaining, item.stackCount));
                    remaining -= split.stackCount;
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan shuttle fuel snapshot " +
                    "failed: " + e.Message);
            }

            return false;
        }

        private static bool FindPawnToMoveInventoryToPrefix(
            ref List<Pawn> candidates)
        {
            if (!MP.IsInMultiplayer || candidates == null ||
                candidates.Count < 2)
            {
                return true;
            }

            List<Pawn> stable = new List<Pawn>(candidates);
            stable.Sort((a, b) =>
                (a?.thingIDNumber ?? int.MaxValue).CompareTo(
                    b?.thingIDNumber ?? int.MaxValue));
            candidates = stable;
            return true;
        }

        private static void LoadCaravanItemsIntoContainerPrefix(
            ref IEnumerable<Pawn> caravanPawns,
            ThingOwner container)
        {
            if (!MP.IsInMultiplayer || caravanPawns == null)
                return;

            try
            {
                List<Pawn> stablePawns = new List<Pawn>(caravanPawns);
                stablePawns.Sort(ComparePawns);

                for (int i = 0; i < stablePawns.Count; i++)
                {
                    Pawn pawn = stablePawns[i];
                    NormalizeThingOwnerOrder(
                        pawn?.inventory?.innerContainer);
                }

                NormalizeThingOwnerOrder(container);
                caravanPawns = stablePawns;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan shuttle load order " +
                    "normalization failed: " + e.Message);
            }
        }

        private static void DropShuttlePrefix(
            ActiveTransporterInfo transporter)
        {
            if (!MP.IsInMultiplayer || transporter == null)
                return;

            try
            {
                NormalizeThingOwnerOrder(transporter.innerContainer);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DropShuttle container order " +
                    "normalization failed: " + e.Message);
            }
        }

        private static void NormalizeThingOwnerOrder(ThingOwner owner)
        {
            if (owner == null || owner.Count < 2)
                return;

            PropertyInfo listProperty = owner.GetType().GetProperty(
                "InnerListForReading",
                BindingFlags.Instance | BindingFlags.Public);
            List<Thing> things = listProperty?.GetValue(owner, null)
                as List<Thing>;
            if (things == null || things.Count < 2)
                return;

            things.Sort(CompareThings);
        }

        private static int ComparePawns(Pawn left, Pawn right)
        {
            int result = SortComparer.Compare(
                BuildPawnSortKey(left),
                BuildPawnSortKey(right));
            if (result != 0)
                return result;

            return (left?.thingIDNumber ?? int.MaxValue).CompareTo(
                right?.thingIDNumber ?? int.MaxValue);
        }

        private static string BuildPawnSortKey(Pawn pawn)
        {
            if (pawn == null)
                return string.Empty;

            return string.Join(
                "|",
                pawn.def?.defName ?? string.Empty,
                pawn.kindDef?.defName ?? string.Empty,
                pawn.Faction?.def?.defName ?? string.Empty,
                pawn.gender.ToString(),
                pawn.Name?.ToString() ?? string.Empty,
                pawn.ageTracker?.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static int CompareThings(Thing left, Thing right)
        {
            int result = SortComparer.Compare(
                BuildThingSortKey(left),
                BuildThingSortKey(right));
            if (result != 0)
                return result;

            return (left?.thingIDNumber ?? int.MaxValue).CompareTo(
                right?.thingIDNumber ?? int.MaxValue);
        }

        private static string BuildThingSortKey(Thing thing)
        {
            if (thing == null)
                return string.Empty;

            IntVec3 position = thing.PositionHeld;
            return string.Join(
                "|",
                thing.GetType().FullName ?? string.Empty,
                thing.def?.defName ?? string.Empty,
                thing.Stuff?.defName ?? string.Empty,
                thing.stackCount.ToString(CultureInfo.InvariantCulture),
                thing.HitPoints.ToString(CultureInfo.InvariantCulture),
                position.x.ToString(CultureInfo.InvariantCulture),
                position.y.ToString(CultureInfo.InvariantCulture),
                position.z.ToString(CultureInfo.InvariantCulture));
        }
    }
}
