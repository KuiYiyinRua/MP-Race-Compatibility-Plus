using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Local UI may request completion, but must never decide simulation tick outcomes.
    public sealed class NivarianSelfBuilding : GameComponent
    {
        static Type componentType;
        static MethodInfo finish;
        static ISyncMethod complete;
        readonly Dictionary<int, float> pending = new Dictionary<int, float>();
        float nextScan;
        public NivarianSelfBuilding(Game game) { }

        internal static void Apply(Harmony harmony)
        {
            componentType = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompSelfBuilding")
                ?? throw new TypeLoadException("Nivarian self-building component");
            finish = AccessTools.DeclaredMethod(componentType, "FinishBuilding", Type.EmptyTypes)
                ?? throw new MissingMethodException(componentType.FullName, "FinishBuilding");
            complete = MP.RegisterSyncMethod(typeof(NivarianSelfBuilding), nameof(Complete),
                new[] { new SyncType(typeof(Map)) { contextMap = true }, new SyncType(typeof(int[])) }).SetDebugOnly();
            harmony.Patch(AccessTools.DeclaredMethod(componentType, "CompTick", Type.EmptyTypes)
                ?? throw new MissingMethodException(componentType.FullName, "CompTick"),
                transpiler: new HarmonyMethod(typeof(NivarianSelfBuilding), nameof(ReplaceLocalCondition)));
            Log.Message("[TaleNivarianCompat] Self-building developer instant completion uses synchronized map/object commands.");
        }

        static bool LocalGodMode() => !MP.IsInMultiplayer && DebugSettings.godMode;
        static IEnumerable<CodeInstruction> ReplaceLocalCondition(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            var field = AccessTools.Field(typeof(DebugSettings), nameof(DebugSettings.godMode));
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, field))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianSelfBuilding), nameof(LocalGodMode));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Self-building god-mode read changed: " + count);
        }

        public override void GameComponentUpdate()
        {
            if (complete == null || !Bootstrap.Ready || !MP.IsInMultiplayer || !MP.InInterface
                || !DebugSettings.godMode || Find.DesignatorManager?.SelectedDesignator == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextScan) return;
            nextScan = now + 0.25f;
            foreach (var id in pending.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) pending.Remove(id);
            // The native condition applies to ticking cores on all loaded maps.
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
            {
                var ids = map.listerThings.AllThings.OfType<ThingWithComps>()
                    .Where(t => t.Spawned && !t.Destroyed && !pending.ContainsKey(t.thingIDNumber)
                        && t.AllComps.Any(c => componentType.IsInstanceOfType(c)))
                    .Select(t => t.thingIDNumber).OrderBy(id => id).ToArray();
                if (ids.Length == 0 || !complete.DoSync(null, map, ids)) continue;
                foreach (int id in ids) pending[id] = now + 5f;
            }
        }

        static void Complete(Map map, int[] ids)
        {
            if (map == null || ids == null) return;
            // Resolve at execution time: another player or ordinary construction may
            // already have finished the same core while this request was in transit.
            foreach (int id in ids.Distinct().OrderBy(value => value))
            {
                var thing = map.listerThings.AllThings.OfType<ThingWithComps>()
                    .FirstOrDefault(t => t.thingIDNumber == id && t.Spawned && !t.Destroyed);
                var comp = thing?.AllComps.FirstOrDefault(c => componentType.IsInstanceOfType(c));
                if (comp != null) finish.Invoke(comp, null);
            }
        }
    }
}
