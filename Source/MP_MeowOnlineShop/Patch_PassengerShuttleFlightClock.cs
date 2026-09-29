using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    // Only the official Odyssey passenger shuttle. Other world transports retain
    // their native speed rules. The host supplies one flight plan through the
    // global command queue; no peer may arrive from its local float accumulator.
    internal static class Patch_PassengerShuttleFlightClock
    {
        private sealed class Flight
        {
            internal int Duration, Elapsed;
            internal float InitialProgress;
            internal float RetryAt; // Local request throttling, never simulation state.
        }

        private static readonly ConditionalWeakTable<TravellingTransporters, Flight> Flights =
            new ConditionalWeakTable<TravellingTransporters, Flight>();
        private static readonly FieldInfo Progress = AccessTools.Field(typeof(TravellingTransporters), "traveledPct");
        private static readonly FieldInfo Origin = AccessTools.Field(typeof(TravellingTransporters), "initialTile");
        private static readonly MethodInfo Speed = AccessTools.PropertyGetter(typeof(TravellingTransporters), "TraveledPctStepPerTick");
        private static ISyncMethod plan;
        internal static bool Active;
        private static Flight State(TravellingTransporters ship) => Flights.GetValue(ship, _ => new Flight());
        private static bool Applies(TravellingTransporters ship) => ship?.def?.defName == "PassengerShuttle";

        internal static void Apply(Harmony harmony)
        {
            var tick = AccessTools.DeclaredMethod(typeof(TravellingTransporters), "TickInterval", new[] { typeof(int) });
            var expose = AccessTools.DeclaredMethod(typeof(TravellingTransporters), nameof(TravellingTransporters.ExposeData));
            if (tick == null || expose == null || Speed == null || Progress == null || Origin == null)
                throw new InvalidOperationException("REQUIRED_TARGET_FAILED passenger shuttle flight clock");
            plan = MP.RegisterSyncMethod(typeof(Patch_PassengerShuttleFlightClock), nameof(SetFlightPlan))
                .SetContext(SyncContext.None).SetHostOnly();
            harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(Patch_PassengerShuttleFlightClock), nameof(ReplaceProgress)));
            harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_PassengerShuttleFlightClock), nameof(Expose)));
            Active = true;
            Log.Message("[MP-MeowOnlineShop] Passenger shuttle flight clock active: host-synchronized duration, integer world-tick progress, serialized rejoin state.");
        }

        // Called only by GameComponentUpdate, outside deterministic ticking.
        // Retries are idempotent and do not change a previously accepted plan.
        internal static void PublishPlans()
        {
            if (!Active || !MP.IsInMultiplayer || !MP.IsHosting || !MP.InInterface || Find.WorldObjects == null) return;
            foreach (var ship in Find.WorldObjects.TravellingTransporters)
            {
                if (!Applies(ship) || ship.Destroyed) continue;
                var state = State(ship);
                if (state.Duration > 0 || Time.realtimeSinceStartup < state.RetryAt) continue;
                state.RetryAt = Time.realtimeSinceStartup + 2f;
                float progress = (float)Progress.GetValue(ship);
                float rate;
                Rand.PushState();
                try { rate = (float)Speed.Invoke(ship, null); }
                finally { Rand.PopState(); }
                if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0 ||
                    float.IsNaN(progress) || progress < 0 || progress > 1)
                {
                    Log.ErrorOnce("[MP-MeowOnlineShop] Invalid passenger shuttle flight rate; refusing unsynchronized arrival.", 0x51F117);
                    continue;
                }
                double remaining = Math.Ceiling((1.0 - progress) / rate);
                if (remaining > int.MaxValue) continue;
                plan.DoSync(null, ship.ID, (PlanetTile)Origin.GetValue(ship), ship.destinationTile,
                    Math.Max(1, (int)remaining), progress);
            }
        }

        private static void SetFlightPlan(int id, PlanetTile origin, PlanetTile destination, int duration, float progress)
        {
            if (duration <= 0 || float.IsNaN(progress) || progress < 0 || progress > 1) return;
            foreach (var ship in Find.WorldObjects.TravellingTransporters)
            {
                if (ship.ID != id || !Applies(ship) || ship.Destroyed || ship.destinationTile != destination ||
                    (PlanetTile)Origin.GetValue(ship) != origin) continue;
                var state = State(ship);
                if (state.Duration > 0) return;
                state.Duration = duration;
                state.Elapsed = 0;
                state.InitialProgress = progress;
                Progress.SetValue(ship, progress);
                return;
            }
        }

        private static float Advance(float previous, float nativeIncrement, TravellingTransporters ship, int delta)
        {
            if (!MP.IsInMultiplayer || !Applies(ship))
            {
                // A flight continued in single player must obtain a new plan if
                // this save is hosted again later.
                if (Applies(ship)) Flights.Remove(ship);
                return previous + nativeIncrement;
            }
            var state = State(ship);
            if (state.Duration <= 0) return previous; // Wait for the shared command on BOTH peers.
            state.Elapsed = (int)Math.Min(state.Duration, (long)state.Elapsed + Math.Max(0, delta));
            if (state.Elapsed == state.Duration) return 1f;
            // Float rounding must never trigger the native >=1 arrival early.
            return Math.Min(0.99999994f, state.InitialProgress +
                (1f - state.InitialProgress) * ((float)state.Elapsed / state.Duration));
        }

        private static void Expose(TravellingTransporters __instance)
        {
            if (!Applies(__instance)) return;
            var state = State(__instance);
            Scribe_Values.Look(ref state.Duration, "mpPassengerFlightDuration");
            Scribe_Values.Look(ref state.Elapsed, "mpPassengerFlightElapsed");
            Scribe_Values.Look(ref state.InitialProgress, "mpPassengerFlightInitialProgress");
        }

        private static IEnumerable<CodeInstruction> ReplaceProgress(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int i = 0; i + 4 < codes.Count; i++)
            {
                if (!codes[i].Calls(Speed) || codes[i + 1].opcode != OpCodes.Ldarg_1 ||
                    codes[i + 2].opcode != OpCodes.Conv_R4 || codes[i + 3].opcode != OpCodes.Mul ||
                    codes[i + 4].opcode != OpCodes.Add) continue;
                var add = codes[i + 4];
                var loadShip = new CodeInstruction(OpCodes.Ldarg_0);
                loadShip.labels.AddRange(add.labels); add.labels.Clear();
                loadShip.blocks.AddRange(add.blocks); add.blocks.Clear();
                add.opcode = OpCodes.Call;
                add.operand = AccessTools.Method(typeof(Patch_PassengerShuttleFlightClock), nameof(Advance));
                codes.InsertRange(i + 4, new[] { loadShip, new CodeInstruction(OpCodes.Ldarg_1) });
                matches++;
                i += 6;
            }
            if (matches != 1) throw new InvalidOperationException("REQUIRED_TARGET_FAILED passenger flight accumulator matches=" + matches);
            return codes;
        }
    }

    public sealed class PassengerShuttleFlightPlans : GameComponent
    {
        public PassengerShuttleFlightPlans(Game game) { }
        public override void GameComponentUpdate() => Patch_PassengerShuttleFlightClock.PublishPlans();
    }
}
