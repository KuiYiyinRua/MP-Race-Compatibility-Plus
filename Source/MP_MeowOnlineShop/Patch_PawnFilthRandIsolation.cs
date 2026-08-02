using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// TickList work can be interleaved in a different object order while
    /// Multiplayer async time is active. Pawn movement filth, plant long
    /// ticks, and incoming drop-pod dust all consume the map Rand stream; a
    /// different inter-object order then makes unrelated tick work advance
    /// that shared stream differently. Keep gameplay results and cosmetic
    /// effects, but give each verified object/tick boundary an independent
    /// deterministic stream.
    /// </summary>
    internal static class Patch_PawnFilthRandIsolation
    {
        private const int PawnFilthSeedSalt = 0x46494C54; // "FILT"
        private const int PlantTickLongSeedSalt = 0x504C4E54; // "PLNT"
        private const int DropPodImpactSeedSalt = 0x504F4449; // "PODI"

        internal static void Apply(Harmony harmony)
        {
            var pawnFilthTarget = AccessTools.Method(
                typeof(Pawn_FilthTracker),
                nameof(Pawn_FilthTracker.Notify_EnteredNewCell));
            var plantTickLongTarget = AccessTools.Method(typeof(Plant), "TickLong");
            var dropPodImpactTarget = AccessTools.Method(typeof(DropPodIncoming), "Impact");
            var prefix = AccessTools.Method(
                typeof(Patch_PawnFilthRandIsolation),
                nameof(Prefix));
            var finalizer = AccessTools.Method(
                typeof(Patch_PawnFilthRandIsolation),
                nameof(Finalizer));
            var plantPrefix = AccessTools.Method(
                typeof(Patch_PawnFilthRandIsolation),
                nameof(PlantTickLongPrefix));
            var dropPodPrefix = AccessTools.Method(
                typeof(Patch_PawnFilthRandIsolation),
                nameof(DropPodImpactPrefix));

            if (pawnFilthTarget == null || prefix == null || finalizer == null ||
                plantPrefix == null || dropPodPrefix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Pawn filth Rand isolation target was not found.");
                return;
            }

            harmony.Patch(
                pawnFilthTarget,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                });

            int additionalTargets = 0;
            if (plantTickLongTarget != null)
            {
                PatchScopedThingMethod(harmony, plantTickLongTarget, plantPrefix, finalizer);
                additionalTargets++;
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Plant TickLong Rand isolation target was not found.");
            }

            if (dropPodImpactTarget != null)
            {
                PatchScopedThingMethod(harmony, dropPodImpactTarget, dropPodPrefix, finalizer);
                additionalTargets++;
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DropPodIncoming Impact Rand isolation target was not found.");
            }

            Log.Message(
                "[MP-MeowOnlineShop] TickList Rand isolation active: pawn filth plus " +
                additionalTargets + " verified object boundaries.");
        }

        private static void PatchScopedThingMethod(
            Harmony harmony, System.Reflection.MethodInfo target,
            System.Reflection.MethodInfo prefix, System.Reflection.MethodInfo finalizer)
        {
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                });
        }

        private static void Prefix(Pawn ___pawn, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || ___pawn == null || ___pawn.Map == null)
                return;

            int seed = Gen.HashCombineInt(PawnFilthSeedSalt, ___pawn.thingIDNumber);
            seed = Gen.HashCombineInt(seed, ___pawn.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, ___pawn.Position.x);
            seed = Gen.HashCombineInt(seed, ___pawn.Position.z);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            __state = true;
        }

        private static void PlantTickLongPrefix(Plant __instance, ref bool __state)
        {
            BeginThingScope(__instance, PlantTickLongSeedSalt, ref __state);
        }

        private static void DropPodImpactPrefix(DropPodIncoming __instance, ref bool __state)
        {
            BeginThingScope(__instance, DropPodImpactSeedSalt, ref __state);
        }

        private static void BeginThingScope(Thing thing, int salt, ref bool state)
        {
            state = false;
            if (!MP.IsInMultiplayer || thing == null || thing.Map == null)
                return;

            int seed = Gen.HashCombineInt(salt, thing.thingIDNumber);
            seed = Gen.HashCombineInt(seed, thing.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, thing.Position.x);
            seed = Gen.HashCombineInt(seed, thing.Position.z);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            state = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
