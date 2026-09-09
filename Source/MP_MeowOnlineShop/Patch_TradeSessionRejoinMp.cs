using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-434/435/436 (2026-08-16, Odyssey shuttle + settlement trade):
    /// after a rejoin the client replays buffered trade count commands
    /// (MpTransferableReference.CountToTransfer) while its MpTradeSession can
    /// no longer be resolved by SessionId. SyncDictMultiplayer then calls
    /// session.GetTransferableByThingId on a null session and throws:
    ///
    ///   Sync Error: Error reading type:
    ///     Multiplayer.Client.Persistent.MpTransferableReference
    ///   World cmd exception (Sync): NullReferenceException
    ///
    /// The count is then applied on the host but not on the client. The next
    /// synchronized MpTradeSession.TryExecute reaches
    /// Settlement_TraderTracker.GiveSoldThingToPlayer with different
    /// countToTransfer/stackCount values, so one peer calls Thing.SplitOff and
    /// consumes an extra UniqueIDsManager draw while the other does not.
    ///
    /// Multiplayer normally keeps ExposableSession instances alive during
    /// Scribe loading, but a trade session whose negotiator reference is
    /// temporarily unresolved at PostLoadInit is dropped by
    /// SessionManager.ExposeSessions before queued commands replay. The
    /// world-side trading list is authoritative and is populated by
    /// MpTradeSession.ExposeData regardless of that drop, so:
    ///
    /// 1. MpTradeSession.IsSessionValid stays true during PostLoadInit,
    ///    preventing the session from being removed before replay;
    /// 2. MultiplayerGame.GetSessions falls back to Multiplayer.WorldComp
    ///    .trading so a session that was already removed from the session
    ///    manager can still be resolved by id during command replay;
    /// 3. GetTransferableByThingId repairs an empty tradeables list through
    ///    MpTradeDeal.Recache instead of throwing in the middle of the sync
    ///    worker.
    ///
    /// Desync-513/514/515/516 and Desync-519/520 (2026-08-20, faction trade):
    /// the same job-ID signature returned even after the transport-ship unload
    /// draft fix was deployed. The remaining hole is the trade session state
    /// itself: after a rejoin one peer re-opens the trade fresh (TryCreate
    /// regenerates the settlement stock and allocates new thing IDs) while the
    /// other peer keeps a restored session whose tradeables reference the
    /// pre-rejoin stock IDs. TryExecute then transfers different things on each
    /// peer, and the traded pawn's job state diverges (one extra
    /// UniqueIDsManager.GetNextJobID later shifts the map Rand stream). To keep
    /// both peers' tradeables on the same thing IDs:
    ///
    /// 4. MpTradeSession.TryCreate postfix: only when a settlement trade has
    ///    an existing session for the exact same trader and negotiator,
    ///    reconcile that restored session's deal (recacheColony/
    ///    recacheTrader = true + MpTradeDeal.Recache) against the
    ///    deterministic stock regenerated at this same command tick. Other
    ///    conflicts (including TradeShip comms) remain Multiplayer's normal
    ///    mutual-exclusion behavior and are never rewritten;
    /// 5. MpTradeSession.TryExecute prefix: when the settlement stock is null,
    ///    force the deterministic RegenerateStock at the synchronized execute
    ///    boundary before any per-peer lazy regeneration can run.
    ///
    /// Desync-578 (2026-08-21, faction trade + multifaction): both peers
    /// executed TradeDeal.TryExecute and Faction.Notify_PlayerTraded at the
    /// same tick, but Multiplayer's MpTradeSession.TryCreate/TryExecute did
    /// not establish the playerNegotiator.Faction context. In a multifaction
    /// session vanilla goodwill/relation code reads Faction.OfPlayer, which
    /// can therefore point at a different local faction on each peer. The
    /// first visible unique-ID split was later in LaunchShuttle's
    /// LoadCaravanItemsIntoContainer; the goodwill call itself was not the
    /// first trace divergence, so the fix scopes the complete trade create /
    /// execute boundary instead of suppressing the goodwill mutation.
    ///
    /// 6. MpTradeSession.TryCreate and TryExecute temporarily enter the
    ///    negotiator's actual player faction context, and TradeDeal.TryExecute
    ///    has a direct fallback boundary for any vanilla trade path that calls
    ///    the deal without the Multiplayer session wrapper. The previous UI
    ///    faction context is restored in a finalizer.
    ///
    /// Desync-594 follow-up: keep the same negotiator faction context around
    /// Faction.Notify_PlayerTraded as a final fallback. Vanilla normally calls
    /// it from TradeDeal.TryExecute, which is already covered above, but a
    /// third-party trade path can invoke the goodwill notification after the
    /// deal boundary has unwound. Without this small boundary, that deferred
    /// call can read a peer-local Faction.OfPlayer in a multifaction session.
    ///
    /// All hooks fail open and add no new sync commands.
    /// </summary>
    internal static class Patch_TradeSessionRejoinMp
    {
        private const string MultiplayerTypeName = "Multiplayer.Client.Multiplayer";
        private const string MultiplayerGameTypeName = "Multiplayer.Client.MultiplayerGame";
        private const string WorldCompTypeName = "Multiplayer.Client.MultiplayerWorldComp";
        private const string MpTradeSessionTypeName = "Multiplayer.Client.MpTradeSession";
        private const string MpTradeDealTypeName = "Multiplayer.Client.MpTradeDeal";
        private const string DiagEnvVarName = "MP_TRADE_DIAG";
        private const int MaxDiagExecutions = 8;

        private static bool _applied;
        private static bool _loggedRepair;
        private static int _diagExecutionCount;

        internal static readonly bool DiagEnabled =
            string.Equals(
                Environment.GetEnvironmentVariable(DiagEnvVarName),
                "1",
                StringComparison.OrdinalIgnoreCase);

        private static Type _multiplayerType;
        private static Type _multiplayerGameType;
        private static Type _worldCompType;
        private static Type _mpTradeSessionType;
        private static Type _mpTradeDealType;

        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _tradingField;
        private static MethodInfo _getSessionsMethod;
        private static MethodInfo _isSessionValidGetter;
        private static MethodInfo _getTransferableMethod;
        private static FieldInfo _dealField;
        private static FieldInfo _tradeablesField;
        private static MethodInfo _recacheMethod;
        private static MethodInfo _tryExecuteMethod;
        private static MethodInfo _tradeDealTryExecuteMethod;
        private static MethodInfo _notifyPlayerTradedMethod;
        private static MethodInfo _giveSoldThingToPlayerMethod;
        private static MethodInfo _giveSoldThingToTraderMethod;
        private static MethodInfo _tryCreateMethod;
        private static MethodInfo _setTradeSessionMethod;
        private static FieldInfo _currentTradeSessionField;
        private static FieldInfo _traderField;
        private static FieldInfo _negotiatorField;
        private static FieldInfo _ofPlayerField;
        private static MethodInfo _factionContextPushMethod;
        private static MethodInfo _factionContextPopMethod;
        private static FieldInfo _stockField;
        private static MethodInfo _regenerateStockMethod;
        private static FieldInfo _dealRecacheColonyField;
        private static FieldInfo _dealRecacheTraderField;
        private static bool _loggedConflictRecache;
        private static bool _loggedTryExecuteRegen;
        private static bool _loggedTradeFactionContext;
        private static bool _loggedTradeFactionContextFailure;

        internal sealed class TradeFactionScopeState
        {
            internal bool Active;
            internal bool ContextPushed;
            internal Faction SavedFaction;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                _multiplayerType = AccessTools.TypeByName(MultiplayerTypeName);
                _multiplayerGameType = AccessTools.TypeByName(MultiplayerGameTypeName);
                _worldCompType = AccessTools.TypeByName(WorldCompTypeName);
                _mpTradeSessionType = AccessTools.TypeByName(MpTradeSessionTypeName);
                _mpTradeDealType = AccessTools.TypeByName(MpTradeDealTypeName);

                _worldCompProperty = AccessTools.Property(_multiplayerType, "WorldComp");
                _tradingField = AccessTools.Field(_worldCompType, "trading");
                _getSessionsMethod = AccessTools.Method(
                    _multiplayerGameType,
                    "GetSessions",
                    new[] { typeof(Map) });
                _isSessionValidGetter = AccessTools.PropertyGetter(
                    _mpTradeSessionType,
                    "IsSessionValid");
                _getTransferableMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "GetTransferableByThingId",
                    new[] { typeof(int) });
                _dealField = AccessTools.Field(_mpTradeSessionType, "deal");
                _tradeablesField = AccessTools.Field(typeof(TradeDeal), "tradeables");
                _recacheMethod = AccessTools.Method(
                    _mpTradeDealType,
                    "Recache",
                    Type.EmptyTypes);
                _tryExecuteMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "TryExecute",
                    Type.EmptyTypes);
                _tradeDealTryExecuteMethod = AccessTools.Method(
                    typeof(TradeDeal),
                    "TryExecute",
                    new[] { typeof(bool).MakeByRefType() });
                _notifyPlayerTradedMethod = AccessTools.Method(
                    typeof(Faction),
                    nameof(Faction.Notify_PlayerTraded));
                _giveSoldThingToPlayerMethod = AccessTools.Method(
                    typeof(Settlement_TraderTracker),
                    "GiveSoldThingToPlayer",
                    new[] { typeof(Thing), typeof(int), typeof(Pawn) });
                _giveSoldThingToTraderMethod = AccessTools.Method(
                    typeof(Settlement_TraderTracker),
                    "GiveSoldThingToTrader",
                    new[] { typeof(Thing), typeof(int), typeof(Pawn) });
                _tryCreateMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "TryCreate",
                    new[] { typeof(ITrader), typeof(Pawn), typeof(bool) });
                _setTradeSessionMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "SetTradeSession",
                    new[] { _mpTradeSessionType });
                _currentTradeSessionField = AccessTools.Field(
                    _mpTradeSessionType,
                    "current");
                _traderField = AccessTools.Field(_mpTradeSessionType, "trader");
                _negotiatorField = AccessTools.Field(
                    _mpTradeSessionType,
                    "playerNegotiator");
                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager),
                    "ofPlayer");
                Type factionContextType = AccessTools.TypeByName(
                    "Multiplayer.Client.FactionContext");
                _factionContextPushMethod = AccessTools.Method(
                    factionContextType,
                    "Push",
                    new[] { typeof(Faction), typeof(bool) });
                _factionContextPopMethod = AccessTools.Method(
                    factionContextType,
                    "Pop",
                    Type.EmptyTypes);
                _stockField = AccessTools.Field(
                    typeof(Settlement_TraderTracker),
                    "stock");
                _regenerateStockMethod = AccessTools.Method(
                    typeof(Settlement_TraderTracker),
                    "RegenerateStock",
                    Type.EmptyTypes);
                _dealRecacheColonyField = AccessTools.Field(
                    _mpTradeDealType,
                    "recacheColony");
                _dealRecacheTraderField = AccessTools.Field(
                    _mpTradeDealType,
                    "recacheTrader");

                int patched = 0;
                if (_getSessionsMethod != null)
                {
                    harmony.Patch(
                        _getSessionsMethod,
                        postfix: new HarmonyMethod(
                            typeof(Patch_TradeSessionRejoinMp),
                            nameof(GetSessionsPostfix))
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (_isSessionValidGetter != null)
                {
                    harmony.Patch(
                        _isSessionValidGetter,
                        prefix: new HarmonyMethod(
                            typeof(Patch_TradeSessionRejoinMp),
                            nameof(IsSessionValidPrefix))
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                if (_getTransferableMethod != null)
                {
                    harmony.Patch(
                        _getTransferableMethod,
                        prefix: new HarmonyMethod(
                            typeof(Patch_TradeSessionRejoinMp),
                            nameof(GetTransferablePrefix))
                        {
                            priority = Priority.First
                        });
                    patched++;
                }
                try
                {
                    if (_tryCreateMethod != null)
                    {
                        harmony.Patch(
                            _tryCreateMethod,
                            postfix: new HarmonyMethod(
                                typeof(Patch_TradeSessionRejoinMp),
                                nameof(TryCreatePostfix))
                            {
                                priority = Priority.Last
                            });
                        patched++;
                    }

                    if (_tryExecuteMethod != null)
                    {
                        harmony.Patch(
                            _tryExecuteMethod,
                            prefix: new HarmonyMethod(
                                typeof(Patch_TradeSessionRejoinMp),
                                nameof(TryExecuteReconcilePrefix))
                            {
                                priority = Priority.First
                            });
                        patched++;
                    }

                    MethodInfo tradeDealFactionPrefix = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(TradeDealTryExecuteFactionPrefix));
                    MethodInfo tradeDealFactionFinalizer = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(TradeDealTryExecuteFactionFinalizer));
                    if (_tradeDealTryExecuteMethod != null &&
                        tradeDealFactionPrefix != null &&
                        tradeDealFactionFinalizer != null)
                    {
                        harmony.Patch(
                            _tradeDealTryExecuteMethod,
                            prefix: new HarmonyMethod(tradeDealFactionPrefix)
                            {
                                priority = Priority.First
                            },
                            finalizer: new HarmonyMethod(
                                tradeDealFactionFinalizer)
                            {
                                priority = Priority.Last
                            });
                        patched++;
                    }

                    MethodInfo notifyPlayerTradedPrefix = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(NotifyPlayerTradedFactionPrefix));
                    MethodInfo notifyPlayerTradedFinalizer = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(NotifyPlayerTradedFactionFinalizer));
                    if (_notifyPlayerTradedMethod != null &&
                        notifyPlayerTradedPrefix != null &&
                        notifyPlayerTradedFinalizer != null)
                    {
                        harmony.Patch(
                            _notifyPlayerTradedMethod,
                            prefix: new HarmonyMethod(notifyPlayerTradedPrefix)
                            {
                                priority = Priority.First
                            },
                            finalizer: new HarmonyMethod(
                                notifyPlayerTradedFinalizer)
                            {
                                priority = Priority.Last
                            });
                        patched++;
                    }

                    MethodInfo tryCreateFactionPrefix = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(TradeTryCreateFactionPrefix));
                    MethodInfo tryCreateFactionFinalizer = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(TradeTryCreateFactionFinalizer));
                    if (_tryCreateMethod != null &&
                        tryCreateFactionPrefix != null &&
                        tryCreateFactionFinalizer != null)
                    {
                        harmony.Patch(
                            _tryCreateMethod,
                            prefix: new HarmonyMethod(tryCreateFactionPrefix)
                            {
                                priority = Priority.First
                            },
                            finalizer: new HarmonyMethod(
                                tryCreateFactionFinalizer)
                            {
                                priority = Priority.Last
                            });
                        patched++;
                    }

                    MethodInfo tryExecuteFactionPrefix = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(TradeTryExecuteFactionPrefix));
                    MethodInfo tryExecuteFactionFinalizer = AccessTools.Method(
                        typeof(Patch_TradeSessionRejoinMp),
                        nameof(TradeTryExecuteFactionFinalizer));
                    if (_tryExecuteMethod != null &&
                        tryExecuteFactionPrefix != null &&
                        tryExecuteFactionFinalizer != null)
                    {
                        harmony.Patch(
                            _tryExecuteMethod,
                            prefix: new HarmonyMethod(tryExecuteFactionPrefix)
                            {
                                priority = Priority.First
                            },
                            finalizer: new HarmonyMethod(
                                tryExecuteFactionFinalizer)
                            {
                                priority = Priority.Last
                            });
                        patched++;
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Trade session reconcile hook " +
                        "registration failed: " + e.Message);
                }

                if (DiagEnabled)
                {
                    if (_tryExecuteMethod != null)
                    {
                        harmony.Patch(
                            _tryExecuteMethod,
                            prefix: new HarmonyMethod(
                                typeof(Patch_TradeSessionRejoinMp),
                                nameof(TryExecuteDiagPrefix))
                            {
                                priority = Priority.First
                            });
                    }

                    if (_giveSoldThingToPlayerMethod != null)
                    {
                        harmony.Patch(
                            _giveSoldThingToPlayerMethod,
                            prefix: new HarmonyMethod(
                                typeof(Patch_TradeSessionRejoinMp),
                                nameof(GiveSoldThingToPlayerDiagPrefix))
                            {
                                priority = Priority.First
                            });
                    }

                    if (_giveSoldThingToTraderMethod != null)
                    {
                        harmony.Patch(
                            _giveSoldThingToTraderMethod,
                            prefix: new HarmonyMethod(
                                typeof(Patch_TradeSessionRejoinMp),
                                nameof(GiveSoldThingToTraderDiagPrefix))
                            {
                                priority = Priority.First
                            });
                    }
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Trade session rejoin guard " +
                        "targets unresolved; skipped.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Trade session rejoin guard active: " +
                    "trade sessions survive PostLoadInit, transferable " +
                    "lookups fall back to the authoritative trading list, and " +
                    "conflicting TryCreate/TryExecute reconcile tradeables " +
                    "against the deterministic settlement stock " +
                    "with the negotiator faction context applied during " +
                    "trade execution " +
                    "(boundaries=" + patched + ", tradeDealTryExecute=" +
                    (_tradeDealTryExecuteMethod != null) +
                    ", goodwillNotify=" +
                    (_notifyPlayerTradedMethod != null) +
                    ", factionContext=" +
                    (_factionContextPushMethod != null &&
                     _factionContextPopMethod != null) + ").");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Trade session rejoin guard apply " +
                    "failed: " + e.Message);
            }
        }

        private static void GetSessionsPostfix(ref IEnumerable<Session> __result)
        {
            if (!MP.IsInMultiplayer || _tradingField == null ||
                _worldCompProperty == null)
            {
                return;
            }

            try
            {
                object worldComp = _worldCompProperty.GetValue(null, null);
                if (worldComp == null)
                    return;

                if (!(_tradingField.GetValue(worldComp) is IList trading))
                    return;

                List<Session> sessions = __result == null
                    ? new List<Session>()
                    : new List<Session>(__result);

                bool added = false;
                for (int i = 0; i < trading.Count; i++)
                {
                    if (!(trading[i] is Session session) ||
                        sessions.Contains(session))
                    {
                        continue;
                    }

                    sessions.Add(session);
                    added = true;
                }

                if (added)
                    __result = sessions;
            }
            catch
            {
                // Fail open: the original session lookup remains authoritative.
            }
        }

        private static bool IsSessionValidPrefix(ref bool __result)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || !MP.enabled)
                return true;

            __result = true;
            return false;
        }

        private static bool GetTransferablePrefix(
            object __instance,
            int thingId,
            ref Transferable __result)
        {
            if (!MP.IsInMultiplayer || __instance == null ||
                _dealField == null || _tradeablesField == null ||
                _recacheMethod == null ||
                _setTradeSessionMethod == null ||
                _currentTradeSessionField == null)
            {
                return true;
            }

            try
            {
                object deal = _dealField.GetValue(__instance);
                if (deal == null)
                    return true;

                if (!(_tradeablesField.GetValue(deal) is IList tradeables) ||
                    tradeables.Count != 0)
                {
                    return true;
                }

                // The deal survived the save but its tradeable list is empty,
                // which happens when the settlement stock getter regenerated
                // during a rejoin load. Rebuild from the current deterministic
                // stock and answer the command directly so the original method
                // cannot throw in the middle of deserialization.
                RecacheWithTradeSession(__instance, deal);
                __result = FindTradeableByThingId(deal, thingId);

                if (!_loggedRepair)
                {
                    _loggedRepair = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Repaired an empty trade deal " +
                        "during rejoin command replay; thingId=" + thingId +
                        " resolved=" + (__result != null) + ".");
                }

                return false;
            }
            catch (Exception e)
            {
                if (!_loggedRepair)
                {
                    _loggedRepair = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Trade deal repair failed during " +
                        "rejoin command replay: " + ExceptionMessage(e));
                }

                return true;
            }
        }

        private static void TryCreatePostfix(
            ITrader trader,
            Pawn playerNegotiator,
            object __result)
        {
            if (!MP.IsInMultiplayer || __result != null ||
                !(trader is Settlement) || playerNegotiator == null ||
                _tradingField == null || _worldCompProperty == null ||
                _traderField == null || _negotiatorField == null ||
                _dealField == null || _recacheMethod == null ||
                _setTradeSessionMethod == null ||
                _currentTradeSessionField == null ||
                _dealRecacheColonyField == null ||
                _dealRecacheTraderField == null)
            {
                return;
            }

            try
            {
                object worldComp = _worldCompProperty.GetValue(null, null);
                if (worldComp == null)
                    return;

                if (!(_tradingField.GetValue(worldComp) is IList trading))
                    return;

                // A null result can be Multiplayer's normal mutual exclusion:
                // either the trader OR the negotiator is already busy. That is
                // not evidence of a damaged rejoin session. Only an exact
                // settlement trader + negotiator pair identifies the restored
                // session this compatibility repair is designed for.
                for (int i = 0; i < trading.Count; i++)
                {
                    object existing = trading[i];
                    if (existing == null)
                        continue;

                    object existingTrader = _traderField.GetValue(existing);
                    object existingNegotiator =
                        _negotiatorField.GetValue(existing);
                    bool exactRestoredSession =
                        ReferenceEquals(existingTrader, trader) &&
                        ReferenceEquals(existingNegotiator, playerNegotiator);
                    if (!exactRestoredSession)
                        continue;

                    object deal = _dealField.GetValue(existing);
                    if (deal == null)
                        continue;

                    _dealRecacheColonyField.SetValue(deal, true);
                    _dealRecacheTraderField.SetValue(deal, true);
                    RecacheWithTradeSession(existing, deal);

                    // A freshly created session starts every tradeable at
                    // count 0. The restored session may carry pre-rejoin
                    // counts, so reset them to keep both peers aligned before
                    // the player's count commands replay on the new window.
                    if (_tradeablesField != null &&
                        _tradeablesField.GetValue(deal) is IList reconciled)
                    {
                        for (int r = 0; r < reconciled.Count; r++)
                        {
                            if (reconciled[r] is Tradeable trad)
                                trad.ForceTo(0);
                        }
                    }

                    if (DiagEnabled && !_loggedConflictRecache)
                    {
                        _loggedConflictRecache = true;
                        Log.Message(
                            "[MP-MeowOnlineShop] Reconciled an exact restored " +
                            "settlement trade session against deterministic " +
                            "stock (trader=" + trader + ").");
                    }
                    return;
                }
            }
            catch (Exception e)
            {
                if (!_loggedConflictRecache)
                {
                    _loggedConflictRecache = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Restored settlement trade session " +
                        "reconciliation failed: " + ExceptionMessage(e));
                }
            }
        }

        private static void RecacheWithTradeSession(object session, object deal)
        {
            object previous = _currentTradeSessionField.GetValue(null);
            _setTradeSessionMethod.Invoke(null, new[] { session });
            try
            {
                _recacheMethod.Invoke(deal, null);
            }
            finally
            {
                _setTradeSessionMethod.Invoke(null, new[] { previous });
            }
        }

        private static string ExceptionMessage(Exception exception)
        {
            TargetInvocationException invocation =
                exception as TargetInvocationException;
            return invocation != null && invocation.InnerException != null
                ? invocation.InnerException.Message
                : exception.Message;
        }

        private static void TradeTryCreateFactionPrefix(
            object[] __args,
            ref TradeFactionScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || __args == null || __args.Length < 2)
                return;

            __state = TryBeginTradeFactionScope(__args[1] as Pawn);
        }

        private static Exception TradeTryCreateFactionFinalizer(
            Exception __exception,
            TradeFactionScopeState __state)
        {
            RestoreTradeFactionScope(__state);
            return __exception;
        }

        private static void TradeTryExecuteFactionPrefix(
            object __instance,
            ref TradeFactionScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || __instance == null ||
                _negotiatorField == null)
            {
                return;
            }

            try
            {
                __state = TryBeginTradeFactionScope(
                    _negotiatorField.GetValue(__instance) as Pawn);
            }
            catch (Exception e)
            {
                LogTradeFactionContextFailure(e);
            }
        }

        private static Exception TradeTryExecuteFactionFinalizer(
            Exception __exception,
            TradeFactionScopeState __state)
        {
            RestoreTradeFactionScope(__state);
            return __exception;
        }

        private static void TradeDealTryExecuteFactionPrefix(
            ref TradeFactionScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer)
                return;

            __state = TryBeginTradeFactionScope(
                TradeSession.playerNegotiator);
        }

        private static Exception TradeDealTryExecuteFactionFinalizer(
            Exception __exception,
            TradeFactionScopeState __state)
        {
            RestoreTradeFactionScope(__state);
            return __exception;
        }

        private static void NotifyPlayerTradedFactionPrefix(
            ref TradeFactionScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer)
                return;

            // TradeSession.playerNegotiator is the same authoritative pawn
            // used by the direct TradeDeal.TryExecute fallback. The nested
            // call made by vanilla simply observes the already-active scope;
            // a delayed third-party call gets its own balanced scope.
            __state = TryBeginTradeFactionScope(
                TradeSession.playerNegotiator);
        }

        private static Exception NotifyPlayerTradedFactionFinalizer(
            Exception __exception,
            TradeFactionScopeState __state)
        {
            RestoreTradeFactionScope(__state);
            return __exception;
        }

        internal static TradeFactionScopeState TryBeginTradeFactionScope(
            Pawn negotiator)
        {
            if (!MP.IsInMultiplayer || negotiator == null ||
                _ofPlayerField == null || Find.FactionManager == null)
            {
                return null;
            }

            try
            {
                Faction faction = negotiator.Faction;
                if (faction?.def?.isPlayer != true)
                    return null;

                Faction previous = _ofPlayerField.GetValue(
                    Find.FactionManager) as Faction;
                if (ReferenceEquals(previous, faction))
                    return null;

                bool contextPushed = false;
                if (_factionContextPushMethod != null &&
                    _factionContextPopMethod != null)
                {
                    _factionContextPushMethod.Invoke(
                        null,
                        new object[] { faction, true });
                    contextPushed = true;
                }
                else
                {
                    _ofPlayerField.SetValue(Find.FactionManager, faction);
                }

                if (!_loggedTradeFactionContext)
                {
                    _loggedTradeFactionContext = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Trade faction context active: " +
                        $"trade create/execute and TradeDeal.TryExecute run " +
                        "under negotiator faction " +
                        $"{faction.Name} so goodwill/relation mutations are " +
                        "peer-identical (FactionContext=" +
                        contextPushed + ").");
                }

                return new TradeFactionScopeState
                {
                    Active = true,
                    ContextPushed = contextPushed,
                    SavedFaction = previous
                };
            }
            catch (Exception e)
            {
                LogTradeFactionContextFailure(e);
                return null;
            }
        }

        internal static void RestoreTradeFactionScope(
            TradeFactionScopeState state)
        {
            if (state?.Active != true || _ofPlayerField == null ||
                Find.FactionManager == null)
            {
                return;
            }

            try
            {
                if (state.ContextPushed && _factionContextPopMethod != null)
                {
                    _factionContextPopMethod.Invoke(null, null);
                }
                else
                {
                    _ofPlayerField.SetValue(
                        Find.FactionManager,
                        state.SavedFaction);
                }
            }
            catch (Exception e)
            {
                LogTradeFactionContextFailure(e);
            }
        }

        private static void LogTradeFactionContextFailure(Exception e)
        {
            if (_loggedTradeFactionContextFailure)
                return;

            _loggedTradeFactionContextFailure = true;
            Log.Warning(
                "[MP-MeowOnlineShop] Trade faction context failed open: " +
                e.Message);
        }

        private static void TryExecuteReconcilePrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null ||
                _traderField == null || _stockField == null ||
                _regenerateStockMethod == null)
            {
                return;
            }

            try
            {
                Settlement_TraderTracker tracker =
                    ResolveSettlementTracker(
                        _traderField.GetValue(__instance));
                if (tracker == null)
                {
                    return;
                }

                // The lazy StockListForReading regeneration can run on a single
                // peer at a different tick. Force it here, at the synchronized
                // TryExecute command boundary, so both peers regenerate with the
                // same per-settlement seed at the same command tick before the
                // deal recaches its tradeables.
                if (_stockField.GetValue(tracker) != null)
                    return;

                _regenerateStockMethod.Invoke(tracker, null);
                if (!_loggedTryExecuteRegen)
                {
                    _loggedTryExecuteRegen = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Deterministic settlement stock " +
                        "regeneration forced at TryExecute boundary.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedTryExecuteRegen)
                {
                    _loggedTryExecuteRegen = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] TryExecute stock reconciliation " +
                        "failed: " + e.Message);
                }
            }
        }

        internal static Settlement_TraderTracker ResolveSettlementTracker(
            object trader)
        {
            Settlement_TraderTracker tracker =
                trader as Settlement_TraderTracker;
            if (tracker != null)
                return tracker;

            Settlement settlement = trader as Settlement;
            return settlement?.trader;
        }

        private static void TryExecuteDiagPrefix(object __instance)
        {
            if (!DiagEnabled || __instance == null ||
                _diagExecutionCount >= MaxDiagExecutions ||
                _dealField == null || _tradeablesField == null)
            {
                return;
            }

            try
            {
                _diagExecutionCount++;
                object deal = _dealField.GetValue(__instance);
                object raw = deal == null
                    ? null
                    : _tradeablesField.GetValue(deal);
                if (!(raw is IList tradeables))
                {
                    Log.Message(
                        "[MP-TRADE-DIAG] TryExecute deal=null session=" +
                        __instance.GetType().Name + ".");
                    return;
                }

                var rows = new List<string>(tradeables.Count);
                for (int i = 0; i < tradeables.Count; i++)
                {
                    if (!(tradeables[i] is Tradeable trad))
                        continue;

                    int colonyId = trad.FirstThingColony?.thingIDNumber ?? -1;
                    int traderId = trad.FirstThingTrader?.thingIDNumber ?? -1;
                    rows.Add(
                        "trad=" + trad.Label +
                        " colonyId=" + colonyId +
                        " traderId=" + traderId +
                        " count=" + trad.CountToTransfer);
                }

                Log.Message(
                    "[MP-TRADE-DIAG] TryExecute tradeables=" +
                    tradeables.Count + " rows=[" +
                    string.Join("; ", rows) + "].");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-TRADE-DIAG] TryExecute diagnostic failed: " +
                    e.Message);
            }
        }

        private static void GiveSoldThingToPlayerDiagPrefix(
            Settlement_TraderTracker __instance,
            Thing toGive,
            int countToGive)
        {
            LogGiveSoldDiag("Player", __instance, toGive, countToGive);
        }

        private static void GiveSoldThingToTraderDiagPrefix(
            Settlement_TraderTracker __instance,
            Thing toGive,
            int countToGive)
        {
            LogGiveSoldDiag("Trader", __instance, toGive, countToGive);
        }

        private static void LogGiveSoldDiag(
            string direction,
            Settlement_TraderTracker tracker,
            Thing toGive,
            int countToGive)
        {
            if (!DiagEnabled || _diagExecutionCount >= MaxDiagExecutions ||
                tracker?.settlement == null || toGive == null)
            {
                return;
            }

            _diagExecutionCount++;
            Log.Message(
                "[MP-TRADE-DIAG] GiveSold" + direction +
                " settlement=" + tracker.settlement.ID +
                " thing=" + toGive.thingIDNumber +
                " def=" + toGive.def.defName +
                " stack=" + toGive.stackCount +
                " count=" + countToGive +
                " split=" + (countToGive < toGive.stackCount));
        }

        private static Transferable FindTradeableByThingId(
            object deal,
            int thingId)
        {
            if (!(_tradeablesField?.GetValue(deal) is IList tradeables))
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
    }
}
