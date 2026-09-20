using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianProgressOwnership
    {
        static Action<Map, Faction, bool> push;
        static Func<Map, Faction> pop;
        static MethodInfo worldGetter;
        static FieldInfo factionData, spectatorFaction;
        static readonly FieldInfo[] mapManagers = new[] { "designationManager", "areaManager", "zoneManager", "planManager",
            "haulDestinationManager", "listerHaulables", "resourceCounter", "listerFilthInHomeArea", "listerMergeables" }
            .Select(n => AccessTools.Field(typeof(Map), n) ?? throw new MissingFieldException(typeof(Map).FullName, n)).ToArray();

        internal static void Apply(Harmony harmony)
        {
            var context = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions")
                ?? throw new TypeLoadException("MP faction extensions");
            push = (Action<Map, Faction, bool>)Delegate.CreateDelegate(typeof(Action<Map, Faction, bool>),
                AccessTools.DeclaredMethod(context, "PushFaction", new[] { typeof(Map), typeof(Faction), typeof(bool) }));
            pop = (Func<Map, Faction>)Delegate.CreateDelegate(typeof(Func<Map, Faction>),
                AccessTools.DeclaredMethod(context, "PopFaction", new[] { typeof(Map) }));
            var manager = AccessTools.TypeByName("Nivarian_Race.Code.Progress.ProgressManager")
                ?? throw new TypeLoadException("Nivarian progress manager");
            var count = AccessTools.DeclaredMethod(manager, "CountPlayerOwned", new[] { typeof(ThingDef) })
                ?? throw new MissingMethodException(manager.FullName, "CountPlayerOwned");
            harmony.Patch(count, prefix: new HarmonyMethod(typeof(NivarianProgressOwnership), nameof(Count)));
            worldGetter = AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "WorldComp")
                ?? throw new MissingMemberException("MP WorldComp");
            var worldType = worldGetter.ReturnType;
            factionData = AccessTools.Field(worldType, "factionData") ?? throw new MissingFieldException(worldType.FullName, "factionData");
            spectatorFaction = AccessTools.Field(worldType, "spectatorFaction") ?? throw new MissingFieldException(worldType.FullName, "spectatorFaction");
            var scan = AccessTools.DeclaredMethod(manager, "RunProgressScan", Type.EmptyTypes)
                ?? throw new MissingMethodException(manager.FullName, "RunProgressScan");
            harmony.Patch(scan, transpiler: new HarmonyMethod(typeof(NivarianProgressOwnership), nameof(ResearchReads)));
            Log.Message("[TaleNivarianCompat] progress inventory and research scans read registered player faction state, excluding spectator research.");
        }

        static IEnumerable<CodeInstruction> ResearchReads(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(ResearchProjectDef), nameof(ResearchProjectDef.IsFinished));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianProgressOwnership), nameof(ResearchFinished));
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Progress research reads changed: " + matches);
        }

        internal static bool ResearchFinished(ResearchProjectDef project)
        {
            if (project == null) return false;
            if (!MP.IsInMultiplayer) return project.IsFinished;
            var world = worldGetter.Invoke(null, null);
            var data = (IDictionary)factionData.GetValue(world);
            var spectator = (Faction)spectatorFaction.GetValue(world);
            // Completion nodes are shared, while each participating faction retains
            // its own ResearchManager. Include factions without a currently loaded map.
            foreach (int id in data.Keys.Cast<int>().OrderBy(id => id))
            {
                var faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == id);
                if (faction == spectator || faction?.def?.isPlayer != true) continue;
                push(null, faction, true);
                try { if (project.IsFinished) return true; }
                finally { pop(null); }
            }
            return false;
        }

        static bool Count(ThingDef def, ref int __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = 0;
            if (def == null) return false;
            // ProgressManager stores one shared completion set. Its world tick may
            // run as spectator, whose home/stockpile managers contain no player stock.
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
            {
                var owner = map.ParentFaction;
                if (owner?.def?.isPlayer != true) continue;
                var savedManagers = mapManagers.Select(f => f.GetValue(map)).ToArray();
                push(map, owner, true);
                try
                {
                    if (map.IsPlayerHome) __result += map.resourceCounter.GetCount(def);
                }
                finally
                {
                    // World commands can enter with issuer world data and spectator
                    // map managers. PopFaction alone restores the issuer's managers.
                    try { pop(map); }
                    finally { for (int i = 0; i < mapManagers.Length; i++) mapManagers[i].SetValue(map, savedManagers[i]); }
                }
            }
            return false;
        }
    }
}
