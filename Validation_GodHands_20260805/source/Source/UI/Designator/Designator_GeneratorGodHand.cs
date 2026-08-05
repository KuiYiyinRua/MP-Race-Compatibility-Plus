using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 手摇发电机设计器
    public class Designator_GeneratorGodHand : GodHandDesignatorBase
    {
        public override bool Visible => GodHandModMain.Settings.enableGeneratorGodHand;

        protected override string HelpLabelKey => "GeneratorGodHand.Label";
        protected override string HelpDescKey => "GeneratorGodHand.Description";

        private Thing currentGeneratingTarget;

        public Designator_GeneratorGodHand()
        {
            defaultLabel = "GeneratorGodHand.Label".Translate();
            defaultDesc = "GeneratorGodHand.Description.Simple".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/GeneratorGodHand");
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map)) return false;

            // 检查建筑物是否连接到了电力网
            var t = c.GetFirstBuilding(Map);
            if (t != null && t.TryGetComp<CompPower>()?.PowerNet != null)
            {
                return true;
            }
            return "GeneratorGodHand.MustBePowerTarget".Translate();
        }

        public override void SelectedUpdate()
        {
            base.SelectedUpdate();

            // 锁定目标发电
            if (GrabbingUtils.IsLeftMouseDown)
            {
                if (currentGeneratingTarget == null)
                {
                    currentGeneratingTarget = UI.MouseCell().GetFirstBuilding(Map);
                }

                if (currentGeneratingTarget != null)
                {
                    GodHandGeneratorController.Update(currentGeneratingTarget, UI.MouseMapPosition());
                }
            }
            else
            {
                if (currentGeneratingTarget != null)
                {
                    GodHandGeneratorController.Update(null, Vector3.zero);
                    currentGeneratingTarget = null;
                }
            }
        }

        protected override bool ShouldShowHelp() => !GodHandModMain.Settings.hasSeenHelp_Generator;
        protected override void SetHelpSeen() => GodHandModMain.Settings.hasSeenHelp_Generator = true;

        public override void DesignateSingleCell(IntVec3 c)
        {
            // 按钮更新逻辑
        }

        protected override string GetModeTranslationKey(int index) => "";

        public override void DrawMouseAttachments()
        {
            if (currentGeneratingTarget != null)
            {
                // 显示实时功率
                float watts = GodHandGeneratorController.CurrentOutputWatts;
                float rpm = GodHandGeneratorController.CurrentRPM;
                string wattsText = "GodHand.Generator.OutputWatts".Translate(watts.ToString("F0"));
                string rpmText = "GodHand.Generator.RPM".Translate(rpm.ToString("F0"));

                // 将建筑位置映射到屏幕空间
                Vector2 screenPos = currentGeneratingTarget.DrawPos.MapToUIPosition();

                // 向上偏移坐标
                screenPos.y -= 90f;

                var oldFont = Text.Font;
                Text.Font = GameFont.Medium;
                Vector2 wattsSize = Text.CalcSize(wattsText);
                Vector2 rpmSize = Text.CalcSize(rpmText);
                float maxWidth = Mathf.Max(wattsSize.x, rpmSize.x);
                float totalHeight = wattsSize.y + rpmSize.y - 5f;

                Rect backgroundRect = new Rect(screenPos.x - maxWidth / 2f, screenPos.y - totalHeight / 2f, maxWidth, totalHeight);
                Rect wattsRect = new Rect(screenPos.x - wattsSize.x / 2f, screenPos.y - totalHeight / 2f, wattsSize.x, wattsSize.y);
                Rect rpmRect = new Rect(screenPos.x - rpmSize.x / 2f, wattsRect.yMax - 5f, rpmSize.x, rpmSize.y);

                // 绘制深色背景面板增强对比度
                Widgets.DrawRectFast(backgroundRect.ExpandedBy(8f), new Color(0, 0, 0, 0.4f));

                Widgets.Label(wattsRect, wattsText);
                Text.Font = GameFont.Small;
                Widgets.Label(rpmRect, rpmText);

                Text.Font = oldFont;
            }
            else
            {
                base.DrawMouseAttachments();
            }
        }
    }
}
