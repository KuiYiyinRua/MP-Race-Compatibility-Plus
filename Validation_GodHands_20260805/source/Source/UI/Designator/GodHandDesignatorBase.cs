using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 神之手工具基类
    public abstract class GodHandDesignatorBase : Designator
    {
        // 持续交互状态
        public virtual bool IsInteracting => false;

        // 配合输入缓冲
        public bool IsWaitingForLeftMouseUp { get; set; }

        // 子类配置
        protected abstract string HelpLabelKey { get; }
        protected abstract string HelpDescKey { get; }

        // 半径控制
        protected virtual bool SupportRadiusAdjustment => false;
        protected virtual float MinRadius => 1f;
        protected virtual float MaxRadius => 10f;
        protected virtual float RadiusStep => 1f;

        // 子类重写此属性以绑定到具体的 Setting 字段
        protected virtual float TargetRadius
        {
            get => 0f;
            set { }
        }

        // 模式切换控制
        protected virtual bool SupportModeSwitch => false;
        protected virtual int CurrentModeIndex
        {
            get => 0;
            set { }
        }
        protected virtual int MaxModeCount => 0;
        protected abstract string GetModeTranslationKey(int index);

        public GodHandDesignatorBase()
        {
            useMouseIcon = true;
        }


        // 允许子类动态控制拖拽属性
        protected virtual int OverrideDraggableDimensions => 0;


        public override void ProcessInput(Event ev)
        {
            if (ShouldShowHelp())
            {
                SetHelpSeen();
                ShowHelpDialog();
                return;
            }

            base.ProcessInput(ev);
        }

        protected abstract bool ShouldShowHelp();
        protected abstract void SetHelpSeen();

        protected virtual void ShowHelpDialog()
        {
            if (Find.WindowStack != null)
            {
                Find.WindowStack.Add(new Dialog_GodHandInfo(
                    (Texture2D)icon,
                    HelpLabelKey.Translate(),
                    HelpDescKey.Translate()
                ));
            }
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                foreach (FloatMenuOption option in base.RightClickFloatMenuOptions)
                {
                    yield return option;
                }

                yield return new FloatMenuOption("GodHand.Help.View".Translate(), ShowHelpDialog);
            }
        }

        public override void SelectedUpdate()
        {
            base.SelectedUpdate();

            // 松开解除锁定
            if (IsWaitingForLeftMouseUp && !GrabbingUtils.IsLeftMouseDown)
            {
                IsWaitingForLeftMouseUp = false;
            }

            // Shift 按下且支持范围调整时
            if (SupportRadiusAdjustment && GrabbingUtils.IsShiftSelected)
            {
                // 绘制范围预览
                DrawRangePreview();

                // 直接读取滚轮输入避免事件被相机系统消耗
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    HandleScrollInput(scroll);
                }
            }
        }

        protected virtual void DrawRangePreview()
        {
            IntVec3 mouseCell = UI.MouseCell();
            if (mouseCell.InBounds(Find.CurrentMap))
            {
                int r = Mathf.RoundToInt(TargetRadius);
                GenDraw.DrawFieldEdges(CellRect.CenteredOn(mouseCell, r).Cells.ToList());
            }
        }

        public override void SelectedProcessInput(Event ev)
        {
            // 统一的模式切换处理 (Q/E)
            if (SupportModeSwitch && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Q)
                {
                    SwitchMode(-1);
                    ev.Use();
                    return;
                }
                if (ev.keyCode == KeyCode.E)
                {
                    SwitchMode(1);
                    ev.Use();
                    return;
                }
            }

            base.SelectedProcessInput(ev);
        }

        // 取消或释放抓取
        public virtual bool CancelAction()
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            return false;
        }

        protected virtual void SwitchMode(int dir)
        {
            if (MaxModeCount <= 1) return;

            int next = CurrentModeIndex;
            int count = 0;
            // 循环查找模式
            do
            {
                next = (next + dir + MaxModeCount) % MaxModeCount;
                count++;
                if (IsModeEnabled(next)) break;
            } while (count < MaxModeCount);

            if (next == CurrentModeIndex) return;

            CurrentModeIndex = next;

            // 播放音效
            SoundDefOf.Tick_High.PlayOneShotOnCamera();

            // 发送提示
            string modeName = GetModeTranslationKey(next).Translate();
            Messages.Message("GodHand.ModeSwitch".Translate(modeName), MessageTypeDefOf.SilentInput, false);
        }

        protected virtual bool IsModeEnabled(int index) => true;

        public override void DrawMouseAttachments()
        {
            if (SupportModeSwitch)
            {
                Vector2 mousePos = Event.current.mousePosition;
                Rect modeRect = new Rect(mousePos.x + 20f, mousePos.y + 20f, 250f, 30f);

                int index = CurrentModeIndex;
                string label = GetModeTranslationKey(index).Translate() + " (Q/E)";
                Color color = GetModeColor(index);

                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(modeRect, BaseContent.WhiteTex);
                GUI.color = color;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(modeRect, label);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        protected virtual Color GetModeColor(int index) => Color.white;

        protected virtual void HandleScrollInput(float scrollDelta)
        {
            // 滚轮上扩大
            // 滚轮下缩小
            float change = (scrollDelta > 0f) ? RadiusStep : -RadiusStep;
            UpdateRadius(change);
        }

        private void UpdateRadius(float delta)
        {
            float current = TargetRadius;
            float target = Mathf.Clamp(current + delta, MinRadius, MaxRadius);

            if (Mathf.Abs(target - current) > 0.001f)
            {
                TargetRadius = target;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }
    }
}
