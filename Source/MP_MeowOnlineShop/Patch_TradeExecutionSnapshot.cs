using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-599: Multiplayer's native trade accept command serializes only
    /// the MpTradeSession. The mutable countToTransfer values arrive through
    /// earlier MpTransferableReference commands keyed by a concrete thing ID.
    /// After a rejoin, peers can therefore resolve the same session while
    /// retaining different tradeable lists or counts. One peer then completes
    /// TradeDeal.TryExecute (including goodwill) while another rejects it.
    ///
    /// Replace only the UI accept boundary with one canonical intent command.
    /// The command carries semantic tradeable keys and the final counts chosen
    /// by the issuing player. At the shared command tick every peer regenerates
    /// settlement stock, rebuilds the deal from authoritative goods, clears all
    /// stale counts, applies the same intent, and finally delegates to native
    /// MpTradeSession.TryExecute. Native pricing, transfer, goodwill, session
    /// removal, and Multiplayer command context remain authoritative.
    /// </summary>
    internal static class Patch_TradeExecutionSnapshot
    {
        private const int CanonicalSnapshotRevision = 4;
        private const string MultiplayerTypeName =
            "Multiplayer.Client.Multiplayer";
        private const string WorldCompTypeName =
            "Multiplayer.Client.MultiplayerWorldComp";
        private const string MpTradeSessionTypeName =
            "Multiplayer.Client.MpTradeSession";
        private const string MpTradeDealTypeName =
            "Multiplayer.Client.MpTradeDeal";
        private const string TradingWindowTypeName =
            "Multiplayer.Client.TradingWindow";

        private static readonly StringComparer KeyComparer =
            StringComparer.Ordinal;

        private static bool _applied;
        private static bool _ready;
        private static bool _loggedFailure;
        private static bool _loggedIntentQueued;
        private static bool _loggedReplayCompleted;
        private static string _resolutionFailure;

        private static Type _multiplayerType;
        private static Type _worldCompType;
        private static Type _mpTradeSessionType;
        private static Type _mpTradeDealType;
        private static Type _tradingWindowType;

        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _tradingField;
        private static FieldInfo _drawingTradeField;
        private static FieldInfo _currentSessionField;
        private static FieldInfo _traderField;
        private static FieldInfo _negotiatorField;
        private static FieldInfo _giftModeField;
        private static FieldInfo _dealField;
        private static FieldInfo _permanentSilverField;
        private static FieldInfo _recacheThingsField;

        private static MethodInfo _setTradeSessionMethod;
        private static MethodInfo _tryExecuteMethod;
        private static MethodInfo _regenerateStockMethod;
        private static MethodInfo _addToTradeablesMethod;
        private static MethodInfo _addAllTradeablesMethod;

        private static ISyncMethod _syncExecuteCanonicalTrade;

        private sealed class IntentRow
        {
            internal string Key;
            internal int Occurrence;
            internal int Count;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                _multiplayerType = AccessTools.TypeByName(
                    MultiplayerTypeName);
                _worldCompType = AccessTools.TypeByName(WorldCompTypeName);
                _mpTradeSessionType = AccessTools.TypeByName(
                    MpTradeSessionTypeName);
                _mpTradeDealType = AccessTools.TypeByName(
                    MpTradeDealTypeName);
                _tradingWindowType = AccessTools.TypeByName(
                    TradingWindowTypeName);

                _worldCompProperty = AccessTools.Property(
                    _multiplayerType,
                    "WorldComp");
                _tradingField = AccessTools.Field(
                    _worldCompType,
                    "trading");
                _drawingTradeField = AccessTools.Field(
                    _tradingWindowType,
                    "drawingTrade");
                _currentSessionField = AccessTools.Field(
                    _mpTradeSessionType,
                    "current");
                _traderField = AccessTools.Field(
                    _mpTradeSessionType,
                    "trader");
                _negotiatorField = AccessTools.Field(
                    _mpTradeSessionType,
                    "playerNegotiator");
                _giftModeField = AccessTools.Field(
                    _mpTradeSessionType,
                    "giftMode");
                _dealField = AccessTools.Field(
                    _mpTradeSessionType,
                    "deal");
                _permanentSilverField = AccessTools.Field(
                    _mpTradeDealType,
                    "permanentSilver");
                _recacheThingsField = AccessTools.Field(
                    _mpTradeDealType,
                    "recacheThings");
                _setTradeSessionMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "SetTradeSession",
                    new[] { _mpTradeSessionType });
                _tryExecuteMethod = AccessTools.Method(
                    _mpTradeSessionType,
                    "TryExecute",
                    Type.EmptyTypes);
                _regenerateStockMethod = AccessTools.Method(
                    typeof(Settlement_TraderTracker),
                    "RegenerateStock",
                    Type.EmptyTypes);
                _addToTradeablesMethod = AccessTools.DeclaredMethod(
                    typeof(TradeDeal),
                    "AddToTradeables",
                    new[] { typeof(Thing), typeof(Transactor) });
                _addAllTradeablesMethod = AccessTools.DeclaredMethod(
                    typeof(TradeDeal),
                    "AddAllTradeables",
                    Type.EmptyTypes);

                MethodInfo tradeDealTryExecute = AccessTools.Method(
                    typeof(TradeDeal),
                    "TryExecute",
                    new[] { typeof(bool).MakeByRefType() });

                string[] missing = GetMissingTargets(tradeDealTryExecute);
                _resolutionFailure = string.Join(", ", missing);

                // Even if replay internals change in a later MP/RW build,
                // retain ownership of the Multiplayer trading-window accept
                // boundary and fail closed. Silently falling back to MP's
                // session-only command is the proven Desync-599/607 path.
                if (tradeDealTryExecute == null ||
                    _drawingTradeField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Canonical trade snapshot " +
                        "cannot own the UI accept boundary; unresolved=" +
                        _resolutionFailure + ".");
                    return;
                }

                if (missing.Length == 0)
                {
                    try
                    {
                        _syncExecuteCanonicalTrade = MP.RegisterSyncMethod(
                            typeof(Patch_TradeExecutionSnapshot),
                            nameof(SyncExecuteCanonicalTrade));
                        _ready = _syncExecuteCanonicalTrade != null;
                        if (!_ready)
                            _resolutionFailure = "sync method registration";
                    }
                    catch (Exception e)
                    {
                        _ready = false;
                        _resolutionFailure =
                            "sync method registration: " + e.Message;
                    }
                }

                MethodInfo uiPrefix = AccessTools.Method(
                    typeof(Patch_TradeExecutionSnapshot),
                    nameof(TradeDealTryExecuteUiPrefix));
                harmony.Patch(
                    tradeDealTryExecute,
                    prefix: new HarmonyMethod(uiPrefix)
                    {
                        // Run before Multiplayer.Client.TradeDealExecutePatch.
                        // Its prefix otherwise emits the old session-only
                        // TryExecute command before this snapshot is queued.
                        priority = Priority.First + 100
                    });

                Patches patchInfo = Harmony.GetPatchInfo(
                    tradeDealTryExecute);
                bool uiPrefixInstalled =
                    uiPrefix != null &&
                    patchInfo?.Prefixes.Any(patch =>
                        patch.owner == harmony.Id &&
                        patch.PatchMethod == uiPrefix) == true;
                if (!uiPrefixInstalled)
                {
                    _ready = false;
                    _resolutionFailure =
                        "TradeDeal.TryExecute UI prefix post-install audit";
                }

                if (_ready)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Canonical trade snapshot active: " +
                        "the accept command carries final semantic trade " +
                        "intent and rebuilds the deal at the shared execution " +
                        "tick (revision=" + CanonicalSnapshotRevision +
                        ", uiPrefixInstalled=" + uiPrefixInstalled +
                        ", unresolved=none)." );
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Canonical trade snapshot is " +
                        "fail-closed; Multiplayer UI trade acceptance is " +
                        "blocked because unresolved=" +
                        _resolutionFailure + ".");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Canonical trade snapshot apply " +
                    "failed: " + e.Message);
            }
        }

        private static string[] GetMissingTargets(
            MethodInfo tradeDealTryExecute)
        {
            var missing = new List<string>();
            Require(_multiplayerType, "Multiplayer", missing);
            Require(_worldCompType, "MultiplayerWorldComp", missing);
            Require(_mpTradeSessionType, "MpTradeSession", missing);
            Require(_mpTradeDealType, "MpTradeDeal", missing);
            Require(_tradingWindowType, "TradingWindow", missing);
            Require(_worldCompProperty, "Multiplayer.WorldComp", missing);
            Require(_tradingField, "MultiplayerWorldComp.trading", missing);
            Require(_drawingTradeField, "TradingWindow.drawingTrade", missing);
            Require(_currentSessionField, "MpTradeSession.current", missing);
            Require(_traderField, "MpTradeSession.trader", missing);
            Require(_negotiatorField, "MpTradeSession.playerNegotiator", missing);
            Require(_giftModeField, "MpTradeSession.giftMode", missing);
            Require(_dealField, "MpTradeSession.deal", missing);
            Require(_permanentSilverField, "MpTradeDeal.permanentSilver", missing);
            Require(_recacheThingsField, "MpTradeDeal.recacheThings", missing);
            Require(_setTradeSessionMethod, "MpTradeSession.SetTradeSession", missing);
            Require(_tryExecuteMethod, "MpTradeSession.TryExecute", missing);
            Require(_regenerateStockMethod, "Settlement_TraderTracker.RegenerateStock", missing);
            Require(_addToTradeablesMethod, "TradeDeal.AddToTradeables", missing);
            Require(_addAllTradeablesMethod, "TradeDeal.AddAllTradeables", missing);
            Require(tradeDealTryExecute, "TradeDeal.TryExecute", missing);
            return missing.ToArray();
        }

        private static void Require(
            object value,
            string name,
            List<string> missing)
        {
            if (value == null)
                missing.Add(name);
        }

        private static bool TradeDealTryExecuteUiPrefix(
            TradeDeal __instance)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _drawingTradeField?.GetValue(null) == null)
            {
                return true;
            }

            // Once Multiplayer's TradingWindow is drawing, this method is the
            // accept boundary owned by the native session-only prefix. Block
            // it before reading any optional replay internals, otherwise one
            // missing field would accidentally restore that unsafe fallback.
            if (!_ready || _syncExecuteCanonicalTrade == null)
            {
                LogFailureOnce(
                    "canonical replay is unavailable; trade blocked; " +
                    "unresolved=" + (_resolutionFailure ?? "unknown"));
                return false;
            }

            object session = _currentSessionField?.GetValue(null);
            object deal = session == null
                ? null
                : _dealField.GetValue(session);
            if (session == null || !ReferenceEquals(deal, __instance))
            {
                LogFailureOnce(
                    "drawn Multiplayer trade session/deal is unavailable; " +
                    "trade blocked");
                return false;
            }

            try
            {
                int sessionId = ((Session)session).SessionId;
                object trader = _traderField.GetValue(session);
                Pawn negotiator = _negotiatorField.GetValue(session) as Pawn;
                bool giftMode = (bool)_giftModeField.GetValue(session);
                string intent = BuildIntentPayload(deal);

                bool sent = _syncExecuteCanonicalTrade.DoSync(
                    null,
                    sessionId,
                    BuildTraderKey(trader),
                    negotiator,
                    giftMode,
                    intent);
                if (!sent)
                {
                    LogFailureOnce(
                        "canonical trade intent was not queued; trade blocked");
                }
                else if (!_loggedIntentQueued)
                {
                    _loggedIntentQueued = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Canonical trade intent queued " +
                        "(revision=" + CanonicalSnapshotRevision +
                        ", sessionId=" + sessionId +
                        ", rows=" + CountIntentRows(intent) + ").");
                }
            }
            catch (Exception e)
            {
                LogFailureOnce(
                    "failed to send canonical trade intent: " + e.Message);
            }

            return false;
        }

        public static void SyncExecuteCanonicalTrade(
            int sessionId,
            string traderKey,
            Pawn negotiator,
            bool giftMode,
            string intentPayload)
        {
            object session = FindSession(
                sessionId,
                traderKey,
                negotiator?.thingIDNumber ?? -1);
            if (session == null)
            {
                LogFailureOnce(
                    "canonical trade session could not be resolved; " +
                    "sessionId=" + sessionId);
                return;
            }

            negotiator = _negotiatorField.GetValue(session) as Pawn ??
                         negotiator;
            Patch_TradeSessionRejoinMp.TradeFactionScopeState factionScope =
                Patch_TradeSessionRejoinMp.TryBeginTradeFactionScope(
                    negotiator);
            bool rebuildContextHeld = false;

            try
            {
                // Set the authoritative mode before publishing the session to
                // RimWorld's static TradeSession. AddAllTradeables reads the
                // static giftMode; setting the field afterwards would rebuild
                // the wrong side of the deal when peers disagreed beforehand.
                _giftModeField.SetValue(session, giftMode);
                _setTradeSessionMethod.Invoke(null, new[] { session });
                rebuildContextHeld = true;

                object deal = _dealField.GetValue(session);
                if (!TryRebuildDeal(session, deal, out IList tradeables))
                    return;

                if (!TryApplyIntent(tradeables, intentPayload))
                    return;

                // SetTradeSession also owns Multiplayer's singleton
                // SyncSessionWithTransferablesMarker. Native TryExecute sets
                // that same context itself, so release the temporary rebuild
                // context before delegating. Keeping it held here makes every
                // normal replay throw "context already set" before a trade can
                // execute.
                _setTradeSessionMethod.Invoke(
                    null,
                    new object[] { null });
                rebuildContextHeld = false;

                // This is already running inside our sync command. The native
                // SyncMethod transpiler therefore executes locally instead of
                // emitting a second network command.
                _tryExecuteMethod.Invoke(session, null);
                if (!_loggedReplayCompleted)
                {
                    _loggedReplayCompleted = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Canonical trade replay " +
                        "completed through native MpTradeSession.TryExecute " +
                        "(revision=" + CanonicalSnapshotRevision +
                        ", sessionId=" + sessionId +
                        ", rows=" + CountIntentRows(intentPayload) + ").");
                }
            }
            catch (Exception e)
            {
                LogFailureOnce(
                    "canonical trade replay failed: " +
                    (e.InnerException?.Message ?? e.Message));
            }
            finally
            {
                if (rebuildContextHeld)
                {
                    try
                    {
                        _setTradeSessionMethod.Invoke(
                            null,
                            new object[] { null });
                    }
                    catch
                    {
                        // Keep the original replay failure authoritative.
                    }
                }

                Patch_TradeSessionRejoinMp.RestoreTradeFactionScope(
                    factionScope);
            }
        }

        private static object FindSession(
            int sessionId,
            string traderKey,
            int negotiatorId)
        {
            try
            {
                object worldComp = _worldCompProperty.GetValue(null, null);
                IList trading = worldComp == null
                    ? null
                    : _tradingField.GetValue(worldComp) as IList;
                if (trading == null)
                    return null;

                object semanticFallback = null;
                for (int i = 0; i < trading.Count; i++)
                {
                    object candidate = trading[i];
                    if (candidate == null)
                        continue;

                    object candidateTrader = _traderField.GetValue(candidate);
                    Pawn candidateNegotiator =
                        _negotiatorField.GetValue(candidate) as Pawn;
                    bool semanticMatch =
                        BuildTraderKey(candidateTrader) == traderKey &&
                        (negotiatorId < 0 ||
                         candidateNegotiator?.thingIDNumber == negotiatorId);
                    int candidateId = ((Session)candidate).SessionId;
                    if (candidateId == sessionId && semanticMatch)
                        return candidate;

                    if (semanticMatch)
                    {
                        semanticFallback = candidate;
                    }
                }

                return semanticFallback;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryRebuildDeal(
            object session,
            object deal,
            out IList tradeables)
        {
            tradeables = null;
            if (session == null || deal == null)
                return false;

            try
            {
                object trader = _traderField.GetValue(session);
                Settlement_TraderTracker settlementTracker =
                    Patch_TradeSessionRejoinMp.ResolveSettlementTracker(
                        trader);
                if (settlementTracker != null)
                {
                    // The previous guard checked for a tracker directly even
                    // though MpTradeSession stores the Settlement ITrader.
                    // Regenerate unconditionally here so restored and fresh
                    // sessions allocate the same stock objects at this shared
                    // command tick before the deal is reconstructed.
                    _regenerateStockMethod.Invoke(
                        settlementTracker,
                        null);
                }

                tradeables = (deal as TradeDeal)?.AllTradeables;
                if (tradeables == null)
                    return false;

                tradeables.Clear();
                (_recacheThingsField.GetValue(deal) as HashSet<Thing>)?.Clear();

                Thing permanentSilver = ThingMaker.MakeThing(
                    ThingDefOf.Silver,
                    null);
                permanentSilver.stackCount = 0;
                _permanentSilverField.SetValue(deal, permanentSilver);
                _addToTradeablesMethod.Invoke(
                    deal,
                    new object[]
                    {
                        permanentSilver,
                        Transactor.Trader
                    });
                _addAllTradeablesMethod.Invoke(deal, null);

                NormalizeTradeables(tradeables);
                return true;
            }
            catch (Exception e)
            {
                LogFailureOnce(
                    "failed to rebuild canonical trade deal: " +
                    (e.InnerException?.Message ?? e.Message));
                return false;
            }
        }

        private static string BuildIntentPayload(object deal)
        {
            IList tradeables = deal == null
                ? null
                : (deal as TradeDeal)?.AllTradeables;
            if (tradeables == null)
                return string.Empty;

            var keyed = new List<KeyValuePair<string, Tradeable>>();
            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable tradeable = tradeables[i] as Tradeable;
                // Currency is derived by native UpdateCurrencyCount from the
                // actual item rows. Carrying the UI's cached currency count as
                // input would add a second, potentially stale authority.
                if (tradeable != null && !tradeable.IsCurrency)
                {
                    keyed.Add(new KeyValuePair<string, Tradeable>(
                        BuildTradeableKey(tradeable),
                        tradeable));
                }
            }

            keyed.Sort((left, right) =>
                KeyComparer.Compare(left.Key, right.Key));

            var occurrences = new Dictionary<string, int>(KeyComparer);
            var encoded = new List<string>(keyed.Count);
            for (int i = 0; i < keyed.Count; i++)
            {
                string key = keyed[i].Key;
                int occurrence = occurrences.TryGetValue(key, out int seen)
                    ? seen
                    : 0;
                occurrences[key] = occurrence + 1;

                if (keyed[i].Value.CountToTransfer == 0)
                    continue;

                encoded.Add(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(key)) +
                    "," + occurrence.ToString(CultureInfo.InvariantCulture) +
                    "," + keyed[i].Value.CountToTransfer.ToString(
                        CultureInfo.InvariantCulture));
            }

            return string.Join(";", encoded);
        }

        private static bool TryApplyIntent(
            IList tradeables,
            string payload)
        {
            List<IntentRow> rows;
            try
            {
                rows = ParseIntentPayload(payload);
            }
            catch (Exception e)
            {
                LogFailureOnce(
                    "invalid canonical trade payload: " + e.Message);
                return false;
            }

            var byKey = new Dictionary<string, List<Tradeable>>(
                KeyComparer);
            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable tradeable = tradeables[i] as Tradeable;
                if (tradeable == null)
                    continue;

                string key = BuildTradeableKey(tradeable);
                if (!byKey.TryGetValue(key, out List<Tradeable> matches))
                {
                    matches = new List<Tradeable>();
                    byKey.Add(key, matches);
                }
                matches.Add(tradeable);
            }

            var claimed = new HashSet<string>(KeyComparer);
            for (int i = 0; i < rows.Count; i++)
            {
                IntentRow row = rows[i];
                string claim = row.Key + "\0" +
                    row.Occurrence.ToString(CultureInfo.InvariantCulture);
                if (!claimed.Add(claim))
                {
                    LogFailureOnce(
                        "canonical trade payload contains a duplicate row; " +
                        "trade aborted before transfer");
                    return false;
                }

                if (!byKey.TryGetValue(
                        row.Key,
                        out List<Tradeable> matches) ||
                    row.Occurrence < 0 ||
                    row.Occurrence >= matches.Count)
                {
                    LogFailureOnce(
                        "canonical trade item is absent after rebuild; " +
                        "trade aborted before mutation");
                    return false;
                }

                if (row.Count < matches[row.Occurrence].GetMinimumToTransfer() ||
                    row.Count > matches[row.Occurrence].GetMaximumToTransfer())
                {
                    LogFailureOnce(
                        "canonical trade count is outside the rebuilt " +
                        "tradeable range; trade aborted before transfer");
                    return false;
                }
            }

            // Apply only after every row has been resolved and validated, so
            // malformed or incomplete snapshots cannot leave partial counts.
            ResetAllCounts(tradeables);
            for (int i = 0; i < rows.Count; i++)
            {
                IntentRow row = rows[i];
                byKey[row.Key][row.Occurrence].ForceTo(row.Count);
            }

            return true;
        }

        private static List<IntentRow> ParseIntentPayload(string payload)
        {
            var rows = new List<IntentRow>();
            if (string.IsNullOrEmpty(payload))
                return rows;

            string[] encodedRows = payload.Split(';');
            for (int i = 0; i < encodedRows.Length; i++)
            {
                string[] parts = encodedRows[i].Split(',');
                if (parts.Length != 3)
                    throw new FormatException("row field count");

                rows.Add(new IntentRow
                {
                    Key = Encoding.UTF8.GetString(
                        Convert.FromBase64String(parts[0])),
                    Occurrence = int.Parse(
                        parts[1],
                        CultureInfo.InvariantCulture),
                    Count = int.Parse(
                        parts[2],
                        CultureInfo.InvariantCulture)
                });
            }

            return rows;
        }

        private static int CountIntentRows(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return 0;

            int count = 1;
            for (int i = 0; i < payload.Length; i++)
            {
                if (payload[i] == ';')
                    count++;
            }
            return count;
        }

        private static void ResetAllCounts(IList tradeables)
        {
            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable tradeable = tradeables[i] as Tradeable;
                if (tradeable != null)
                    tradeable.ForceTo(0);
            }
        }

        private static void NormalizeTradeables(IList rawTradeables)
        {
            var tradeables = new List<Tradeable>(rawTradeables.Count);
            for (int i = 0; i < rawTradeables.Count; i++)
            {
                Tradeable tradeable = rawTradeables[i] as Tradeable;
                if (tradeable == null)
                    continue;

                tradeable.thingsColony.Sort(CompareThings);
                tradeable.thingsTrader.Sort(CompareThings);
                tradeables.Add(tradeable);
            }

            tradeables.Sort((left, right) =>
                KeyComparer.Compare(
                    BuildTradeableKey(left),
                    BuildTradeableKey(right)));
            rawTradeables.Clear();
            for (int i = 0; i < tradeables.Count; i++)
                rawTradeables.Add(tradeables[i]);
        }

        private static int CompareThings(Thing left, Thing right)
        {
            int result = KeyComparer.Compare(
                BuildThingKey(left),
                BuildThingKey(right));
            if (result != 0)
                return result;

            return (left?.thingIDNumber ?? int.MaxValue).CompareTo(
                right?.thingIDNumber ?? int.MaxValue);
        }

        private static string BuildTradeableKey(Tradeable tradeable)
        {
            if (tradeable == null)
                return string.Empty;

            string colony = BuildSideKey(tradeable.thingsColony);
            string trader = BuildSideKey(tradeable.thingsTrader);

            return string.Join(
                "|",
                tradeable.GetType().FullName ?? string.Empty,
                "C=" + colony,
                "T=" + trader);
        }

        private static string BuildSideKey(IEnumerable<Thing> things)
        {
            return string.Join(
                "~",
                things
                    .Where(thing => thing != null)
                    .GroupBy(BuildThingIdentityKey, KeyComparer)
                    .OrderBy(group => group.Key, KeyComparer)
                    .Select(group =>
                        group.Key + "#" +
                        group.Sum(thing => thing.stackCount).ToString(
                            CultureInfo.InvariantCulture))
                    .ToArray());
        }

        private static string BuildThingKey(Thing thing)
        {
            if (thing == null)
                return string.Empty;

            return BuildThingIdentityKey(thing) + "^" +
                   thing.stackCount.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildThingIdentityKey(Thing thing)
        {
            if (thing == null)
                return string.Empty;

            Pawn pawn = thing as Pawn;
            CompQuality quality = thing.TryGetComp<CompQuality>();
            return string.Join(
                "^",
                thing.GetType().FullName ?? string.Empty,
                thing.def?.defName ?? string.Empty,
                thing.Stuff?.defName ?? string.Empty,
                quality == null ? string.Empty : quality.Quality.ToString(),
                thing.HitPoints.ToString(CultureInfo.InvariantCulture),
                pawn?.kindDef?.defName ?? string.Empty,
                pawn?.Name?.ToStringFull ?? string.Empty,
                pawn == null ? string.Empty : pawn.gender.ToString());
        }

        private static string BuildTraderKey(object trader)
        {
            Settlement settlement = trader as Settlement;
            if (settlement != null)
            {
                return "Settlement:" + settlement.ID.ToString(
                    CultureInfo.InvariantCulture);
            }

            Pawn pawn = trader as Pawn;
            if (pawn != null)
            {
                return "Pawn:" + pawn.thingIDNumber.ToString(
                    CultureInfo.InvariantCulture);
            }

            ILoadReferenceable loadReferenceable =
                trader as ILoadReferenceable;
            if (loadReferenceable != null)
            {
                return (trader.GetType().FullName ?? string.Empty) + ":" +
                       loadReferenceable.GetUniqueLoadID();
            }

            ITrader typedTrader = trader as ITrader;
            return (trader?.GetType().FullName ?? "null") + ":" +
                   (typedTrader?.TraderName ?? string.Empty);
        }

        private static void LogFailureOnce(string reason)
        {
            if (_loggedFailure)
                return;

            _loggedFailure = true;
            Log.Warning(
                "[MP-MeowOnlineShop] Canonical trade snapshot: " + reason +
                ".");
        }
    }
}
