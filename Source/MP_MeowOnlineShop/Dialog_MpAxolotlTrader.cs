using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    public class Dialog_MpAxolotlTrader : Window
    {
        private enum ViewPage
        {
            Main,
            BaseTrade,
            BookSect,
            BookTrade
        }

        private struct SectOption
        {
            public readonly string Name;
            public readonly string Category;
            public readonly string WelcomeText;

            public SectOption(string name, string category, string welcomeText)
            {
                Name = name;
                Category = category;
                WelcomeText = welcomeText;
            }
        }

        private readonly Pawn _negotiator;
        private readonly Faction _faction;
        private bool _unifiedCloseRequested;
        private string _closeSource = "unknown";
        private int _uiSnapshotTick = int.MinValue;
        private bool _tradeBlockedSnapshot;
        private string _tradeBlockedReasonSnapshot;
        private float _marketFactorSnapshot = 1.5f;
        private int _sendableSilverSnapshot;
        private readonly Dictionary<string, string> _disableReasonByTrade = new Dictionary<string, string>();
        private int _disableReasonCacheTick = int.MinValue;
        private ViewPage _page = ViewPage.Main;
        private SectOption? _currentSect;
        private const int UiSnapshotIntervalTicks = 15;
        private const int BookCacheTtlTicks = 900;

        public Pawn Negotiator => _negotiator;

        private static readonly List<SectOption> SectOptions = new List<SectOption>
        {
            new SectOption("FriendFaction_Sect_Fire".Translate(), "Fire", "FriendFaction_Sect_Welcome_Fire".Translate()),
            new SectOption("FriendFaction_Sect_Flower".Translate(), "Flower", "FriendFaction_Sect_Welcome_Flower".Translate()),
            new SectOption("FriendFaction_Sect_Water".Translate(), "Water", "FriendFaction_Sect_Welcome_Water".Translate()),
            new SectOption("FriendFaction_Sect_Thunder".Translate(), "Thunder", "FriendFaction_Sect_Welcome_Thunder".Translate()),
            new SectOption("FriendFaction_Sect_Rock".Translate(), "Rock", "FriendFaction_Sect_Welcome_Rock".Translate())
        };

        private sealed class BookCacheEntry
        {
            public int expiresAtTick;
            public List<ThingDef> books;
        }

        private static readonly Dictionary<string, BookCacheEntry> BookCacheByCategory = new Dictionary<string, BookCacheEntry>();
        private static bool _loggedTradeSyncMissingInMp;
        private static bool _loggedRequestTraderSyncMissingInMp;
        private static bool _loggedRequestMilitarySyncMissingInMp;

        public Dialog_MpAxolotlTrader(Pawn negotiator, Faction faction)
        {
            _negotiator = negotiator;
            _faction = faction;
            Log.Message($"[MP-MeowOnlineShop] Dialog_MpAxolotlTrader opened: negotiator={_negotiator?.LabelShort ?? "null"}, faction={_faction?.def?.defName ?? "null"}.");

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnCancel = true;
            forcePause = false;
            draggable = true;
            onlyOneOfTypeAllowed = true;
            optionalTitle = "RiM.ContactTrader".Translate();
        }

        public override Vector2 InitialSize => new Vector2(620f, 520f);

        private void RequestUnifiedClose(string source)
        {
            _closeSource = source ?? "unknown";
            if (_unifiedCloseRequested)
                return;
            _unifiedCloseRequested = true;
            Patch_AxolotlComms.CloseRelatedCommsWindows(_negotiator, this, _closeSource);
            if (Find.WindowStack != null && Find.WindowStack.IsOpen(this))
                Close();
        }

        private void RequestBackToMain(string source)
        {
            _page = ViewPage.Main;
            InvalidateUiSnapshot();
            Patch_AxolotlComms.CloseRelatedCommsWindows(_negotiator, this, source ?? "back_to_main");
        }

        private void RequestBackToSect(string source)
        {
            _page = ViewPage.BookSect;
            InvalidateUiSnapshot();
            Patch_AxolotlComms.CloseRelatedCommsWindows(_negotiator, this, source ?? "back_to_sect");
        }

        public override void PreClose()
        {
            if (!_unifiedCloseRequested)
            {
                _closeSource = "x_or_esc";
                _unifiedCloseRequested = true;
            }
            Patch_AxolotlComms.CloseRelatedCommsWindows(_negotiator, this, _closeSource);
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            EnsureUiSnapshot();
            if (_tradeBlockedSnapshot)
            {
                listing.Label("MP Axolotl Trade unavailable: " + _tradeBlockedReasonSnapshot);
                listing.End();
                return;
            }

            DrawCurrentPage(listing);
            listing.End();
        }

        private void DrawCurrentPage(Listing_Standard listing)
        {
            switch (_page)
            {
                case ViewPage.Main:
                    DrawMainPage(listing);
                    break;
                case ViewPage.BaseTrade:
                    DrawBaseTradePage(listing);
                    break;
                case ViewPage.BookSect:
                    DrawBookSectPage(listing);
                    break;
                case ViewPage.BookTrade:
                    DrawBookTradePage(listing);
                    break;
            }
        }

        private void DrawMainPage(Listing_Standard listing)
        {
            listing.Label("FriendFaction_SpeicalTradeDialog_DiaNodeText".Translate(_faction.LeaderTitle, _faction.leader?.Name?.ToStringFull ?? _faction.Name));
            listing.Gap(10f);

            if (listing.ButtonText("FriendFaction_OpenSpeicalTradeDialog_Text".Translate()))
                _page = ViewPage.BaseTrade;

            if (listing.ButtonText("FriendFaction_OpenBookTradeDialog_Text".Translate()))
                _page = ViewPage.BookSect;

            listing.Gap(8f);
            DrawVanillaRequestButton(listing, Patch_AxolotlComms.VanillaRequestKind.TraderCaravan);
            DrawVanillaRequestButton(listing, Patch_AxolotlComms.VanillaRequestKind.MilitaryAid);

            listing.Gap(12f);
            if (listing.ButtonText("(" + "Disconnect".Translate() + ")"))
                RequestUnifiedClose("disconnect");
        }

        private void DrawVanillaRequestButton(Listing_Standard listing, Patch_AxolotlComms.VanillaRequestKind kind)
        {
            if (!Patch_AxolotlComms.TryGetVanillaRequestUiData(_negotiator, _faction, kind, out var label, out var disabledReason))
                return;
            if (string.IsNullOrEmpty(label))
                return;

            if (string.IsNullOrEmpty(disabledReason))
            {
                if (listing.ButtonText(label))
                    RequestVanillaSupport(kind);
            }
            else
            {
                listing.Label(label + " (" + disabledReason + ")");
            }
        }

        private void DrawBaseTradePage(Listing_Standard listing)
        {
            listing.Label("FriendFaction_SpeicalTradeDialog_DiaNodeText".Translate(_faction.LeaderTitle, _faction.leader?.Name?.ToStringFull ?? _faction.Name));
            listing.Gap(8f);
            var slave = DefDatabase<PawnKindDef>.GetNamedSilentFail("Axolotl_Slave");
            var slaveLabel = slave?.label ?? "Axolotl_Slave";
            DrawTradeButton(listing, "base:pawn", "FriendFaction_BuyPawns_Option".Translate(slaveLabel, Patch_AxolotlComms.CalculatePawnPrice(Patch_AxolotlComms.GetMarketFactor(_faction))), "alliance_required");
            DrawTradeButton(listing, "base:bamboo", BuildThingOfferLabel("Axolotl_Bamboo", 225), null);
            if (Patch_AxolotlComms.IsAlchemyFinished())
                DrawTradeButton(listing, "base:core", BuildThingOfferLabel("Axolotl_CoreOfHeavenReachingCauldron", 1), null);

            listing.Gap(10f);
            if (listing.ButtonText("Back".Translate()))
                RequestBackToMain("base_trade_back");
            if (listing.ButtonText("(" + "Disconnect".Translate() + ")"))
                RequestUnifiedClose("disconnect");
        }

        private void DrawBookSectPage(Listing_Standard listing)
        {
            listing.Label("FriendFaction_BookTradeDialog_DiaNodeText".Translate());
            listing.Gap(8f);
            foreach (var sect in SectOptions)
            {
                if (listing.ButtonText(sect.Name))
                {
                    _currentSect = sect;
                    _page = ViewPage.BookTrade;
                }
            }

            listing.Gap(10f);
            if (listing.ButtonText("Back".Translate()))
                RequestBackToMain("book_sect_back");
            if (listing.ButtonText("(" + "Disconnect".Translate() + ")"))
                RequestUnifiedClose("disconnect");
        }

        private void DrawBookTradePage(Listing_Standard listing)
        {
            var sect = _currentSect ?? SectOptions[0];
            listing.Label(sect.WelcomeText);
            listing.Gap(8f);

            var books = GetBooksForSect(sect.Category);
            if (books.Count == 0)
            {
                listing.Label("NoneLower".Translate());
            }
            else
            {
                foreach (var book in books)
                    DrawTradeButton(listing, "book:" + book.defName, BuildThingOfferLabel(book.defName, 1), null);
            }

            listing.Gap(10f);
            if (listing.ButtonText("Back".Translate()))
                RequestBackToSect("book_trade_back");
            if (listing.ButtonText("(" + "Disconnect".Translate() + ")"))
                RequestUnifiedClose("disconnect");
        }

        private string BuildThingOfferLabel(string thingDefName, int count)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            if (def == null)
                return thingDefName;
            EnsureUiSnapshot();
            int price = Patch_AxolotlComms.CalculateThingPrice(def, count, _marketFactorSnapshot);
            return "FriendFaction_BuyThings_Option".Translate(count, def.label, price.ToString());
        }

        private List<ThingDef> GetBooksForSect(string category)
        {
            int tick = GetTicksGame();
            BookCacheEntry entry;
            if (BookCacheByCategory.TryGetValue(category ?? "", out entry) && entry != null && entry.expiresAtTick > tick && entry.books != null)
                return entry.books;

            var books = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(td => td != null && td.defName != null)
                .Where(td => td.defName.Contains("Axolotl_Book") && td.defName.Contains(category))
                .Where(Patch_AxolotlComms.IsBookMainType)
                .OrderBy(td => ExtractSortDigit(td.defName))
                .ToList();

            BookCacheByCategory[category ?? ""] = new BookCacheEntry
            {
                expiresAtTick = tick + BookCacheTtlTicks,
                books = books
            };
            return books;
        }

        private int ExtractSortDigit(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            foreach (char c in value)
            {
                if (char.IsDigit(c))
                    return c - '0';
            }
            return 0;
        }

        private void DrawTradeButton(Listing_Standard listing, string tradeId, string label, string extraRule)
        {
            string disableReason = GetDisableReason(tradeId, extraRule);
            if (disableReason == null)
            {
                if (listing.ButtonText(label))
                    RequestTrade(tradeId);
            }
            else
            {
                listing.Label(label + " (" + disableReason + ")");
            }
        }

        private string GetDisableReason(string tradeId, string extraRule)
        {
            EnsureUiSnapshot();
            int tick = GetTicksGame();
            if (_disableReasonCacheTick != tick)
            {
                _disableReasonByTrade.Clear();
                _disableReasonCacheTick = tick;
            }

            string cacheKey = (tradeId ?? "null") + "|" + (extraRule ?? "none");
            string cached;
            if (_disableReasonByTrade.TryGetValue(cacheKey, out cached))
                return cached;

            string reason = null;
            if (extraRule == "alliance_required" && _faction.PlayerRelationKind != FactionRelationKind.Ally)
                reason = "FriendFaction_BuyPawns_DisableReason_MustAllyFaction".Translate();

            if (reason == null && _negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
                reason = "WorkTypeDisablesOption".Translate(SkillDefOf.Social.label);
            if (reason == null && _faction.PlayerRelationKind == FactionRelationKind.Hostile)
                reason = "FriendFaction_BuyThings_DisableReason_HostileFaction".Translate();

            int needSilver = EstimateSilverForTrade(tradeId);
            if (reason == null && needSilver > _sendableSilverSnapshot)
                reason = "FriendFaction_BuyThings_DisableReason_NotEnoughSilver".Translate();

            var cooldownKey = BuildCooldownKey(tradeId);
            if (reason == null && !string.IsNullOrEmpty(cooldownKey))
            {
                int cooldownUntil = Patch_AxolotlComms.GetFactionTradeCooldown(cooldownKey);
                int currentTick = GetTicksGame();
                if (cooldownUntil >= currentTick)
                {
                    int remainDays = (cooldownUntil - currentTick) / 60000;
                    reason = "FriendFaction_BuyThings_DisableReason_InCooldown".Translate(remainDays);
                }
            }

            _disableReasonByTrade[cacheKey] = reason;
            return reason;
        }

        private int EstimateSilverForTrade(string tradeId)
        {
            float factor = _marketFactorSnapshot;
            if (tradeId == "base:pawn")
                return Patch_AxolotlComms.CalculatePawnPrice(factor);
            if (tradeId == "base:bamboo")
                return Patch_AxolotlComms.CalculateThingPrice(DefDatabase<ThingDef>.GetNamedSilentFail("Axolotl_Bamboo"), 225, factor);
            if (tradeId == "base:core")
                return Patch_AxolotlComms.CalculateThingPrice(DefDatabase<ThingDef>.GetNamedSilentFail("Axolotl_CoreOfHeavenReachingCauldron"), 1, factor);
            if (tradeId.StartsWith("book:", StringComparison.Ordinal))
                return Patch_AxolotlComms.CalculateThingPrice(DefDatabase<ThingDef>.GetNamedSilentFail(tradeId.Substring(5)), 1, factor);
            return int.MaxValue;
        }

        private string BuildCooldownKey(string tradeId)
        {
            if (_faction == null) return null;
            if (tradeId == "base:pawn")
                return Patch_AxolotlComms.BuildPawnTradeKey(_faction, DefDatabase<PawnKindDef>.GetNamedSilentFail("Axolotl_Slave"));
            if (tradeId == "base:bamboo")
                return Patch_AxolotlComms.BuildThingTradeKey(_faction, DefDatabase<ThingDef>.GetNamedSilentFail("Axolotl_Bamboo"), 225);
            if (tradeId == "base:core")
                return Patch_AxolotlComms.BuildThingTradeKey(_faction, DefDatabase<ThingDef>.GetNamedSilentFail("Axolotl_CoreOfHeavenReachingCauldron"), 1);
            return null;
        }

        private void RequestTrade(string tradeId)
        {
            int mapIndex = _negotiator?.Map?.Index ?? -1;
            int negotiatorId = _negotiator?.thingIDNumber ?? -1;
            if (mapIndex < 0 || negotiatorId < 0 || string.IsNullOrEmpty(tradeId))
                return;

            if (Multiplayer.API.MP.IsInMultiplayer)
            {
                Patch_AxolotlComms.EnsureSyncMethodsReady("Dialog_MpAxolotlTrader.RequestTrade");
                if (Patch_AxolotlComms.SyncAxolotlTradeMethod == null)
                {
                    if (!_loggedTradeSyncMissingInMp)
                    {
                        _loggedTradeSyncMissingInMp = true;
                        Log.Warning($"[MP-MeowOnlineShop] Axolotl trade blocked: sync method missing in multiplayer (tradeId={tradeId}, mapIndex={mapIndex}, negotiatorId={negotiatorId}, syncMethodNull=true).");
                    }
                    return;
                }

                try
                {
                    Patch_AxolotlComms.SyncAxolotlTradeMethod.DoSync(null, mapIndex, negotiatorId, tradeId);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Axolotl trade sync failed in multiplayer, local fallback blocked to avoid desync: " + e.Message);
                    return;
                }
            }
            else
            {
                if (Patch_AxolotlComms.SyncAxolotlTradeMethod != null)
                {
                    try
                    {
                        Patch_AxolotlComms.SyncAxolotlTradeMethod.DoSync(null, mapIndex, negotiatorId, tradeId);
                    }
                    catch (Exception)
                    {
                        Patch_AxolotlComms.RequestAxolotlTrade(mapIndex, negotiatorId, tradeId);
                    }
                }
                else
                {
                    Patch_AxolotlComms.RequestAxolotlTrade(mapIndex, negotiatorId, tradeId);
                }
            }

            RequestUnifiedClose("trade");
            InvalidateUiSnapshot();
        }

        private void RequestVanillaSupport(Patch_AxolotlComms.VanillaRequestKind kind)
        {
            int mapIndex = _negotiator?.Map?.Index ?? -1;
            int negotiatorId = _negotiator?.thingIDNumber ?? -1;
            if (mapIndex < 0 || negotiatorId < 0)
                return;

            if (kind == Patch_AxolotlComms.VanillaRequestKind.TraderCaravan)
            {
                if (Multiplayer.API.MP.IsInMultiplayer)
                {
                    Patch_AxolotlComms.EnsureSyncMethodsReady("Dialog_MpAxolotlTrader.RequestVanillaSupport:trader");
                    if (Patch_AxolotlComms.SyncAxolotlRequestTraderMethod == null)
                    {
                        if (!_loggedRequestTraderSyncMissingInMp)
                        {
                            _loggedRequestTraderSyncMissingInMp = true;
                            Log.Warning($"[MP-MeowOnlineShop] Axolotl trader request blocked: sync method missing in multiplayer (mapIndex={mapIndex}, negotiatorId={negotiatorId}, syncMethodNull=true).");
                        }
                        return;
                    }
                    try
                    {
                        Patch_AxolotlComms.SyncAxolotlRequestTraderMethod.DoSync(null, mapIndex, negotiatorId);
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Axolotl trader request sync failed in multiplayer, local fallback blocked to avoid desync: " + e.Message);
                        return;
                    }
                }
                else
                {
                    if (Patch_AxolotlComms.SyncAxolotlRequestTraderMethod != null)
                    {
                        try
                        {
                            Patch_AxolotlComms.SyncAxolotlRequestTraderMethod.DoSync(null, mapIndex, negotiatorId);
                        }
                        catch (Exception)
                        {
                            Patch_AxolotlComms.RequestAxolotlTraderCaravan(mapIndex, negotiatorId);
                        }
                    }
                    else
                    {
                        Patch_AxolotlComms.RequestAxolotlTraderCaravan(mapIndex, negotiatorId);
                    }
                }
                RequestUnifiedClose("request_trader");
                InvalidateUiSnapshot();
                return;
            }

            if (Multiplayer.API.MP.IsInMultiplayer)
            {
                Patch_AxolotlComms.EnsureSyncMethodsReady("Dialog_MpAxolotlTrader.RequestVanillaSupport:military");
                if (Patch_AxolotlComms.SyncAxolotlRequestMilitaryAidMethod == null)
                {
                    if (!_loggedRequestMilitarySyncMissingInMp)
                    {
                        _loggedRequestMilitarySyncMissingInMp = true;
                        Log.Warning($"[MP-MeowOnlineShop] Axolotl military request blocked: sync method missing in multiplayer (mapIndex={mapIndex}, negotiatorId={negotiatorId}, syncMethodNull=true).");
                    }
                    return;
                }
                try
                {
                    Patch_AxolotlComms.SyncAxolotlRequestMilitaryAidMethod.DoSync(null, mapIndex, negotiatorId);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Axolotl military request sync failed in multiplayer, local fallback blocked to avoid desync: " + e.Message);
                    return;
                }
            }
            else
            {
                if (Patch_AxolotlComms.SyncAxolotlRequestMilitaryAidMethod != null)
                {
                    try
                    {
                        Patch_AxolotlComms.SyncAxolotlRequestMilitaryAidMethod.DoSync(null, mapIndex, negotiatorId);
                    }
                    catch (Exception)
                    {
                        Patch_AxolotlComms.RequestAxolotlMilitaryAid(mapIndex, negotiatorId);
                    }
                }
                else
                {
                    Patch_AxolotlComms.RequestAxolotlMilitaryAid(mapIndex, negotiatorId);
                }
            }
            RequestUnifiedClose("request_military");
            InvalidateUiSnapshot();
        }

        private void EnsureUiSnapshot()
        {
            int tick = GetTicksGame();
            if (tick <= _uiSnapshotTick && _uiSnapshotTick != int.MinValue)
                return;

            if (_uiSnapshotTick != int.MinValue && tick - _uiSnapshotTick < UiSnapshotIntervalTicks)
                return;

            _uiSnapshotTick = tick;
            _marketFactorSnapshot = Patch_AxolotlComms.GetMarketFactor(_faction);
            _sendableSilverSnapshot = Patch_AxolotlComms.GetSendableSilver(_negotiator?.Map);
            _tradeBlockedSnapshot = Patch_AxolotlComms.IsTradeBlocked(_negotiator, _faction, out _tradeBlockedReasonSnapshot);
            if (!_tradeBlockedSnapshot)
                _tradeBlockedReasonSnapshot = null;
            _disableReasonByTrade.Clear();
            _disableReasonCacheTick = tick;
        }

        private void InvalidateUiSnapshot()
        {
            _uiSnapshotTick = int.MinValue;
            _disableReasonCacheTick = int.MinValue;
            _disableReasonByTrade.Clear();
        }

        private static int GetTicksGame()
        {
            return Find.TickManager?.TicksGame ?? 0;
        }
    }
}
