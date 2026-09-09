using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenDroneStationActions
    {
        private static Type stationType;
        internal static void Apply(Harmony harmony)
        {
            stationType = AccessTools.TypeByName("RavenRace.Features.Drone.Buildings.Building_RavenDroneStation");
            if (stationType == null) throw new TypeLoadException("Raven drone station");
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(stationType, "SetDesiredDroneSlot"), null);
            MP.RegisterSyncMethod(typeof(RavenDroneStationActions), nameof(Eject));
            harmony.Patch(AccessTools.DeclaredMethod(stationType, "TryEjectSlot"),
                prefix: new HarmonyMethod(typeof(RavenDroneStationActions), nameof(BeforeEject)));
        }
        private static Def SlotDef(Thing station, int index)
        {
            var slots = (IList)AccessTools.Field(stationType, "droneInventory").GetValue(station);
            if (index < 0 || index >= slots.Count || slots[index] == null) return null;
            return (Def)AccessTools.Field(slots[index].GetType(), "DroneDef").GetValue(slots[index]);
        }
        private static bool BeforeEject(Thing __instance, int __0, ref int __1, ref bool __result)
        {
            if (!MP.InInterface) return true;
            __1 = 0; __result = false;
            Eject(__instance, __0, SlotDef(__instance, __0));
            return false;
        }
        private static void Eject(Thing station, int index, Def expected)
        {
            if (station == null || station.GetType() != stationType || !station.Spawned || expected == null || SlotDef(station, index) != expected) return;
            object[] args = { index, 0 };
            if ((bool)AccessTools.DeclaredMethod(stationType, "TryEjectSlot").Invoke(station, args))
                Messages.Message("RavenRace_Drone_Dialog_RavenDroneStationInventory_Message".Translate() + args[1].ToString()
                    + "RavenRace_Drone_Dialog_RavenDroneStationInventory_Message_2".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
