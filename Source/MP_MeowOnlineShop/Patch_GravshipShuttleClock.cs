using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    // A shuttle carried as gravship cargo keeps its existing cooldown, unlike
    // a shuttle's own flight, which resets the timestamp in Notify_Arrived.
    internal static class Patch_GravshipShuttleClock
    {
        private sealed class State
        {
            internal bool InTransit;
            internal bool TimestampValid;
            internal int WorldDue = -1;
        }
        private sealed class Capture { internal int WorldDue; }
        private static readonly ConditionalWeakTable<CompLaunchable, State> States = new ConditionalWeakTable<CompLaunchable, State>();
        private static State For(CompLaunchable comp) => States.GetValue(comp, key => new State());

        internal static void Apply(Harmony harmony)
        {
            var despawn = AccessTools.DeclaredMethod(typeof(Building), nameof(Thing.DeSpawn), new[] { typeof(DestroyMode) });
            var spawn = AccessTools.Method(typeof(Building_PassengerShuttle), nameof(Thing.SpawnSetup));
            var expose = AccessTools.Method(typeof(CompLaunchable), nameof(ThingComp.PostExposeData));
            var arrived = AccessTools.Method(typeof(CompLaunchable), nameof(CompLaunchable.Notify_Arrived));
            if (despawn == null || spawn == null || expose == null || arrived == null)
                throw new InvalidOperationException("REQUIRED_TARGET_FAILED carried shuttle clocks");
            harmony.Patch(despawn, prefix: new HarmonyMethod(typeof(Patch_GravshipShuttleClock), nameof(DespawnPrefix)),
                finalizer: new HarmonyMethod(typeof(Patch_GravshipShuttleClock), nameof(DespawnFinalizer)));
            harmony.Patch(spawn, postfix: new HarmonyMethod(typeof(Patch_GravshipShuttleClock), nameof(SpawnPostfix)));
            harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_GravshipShuttleClock), nameof(ExposePostfix)));
            harmony.Patch(arrived, postfix: new HarmonyMethod(typeof(Patch_GravshipShuttleClock), nameof(ArrivedPostfix)));
            foreach (string method in new[] { nameof(CompLaunchable.CanLaunch), nameof(CompLaunchable.CompTick), nameof(CompLaunchable.CompInspectStringExtra) })
            {
                var target = AccessTools.Method(typeof(CompLaunchable), method);
                if (target == null) throw new InvalidOperationException("REQUIRED_TARGET_FAILED launchable " + method);
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Patch_GravshipShuttleClock), nameof(AllowRebasedTimestamp)));
            }
            Log.Message("[MP-MeowOnlineShop] Carried shuttle clock conversion active, including serialized negative map timestamps.");
        }

        private static void DespawnPrefix(Thing __instance, out Capture __state)
        {
            __state = null;
            if (!GravshipUtility.generatingGravship || !(__instance is Building_PassengerShuttle shuttle) ||
                !Patch_GravshipLandingMp.TryReadClocks(shuttle.Map, out int mapTick, out int worldTick)) return;
            var comp = shuttle.TryGetComp<CompLaunchable>();
            if (comp == null || !HasTimestamp(comp)) return;
            __state = new Capture { WorldDue = worldTick + comp.lastLaunchTick + comp.Props.cooldownTicks - mapTick };
        }

        private static Exception DespawnFinalizer(Thing __instance, Capture __state, Exception __exception)
        {
            if (__exception == null && __state != null && !__instance.Spawned)
            {
                var comp = ((Building_PassengerShuttle)__instance).TryGetComp<CompLaunchable>();
                var state = For(comp);
                state.InTransit = true;
                state.WorldDue = __state.WorldDue;
                state.TimestampValid = true;
                comp.lastLaunchTick = state.WorldDue - comp.Props.cooldownTicks;
            }
            return __exception;
        }

        private static void SpawnPostfix(Building_PassengerShuttle __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad || !Patch_GravshipLandingMp.TryReadClocks(map, out int mapTick, out int worldTick)) return;
            var comp = __instance.TryGetComp<CompLaunchable>();
            var state = For(comp);
            if (!state.InTransit) return;
            int remaining = state.WorldDue - worldTick;
            state.InTransit = false;
            state.TimestampValid = remaining > 0;
            comp.lastLaunchTick = remaining > 0 ? mapTick + remaining - comp.Props.cooldownTicks : -1;
        }

        private static void ArrivedPostfix(CompLaunchable __instance)
        {
            if (!(__instance.parent is Building_PassengerShuttle)) return;
            var state = For(__instance);
            state.InTransit = state.TimestampValid = false;
            state.WorldDue = -1;
        }

        private static void ExposePostfix(CompLaunchable __instance)
        {
            if (!(__instance.parent is Building_PassengerShuttle)) return;
            var state = For(__instance);
            Scribe_Values.Look(ref state.InTransit, "mpGravShuttleInTransit");
            Scribe_Values.Look(ref state.TimestampValid, "mpGravShuttleTimestampValid");
            Scribe_Values.Look(ref state.WorldDue, "mpGravShuttleWorldDue", -1);
        }

        private static bool HasTimestamp(CompLaunchable comp) => comp.lastLaunchTick > 0 ||
            (MP.IsInMultiplayer && comp.parent is Building_PassengerShuttle && States.TryGetValue(comp, out State state) && state.TimestampValid);

        private static IEnumerable<CodeInstruction> AllowRebasedTimestamp(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var field = AccessTools.Field(typeof(CompLaunchable), nameof(CompLaunchable.lastLaunchTick));
            var helper = AccessTools.Method(typeof(Patch_GravshipShuttleClock), nameof(HasTimestamp));
            int matches = 0;
            for (int i = 0; i + 2 < codes.Count; i++)
            {
                if (codes[i].LoadsField(field) && codes[i + 1].LoadsConstant(0) &&
                    (codes[i + 2].opcode == OpCodes.Ble || codes[i + 2].opcode == OpCodes.Ble_S))
                {
                    // Keep vanilla arithmetic and branch semantics. A rebased
                    // negative timestamp is valid; an untouched -1 is not.
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = helper;
                    matches++;
                }
            }
            if (matches != 1) throw new InvalidOperationException("REQUIRED_TARGET_FAILED shuttle timestamp guard matches=" + matches);
            return codes;
        }
    }
}
