using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 联机专用：萌螈后勤通讯窗口。不使用 <see cref="Dialog_Negotiation"/>，避免与原通讯状态机冲突。
    /// 仅通过 <see cref="Patch_RigorMortisComms.SyncRigorTraderTrade"/> 同步交易结果。
    /// </summary>
    public class Dialog_MpRigorTrader : Window
    {
        private readonly Pawn _negotiator;
        private bool _unifiedCloseRequested;
        private string _closeSource = "unknown";
        public Pawn Negotiator => _negotiator;

        public Dialog_MpRigorTrader(Pawn negotiator)
        {
            _negotiator = negotiator;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnCancel = true;
            forcePause = false;
            draggable = true;
            onlyOneOfTypeAllowed = true;
            optionalTitle = "RiM.ContactTrader".Translate();
        }

        public override Vector2 InitialSize => new Vector2(520f, 420f);

        private void RequestUnifiedClose(string source)
        {
            _closeSource = source ?? "unknown";
            if (_unifiedCloseRequested)
                return;

            _unifiedCloseRequested = true;
            Log.Message($"[MP-MeowOnlineShop] Dialog_MpRigorTrader RequestUnifiedClose (source={_closeSource}, negotiator={_negotiator?.LabelShort ?? "null"}).");
            Patch_RigorMortisComms.CloseRelatedCommsWindows(_negotiator, this, _closeSource);

            if (Find.WindowStack != null && Find.WindowStack.IsOpen(this))
                Close();
        }

        public override void PreClose()
        {
            if (!_unifiedCloseRequested)
            {
                _closeSource = "x_or_esc";
                _unifiedCloseRequested = true;
            }

            Patch_RigorMortisComms.CloseRelatedCommsWindows(_negotiator, this, _closeSource);
            Log.Message($"[MP-MeowOnlineShop] Dialog_MpRigorTrader closing (source={_closeSource}, negotiator={_negotiator?.LabelShort ?? "null"}).");
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            string body = Patch_RigorMortisComms.GetTraderBodyText(_negotiator);
            listing.Label(body, maxHeight: 220f);

            listing.Gap(12f);

            if (Patch_RigorMortisComms.ShouldShowTradeOffer(_negotiator))
            {
                var block = Patch_RigorMortisComms.GetTradeBlockReason(_negotiator);
                if (block == null)
                {
                    if (listing.ButtonText("RiM.ContactTraderTrade".Translate()))
                    {
                        int mapIndex = _negotiator?.Map?.Index ?? -1;
                        int nid = _negotiator?.thingIDNumber ?? -1;
                        bool syncSent = false;
                        if (Patch_RigorMortisComms.SyncRigorTraderTradeMethod != null)
                        {
                            try
                            {
                                Patch_RigorMortisComms.SyncRigorTraderTradeMethod.DoSync(null, mapIndex, nid);
                                syncSent = true;
                            }
                            catch (System.Exception e)
                            {
                                Log.Warning($"[MP-MeowOnlineShop] Dialog_MpRigorTrader DoSync failed, abort local execution to avoid desync: {e.Message}");
                            }
                        }
                        else
                        {
                            Log.Warning("[MP-MeowOnlineShop] Dialog_MpRigorTrader trade aborted: SyncRigorTraderTradeMethod is null.");
                        }

                        if (!syncSent)
                        {
                            // 绝不在自定义窗口里做本地兜底执行，避免“只有点击端有运输仓”的单端状态写入。
                            return;
                        }
                        RequestUnifiedClose("trade");
                    }
                }
                else
                {
                    listing.Label(block);
                }
            }

            listing.Gap(8f);

            if (listing.ButtonText("(" + "Disconnect".Translate() + ")"))
            {
                RequestUnifiedClose("disconnect");
            }

            listing.End();
        }
    }
}
