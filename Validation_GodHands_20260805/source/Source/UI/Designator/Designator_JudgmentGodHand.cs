using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 神之裁决设计器
    public class Designator_JudgmentGodHand : GodHandDesignatorBase
    {
        public override bool Visible => GodHandModMain.Settings.enableJudgment;

        // 统一伤害波半径
        private const int DAMAGE_RADIUS = 16;
        private const float BASE_CRUSH_RADIUS = 4f;

        // 基类接口实现
        protected override string HelpLabelKey => "JudgmentGodHand.Label";
        protected override string HelpDescKey => "JudgmentGodHand.Description";

        protected override bool ShouldShowHelp() => !GodHandModMain.Settings.hasSeenHelp_Judgment;
        protected override void SetHelpSeen() => GodHandModMain.Settings.hasSeenHelp_Judgment = true;

        protected override bool SupportModeSwitch => true;
        protected override int MaxModeCount => Enum.GetValues(typeof(JudgmentMode)).Length;
        protected override int CurrentModeIndex
        {
            get => GodHandModMain.Settings.currentJudgmentMode;
            set => GodHandModMain.Settings.currentJudgmentMode = value;
        }

        protected override string GetModeTranslationKey(int index) => "GodHand.Judgment.Mode." + (JudgmentMode)index;

        public Designator_JudgmentGodHand()
        {
            defaultLabel = "JudgmentGodHand.Label".Translate();
            defaultDesc = "JudgmentGodHand.Description.Simple".Translate();
            UpdateIcon();
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Cancel;
        }

        private void UpdateIcon()
        {
            if (GodHandModMain.Settings.judgmentReviewMode)
            {
                icon = ContentFinder<Texture2D>.Get("UI/Designators/GodJudgment_Review", false)
                       ?? ContentFinder<Texture2D>.Get("UI/Designators/GodJudgment", true);
            }
            else
            {
                icon = ContentFinder<Texture2D>.Get("UI/Designators/GodJudgment", false)
                       ?? ContentFinder<Texture2D>.Get("UI/Designators/Noimage", true);
            }
        }

        protected override void ShowHelpDialog()
        {
            if (Find.WindowStack != null)
            {
                Find.WindowStack.Add(new Dialog_GodHandInfo(
                    (Texture2D)icon,
                    HelpLabelKey.Translate(),
                    HelpDescKey.Translate(
                        500f * GodHandModMain.Settings.judgmentCrushDamageMultiplier,
                        300f * GodHandModMain.Settings.judgmentShockwaveDamageMultiplier
                    )
                ));
            }
        }

        protected override void SwitchMode(int dir)
        {
            base.SwitchMode(dir);
            // 裁决使用特定的提示键
            JudgmentMode mode = (JudgmentMode)CurrentModeIndex;
            string key = "GodHand.Judgment.Mode." + mode.ToString();
            Messages.Message("GodHand.Judgment.ModeSwitch".Translate(key.Translate()), MessageTypeDefOf.NeutralEvent, false);
        }

        public override void SelectedUpdate()
        {
            base.SelectedUpdate();
            UpdateIcon();

            IntVec3 mouseCell = UI.MouseCell();
            if (!mouseCell.InBounds(Find.CurrentMap)) return;

            JudgmentMode mode = (JudgmentMode)CurrentModeIndex;

            // 显示复杂范围
            switch (mode)
            {
                case JudgmentMode.Catastrophic:
                    float shockwaveRadius = DAMAGE_RADIUS * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;
                    DrawRadiusRing(mouseCell, shockwaveRadius, new Color(1f, 0.2f, 0.2f, 0.3f));
                    float crushRadius = BASE_CRUSH_RADIUS * GodHandModMain.Settings.judgmentCrushRadiusMultiplier;
                    DrawRadiusRing(mouseCell, crushRadius, new Color(1f, 0.5f, 0f, 0.5f));
                    DrawCrossPattern(mouseCell, 1, new Color(0.8f, 0f, 0f, 0.7f));
                    break;

                case JudgmentMode.SuddenDeathField:
                    float fieldRadius = (DAMAGE_RADIUS / 2f) * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;
                    DrawRadiusRing(mouseCell, fieldRadius, new Color(1f, 1f, 1f, 0.6f));
                    break;

                case JudgmentMode.StasisLock:
                case JudgmentMode.Forgiveness:
                    float soulRadius = (DAMAGE_RADIUS / 2f) * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;
                    Color ringColor = mode == JudgmentMode.StasisLock ? new Color(0f, 1f, 0.5f, 0.4f) : new Color(0.5f, 1f, 0f, 0.4f);
                    DrawRadiusRing(mouseCell, soulRadius, ringColor);
                    break;
            }

            GenDraw.DrawTargetHighlight(new LocalTargetInfo(mouseCell));
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c) => c.InBounds(Find.CurrentMap);

        public override void DesignateSingleCell(IntVec3 c)
        {
            JudgmentGodHandController.ExecuteJudgment(c, Find.CurrentMap);
        }

        private void DrawRadiusRing(IntVec3 center, float radius, Color color)
        {
            int radiusInt = Mathf.CeilToInt(radius);
            List<IntVec3> cells = GenRadial.RadialCellsAround(center, radius, true).Where(c => c.InBounds(Find.CurrentMap) && c.DistanceTo(center) >= radius - 1f).ToList();
            if (cells.Count > 0) GenDraw.DrawFieldEdges(cells, color);
        }

        private void DrawCrossPattern(IntVec3 center, int radius, Color color)
        {
            List<IntVec3> cells = new List<IntVec3> { center };
            for (int i = 1; i <= radius; i++)
            {
                IntVec3[] adjacent = { center + new IntVec3(0, 0, i), center + new IntVec3(0, 0, -i), center + new IntVec3(i, 0, 0), center + new IntVec3(-i, 0, 0) };
                foreach (var adj in adjacent) if (adj.InBounds(Find.CurrentMap)) cells.Add(adj);
            }
            if (cells.Count > 0) GenDraw.DrawFieldEdges(cells, color);
        }

        public override void RenderHighlight(List<IntVec3> dragCells) { }
    }
}
