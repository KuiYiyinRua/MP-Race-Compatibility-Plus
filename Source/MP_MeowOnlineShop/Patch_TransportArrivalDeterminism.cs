using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-511 (2026-08-19, faction trade via transport pods/shuttle):
    /// the world-side arrival of a trade convoy runs
    /// `TransportersArrivalAction_Trade.Arrived` ->
    /// `TransportersArrivalAction_FormCaravan.Arrived`, which redistributes
    /// every carried item onto the forming caravan pawn:
    ///
    ///   CaravanMaker.MakeCaravan -> CaravanNameGenerator.GenerateCaravanName
    ///   CaravanInventoryUtility.GiveThing ->
    ///     FindPawnToMoveInventoryTo -> TryRandomElement x3
    ///   Messages.Message
    ///
    /// Those calls consume the LIVE world/static Rand stream on the world
    /// tick. Multiplayer does not cover this arrival distribution, and the
    /// existing `Patch_CaravanShuttleMp` candidate-sort is not enough: any
    /// peer-local difference in stack split, encumbrance cascade, or
    /// transporter/pawn order advances the live stream by a different number
    /// of draws, so the desync window reports "Wrong random state for the
    /// world". The first divergent trace is host `GenerateCaravanName`/arrival
    /// Message versus client `FindPawnToMoveInventoryTo -> RandomElement`
    /// inside the same `FormCaravan.Arrived`.
    ///
    /// Fix: in multiplayer run the whole `FormCaravan.Arrived` cargo
    /// distribution inside a DeterministicRandScope seeded from the shared
    /// game tick. The internal draws use the
    /// isolated seeded stream and are popped before returning, so the live
    /// synchronized Rand state is identical on every peer regardless of any
    /// internal ordering/stacking difference. Singleplayer is untouched;
    /// failures fail open.
    /// </summary>
    internal static class Patch_TransportArrivalDeterminism
    {
        private const int ArrivalSeedSalt = 0x43524156; // "CARA"
        private const int ArrivalWorldSeedOffset = 0x43524157; // "CARW"

        private static bool _applied;

        [ThreadStatic]
        private static Map _scopeMapForPop;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(TransportersArrivalAction_FormCaravan),
                    "Arrived");
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_TransportArrivalDeterminism),
                    nameof(ArrivedPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_TransportArrivalDeterminism),
                    nameof(ArrivedFinalizer));

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Transport arrival determinism " +
                        "target resolution failed; skipped.");
                    return;
                }

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

                Log.Message(
                    "[MP-MeowOnlineShop] Transport arrival determinism active: " +
                    "world/static Rand isolated during caravan cargo " +
                    "distribution at transport arrival.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Transport arrival determinism apply " +
                    "failed: " + e.Message);
            }
        }

        private static void ArrivedPrefix(ref int __state)
        {
            __state = 0;
            _scopeMapForPop = null;
            if (!MP.IsInMultiplayer)
                return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            int seed = Gen.HashCombineInt(ArrivalSeedSalt, tick);

            if (DeterministicRandScope.Begin(
                    null,
                    seed,
                    ArrivalWorldSeedOffset,
                    ref __state,
                    out var mapForPop,
                    ignoreGate: true))
            {
                _scopeMapForPop = mapForPop;
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception ArrivedFinalizer(Exception __exception, int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _scopeMapForPop);
            _scopeMapForPop = null;
            return __exception;
        }
    }
}
