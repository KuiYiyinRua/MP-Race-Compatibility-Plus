using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-166: opening a trade with a settlement runs
    /// `Settlement_TraderTracker.StockListForReading` on the peer that executes
    /// the caravan-arrival trade dialog (`Dialog_Trade..ctor ->
    /// MpTradeSession.TryCreate -> TradeDeal.AddAllTradeables ->
    /// Settlement.get_Goods`). When the settlement has never generated stock,
    /// the getter calls `RegenerateStock`, which consumes the LIVE world Rand
    /// stream and allocates the stock on only that peer. In async time the two
    /// peers reach the same world tick at different real times, so the first
    /// divergent trace is host `WorldPawnsTick`/map tick versus client
    /// `RegenerateStock -> StockGenerator.RandomCountOf -> Rand.RangeInclusive`
    /// at the same tick, and the opinion reports "Wrong random state for the
    /// world".
    ///
    /// Fix: in multiplayer, regenerate settlement stock inside a deterministic
    /// Rand scope seeded from the settlement's stable world-object ID. The same
    /// seed on every peer yields identical stock regardless of when each peer
    /// lazily reads the goods, and the live synchronized world Rand stream is
    /// never advanced by the regeneration. Singleplayer is untouched; failures
    /// fail open.
    /// </summary>
    internal static class Patch_TraderStockDeterminism
    {
        private const int StockSeedOffset = 0x54524144; // "TRAD"

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(Settlement_TraderTracker),
                    "RegenerateStock",
                    Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_TraderStockDeterminism),
                    nameof(RegenerateStockPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_TraderStockDeterminism),
                    nameof(RegenerateStockFinalizer));

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Trader stock determinism target " +
                        "resolution failed; settlement stock can desync.");
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
                    "[MP-MeowOnlineShop] Trader stock determinism active: " +
                    "settlement stock regeneration uses a stable per-settlement " +
                    "Rand seed in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Trader stock determinism apply failed: " +
                    e.Message);
            }
        }

        private static void RegenerateStockPrefix(
            Settlement_TraderTracker __instance,
            ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || __instance?.settlement == null)
                return;

            try
            {
                Rand.PushState(
                    Gen.HashCombineInt(
                        __instance.settlement.ID,
                        StockSeedOffset));
                __state = true;
            }
            catch
            {
                __state = false;
            }
        }

        private static void RegenerateStockFinalizer(bool __state)
        {
            if (!__state)
                return;

            try
            {
                Rand.PopState();
            }
            catch
            {
                // Fail open; the live stream may have advanced once, which is
                // strictly better than breaking stock generation.
            }
        }
    }
}
