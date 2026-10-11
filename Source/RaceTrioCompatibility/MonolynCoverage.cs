using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynCoverage
    {
        sealed class Cached { internal string Fingerprint; internal List<IntVec3> Cells; }
        static readonly Dictionary<Map, Cached> cache = new Dictionary<Map, Cached>();
        static FieldInfo cells, distributors, connected, radius;
        internal static void Apply(Harmony harmony)
        {
            var manager = AccessTools.TypeByName("ASEL.ASELComponent") ?? throw new TypeLoadException("ASEL.ASELComponent");
            cells = AccessTools.Field(manager, "DistributorCells") ?? throw new MissingFieldException(manager.FullName, "DistributorCells");
            distributors = AccessTools.Field(manager, "Distributors") ?? throw new MissingFieldException(manager.FullName, "Distributors");
            var distributor = AccessTools.TypeByName("ASEL.CompLightDistributor") ?? throw new TypeLoadException("ASEL.CompLightDistributor");
            connected = AccessTools.Field(distributor, "ConnectedWithTower") ?? throw new MissingFieldException(distributor.FullName, "ConnectedWithTower");
            var props = AccessTools.TypeByName("ASEL.CompProperties_LightDistributor") ?? throw new TypeLoadException("ASEL.CompProperties_LightDistributor");
            radius = AccessTools.Field(props, "distributeRadius") ?? throw new MissingFieldException(props.FullName, "distributeRadius");
            var teleport = AccessTools.TypeByName("ASEL.CompTeleport") ?? throw new TypeLoadException("ASEL.CompTeleport");
            harmony.Patch(AccessTools.DeclaredMethod(teleport, "IsValidTeleportLocation", new[] { typeof(IntVec3) }) ?? throw new MissingMethodException(teleport.FullName, "IsValidTeleportLocation"),
                transpiler: new HarmonyMethod(typeof(MonolynCoverage), nameof(ReplaceCoverage)));
            Log.Message("[MonolynCoverage] resolved=teleport owner-map coverage validation; missing=0");
        }
        static IEnumerable<CodeInstruction> ReplaceCoverage(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode != OpCodes.Ldsfld || !Equals(instruction.operand, cells)) { yield return instruction; continue; }
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels); load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MonolynCoverage), nameof(ForOwner)));
                count++;
            }
            if (count != 1) throw new InvalidOperationException("Teleport expected one local coverage read, found " + count);
        }
        static List<IntVec3> ForOwner(ThingComp owner)
        {
            if (!MP.IsInMultiplayer) return (List<IntVec3>)cells.GetValue(null);
            var map = owner.parent.Map;
            if (map == null) return new List<IntVec3>();
            foreach (var stale in cache.Keys.Where(m => !Find.Maps.Contains(m)).ToList()) cache.Remove(stale);
            var live = ((IEnumerable)distributors.GetValue(null)).Cast<ThingComp>()
                .Where(c => c?.parent != null && c.parent.Spawned && c.parent.Map == map && (bool)connected.GetValue(c))
                .OrderBy(c => c.parent.thingIDNumber).ToList();
            var counts = live.Select(c => GenRadial.NumCellsInRadius((float)radius.GetValue(c.props))).ToArray();
            // Every input to the native radial union is included. UI timing,
            // clock differences, and hash insertion order cannot stale it.
            string fingerprint = string.Join(";", live.Select((c, i) => c.parent.thingIDNumber + ":" + c.parent.Position.x + ":" + c.parent.Position.z + ":" + counts[i]));
            if (cache.TryGetValue(map, out var prior) && prior.Fingerprint == fingerprint) return prior.Cells;
            var result = new List<IntVec3>();
            for (int i = 0; i < live.Count; i++)
                for (int j = 0; j < counts[i]; j++) result.Add(live[i].parent.Position + GenRadial.RadialPattern[j]);
            cache[map] = new Cached { Fingerprint = fingerprint, Cells = result };
            return result;
        }
    }
}
