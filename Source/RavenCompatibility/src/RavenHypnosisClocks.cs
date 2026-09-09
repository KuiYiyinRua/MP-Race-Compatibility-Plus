using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Patches;
using RimWorld;
using Verse;
using Runtime = Multiplayer.Client.Multiplayer;

namespace MP_MeowOnlineShop
{
    internal static class RavenHypnosisClocks
    {
        private static Type worldType;
        private static FieldInfo entries, selected;
        internal static void Apply(Harmony harmony)
        {
            worldType = AccessTools.TypeByName("RavenRace.Features.Hypnosis.WorldComponent_Hypnosis");
            var utility = AccessTools.TypeByName("RavenRace.Features.Hypnosis.Commands.HypnosisCommandUtility");
            var dialog = AccessTools.TypeByName("RavenRace.Features.Hypnosis.Dialog_HypnosisControl");
            entries = AccessTools.Field(worldType, "commandCooldowns");
            selected = AccessTools.Field(dialog, "selectedSlave");
            if (entries == null || selected == null) throw new MissingFieldException("Raven hypnosis clocks");
            foreach (var method in new[] { AccessTools.DeclaredMethod(worldType, "GetCommandCooldown"),
                AccessTools.DeclaredMethod(utility, "AddCooldown"), AccessTools.DeclaredMethod(dialog, "DrawActionPanel") })
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(RavenHypnosisClocks), nameof(ReplaceClock)));
            harmony.Patch(AccessTools.Method(typeof(TimestampFixer), nameof(TimestampFixer.FixPawn)),
                prefix: new HarmonyMethod(typeof(RavenHypnosisClocks), nameof(Transfer)));
        }

        internal static int PawnTick(Pawn pawn) => MP.IsInMultiplayer && Runtime.AsyncWorldTime != null
            ? pawn?.MapHeld?.AsyncTime().mapTicks ?? Runtime.AsyncWorldTime.worldTicks : Find.TickManager.TicksGame;
        internal static int PawnIdTick(int id)
        {
            if (!MP.IsInMultiplayer) return Find.TickManager.TicksGame;
            return PawnTick(PawnsFinder.AllMapsAndWorld_Alive.FirstOrDefault(p => p.thingIDNumber == id));
        }
        private static int ForPawn(TickManager manager, Pawn pawn) => PawnTick(pawn);
        private static int ForId(TickManager manager, int id) => PawnIdTick(id);
        private static int ForDialog(TickManager manager, object dialog) => PawnTick((Pawn)selected.GetValue(dialog));

        private static IEnumerable<CodeInstruction> ReplaceClock(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int count = 0;
            var ticks = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TicksGame));
            bool id = __originalMethod.Name == "GetCommandCooldown";
            string replacement = id ? nameof(ForId) : __originalMethod.Name == "AddCooldown" ? nameof(ForPawn) : nameof(ForDialog);
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(ticks))
                {
                    var argument = new CodeInstruction(id ? OpCodes.Ldarg_1 : OpCodes.Ldarg_0);
                    argument.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    argument.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return argument;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RavenHypnosisClocks), replacement);
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Raven hypnosis clock sites: " + __originalMethod + " count=" + count);
        }

        // These deadlines live in the world component and are not visited by the
        // pawn's auxiliary ExposeData traversal used by Multiplayer's fixer.
        private static void Transfer(Pawn p, Map oldMap, Map newMap)
        {
            if (!MP.IsInMultiplayer || Runtime.AsyncWorldTime == null || p == null) return;
            var world = AccessTools.Property(worldType, "Instance").GetValue(null);
            var all = (Dictionary<int, Dictionary<string, int>>)entries.GetValue(world);
            if (!all.TryGetValue(p.thingIDNumber, out var commands)) return;
            int offset = (newMap?.AsyncTime().mapTicks ?? Runtime.AsyncWorldTime.worldTicks)
                - (oldMap?.AsyncTime().mapTicks ?? Runtime.AsyncWorldTime.worldTicks);
            foreach (var key in commands.Keys.ToArray()) commands[key] += offset;
        }
    }
}
