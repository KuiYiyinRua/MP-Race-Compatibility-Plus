using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;

namespace GodHandMod
{
    // 神之助手设计器
    public class Designator_GodAssistant : GodHandDesignatorBase
    {
        public override bool Visible => GodHandModMain.Settings.enableAssistant;

        private int currentAssistantMode = 0; // 工作台与医疗

        protected override string HelpLabelKey => "GodHand.AssistantHand";
        protected override string HelpDescKey => "GodHand.AssistantHandDesc";

        protected override bool SupportModeSwitch => true;
        protected override int CurrentModeIndex
        {
            get => currentAssistantMode;
            set => currentAssistantMode = value;
        }
        protected override int MaxModeCount => 2;

        protected override string GetModeTranslationKey(int index)
        {
            return index == 0 ? "GodHand.AssistantMode.WorkTable" : "GodHand.AssistantMode.Medical";
        }

        public Designator_GodAssistant()
        {
            this.defaultLabel = "GodHand.AssistantHand".Translate();
            this.defaultDesc = "GodHand.AssistantHandDesc.Simple".Translate();
            this.icon = ContentFinder<Texture2D>.Get("UI/Designators/GodAssistant", false);
            if (this.icon == null)
            {
                this.icon = ContentFinder<Texture2D>.Get("UI/Designators/Noimage", false);
                if (this.icon == null) this.icon = BaseContent.BadTex;
            }
            this.useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map)) return false;
            foreach (var t in c.GetThingList(Map))
            {
                if (CanDesignateThing(t).Accepted) return true;
            }
            return false;
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (currentAssistantMode == 0) return t is Building_WorkTable;
            if (currentAssistantMode == 1) return (t is Pawn { Spawned: true }) || (t is Building_Bed { Spawned: true });
            return false;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            foreach (var t in c.GetThingList(Map))
            {
                if (CanDesignateThing(t).Accepted)
                {
                    DesignateThing(t);
                    return;
                }
            }
        }

        public override void DesignateThing(Thing t)
        {
            GodAssistantController.Execute(t, Map, currentAssistantMode);
        }

        public override void SelectedUpdate()
        {
            base.SelectedUpdate();
            // 在鼠标位置显示当前模式的高亮
            TargetInfo target = TargetUnderMouse();
            if (target.HasThing && CanDesignateThing(target.Thing).Accepted)
            {
                TargetHighlighter.Highlight(target, arrow: true, colonistBar: true, circleOverlay: true);
            }
        }

        private TargetInfo TargetUnderMouse()
        {
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(Map)) return TargetInfo.Invalid;

            foreach (Thing t in cell.GetThingList(Map))
            {
                if (CanDesignateThing(t).Accepted) return new TargetInfo(t);
            }
            return TargetInfo.Invalid;
        }

        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();
            TargetInfo target = TargetUnderMouse();
            if (target.HasThing)
            {
                GenMapUI.DrawThingLabel(target.Thing, GetModeTranslationKey(currentAssistantMode).Translate(), Color.white);
            }
        }

        protected override bool ShouldShowHelp() => !GodHandModMain.Settings.hasSeenHelp_Assistant;
        protected override void SetHelpSeen() => GodHandModMain.Settings.hasSeenHelp_Assistant = true;
    }
}
