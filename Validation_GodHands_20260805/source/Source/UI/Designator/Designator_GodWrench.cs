using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 神之扳手
    public class Designator_GodWrench : GodHandDesignatorBase
    {
        public override bool Visible => GodHandModMain.Settings.enableGodWrench;

        // 扳手模式枚举
        private enum WrenchMode
        {
            Building,
            Precision,
            Roof,
            DeepResource,
            TurretHead
        }

        // 基类接口实现
        protected override string HelpLabelKey => "GodWrench.Label";
        protected override string HelpDescKey => "GodWrench.Description";
        protected override bool ShouldShowHelp() => GodHandModMain.Settings != null && !GodHandModMain.Settings.hasSeenHelp_GodWrench;
        protected override void SetHelpSeen() => GodHandModMain.Settings.hasSeenHelp_GodWrench = true;

        protected override float MaxRadius => 50f;
        protected override float TargetRadius
        {
            get => GodHandModMain.Settings.godHandGrabRadius;
            set => GodHandModMain.Settings.godHandGrabRadius = value;
        }

        protected override bool SupportRadiusAdjustment =>
            currentMode == WrenchMode.Building || currentMode == WrenchMode.Roof || currentMode == WrenchMode.DeepResource;

        // 模式切换
        protected override bool SupportModeSwitch => true;
        protected override int MaxModeCount => Enum.GetValues(typeof(WrenchMode)).Length;
        protected override int CurrentModeIndex
        {
            get => (int)currentMode;
            set => currentMode = (WrenchMode)value;
        }
        protected override string GetModeTranslationKey(int index) => "GodHand.Wrench.Mode." + (WrenchMode)index;
        protected override Color GetModeColor(int index) => GetModeColor((WrenchMode)index);
        protected override bool IsModeEnabled(int index)
        {
            WrenchMode mode = (WrenchMode)index;
            if (mode == WrenchMode.Building) return true;
            if (mode == WrenchMode.Precision) return GodHandModMain.Settings?.godWrenchEnablePrecisionMode ?? true;
            return true;
        }

        private GodWrenchController controller = new GodWrenchController();
        private IntVec3 lastMouseCell = IntVec3.Invalid;
        private int selectionIndex = 0;
        private WrenchMode currentMode = WrenchMode.Building;

        private bool isDragging = false;
        private IntVec3 dragStartCell = IntVec3.Invalid;
        private RoofDef draggedRoof = null;
        private ThingDef draggedResource = null;
        private int draggedResourceCount = 0;

        public Designator_GodWrench()
        {
            this.defaultLabel = "GodWrench.Label".Translate();
            this.defaultDesc = "GodWrench.Description.Simple".Translate();
            this.icon = ContentFinder<Texture2D>.Get("UI/Designators/GodWrench", false) ?? ContentFinder<Texture2D>.Get("UI/Designators/Noimage", true);
            this.useMouseIcon = true;
            this.soundDragSustain = SoundDefOf.Designate_DragStandard;
            this.soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            this.soundSucceeded = SoundDefOf.Designate_PlaceBuilding;
            controller = new GodWrenchController();
        }

        protected override void SwitchMode(int dir)
        {
            if (controller.IsActive || isDragging)
            {
                controller.ForceReleaseGrab();
                CancelDrag();
            }
            base.SwitchMode(dir);
        }

        public override void ProcessInput(Event ev)
        {
            if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Tab)
            {
                selectionIndex++;
                ev.Use();
                return;
            }
            base.ProcessInput(ev);
        }

        public override bool IsInteracting => controller.IsActive || isDragging;

        public override bool CancelAction()
        {
            if (IsInteracting)
            {
                Map map = Find.CurrentMap;
                if (map != null)
                {
                    controller.ReleaseGrab(map, UI.MouseCell());
                    IsWaitingForLeftMouseUp = true; // 开启输入锁
                }
                CancelDrag();
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return true;
            }
            return base.CancelAction();
        }

        public override void SelectedUpdate()
        {
            base.SelectedUpdate();

            controller.Update();
            // 批量移动期间绘制预览
            if (controller.IsActive) controller.Draw();

            HandleInput();

            Map map = Find.CurrentMap;
            if (map != null)
            {
                if (currentMode == WrenchMode.Roof) map.roofGrid.Drawer.MarkForDraw();
                else if (currentMode == WrenchMode.DeepResource) map.deepResourceGrid.MarkForDraw();
            }
        }

        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();

            if (isDragging)
            {
                IntVec3 cell = UI.MouseCell();
                if (cell.InBounds(Find.CurrentMap))
                {
                    GenDraw.DrawTargetHighlight(new LocalTargetInfo(cell));
                    string label = (draggedRoof != null) ? draggedRoof.LabelCap : (draggedResource != null ? draggedResource.LabelCap + " x" + draggedResourceCount : "");
                    if (!string.IsNullOrEmpty(label)) GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(cell), label, GetModeColor(currentMode));
                }
            }

            if (currentMode == WrenchMode.Building || currentMode == WrenchMode.Precision)
            {
                Thing target = FindThingUnderMouse();
                if (target != null)
                {
                    TargetHighlighter.Highlight(new TargetInfo(target), arrow: false, colonistBar: false, circleOverlay: true);
                    string label = target.LabelShortCap + (currentMode == WrenchMode.Precision ? " (" + "GodHand.Wrench.ClickToAdjust".Translate() + ")" : "");
                    IntVec3 cell = UI.MouseCell();
                    if (cell.InBounds(Find.CurrentMap))
                    {
                        int count = CountGrabbableThingsAt(cell);
                        if (count > 1) label += "GodHand.Item.SelectionHint".Translate(selectionIndex % count + 1, count);
                    }
                    GenMapUI.DrawThingLabel(target, label, currentMode == WrenchMode.Precision ? Color.cyan : Color.green);
                }
            }
            else if (currentMode == WrenchMode.Roof || currentMode == WrenchMode.DeepResource || currentMode == WrenchMode.TurretHead)
            {
                IntVec3 cell = UI.MouseCell();
                if (cell.InBounds(Find.CurrentMap))
                {
                    if (currentMode == WrenchMode.Roof)
                    {
                        RoofDef roof = Find.CurrentMap.roofGrid.RoofAt(cell);
                        if (roof != null) GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(cell), roof.LabelCap, GetModeColor(currentMode));
                    }
                    else if (currentMode == WrenchMode.DeepResource)
                    {
                        ThingDef res = Find.CurrentMap.deepResourceGrid.ThingDefAt(cell);
                        if (res != null) GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(cell), res.LabelCap + " x" + Find.CurrentMap.deepResourceGrid.CountAt(cell), GetModeColor(currentMode));
                    }
                    else
                    {
                        foreach (Thing t in Find.CurrentMap.thingGrid.ThingsAt(cell))
                            if (GodWrenchController.IsTurretBuilding(t)) GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(cell), "GodHand.Wrench.SnatchTurret".Translate(), Color.red);
                    }
                }
            }
        }

        private void HandleInput()
        {
            Map map = Find.CurrentMap; IntVec3 cell = UI.MouseCell();
            if (map == null || !cell.InBounds(map)) return;

            switch (currentMode)
            {
                case WrenchMode.Building: HandleBuildingMode(map, cell); break;
                case WrenchMode.Precision: HandlePrecisionMode(map, cell); break;
                case WrenchMode.Roof: HandleRoofMode(map, cell); break;
                case WrenchMode.DeepResource: HandleDeepResourceMode(map, cell); break;
                case WrenchMode.TurretHead: HandleTurretHeadMode(map, cell); break;
            }
        }

        private void HandleBuildingMode(Map map, IntVec3 cell)
        {
            bool mouseDown = GrabbingUtils.IsLeftMouseDown;

            if (mouseDown && !IsWaitingForLeftMouseUp && !controller.IsActive)
            {
                if (GrabbingUtils.IsShiftSelected && GodHandModMain.Settings.godWrenchEnableBulkScoop) controller.StartBulkScoop(cell, GodWrenchBulkHandler.ScoopType.Building);
                else { Thing t = FindThingUnderMouse(); if (t != null) controller.TryStartGrab(map, t, cell); }
                Event.current?.Use();
            }
            if (mouseDown && controller.IsActive) controller.UpdateDraggedThing(map, cell);
            if (!mouseDown && controller.IsActive) { controller.ReleaseGrab(map, cell); Event.current?.Use(); }
        }

        private void HandlePrecisionMode(Map map, IntVec3 cell)
        {
            if (Input.GetMouseButton(0) && controller.IsActive) controller.UpdateDraggedThing(map, cell);
            if (Input.GetMouseButtonUp(0) && controller.IsActive) { controller.ReleaseGrab(map, cell); Event.current?.Use(); }
            if (Input.GetMouseButtonDown(0))
            {
                Thing t = FindThingUnderMouse();
                if (t != null) { Find.WindowStack.Add(new Dialog_PrecisionAdjust(t)); Event.current?.Use(); }
            }
        }

        private void HandleRoofMode(Map map, IntVec3 cell)
        {
            bool mouseDown = GrabbingUtils.IsLeftMouseDown;

            if (mouseDown && controller.IsActive) controller.UpdateDraggedThing(map, cell);
            if (!mouseDown && controller.IsActive) { controller.ReleaseGrab(map, cell); Event.current?.Use(); }

            if (mouseDown && !isDragging)
            {
                if (GrabbingUtils.IsShiftSelected && GodHandModMain.Settings.godWrenchEnableBulkScoop) controller.StartBulkScoop(cell, GodWrenchBulkHandler.ScoopType.Roof);
                else { RoofDef r = map.roofGrid.RoofAt(cell); if (r != null) { isDragging = true; dragStartCell = cell; draggedRoof = r; map.roofGrid.SetRoof(cell, null); RefreshCellAndNeighbors(map, cell); } }
                Event.current?.Use();
            }
            if (!mouseDown && isDragging)
            {
                if (draggedRoof != null) { map.roofGrid.SetRoof(cell, draggedRoof); RefreshCellAndNeighbors(map, cell); if (dragStartCell != cell) RefreshCellAndNeighbors(map, dragStartCell); }
                CancelDrag(); Event.current?.Use();
            }
        }

        private void HandleDeepResourceMode(Map map, IntVec3 cell)
        {
            bool mouseDown = GrabbingUtils.IsLeftMouseDown;

            if (mouseDown && controller.IsActive) controller.UpdateDraggedThing(map, cell);
            if (!mouseDown && controller.IsActive) { controller.ReleaseGrab(map, cell); Event.current?.Use(); }

            if (mouseDown && !isDragging)
            {
                if (GrabbingUtils.IsShiftSelected && GodHandModMain.Settings.godWrenchEnableBulkScoop) controller.StartBulkScoop(cell, GodWrenchBulkHandler.ScoopType.DeepResource);
                else { ThingDef res = map.deepResourceGrid.ThingDefAt(cell); int count = map.deepResourceGrid.CountAt(cell); if (res != null && count > 0) { isDragging = true; dragStartCell = cell; draggedResource = res; draggedResourceCount = count; map.deepResourceGrid.SetAt(cell, null, 0); RefreshCellAndNeighbors(map, cell); } }
                Event.current?.Use();
            }
            if (!mouseDown && isDragging)
            {
                if (draggedResource != null) { map.deepResourceGrid.SetAt(cell, draggedResource, draggedResourceCount); RefreshCellAndNeighbors(map, cell); if (dragStartCell != cell) RefreshCellAndNeighbors(map, dragStartCell); }
                CancelDrag(); Event.current?.Use();
            }
        }

        private void HandleTurretHeadMode(Map map, IntVec3 cell)
        {
            bool mouseDown = GrabbingUtils.IsLeftMouseDown;

            if (mouseDown && !controller.IsActive)
            {
                foreach (Thing t in map.thingGrid.ThingsAt(cell))
                {
                    if (GodWrenchController.IsTurretBuilding(t)) { controller.TrySnatchTurretHead(map, t); Event.current?.Use(); return; }
                    if (t is GodHandTurretHead groundHead && groundHead.Spawned) { controller.TryStartGrab(map, groundHead, cell); Event.current?.Use(); return; }
                    if (t is Pawn pawn && pawn.apparel != null)
                    {
                        GodHandTurretHead worn = pawn.apparel.WornApparel.OfType<GodHandTurretHead>().FirstOrDefault();
                        if (worn != null && worn.TurretSlots.Count > 0) { controller.TryRemoveTurretFromPawn(map, pawn, worn); Event.current?.Use(); return; }
                    }
                }
            }
            if (mouseDown && controller.IsActive) controller.UpdateDraggedThing(map, cell);
            if (!mouseDown && controller.IsActive) { controller.ReleaseGrab(map, cell); Event.current?.Use(); }
        }

        private Thing FindThingUnderMouse()
        {
            Map map = Find.CurrentMap; IntVec3 cell = UI.MouseCell();
            if (map == null || !cell.InBounds(map)) return null;
            if (cell != lastMouseCell) { lastMouseCell = cell; selectionIndex = 0; }
            List<Thing> candidates = cell.GetThingList(map).Where(CanGrabThing).ToList();
            return candidates.Count == 0 ? null : candidates[selectionIndex % candidates.Count];
        }

        private int CountGrabbableThingsAt(IntVec3 cell) => cell.GetThingList(Find.CurrentMap).Count(CanGrabThing);
        private bool CanGrabThing(Thing t) => t != null && !t.Destroyed && t.Spawned && (t is Building || t is Plant || t is GodHandTurretHead);
        public override AcceptanceReport CanDesignateCell(IntVec3 c) => true;
        public override void DesignateSingleCell(IntVec3 c) { }

        private string GetModeDisplayName(WrenchMode mode) => ("GodHand.Wrench.Mode." + mode).Translate();
        private Color GetModeColor(WrenchMode mode)
        {
            switch (mode)
            {
                case WrenchMode.Precision: return Color.cyan;
                case WrenchMode.Roof: return new Color(0.3f, 1f, 0.4f);
                case WrenchMode.DeepResource: return new Color(1f, 0.8f, 0.2f);
                case WrenchMode.TurretHead: return new Color(1f, 0.2f, 0.2f);
                default: return Color.white;
            }
        }

        private void CancelDrag() { isDragging = false; dragStartCell = IntVec3.Invalid; draggedRoof = null; draggedResource = null; draggedResourceCount = 0; }
        private void RefreshCellAndNeighbors(Map map, IntVec3 cell) { RefreshCell(map, cell); foreach (IntVec3 adj in GenAdj.CellsAdjacentCardinal(cell, Rot4.North, IntVec2.One)) if (adj.InBounds(map)) RefreshCell(map, adj); }
        private void RefreshCell(Map map, IntVec3 cell) { var flags = MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings | MapMeshFlagDefOf.Terrain | MapMeshFlagDefOf.Roofs; map.mapDrawer.MapMeshDirty(cell, flags); map.glowGrid.DirtyCell(cell); }
    }
}
