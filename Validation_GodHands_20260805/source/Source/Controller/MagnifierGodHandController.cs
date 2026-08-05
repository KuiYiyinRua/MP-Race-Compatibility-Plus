using System;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 神之放大镜控制器
    public class MagnifierGodHandController
    {
        private Pawn targetPawn;
        private bool isActive;
        private bool visualScaleMode = true;

        private static FieldInfo renderTreeField;

        private const float MIN_SCALE = 0.1f;
        private const float MAX_SCALE = 5.0f;
        private const float SCALE_STEP = 0.1f;

        public bool IsActive => isActive && targetPawn != null;
        public bool IsVisualScaleMode => visualScaleMode;

        static MagnifierGodHandController()
        {
            renderTreeField = typeof(PawnRenderer).GetField("renderTree", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public void TryStartMagnify(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                Messages.Message("GodHand.Magnifier.InvalidTarget".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            targetPawn = pawn;
            isActive = true;
            string modeText = visualScaleMode ? "GodHand.Magnifier.VisualMode".Translate() : "GodHand.Magnifier.RealMode".Translate();
            Messages.Message("GodHand.Magnifier.Started".Translate(pawn.LabelShort, modeText), pawn, MessageTypeDefOf.NeutralEvent);
        }

        public void ToggleScaleMode()
        {
            if (!IsActive) return;
            visualScaleMode = !visualScaleMode;
            string modeText = visualScaleMode ? "GodHand.Magnifier.VisualMode".Translate() : "GodHand.Magnifier.RealMode".Translate();
            Messages.Message("GodHand.Magnifier.ModeChanged".Translate(modeText), targetPawn, MessageTypeDefOf.NeutralEvent);
        }

        public void IncreaseScale()
        {
            if (!IsActive) return;
            if (visualScaleMode) IncreaseVisualScale();
            else IncreaseRealScale();
        }

        public void DecreaseScale()
        {
            if (!IsActive) return;
            if (visualScaleMode) DecreaseVisualScale();
            else DecreaseRealScale();
        }

        private void IncreaseVisualScale()
        {
            float newScale = Mathf.Min(GetVisualScale() + SCALE_STEP, MAX_SCALE);
            SetVisualScale(newScale);
            Messages.Message("GodHand.Magnifier.VisualScaleChanged".Translate(targetPawn.LabelShort, newScale.ToString("F1")), targetPawn, MessageTypeDefOf.NeutralEvent);
        }

        private void DecreaseVisualScale()
        {
            float newScale = Mathf.Max(GetVisualScale() - SCALE_STEP, MIN_SCALE);
            SetVisualScale(newScale);
            Messages.Message("GodHand.Magnifier.VisualScaleChanged".Translate(targetPawn.LabelShort, newScale.ToString("F1")), targetPawn, MessageTypeDefOf.NeutralEvent);
        }

        private void IncreaseRealScale()
        {
            if (targetPawn?.RaceProps == null) return;
            float newScale = Mathf.Min(targetPawn.BodySize + SCALE_STEP, MAX_SCALE);
            SetBodySize(targetPawn, newScale);
            Messages.Message("GodHand.Magnifier.RealScaleChanged".Translate(targetPawn.LabelShort, newScale.ToString("F1")), targetPawn, MessageTypeDefOf.PositiveEvent);
            GodHandMemoryTracker.AddMemory(targetPawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.BodySizeChanged", newScale.ToString("F1")));
        }

        private void DecreaseRealScale()
        {
            if (targetPawn?.RaceProps == null) return;
            float newScale = Mathf.Max(targetPawn.BodySize - SCALE_STEP, MIN_SCALE);
            SetBodySize(targetPawn, newScale);
            Messages.Message("GodHand.Magnifier.RealScaleChanged".Translate(targetPawn.LabelShort, newScale.ToString("F1")), targetPawn, MessageTypeDefOf.PositiveEvent);
            GodHandMemoryTracker.AddMemory(targetPawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.BodySizeChanged", newScale.ToString("F1")));
        }

        private void SetBodySize(Pawn pawn, float newSize)
        {
            var bodySizeField = typeof(RaceProperties).GetField("baseBodySize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (bodySizeField != null) bodySizeField.SetValue(pawn.RaceProps, newSize);
        }

        private float GetVisualScale() => GetCustomVisualScale();

        private void SetVisualScale(float scale)
        {
            if (targetPawn?.Drawer?.renderer == null) return;
            SetCustomVisualScale(scale);
            targetPawn.Drawer.renderer.SetAllGraphicsDirty();
        }

        private float GetCustomVisualScale() => targetPawn?.GetComp<CompMagnifierScale>()?.visualScale ?? 1.0f;

        private void SetCustomVisualScale(float scale)
        {
            var comp = targetPawn.GetComp<CompMagnifierScale>();
            if (comp == null)
            {
                comp = new CompMagnifierScale { parent = targetPawn };
                if (targetPawn.AllComps == null) targetPawn.InitializeComps();
                targetPawn.AllComps.Add(comp);
            }
            comp.visualScale = scale;
        }

        public void Cancel()
        {
            if (!isActive) return;
            Messages.Message("GodHand.Magnifier.Cancelled".Translate(targetPawn?.LabelShort ?? ""), MessageTypeDefOf.NeutralEvent);
            isActive = false;
            targetPawn = null;
        }

        public void Update() { if (IsActive && (targetPawn == null || targetPawn.Dead || targetPawn.Destroyed)) Cancel(); }

        public void DrawTargetHighlight()
        {
            if (!IsActive) return;
            GenDraw.DrawTargetHighlight(new LocalTargetInfo(targetPawn));
            Vector3 drawPos = targetPawn.DrawPos + new Vector3(0, 0, 1f);
            GenMapUI.DrawText(new Vector2(drawPos.x, drawPos.z), visualScaleMode ? "视觉" : "真实", Color.yellow);
        }
    }

    public class CompMagnifierScale : ThingComp
    {
        public float visualScale = 1.0f;
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref visualScale, "magnifierVisualScale", 1.0f);
        }
    }
}
