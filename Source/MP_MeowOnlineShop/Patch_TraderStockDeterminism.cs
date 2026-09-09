using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
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
    ///
    /// Desync-563: the trade had already completed before the shuttle arrival,
    /// but the two peers entered DropShuttle with different ThingOwner ordering.
    /// A stable stock order is required in addition to a stable stock seed:
    /// MpTradeDeal.Recache preserves existing list order, and TradeDeal resolves
    /// those lists in order. Normalize both the settlement stock getter and the
    /// recached tradeables so a purchased/sold stack is transferred in the same
    /// order before the later shuttle load/drop path.
    /// </summary>
    internal static class Patch_TraderStockDeterminism
    {
        private const int StockSeedOffset = 0x54524144; // "TRAD"
        private const int TradeCompletionSeedSalt = 0x5452534C; // "TRSL"
        private const int TradeCompletionWorldSeedOffset = 0x54525357; // "TRSW"

        private static bool _applied;
        private static bool _tradePatchesApplied;
        private static bool _tradeLookupRecoveryWarningLogged;
        private static bool _tradeCompletionRandInstalled;

        private static readonly StringComparer SortComparer =
            StringComparer.Ordinal;

        [ThreadStatic]
        private static Map _tradeCompletionMapForPop;

        private static MethodInfo _regenerateStockMethod;

        private static Type _mpTradeSessionType;
        private static MethodInfo _tradeSessionTryCreateMethod;
        private static MethodInfo _tradeSessionGetTransferableMethod;
        private static MethodInfo _tradeSessionSetMethod;
        private static MethodInfo _tradeDealRecacheMethod;
        private static FieldInfo _tradeSessionDealField;
        private static FieldInfo _tradeDealTradeablesField;
        private static Type _multiplayerType;
        private static PropertyInfo _multiplayerTickingProperty;
        private static PropertyInfo _multiplayerExecutingCmdsProperty;
        private static Func<bool> _multiplayerTickingGetter;
        private static Func<bool> _multiplayerExecutingCmdsGetter;

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

                _regenerateStockMethod = target;

                MethodInfo stockGetter = AccessTools.PropertyGetter(
                    typeof(Settlement_TraderTracker),
                    nameof(Settlement_TraderTracker.StockListForReading));
                MethodInfo stockPostfix = AccessTools.Method(
                    typeof(Patch_TraderStockDeterminism),
                    nameof(NormalizeStockListPostfix));

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

                if (stockGetter != null && stockPostfix != null)
                {
                    harmony.Patch(
                        stockGetter,
                        postfix: new HarmonyMethod(stockPostfix)
                        {
                            priority = Priority.Last
                        });
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Trader stock determinism active: " +
                    "settlement stock regeneration uses a stable per-settlement " +
                    "Rand seed and stable stock/tradeable order in multiplayer " +
                    "(stockGetter=" + (stockGetter != null) + ").");

                ApplyTradePatches(harmony);
                ApplyTradeCompletionRandIsolation(harmony);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Trader stock determinism apply failed: " +
                    e.Message);
            }
        }

        private static void ApplyTradePatches(Harmony harmony)
        {
            if (_tradePatchesApplied || harmony == null)
                return;
            _tradePatchesApplied = true;

            try
            {
                _mpTradeSessionType =
                    AccessTools.TypeByName("Multiplayer.Client.MpTradeSession");
                if (_mpTradeSessionType == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Trade session stock sync could not " +
                        "resolve Multiplayer.Client.MpTradeSession; trade counts can " +
                        "still desync after settlement stock regeneration.");
                    return;
                }

                _tradeSessionTryCreateMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "TryCreate",
                    new[] { typeof(ITrader), typeof(Pawn), typeof(bool) });
                _tradeSessionGetTransferableMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "GetTransferableByThingId",
                    new[] { typeof(int) });
                _tradeSessionSetMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "SetTradeSession");
                _tradeSessionDealField =
                    AccessTools.Field(_mpTradeSessionType, "deal");
                _tradeDealTradeablesField =
                    AccessTools.Field(typeof(TradeDeal), "tradeables");
                _tradeDealRecacheMethod = AccessTools.Method(
                    AccessTools.TypeByName("Multiplayer.Client.MpTradeDeal"),
                    "Recache",
                    Type.EmptyTypes);
                _multiplayerType =
                    AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
                _multiplayerTickingProperty =
                    AccessTools.Property(_multiplayerType, "Ticking");
                _multiplayerExecutingCmdsProperty =
                    AccessTools.Property(_multiplayerType, "ExecutingCmds");
                _multiplayerTickingGetter =
                    TryCompileStaticBoolGetter(_multiplayerTickingProperty);
                _multiplayerExecutingCmdsGetter =
                    TryCompileStaticBoolGetter(_multiplayerExecutingCmdsProperty);

                if (_tradeSessionTryCreateMethod != null)
                {
                    harmony.Patch(
                        _tradeSessionTryCreateMethod,
                        prefix: new HarmonyMethod(
                            typeof(Patch_TraderStockDeterminism),
                            nameof(TradeSessionTryCreatePrefix))
                        {
                            priority = Priority.First
                        });
                }

                if (_tradeSessionGetTransferableMethod != null)
                {
                    harmony.Patch(
                        _tradeSessionGetTransferableMethod,
                        postfix: new HarmonyMethod(
                            typeof(Patch_TraderStockDeterminism),
                            nameof(TradeSessionGetTransferablePostfix))
                        {
                            priority = Priority.Last
                        });
                }

                MethodInfo recachePostfix = AccessTools.Method(
                    typeof(Patch_TraderStockDeterminism),
                    nameof(NormalizeTradeDealPostfix));
                if (_tradeDealRecacheMethod != null && recachePostfix != null)
                {
                    harmony.Patch(
                        _tradeDealRecacheMethod,
                        postfix: new HarmonyMethod(recachePostfix)
                        {
                            priority = Priority.Last
                        });
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Trade session stock sync active: " +
                    "settlement stock is refreshed deterministically at session " +
                    "creation and missing tradeable lookups are reconciled before " +
                    "applying count commands.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Trade session stock sync apply failed: " +
                    e.Message);
            }
        }

        private static void ApplyTradeCompletionRandIsolation(Harmony harmony)
        {
            if (_tradeCompletionRandInstalled || harmony == null)
                return;

            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_TraderStockDeterminism),
                nameof(TradeCompletionGiveSoldPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_TraderStockDeterminism),
                nameof(TradeCompletionGiveSoldFinalizer));
            if (prefix == null || finalizer == null)
                return;

            MethodInfo giveToPlayer = AccessTools.Method(
                typeof(Settlement_TraderTracker),
                "GiveSoldThingToPlayer",
                new[] { typeof(Thing), typeof(int), typeof(Pawn) });
            MethodInfo giveToTrader = AccessTools.Method(
                typeof(Settlement_TraderTracker),
                "GiveSoldThingToTrader",
                new[] { typeof(Thing), typeof(int), typeof(Pawn) });

            int patched = 0;
            MethodInfo[] targets = { giveToPlayer, giveToTrader };
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    continue;

                harmony.Patch(
                    targets[i],
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(finalizer)
                    {
                        priority = Priority.Last
                    });
                patched++;
            }

            if (patched == 0)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Trade-completion Rand isolation " +
                    "targets not resolved; skipped.");
                return;
            }

            _tradeCompletionRandInstalled = true;
            Log.Message(
                "[MP-MeowOnlineShop] Trade-completion Rand isolation active " +
                "(giveSoldBoundaries=" + patched + ").");
        }

        private static void TradeCompletionGiveSoldPrefix(
            Settlement_TraderTracker __instance,
            Thing toGive,
            ref int __state)
        {
            __state = 0;
            _tradeCompletionMapForPop = null;
            if (!MP.IsInMultiplayer ||
                __instance?.settlement == null ||
                toGive == null)
            {
                return;
            }

            int seed = Gen.HashCombineInt(
                TradeCompletionSeedSalt,
                __instance.settlement.ID);
            seed = Gen.HashCombineInt(seed, toGive.thingIDNumber);
            seed = Gen.HashCombineInt(seed, Find.TickManager.TicksGame);

            // World-side settlement trade: isolate the world/static Rand
            // streams that CaravanInventoryUtility.FindPawnToMoveInventoryTo
            // consumes while handing the purchased item to a caravan pawn.
            if (DeterministicRandScope.Begin(
                    null,
                    seed,
                    TradeCompletionWorldSeedOffset,
                    ref __state,
                    out var mapForPop,
                    ignoreGate: true))
            {
                _tradeCompletionMapForPop = mapForPop;
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception TradeCompletionGiveSoldFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _tradeCompletionMapForPop);
            _tradeCompletionMapForPop = null;
            return __exception;
        }

        private static void TradeSessionTryCreatePrefix(object[] __args)
        {
            if (!MP.IsInMultiplayer || !IsMultiplayerTickingOrExecuting() ||
                __args == null || __args.Length == 0 ||
                !(__args[0] is Settlement settlement) ||
                settlement.trader == null)
            {
                return;
            }

            TryRegenerateDeterministicStock(settlement.trader);
        }

        private static bool IsMultiplayerTickingOrExecuting()
        {
            try
            {
                if (_multiplayerTickingGetter != null &&
                    _multiplayerTickingGetter())
                {
                    return true;
                }
                if (_multiplayerExecutingCmdsGetter != null &&
                    _multiplayerExecutingCmdsGetter())
                {
                    return true;
                }
                if (_multiplayerTickingProperty != null &&
                    (bool)_multiplayerTickingProperty.GetValue(null, null))
                {
                    return true;
                }
                if (_multiplayerExecutingCmdsProperty != null &&
                    (bool)_multiplayerExecutingCmdsProperty.GetValue(null, null))
                {
                    return true;
                }
            }
            catch
            {
                // Fail open: TryCreate itself is already MP-gated, so allowing
                // the deterministic refresh is safer than skipping it.
                return true;
            }

            return false;
        }

        private static Func<bool> TryCompileStaticBoolGetter(PropertyInfo property)
        {
            if (property == null)
                return null;
            var getter = property.GetGetMethod(true);
            if (getter == null)
                return null;
            try
            {
                return (Func<bool>)Delegate.CreateDelegate(
                    typeof(Func<bool>),
                    getter);
            }
            catch
            {
                return null;
            }
        }

        private static void TradeSessionGetTransferablePostfix(
            object __instance,
            int thingId,
            ref Transferable __result)
        {
            if (__result != null || !MP.IsInMultiplayer ||
                !MP.IsExecutingSyncCommand || __instance == null)
            {
                return;
            }

            object deal = _tradeSessionDealField?.GetValue(__instance);
            if (deal == null || _tradeDealRecacheMethod == null)
                return;

            try
            {
                if (_tradeSessionSetMethod != null)
                    _tradeSessionSetMethod.Invoke(null, new[] { __instance });

                _tradeDealRecacheMethod.Invoke(deal, null);
                __result = FindTradeableByThingId(deal, thingId);
            }
            catch (Exception e)
            {
                if (!_tradeLookupRecoveryWarningLogged)
                {
                    _tradeLookupRecoveryWarningLogged = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Tradeable lookup recovery failed: " +
                        e.Message);
                }
            }
            finally
            {
                if (_tradeSessionSetMethod != null)
                    _tradeSessionSetMethod.Invoke(null, new object[] { null });
            }
        }

        private static Transferable FindTradeableByThingId(object deal, int thingId)
        {
            object raw = _tradeDealTradeablesField?.GetValue(deal);
            if (!(raw is IList tradeables))
                return null;

            for (int i = 0; i < tradeables.Count; i++)
            {
                if (!(tradeables[i] is Tradeable trad))
                    continue;
                if (trad.FirstThingColony?.thingIDNumber == thingId)
                    return trad;
                if (trad.FirstThingTrader?.thingIDNumber == thingId)
                    return trad;
            }

            return null;
        }

        private static void TryRegenerateDeterministicStock(
            Settlement_TraderTracker tracker)
        {
            if (tracker == null || _regenerateStockMethod == null)
                return;

            try
            {
                _regenerateStockMethod.Invoke(tracker, null);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Forced settlement stock regeneration " +
                    "failed: " + e.Message);
            }
        }

        private static void NormalizeStockListPostfix(
            ref System.Collections.Generic.List<Thing> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || __result.Count < 2)
                return;

            try
            {
                __result.Sort(CompareThings);
            }
            catch
            {
                // Sorting is a determinism aid only; never break the vanilla
                // stock getter if a third-party Thing implementation misbehaves.
            }
        }

        private static void NormalizeTradeDealPostfix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null ||
                _tradeDealTradeablesField == null)
            {
                return;
            }

            try
            {
                if (!(_tradeDealTradeablesField.GetValue(__instance)
                    is IList rawTradeables))
                {
                    return;
                }

                var tradeables = new System.Collections.Generic.List<Tradeable>(
                    rawTradeables.Count);
                for (int i = 0; i < rawTradeables.Count; i++)
                {
                    if (rawTradeables[i] is Tradeable tradeable)
                    {
                        tradeable.thingsColony.Sort(CompareThings);
                        tradeable.thingsTrader.Sort(CompareThings);
                        tradeables.Add(tradeable);
                    }
                }

                tradeables.Sort(CompareTradeables);
                rawTradeables.Clear();
                for (int i = 0; i < tradeables.Count; i++)
                    rawTradeables.Add(tradeables[i]);
            }
            catch
            {
                // The original MpTradeDeal recache has already completed. A
                // failed normalization must leave that valid deal intact.
            }
        }

        private static int CompareTradeables(
            Tradeable left,
            Tradeable right)
        {
            string leftKey = string.Join(
                "|",
                left?.GetType().FullName ?? string.Empty,
                BuildThingSortKey(left?.FirstThingTrader),
                BuildThingSortKey(left?.FirstThingColony));
            string rightKey = string.Join(
                "|",
                right?.GetType().FullName ?? string.Empty,
                BuildThingSortKey(right?.FirstThingTrader),
                BuildThingSortKey(right?.FirstThingColony));
            int result = SortComparer.Compare(leftKey, rightKey);
            if (result != 0)
                return result;

            return (left?.FirstThingTrader?.thingIDNumber ?? int.MaxValue)
                .CompareTo(right?.FirstThingTrader?.thingIDNumber ?? int.MaxValue);
        }

        private static int CompareThings(Thing left, Thing right)
        {
            int result = SortComparer.Compare(
                BuildThingSortKey(left),
                BuildThingSortKey(right));
            if (result != 0)
                return result;

            return (left?.thingIDNumber ?? int.MaxValue).CompareTo(
                right?.thingIDNumber ?? int.MaxValue);
        }

        private static string BuildThingSortKey(Thing thing)
        {
            if (thing == null)
                return string.Empty;

            IntVec3 position = thing.PositionHeld;
            return string.Join(
                "|",
                thing.GetType().FullName ?? string.Empty,
                thing.def?.defName ?? string.Empty,
                thing.Stuff?.defName ?? string.Empty,
                thing.stackCount.ToString(CultureInfo.InvariantCulture),
                thing.HitPoints.ToString(CultureInfo.InvariantCulture),
                position.x.ToString(CultureInfo.InvariantCulture),
                position.y.ToString(CultureInfo.InvariantCulture),
                position.z.ToString(CultureInfo.InvariantCulture));
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
