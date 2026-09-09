using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Patches;
using Verse;
using Runtime = Multiplayer.Client.Multiplayer;

namespace MP_MeowOnlineShop
{
    internal static class RavenClocks
    {
        private sealed class ConcealmentState { public bool worldClock; }
        private static readonly ConditionalWeakTable<Thing, ConcealmentState> concealmentStates = new ConditionalWeakTable<Thing, ConcealmentState>();
        private static FieldInfo apparelDeadline, concealmentAttack, eggDeadline;
        internal static void Apply(Harmony harmony)
        {
            var egg = AccessTools.TypeByName("RavenRace.Race.Comps.CompEggLayerUnfertilized");
            eggDeadline = AccessTools.Field(egg, "nextEggLayTick") ?? throw new MissingFieldException("Raven unfertilized egg deadline");
            harmony.Patch(AccessTools.DeclaredMethod(egg, "PostExposeData"),
                prefix: new HarmonyMethod(typeof(RavenClocks), nameof(TransferEggDeadline)));
            var apparel = AccessTools.TypeByName("RavenRace.Features.UniqueEquipment.Sandevistan.CompApparelTrail");
            apparelDeadline = AccessTools.Field(apparel, "activeUntilTick");
            if (apparelDeadline == null) throw new MissingFieldException("Raven apparel deadline");
            foreach (var name in new[] { "get_IsActive", "get_RemainingTicks", "Activate", "GetVisualIntensity", "GetBurstIntensity" })
                harmony.Patch(AccessTools.DeclaredMethod(apparel, name),
                    transpiler: new HarmonyMethod(typeof(RavenClocks), nameof(ApparelClock)));
            harmony.Patch(AccessTools.DeclaredMethod(apparel, "PostExposeData"),
                prefix: new HarmonyMethod(typeof(RavenClocks), nameof(TransferApparelDeadline)));
            var concealment = AccessTools.TypeByName("RavenRace.Features.DefenseSystem.Concealment.Building_Concealment");
            concealmentAttack = AccessTools.Field(concealment, "lastAttackTick") ?? throw new MissingFieldException("Raven concealment reveal clock");
            harmony.Patch(AccessTools.DeclaredMethod(concealment, "ExposeData"), postfix: new HarmonyMethod(typeof(RavenClocks), nameof(ExposeConcealmentClock)));
            harmony.Patch(AccessTools.DeclaredMethod(concealment, "DeSpawn"), prefix: new HarmonyMethod(typeof(RavenClocks), nameof(BeforeConcealmentDespawn)), postfix: new HarmonyMethod(typeof(RavenClocks), nameof(AfterConcealmentDespawn)));
            harmony.Patch(AccessTools.DeclaredMethod(concealment, "SpawnSetup"), postfix: new HarmonyMethod(typeof(RavenClocks), nameof(AfterConcealmentSpawn)));
            harmony.Patch(AccessTools.DeclaredPropertyGetter(concealment, "IsRevealed"), prefix: new HarmonyMethod(typeof(RavenClocks), nameof(ConcealmentRevealed)));
            var hub = AccessTools.TypeByName("RavenRace.Features.CentralHub.GameComponent_RavenCentralHubSystem");
            harmony.Patch(AccessTools.DeclaredPropertyGetter(hub, "CurrentTick"),
                prefix: new HarmonyMethod(typeof(RavenClocks), nameof(HubClock)));
            var abilities = AccessTools.TypeByName("RavenRace.Features.CentralHub.Abilities.GameComponent_RavenCentralAbilitySystem");
            harmony.Patch(AccessTools.DeclaredMethod(abilities, "TryStartAbility"),
                transpiler: new HarmonyMethod(typeof(RavenClocks), nameof(AbilityClock)));
            var fusang = AccessTools.TypeByName("RavenRace.WorldComponent_Fusang");
            foreach (string name in new[] { "CanTradeNow", "GetTicksUntilTrade" })
                harmony.Patch(AccessTools.DeclaredMethod(fusang, name),
                    transpiler: new HarmonyMethod(typeof(RavenClocks), nameof(AbilityClock)));
        }

