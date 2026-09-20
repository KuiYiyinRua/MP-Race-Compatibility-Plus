using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    // Desync-09/10: Room.Role lazily refreshes Brothel's price cache on only one peer.
    // Cache hits and misses both draw Rand.Int, and the cache isn't serialized. The
    // room/comfort formula itself has no randomness: use it directly in multiplayer.
    internal static class BrothelBedPrices
    {
        static Func<Room, int, float> roomFactor;
        static Func<Building_Bed, bool> allowed;

        internal static void Apply(Harmony harmony)
        {
            if (!ModsConfig.IsActive("calamabanana.rjw.brothelcolony")) return;
            var type = AccessTools.TypeByName("BrothelColony.WhoreBed_Utility")
                ?? throw new TypeLoadException("BrothelColony.WhoreBed_Utility");
            var price = AccessTools.DeclaredMethod(type, "CalculatePriceFactor", new[] { typeof(Building_Bed), typeof(float) });
            var room = AccessTools.DeclaredMethod(type, "CalculateBedFactorsForRoom", new[] { typeof(Room), typeof(Building_Bed) });
            var factor = AccessTools.DeclaredMethod(type, "CalculateRoomFactor", new[] { typeof(Room), typeof(int) });
            var permitted = AccessTools.DeclaredMethod(type, "IsAllowedForWhoringOwner", new[] { typeof(Building_Bed) });
            if (price == null || room == null || factor == null || permitted == null)
                throw new MissingMethodException("Required Brothel bed price methods changed");
            roomFactor = (Func<Room, int, float>)Delegate.CreateDelegate(typeof(Func<Room, int, float>), factor);
            allowed = (Func<Building_Bed, bool>)Delegate.CreateDelegate(typeof(Func<Building_Bed, bool>), permitted);
            harmony.Patch(price, prefix: new HarmonyMethod(typeof(BrothelBedPrices), nameof(Price)));
            harmony.Patch(room, prefix: new HarmonyMethod(typeof(BrothelBedPrices), nameof(RoomPrices)));
            Log.Message("[BrothelBedPriceCompat] deterministic uncached room/comfort prices installed; cache-only Rand draws removed in MP.");
        }

        static bool Price(Building_Bed bed, float room_multiplier, ref float __result)
        {
            if (!MP.IsInMultiplayer) return true;
            if (bed == null) { __result = 0f; return false; }
            if (room_multiplier < 0f)
            {
                Room room = bed.Map != null && bed.Map.regionAndRoomUpdater.Enabled ? bed.GetRoom() : null;
                room_multiplier = CalculateRoom(room);
            }
            __result = room_multiplier * bed.GetStatValue(StatDefOf.Comfort);
            return false;
        }

        static bool RoomPrices(Room room, ref float __result)
        {
            if (!MP.IsInMultiplayer) return true;
            // No eager writes to every bed. All consumers use Price and recompute on demand.
            __result = CalculateRoom(room);
            return false;
        }

        static float CalculateRoom(Room room)
        {
            if (room == null) return 0.1f;
            var beds = room.ContainedBeds.Where(b => b.def.building.bed_humanlike).ToList();
            return beds.Count == 0 || !beds.Any(allowed) ? 0.1f : roomFactor(room, beds.Count);
        }
    }
}
