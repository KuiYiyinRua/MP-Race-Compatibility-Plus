using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.PerformanceCompatibility
{
    // Results are a pure projection of the *current* authoritative list.
    // The key is the actual list, so MP faction/map list swaps cannot share
    // an index accidentally. The List version also detects same-count edits.
    public static class BuildingIndex
    {
        private sealed class State
        {
            public State() { }
            internal int Version;
            internal bool Valid;
            internal readonly Dictionary<ThingDef, List<Building>> ByDef = new Dictionary<ThingDef, List<Building>>();
        }

        private static readonly ConditionalWeakTable<List<Building>, State> States = new ConditionalWeakTable<List<Building>, State>();
        private static AccessTools.FieldRef<List<Building>, int> version;
        private static readonly AccessTools.FieldRef< List<Building>> ResultBuffer =
            AccessTools.StaticFieldRefAccess<List<Building>>(AccessTools.Field(typeof(ListerBuildings), "allBuildingsColonistOfDefResult"));

        public static long RebuildCount { get; private set; }
        public static long LookupCount { get; private set; }

        internal static void Install()
        {
            version = AccessTools.FieldRefAccess<List<Building>, int>("_version");
            var t = typeof(BuildingIndex);
            PerformanceCompatibilityMod.Prefix(AccessTools.Method(typeof(ListerBuildings), "AllBuildingsColonistOfDef", new[] { typeof(ThingDef) }), t, nameof(Query));
            PerformanceCompatibilityMod.Prefix(AccessTools.Method(typeof(ListerBuildings), "ColonistsHaveBuilding", new[] { typeof(ThingDef) }), t, nameof(Any));
            PerformanceCompatibilityMod.Prefix(AccessTools.Method(typeof(ListerBuildings), "ColonistsHaveBuildingWithPowerOn", new[] { typeof(ThingDef) }), t, nameof(Powered));
            Log.Message("[Meow.Performance] building index installed: list identity + version; power read live; vanilla result buffer preserved.");
        }

        private static List<Building> Get(ListerBuildings lister, ThingDef def)
        {
            var list = lister.allBuildingsColonist;
            var state = States.GetOrCreateValue(list);
            int current = version(list);
            if (!state.Valid || state.Version != current)
            {
                state.ByDef.Clear();
                foreach (var building in list)
                {
                    if (!state.ByDef.TryGetValue(building.def, out var group))
                        state.ByDef.Add(building.def, group = new List<Building>());
                    group.Add(building);
                }
                state.Version = current;
                state.Valid = true;
                RebuildCount++;
            }
            LookupCount++;
            return state.ByDef.TryGetValue(def, out var result) ? result : null;
        }

        private static bool Query(ListerBuildings __instance, ThingDef def, ref List<Building> __result)
        {
            if (!MP.IsInMultiplayer || def == null) return true;
            var found = Get(__instance, def);
            var buffer = ResultBuffer();
            buffer.Clear();
            if (found != null) buffer.AddRange(found);
            __result = buffer;
            return false;
        }

        private static bool Any(ListerBuildings __instance, ThingDef def, ref bool __result)
        {
            if (!MP.IsInMultiplayer || def == null) return true;
            __result = Get(__instance, def)?.Count > 0;
            return false;
        }

        private static bool Powered(ListerBuildings __instance, ThingDef def, ref bool __result)
        {
            if (!MP.IsInMultiplayer || def == null) return true;
            __result = false;
            var found = Get(__instance, def);
            if (found != null)
                foreach (var building in found)
                {
                    var power = building.TryGetComp<CompPowerTrader>();
                    if (power == null || power.PowerOn) { __result = true; break; }
                }
            return false;
        }
    }
}