        private static void ExposeConcealmentClock(Thing __instance)
        {
            var state = concealmentStates.GetOrCreateValue(__instance);
            Scribe_Values.Look(ref state.worldClock, "mpRavenConcealmentWorldClock", false);
        }
        private static void BeforeConcealmentDespawn(Thing __instance, out int? __state)
        {
            __state = MP.IsInMultiplayer && __instance.Map != null ? (int?)__instance.Map.AsyncTime().mapTicks : null;
        }
        private static void AfterConcealmentDespawn(Thing __instance, int? __state)
        {
            if (!__state.HasValue || __instance.Spawned) return;
            concealmentAttack.SetValue(__instance, (int)concealmentAttack.GetValue(__instance) + Runtime.AsyncWorldTime.worldTicks - __state.Value);
            concealmentStates.GetOrCreateValue(__instance).worldClock = true;
        }
        private static void AfterConcealmentSpawn(Thing __instance)
        {
            var state = concealmentStates.GetOrCreateValue(__instance);
            if (!state.worldClock) return;
            if (MP.IsInMultiplayer)
                concealmentAttack.SetValue(__instance, (int)concealmentAttack.GetValue(__instance) + __instance.Map.AsyncTime().mapTicks - Runtime.AsyncWorldTime.worldTicks);
            state.worldClock = false;
        }
        private static bool ConcealmentRevealed(Thing __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || Runtime.AsyncWorldTime == null) return true;
            int now = concealmentStates.GetOrCreateValue(__instance).worldClock ? Runtime.AsyncWorldTime.worldTicks : (__instance.MapHeld?.AsyncTime().mapTicks ?? Runtime.AsyncWorldTime.worldTicks);
            __result = now < (int)concealmentAttack.GetValue(__instance) + 600;
            return false;
        }
        private static bool HubClock(ref int __result)
        {
            if (!MP.IsInMultiplayer || Runtime.AsyncWorldTime == null) return true;
            __result = Runtime.AsyncWorldTime.worldTicks;
            return false;
        }
        private static int WorldClock(TickManager manager) => MP.IsInMultiplayer && Runtime.AsyncWorldTime != null
            ? Runtime.AsyncWorldTime.worldTicks : manager.TicksGame;
        private static int PawnClock(TickManager manager, ThingComp comp)
        {
            if (!MP.IsInMultiplayer || Runtime.AsyncWorldTime == null) return manager.TicksGame;
            return comp.parent.MapHeld?.AsyncTime().mapTicks ?? Runtime.AsyncWorldTime.worldTicks;
        }
        private static void TransferApparelDeadline(ThingComp __instance)
        {
            if (!TimestampFixer.currentOffset.HasValue) return;
            int deadline = (int)apparelDeadline.GetValue(__instance);
            if (deadline >= 0) apparelDeadline.SetValue(__instance, deadline + TimestampFixer.currentOffset.Value);
        }
        private static void TransferEggDeadline(ThingComp __instance)
        {
            // MP visits pawn comps through an auxiliary save when changing the
            // pawn's map/world clock basis. Preserve the native uninitialized -1.
            if (!TimestampFixer.currentOffset.HasValue) return;
            int deadline = (int)eggDeadline.GetValue(__instance);
            if (deadline != -1) eggDeadline.SetValue(__instance, deadline + TimestampFixer.currentOffset.Value);
        }
        private static IEnumerable<CodeInstruction> ApparelClock(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            var ticks = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TicksGame));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(ticks))
                {
                    var receiver = new CodeInstruction(OpCodes.Ldarg_0);
                    receiver.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    receiver.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return receiver;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RavenClocks), nameof(PawnClock));
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Raven apparel clock target count " + replaced);
        }
        private static IEnumerable<CodeInstruction> AbilityClock(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            var ticks = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TicksGame));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(ticks))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RavenClocks), nameof(WorldClock));
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Raven ability clock target count " + replaced);
        }
    }
}
