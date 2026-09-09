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
    /// Vanilla CompLaunchable.TryLaunch transfers the transporter's contents
    /// into an ActiveTransporter and then spawns the FlyShipLeaving at the
    /// launchable's position. On a small secondary map a shuttle can be saved
    /// at the edge, while the leaving skyfaller is larger than the remaining
    /// edge cells. GenSpawn.Spawn then returns null after the contents have
    /// already been removed from the shuttle, which strands the shuttle and
    /// all passengers.
    ///
    /// Keep the synced vanilla launch flow intact and change only the spawn
    /// cell, using a deterministic nearest in-bounds cell when the requested
    /// cell cannot contain the leaving skyfaller. This is deliberately scoped
    /// to multiplayer and to CompLaunchable.TryLaunch's Spawn call.
    /// </summary>
    internal static class Patch_TransportLaunchBoundary
    {
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(CompLaunchable),
                    nameof(CompLaunchable.TryLaunch),
                    new[] { typeof(PlanetTile), typeof(TransportersArrivalAction) });
                MethodInfo spawn = AccessTools.Method(
                    typeof(GenSpawn),
                    nameof(GenSpawn.Spawn),
                    new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode) });
                MethodInfo replacement = AccessTools.Method(
                    typeof(Patch_TransportLaunchBoundary),
                    nameof(SpawnLeavingAtSafeCell));
                MethodInfo transpiler = AccessTools.Method(
                    typeof(Patch_TransportLaunchBoundary),
                    nameof(Transpiler));

                if (target == null || spawn == null || replacement == null || transpiler == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Transport launch boundary skipped: " +
                        $"target={target != null} spawn={spawn != null} replacement={replacement != null} transpiler={transpiler != null}.");
                    return;
                }

                harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(transpiler));

                Log.Message(
                    "[MP-MeowOnlineShop] Transport launch boundary active: " +
                    "CompLaunchable.TryLaunch keeps leaving shuttles inside the map when an edge cell is too small.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Transport launch boundary apply failed: " + e);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo spawn = AccessTools.Method(
                typeof(GenSpawn),
                nameof(GenSpawn.Spawn),
                new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode) });
            MethodInfo replacement = AccessTools.Method(
                typeof(Patch_TransportLaunchBoundary),
                nameof(SpawnLeavingAtSafeCell));

            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (spawn != null && replacement != null && instruction.Calls(spawn))
                {
                    instruction.operand = replacement;
                    replacements++;
                }

                yield return instruction;
            }

            if (replacements == 0)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Transport launch boundary found no GenSpawn.Spawn call in CompLaunchable.TryLaunch.");
            }
        }

        private static Thing SpawnLeavingAtSafeCell(
            Thing thing,
            IntVec3 requested,
            Map map,
            WipeMode wipeMode)
        {
            if (!MP.IsInMultiplayer || !(thing is FlyShipLeaving) || map == null)
                return GenSpawn.Spawn(thing, requested, map, wipeMode);

            IntVec3 safe = FindNearestSafeCell(thing, requested, map);
            if (safe != requested)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Corrected out-of-bounds transport departure: " +
                    $"map={map.uniqueID} def={thing.def?.defName ?? "null"} " +
                    $"requested={requested} safe={safe} size={thing.def?.Size.ToString() ?? "null"}.");
            }

            return GenSpawn.Spawn(thing, safe, map, wipeMode);
        }

        private static IntVec3 FindNearestSafeCell(
            Thing thing,
            IntVec3 requested,
            Map map)
        {
            if (thing?.def == null || FitsAt(thing.def, requested, map))
                return requested;

            CellRect wholeMap = CellRect.WholeMap(map);
            IntVec3 clamped = requested;
            clamped.x = Math.Max(wholeMap.minX, Math.Min(wholeMap.maxX, clamped.x));
            clamped.z = Math.Max(wholeMap.minZ, Math.Min(wholeMap.maxZ, clamped.z));
            clamped.y = requested.y;
            if (FitsAt(thing.def, clamped, map))
                return clamped;

            IntVec3 best = clamped;
            int bestDistance = int.MaxValue;
            foreach (IntVec3 candidate in wholeMap)
            {
                if (!FitsAt(thing.def, candidate, map))
                    continue;

                int distance = Math.Abs(candidate.x - requested.x) +
                    Math.Abs(candidate.z - requested.z);
                if (distance < bestDistance ||
                    (distance == bestDistance && IsEarlier(candidate, best)))
                {
                    best = new IntVec3(candidate.x, requested.y, candidate.z);
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static bool FitsAt(ThingDef def, IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return false;

            if (GenAdj.OccupiedRect(cell, Rot4.North, def.Size).InBounds(map))
                return true;

            if (!def.randomizeRotationOnSpawn)
                return false;

            return GenAdj.OccupiedRect(cell, Rot4.East, def.Size).InBounds(map) &&
                GenAdj.OccupiedRect(cell, Rot4.South, def.Size).InBounds(map) &&
                GenAdj.OccupiedRect(cell, Rot4.West, def.Size).InBounds(map);
        }

        private static bool IsEarlier(IntVec3 left, IntVec3 right)
        {
            return left.z < right.z || (left.z == right.z && left.x < right.x);
        }
    }
}
