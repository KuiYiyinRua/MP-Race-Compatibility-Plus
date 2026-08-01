using System;
using Multiplayer.API;
using UnityEngine;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 联机主线剧情镜像窗口：仅触发端可见并可点击，动作走同步事件。
    /// </summary>
    public class Dialog_MpRigorStory : Window
    {
        private readonly int _sessionId;
        private bool _requestClose;

        public Dialog_MpRigorStory(int sessionId)
        {
            _sessionId = sessionId;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnCancel = true;
            forcePause = false;
            draggable = true;
            optionalTitle = "RigorMortis Story (MP)";
            onlyOneOfTypeAllowed = false;
        }

        public override Vector2 InitialSize => new Vector2(760f, 560f);

        public override void PreOpen()
        {
            base.PreOpen();
            Patch_RigorMortisStoryDialogs.MarkMirrorWindowOpen(_sessionId, this);
        }

        public override void PreClose()
        {
            Patch_RigorMortisStoryDialogs.MarkMirrorWindowClosed(_sessionId, this);
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            if (!RigorMortisStorySessionManager.TryGetById(_sessionId, out var session))
            {
                listing.Label("剧情会话已结束。");
                _requestClose = true;
                listing.End();
                return;
            }

            var node = RigorMortisStoryRuntimeMap.TryGetCurrentNode(session.SourceWindow);
            if (node == null)
            {
                listing.Label("等待剧情节点数据...");
                listing.End();
                return;
            }

            listing.Label(node.text.ToString());
            listing.GapLine();

            bool canChoose = MP.enabled && MP.IsInMultiplayer;
            if (!canChoose)
                listing.Label("当前不在联机会话，镜像窗口只读。");

            var options = node.options;
            if (options == null || options.Count == 0)
            {
                listing.Gap(8f);
                listing.Label("（无可用选项）");
            }
            else
            {
                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    string label = RigorMortisStoryRuntimeMap.GetOptionText(option, i);
                    bool disabledByGame = RigorMortisStoryRuntimeMap.IsOptionDisabled(option);
                    string disabledReason = RigorMortisStoryRuntimeMap.GetOptionDisabledReason(option);
                    bool canClick = canChoose && !disabledByGame;

                    if (canClick)
                    {
                        if (listing.ButtonText(label))
                            Patch_RigorMortis.TrySyncStoryChooseOption(_sessionId, i, session.Version);
                    }
                    else
                    {
                        if (disabledByGame && !string.IsNullOrEmpty(disabledReason))
                            listing.Label($"{label} ({disabledReason})");
                        else
                            listing.Label(label);
                    }
                    listing.Gap(4f);
                }
            }

            listing.End();

            if (_requestClose && Find.WindowStack.IsOpen(this))
                Close();
        }
    }
}
