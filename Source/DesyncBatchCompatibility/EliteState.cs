using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class EliteState
    {
        internal static FieldInfo tickingMap, executingMap;
        internal static void Apply(Harmony harmony)
        {
            var asyncTime = Bootstrap.Type("Multiplayer.Client.AsyncTimeComp");
            tickingMap = Bootstrap.Field(asyncTime, "tickingMap", typeof(Map));
            executingMap = Bootstrap.Field(asyncTime, "executingCmdMap", typeof(Map));
            var type = Bootstrap.Type("EliteRaid.CR_Powerup");
            foreach (var name in new[] { "spawnTimes", "_currentTickCount", "woundMergeTickCounter" })
                Bootstrap.Field(type, name, typeof(int));
            foreach (var name in new[] { "PostMake", "Tick" })
                harmony.Patch(Bootstrap.Method(type, name), transpiler: new HarmonyMethod(typeof(EliteState), nameof(Context)));
            harmony.Patch(Bootstrap.Method(type, "ExposeData"), postfix: new HarmonyMethod(typeof(EliteState), nameof(Expose)));
        }

        internal static IEnumerable<CodeInstruction> Context(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var local = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer));
            var isPlayer = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.IsPlayer));
            int factionReads = 0, playerReads = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(local))
                {
                    // Branches targeting the getter must also execute the newly inserted argument.
                    var argument = new CodeInstruction(OpCodes.Ldarg_0);
                    argument.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    argument.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return argument;
                    instruction.operand = AccessTools.Method(typeof(EliteState), nameof(TargetFaction));
                    instruction.opcode = OpCodes.Call;
                    factionReads++;
                }
                else if (instruction.Calls(isPlayer))
                {
                    instruction.operand = AccessTools.Method(typeof(EliteState), nameof(IsAnyPlayer));
                    instruction.opcode = OpCodes.Call;
                    playerReads++;
                }
                yield return instruction;
            }
            int expectedPlayer = __originalMethod.Name == "Tick" ? 1 : 0;
            if (factionReads != 1 || playerReads != expectedPlayer)
                throw new InvalidOperationException("Elite faction reads changed: " + __originalMethod + " " + factionReads + "/" + playerReads);
        }

        internal static Faction TargetFaction(Hediff hediff)
        {
            if (!MP.IsInMultiplayer) return Faction.OfPlayer;
            var pawn = hediff.pawn;
            var owner = pawn?.MapHeld?.ParentFaction;
            if (owner?.def?.isPlayer == true) return owner;
            if (pawn?.Faction?.def?.isPlayer == true) return pawn.Faction;
            // PostMake may run before the raid pawn is spawned. MP's explicit simulation
            // map (including world-triggered incidents) is shared; Find.CurrentMap is not.
            var eventMap = (Map)executingMap?.GetValue(null) ?? (Map)tickingMap?.GetValue(null);
            if (eventMap?.ParentFaction?.def?.isPlayer == true) return eventMap.ParentFaction;
            // World/NPC-map cases have no player map owner. A stable faction is preferable to
            // spectator/local perspective; retain the first shared player faction by load ID.
            Faction first = null;
            foreach (var faction in Find.FactionManager.AllFactionsListForReading)
                if (faction.def.isPlayer && (first == null || faction.loadID < first.loadID)) first = faction;
            return first;
        }
        internal static bool IsAnyPlayer(Faction faction) => MP.IsInMultiplayer ? faction.def.isPlayer : faction.IsPlayer;

        internal static void Expose(ref int ___spawnTimes, ref int ____currentTickCount, ref int ___woundMergeTickCounter)
        {
            // Save in SP too so switching between SP/MP does not discard an existing schedule.
            // Defaults match the installed constructors for old saves.
            Scribe_Values.Look(ref ___spawnTimes, "meowEliteSpawnTimes", 0);
            Scribe_Values.Look(ref ____currentTickCount, "meowEliteCurrentTickCount", 0);
            Scribe_Values.Look(ref ___woundMergeTickCounter, "meowEliteWoundMergeTickCounter", 0);
            // m_GameTick deliberately remains transient: its constructor sentinel forces
            // RestoreData after loading. Saving it could skip reconstruction of stat data.
        }
    }
}
