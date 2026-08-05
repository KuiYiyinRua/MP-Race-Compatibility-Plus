using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 清洁型神之手
    [StaticConstructorOnStartup]
    public class Designator_CleaningGodHand : GodHandDesignatorBase
    {
        public override bool Visible => GodHandModMain.Settings.enableCleaning;

        private bool isCleaning = false;
        private IntVec3 lastCleanedCell = IntVec3.Invalid;

        // 基类接口实现
        protected override string HelpLabelKey => "CleaningGodHand.Label";
        protected override string HelpDescKey => "CleaningGodHand.Description";
        protected override bool ShouldShowHelp() => !GodHandModMain.Settings.hasSeenHelp_Cleaning;
        protected override void SetHelpSeen() => GodHandModMain.Settings.hasSeenHelp_Cleaning = true;

        protected override bool SupportRadiusAdjustment => true;
        protected override float MinRadius => 1f;
        protected override float MaxRadius => 50f;
        protected override float TargetRadius
        {
            get => CleaningGodHandController.CleanRadius;
            set => CleaningGodHandController.CleanRadius = Mathf.RoundToInt(value);
        }

        protected override bool SupportModeSwitch => true;
        protected override int MaxModeCount => 3;
        protected override int CurrentModeIndex
        {
            get => (int)CleaningGodHandController.CurrentMode;
            set => CleaningGodHandController.CurrentMode = (CleaningMode)value;
        }

        protected override string GetModeTranslationKey(int index)
        {
            return (CleaningMode)index switch
            {
                CleaningMode.Cleaning => "GodHand.Cleaning.Mode.Cleaning",
                CleaningMode.Pumping => "GodHand.Cleaning.Mode.Pumping",
                CleaningMode.WaterErase => "GodHand.Cleaning.Mode.WaterErase",
                _ => "Unknown"
            };
        }

        protected override Color GetModeColor(int index)
        {
            return (CleaningMode)index switch
            {
                CleaningMode.Cleaning => new Color(0.3f, 1f, 0.4f),
                CleaningMode.Pumping => Color.cyan,
                CleaningMode.WaterErase => new Color(0.3f, 0.6f, 1f),
                _ => Color.white
            };
        }

        public Designator_CleaningGodHand()
        {
            defaultLabel = "CleaningGodHand.Label".Translate();
            defaultDesc = "CleaningGodHand.Description.Simple".Translate();

            icon = ContentFinder<Texture2D>.Get("UI/Designators/GodCleaning", false)
                   ?? ContentFinder<Texture2D>.Get("UI/Designators/Noimage", true);

            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
            hotKey = KeyBindingDefOf.Misc4;
        }

        protected override void ShowHelpDialog()
        {
            if (Find.WindowStack != null)
            {
                Find.WindowStack.Add(new Dialog_GodHandInfo(
                    (Texture2D)icon,
                    HelpLabelKey.Translate(),
                    "CleaningGodHand.Description".Translate(
                        GodHandModMain.Settings.cleaningHealAmount,
                        (GodHandModMain.Settings.cleaningPlantGrowthBonus * 100).ToString("F0")
                    )
                ));
            }
        }

        public override void Selected()
        {
            base.Selected();
            CleaningGodHandController.InitializeMusicPlaylist();
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            return c.InBounds(base.Map) && !c.Fogged(base.Map);
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            if (!isCleaning)
            {
                isCleaning = true;
                CleaningGodHandController.StartMusic();
            }

            CleaningGodHandController.CleanAreaAtPosition(base.Map, c);
            lastCleanedCell = c;
        }

        public override void SelectedUpdate()
        {
            // 常态显示范围预览
            DrawRangePreview();

            // 仅在按住 Shift 时允许滚轮缩放
            if (GrabbingUtils.IsShiftSelected)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    HandleScrollInput(scroll);
                }
            }

            CleaningGodHandController.UpdateMusic();
            IntVec3 mouseCell = UI.MouseCell();

            // 按住鼠标持续清洁
            if (GrabbingUtils.IsLeftMouseDown)
            {
                if (mouseCell.InBounds(Find.CurrentMap) && !mouseCell.Fogged(Find.CurrentMap) && !IsWaitingForLeftMouseUp)
                {
                    if (!isCleaning)
                    {
                        isCleaning = true;
                        CleaningGodHandController.StartMusic();
                    }
                    CleaningGodHandController.CleanAreaAtPosition(Find.CurrentMap, mouseCell);
                    lastCleanedCell = mouseCell;
                }
            }
            else if (isCleaning)
            {
                isCleaning = false;
                CleaningGodHandController.StopMusic();
                lastCleanedCell = IntVec3.Invalid;
            }

            // 必须调用基类
            base.SelectedUpdate();
        }

        public override void Deselected()
        {
            base.Deselected();
            if (isCleaning)
            {
                isCleaning = false;
                CleaningGodHandController.StopMusic();
                lastCleanedCell = IntVec3.Invalid;
            }
        }
    }


}
