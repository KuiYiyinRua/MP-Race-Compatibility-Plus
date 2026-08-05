using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 神之手工具逻辑
    [StaticConstructorOnStartup]
    public class Designator_GodHand : GodHandDesignatorBase
    {
        private static readonly Texture2D GodHandIcon = ContentFinder<Texture2D>.Get("UI/Designators/GodHand", false)
                                                        ?? ContentFinder<Texture2D>.Get("UI/Designators/Noimage", true);
        private readonly GodHandController controller;

        // 抓取模式
        private enum GrabMode { Pawn, Item }
        private GrabMode currentMode = GrabMode.Pawn;

        // 物品选择循环索引
        private int itemSelectionIndex = 0;
        private IntVec3 lastMouseCell = IntVec3.Invalid;

        // 基类接口实现
        protected override string HelpLabelKey => "GodHand.Label";
        protected override string HelpDescKey => "GodHand.Description";
        protected override bool ShouldShowHelp() => !GodHandModMain.Settings.hasSeenHelp_GodHand;
        protected override void SetHelpSeen() => GodHandModMain.Settings.hasSeenHelp_GodHand = true;

        protected override float MaxRadius => 10f;
        protected override float TargetRadius
        {
            get => GodHandModMain.Settings.godHandGrabRadius;
            set => GodHandModMain.Settings.godHandGrabRadius = value;
        }
        protected override bool SupportRadiusAdjustment => true;

        // 归一化模式切换 (Q/E)
        protected override bool SupportModeSwitch => true;
        protected override int MaxModeCount => 2;
        protected override int CurrentModeIndex
        {
            get => (int)currentMode;
            set => currentMode = (GrabMode)value;
        }
        protected override string GetModeTranslationKey(int index) => "GodHand.Mode." + (GrabMode)index;
        protected override Color GetModeColor(int index) => (GrabMode)index == GrabMode.Pawn ? Color.yellow : Color.cyan;

        public override bool DragDrawMeasurements => false;
        public override bool Visible => GodHandModMain.Settings.enableGodHand;

        public Designator_GodHand()
        {
            defaultLabel = "GodHand.Label".Translate();
            defaultDesc = "GodHand.Description.Simple".Translate();
            icon = GodHandIcon ?? BaseContent.BadTex;
            useMouseIcon = true;
            soundDragSustain = null;
            soundDragChanged = null;
            soundSucceeded = null;

            controller = new GodHandController();
        }

        protected override void SwitchMode(int dir)
        {
            if (controller.IsActive)
            {
                // 抓取时切换战斗模式
                if (dir == -1) controller.ToggleShootingMode();
                else controller.ToggleMeleeMode();
            }
            else
            {
                // 切换目标模式
                base.SwitchMode(dir);
            }
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c) => true;
        public override void DesignateSingleCell(IntVec3 c) { }

        public override void SelectedUpdate()
        {
            base.SelectedUpdate();
            // 基类处理输入
            // 这里处理 鼠标位置同步 和 物品选择循环 (Tab)

            if (currentMode == GrabMode.Item && Input.GetKeyDown(KeyCode.Tab))
            {
                itemSelectionIndex++;
                Event.current?.Use();
            }

            HandleInput();

            if (controller.IsActive)
            {
                controller.DrawDraggedThing();
                if (controller.IsShootingMode)
                {
                    Vector3 weaponPos = controller.FixedWeaponPos;
                    Vector3 mousePos = UI.MouseMapPosition();
                    GenDraw.DrawLineBetween(weaponPos, mousePos, SimpleColor.Red);
                    float range = controller.GetWeaponRange();
                    if (range > 0) GenDraw.DrawRadiusRing(weaponPos.ToIntVec3(), range);
                }
            }
        }

        public override void RenderHighlight(List<IntVec3> dragCells)
        {
            if (controller.IsActive) controller.Draw();
        }
        public override void DrawMouseAttachments()
        {
            if (!controller.IsActive)
            {
                base.DrawMouseAttachments();

                // 绘制目标高亮
                if (currentMode == GrabMode.Pawn)
                {
                    Pawn pawn = FindPawnUnderMouse();
                    if (pawn != null)
                    {
                        TargetHighlighter.Highlight(new TargetInfo(pawn), arrow: false, colonistBar: true, circleOverlay: true);
                        GenMapUI.DrawThingLabel(pawn, pawn.LabelShortCap, Color.yellow);
                    }
                }
                else
                {
                    Thing thing = FindThingUnderMouse();
                    if (thing != null)
                    {
                        TargetHighlighter.Highlight(new TargetInfo(thing), arrow: false, colonistBar: false, circleOverlay: true);
                        string label = thing.LabelShortCap;
                        if (thing.stackCount > 1) label += $" x{thing.stackCount}";
                        IntVec3 cell = UI.MouseCell();
                        if (cell.InBounds(Find.CurrentMap))
                        {
                            int itemCount = CountItemsAt(cell);
                            if (itemCount > 1) label += "GodHand.Item.SelectionHint".Translate(itemSelectionIndex % itemCount + 1, itemCount);
                        }
                        GenMapUI.DrawThingLabel(thing, label, Color.cyan);
                    }
                }
            }
            else if (controller.IsHoldingWeapon())
            {
                // 显示武器操作提示
                string weaponHint = "";
                Color hintColor = Color.white;

                if (controller.IsShootingMode)
                {
                    Vector2 mouse = Event.current.mousePosition;
                    float size = 12f;
                    float thickness = 2f;
                    GUI.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                    GUI.DrawTexture(new Rect(mouse.x - size, mouse.y - thickness / 2, size * 2, thickness), BaseContent.WhiteTex);
                    GUI.DrawTexture(new Rect(mouse.x - thickness / 2, mouse.y - size, thickness, size * 2), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                    weaponHint = "GodHand.Weapon.Mode.Shooting.Hint".Translate();
                    hintColor = new Color(0.3f, 0.6f, 1f);
                }
                else if (controller.IsMeleeMode)
                {
                    weaponHint = "GodHand.Weapon.Mode.Melee.Hint".Translate();
                    hintColor = new Color(1f, 0.3f, 0.3f);
                }
                else if (controller.IsHoldingHybridWeapon())
                {
                    weaponHint = "GodHand.Weapon.Mode.Hybrid.Hint".Translate();
                    hintColor = new Color(1f, 0.5f, 1f);
                }
                else if (controller.IsHoldingMeleeWeapon())
                {
                    weaponHint = "GodHand.Weapon.Mode.MeleeOnly.Hint".Translate();
                    hintColor = Color.yellow;
                }
                else if (controller.IsHoldingRangedWeapon())
                {
                    weaponHint = "GodHand.Weapon.Mode.RangedOnly.Hint".Translate();
                    hintColor = Color.cyan;
                }

                if (!string.IsNullOrEmpty(weaponHint))
                {
                    Vector2 mousePos = Event.current.mousePosition;
                    Rect labelRect = new Rect(mousePos.x + 20, mousePos.y + 55, 300, 30);
                    GUI.color = new Color(0, 0, 0, 0.7f);
                    GUI.DrawTexture(labelRect, BaseContent.WhiteTex);
                    GUI.color = hintColor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(labelRect, weaponHint);
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
            }
        }

        public override bool IsInteracting => controller.IsActive;

        public override bool CancelAction()
        {
            if (controller.IsActive)
            {
                Map map = Find.CurrentMap;
                if (map != null)
                {
                    controller.ReleaseGrab(map, UI.MouseCell(), Time.time);
                    IsWaitingForLeftMouseUp = true; // 开启输入锁
                }
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return true;
            }
            return base.CancelAction();
        }

        private void HandleInput()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            controller.Update();

            // 保持左键抓取逻辑
            bool mouseDown = GrabbingUtils.IsLeftMouseDown;

            if (mouseDown && !IsWaitingForLeftMouseUp)
            {
                IntVec3 mouseCell = UI.MouseCell();
                if (!mouseCell.InBounds(map)) return;

                if (!controller.IsActive)
                {
                    bool isShift = GrabbingUtils.IsShiftSelected;
                    IntVec3 targetCell = UI.MouseCell(); // 修改变量名以避免冲突
                    if (currentMode == GrabMode.Pawn)
                    {
                        Pawn target = FindPawnUnderMouse();
                        if (target != null || isShift) controller.TryStartGrab(map, target, targetCell);
                    }
                    else
                    {
                        Thing target = FindThingUnderMouse();
                        if (target != null || isShift) controller.TryStartGrabItem(map, target, targetCell);
                    }
                    Event.current?.Use();
                }
                else
                {
                    // 已激活时的持续交互
                    if (controller.IsShootingMode)
                    {
                        IntVec3 aimCell = UI.MouseCell();
                        if (aimCell.InBounds(map)) controller.ShootInShootingMode(map, aimCell, Time.time);
                    }
                    else
                    {
                        IntVec3 dragCell = IntVec3.FromVector3(UI.MouseMapPosition());
                        controller.UpdateDragged(map, dragCell, Time.time);
                        if (controller.IsMeleeMode) controller.UpdateMeleeMode(map, Time.time);
                    }
                }
            }
            else if (controller.IsActive)
            {
                // 战斗模式不释放
                if (controller.IsShootingMode)
                {
                    controller.ResetMouseRelease();
                }
                else if (!controller.IsMeleeMode)
                {
                    // 普通模式保持松开即释放
                    controller.ReleaseGrab(map, IntVec3.FromVector3(UI.MouseMapPosition()), Time.time);
                }
                Event.current?.Use();
            }
        }

        private IntVec3 lastPawnSearchCell = IntVec3.Invalid;
        private Pawn lastFoundPawn = null;
        private int lastPawnSearchTick = -1;

        private Pawn FindPawnUnderMouse()
        {
            if (Find.CurrentMap == null) return null;
            IntVec3 cell = UI.MouseCell();
            int tick = Find.TickManager.TicksGame;
            if (cell == lastPawnSearchCell && tick == lastPawnSearchTick) return lastFoundPawn;
            lastPawnSearchCell = cell; lastPawnSearchTick = tick;

            if (cell.InBounds(Find.CurrentMap))
            {
                foreach (Thing t in cell.GetThingList(Find.CurrentMap))
                    if (t is Pawn p && p.Spawned) return lastFoundPawn = p;
            }

            Vector3 mousePos = UI.MouseMapPosition();
            Pawn best = null; float bestDist = 0.49f;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    IntVec3 check = cell + new IntVec3(x, 0, z);
                    if (!check.InBounds(Find.CurrentMap)) continue;
                    foreach (Thing t in check.GetThingList(Find.CurrentMap))
                        if (t is Pawn p && p.Spawned)
                        {
                            float d = (p.DrawPos - mousePos).MagnitudeHorizontalSquared();
                            if (d < bestDist) { bestDist = d; best = p; }
                        }
                }
            return lastFoundPawn = best;
        }

        private Thing FindThingUnderMouse()
        {
            if (Find.CurrentMap == null) return null;
            IntVec3 cell = UI.MouseCell();
            if (cell.InBounds(Find.CurrentMap))
            {
                List<Thing> candidates = cell.GetThingList(Find.CurrentMap).Where(t => t.def.category == ThingCategory.Item).ToList();
                if (candidates.Count > 0) return candidates[itemSelectionIndex % candidates.Count];
            }
            return null;
        }

        private int CountItemsAt(IntVec3 cell) => cell.GetThingList(Find.CurrentMap).Count(t => t.def.category == ThingCategory.Item);
    }
}
