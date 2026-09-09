using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenDroneReadOnlyQueries
    {
        private static Type reservationType;
        private static FieldInfo drones;
        private static MethodInfo rebuild, reserved, fill;
        internal static void Apply(Harmony harmony)
        {
            var finder = AccessTools.TypeByName("RavenRace.Features.Drone.Hauling.DroneHaulTaskFinder");
            reservationType = AccessTools.TypeByName("RavenRace.Features.Drone.Hauling.DroneReservationSnapshot");
            drones = AccessTools.Field(finder, "drones");
            rebuild = AccessTools.DeclaredMethod(reservationType, "Rebuild");
            reserved = AccessTools.DeclaredMethod(reservationType, "ReservedToCell");
            fill = AccessTools.DeclaredMethod(reservationType, "FillTargetReservations");
            if (drones == null || rebuild == null || reserved == null || fill == null) throw new MissingMemberException("Raven drone reservation queries");
            harmony.Patch(AccessTools.DeclaredMethod(finder, "InTransitToCell"),
                prefix: new HarmonyMethod(typeof(RavenDroneReadOnlyQueries), nameof(InTransit)));
            harmony.Patch(AccessTools.DeclaredMethod(finder, "FillInTransitToBox"),
                prefix: new HarmonyMethod(typeof(RavenDroneReadOnlyQueries), nameof(Fill)));
        }
        private static object Snapshot(object finder)
        {
            var snapshot = Activator.CreateInstance(reservationType);
            rebuild.Invoke(snapshot, new[] { drones.GetValue(finder) });
            return snapshot;
        }
        // EnsurePrepared calls BeginDispatch, which refreshes world indexes and
        // drains the registry's change queue. UI queries must not prepare that state.
        private static bool InTransit(object __instance, Thing __0, int __1, ref int __result)
        {
            if (!MP.InInterface) return true;
            __result = (int)reserved.Invoke(Snapshot(__instance), new object[] { __0, __1 });
            return false;
        }
        private static bool Fill(object __instance, Thing __0, Dictionary<int, int> __1)
        {
            if (!MP.InInterface) return true;
            fill.Invoke(Snapshot(__instance), new object[] { __0, __1 });
            return false;
        }
    }
}
