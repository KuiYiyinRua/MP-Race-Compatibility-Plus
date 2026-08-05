using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 神之庇护设计器
    public class Designator_ProtectionGodHand : GodHandDesignatorBase
    {
        public override bool Visible => GodHandModMain.Settings.enableProtection;

        protected override string HelpLabelKey => "GodHand.Help.Protection.Label";
        protected override string HelpDescKey => "GodHand.Help.Protection.Desc";

        // 移除壁障模式相关的测量尺逻辑
        public override bool DragDrawMeasurements => false;

        protected override bool SupportRadiusAdjustment => true;
        protected override float MinRadius => 1f;
        protected override float MaxRadius => 50f;
        protected override float RadiusStep => 1f;

        protected override float TargetRadius
        {
            get
            {
                var s = GodHandModMain.Settings;
                // 0护盾1单体2消除
                if (s.currentProtectionMode == 0 || s.currentProtectionMode == 2) return s.protectionDomeRadius;
                return 1f;
            }
            set
            {
                var s = GodHandModMain.Settings;
                if (s.currentProtectionMode == 0 || s.currentProtectionMode == 2) s.protectionDomeRadius = value;
            }
        }

        protected override bool SupportModeSwitch => true;
        protected override int CurrentModeIndex
        {
            get => GodHandModMain.Settings.currentProtectionMode;
            set => GodHandModMain.Settings.currentProtectionMode = value;
        }
        protected override int MaxModeCount => 3;

        protected override string GetModeTranslationKey(int index)
        {
            switch (index)
            {
                case 0: return "GodHand.ProtectionMode.Dome";
                case 1: return "GodHand.ProtectionMode.Individual";
                case 2: return "GodHand.ProtectionMode.Erase";
                default: return "";
            }
        }

        protected override Color GetModeColor(int index)
        {
            if (index == 3) return Color.red;
            return Color.cyan;
        }

        public Designator_ProtectionGodHand()
        {
            this.defaultLabel = "GodHand.ProtectionHand".Translate();
            this.defaultDesc = "GodHand.ProtectionHandDesc.Simple".Translate();
            this.icon = ContentFinder<Texture2D>.Get("UI/Designators/GodProtection");
        }

        protected override bool ShouldShowHelp() => !GodHandModMain.Settings.hasSeenHelp_Protection;
        protected override void SetHelpSeen() => GodHandModMain.Settings.hasSeenHelp_Protection = true;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map)) return false;

            return true;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            // 单体点击处理
            if (GodHandModMain.Settings.currentProtectionMode == 0 || GodHandModMain.Settings.currentProtectionMode == 2)
            {
                ProtectionGodHandController.Execute(c, Map, GodHandModMain.Settings.currentProtectionMode);
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (GodHandModMain.Settings.currentProtectionMode == 1)
            {
                return t is Pawn;
            }
            return base.CanDesignateThing(t);
        }

        public override void DesignateThing(Thing t)
        {
            if (t is Pawn pawn && GodHandModMain.Settings.currentProtectionMode == 1)
            {
                ProtectionGodHandController.ApplyIndividual(pawn);
                return;
            }
            base.DesignateThing(t);
        }

        private IntVec3 draggingStart = IntVec3.Invalid;

        public override void SelectedUpdate()
        {
            if (SupportRadiusAdjustment)
            {
                // 仅 Dome(0) 和 Erase(2) 显示范围预览
                if (GodHandModMain.Settings.currentProtectionMode == 0 || GodHandModMain.Settings.currentProtectionMode == 2)
                {
                    DrawRangePreview();
                }

                if (GrabbingUtils.IsShiftSelected)
                {
                    float scroll = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        HandleScrollInput(scroll);
                    }
                }
            }

            // 模式1 (Individual) 手动高亮与点击逻辑
            if (GodHandModMain.Settings.currentProtectionMode == 1)
            {
                Pawn pawn = FindPawnUnderMouse();
                if (pawn != null)
                {
                    TargetHighlighter.Highlight(new TargetInfo(pawn), arrow: false, colonistBar: true, circleOverlay: true);
                    // 标签绘制移出

                    if (Input.GetMouseButtonDown(0))
                    {
                        ProtectionGodHandController.ApplyIndividual(pawn);
                    }
                }
            }

            base.SelectedUpdate();
        }

        private void DrawDraggingPreview()
        {
            // 屏障已移除
        }

        protected override void DrawRangePreview()
        {
            IntVec3 mouseCell = UI.MouseCell();
            if (mouseCell.InBounds(Map))
            {
                int mode = GodHandModMain.Settings.currentProtectionMode;
                float radius = TargetRadius;

                if (mode == 0 || mode == 2)
                {
                    GenDraw.DrawRadiusRing(mouseCell, radius);
                }
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
            Pawn best = null; float bestDist = 1.0f; // 范围增加
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
        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();
            if (GodHandModMain.Settings.currentProtectionMode == 1)
            {
                Pawn pawn = FindPawnUnderMouse();
                if (pawn != null)
                {
                    GenMapUI.DrawThingLabel(pawn, pawn.LabelShortCap, Color.cyan);
                }
            }
        }
    }
}
