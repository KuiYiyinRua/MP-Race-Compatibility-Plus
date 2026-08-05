using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod.SettingsPages
{
    // 神之助手（工作/医疗）设置页面
    public class AssistantSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.Assistant".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("GodHand.Settings.Page.Assistant".Translate());
            Text.Font = GameFont.Small;
            listing.GapLine();

            // 基础配置
            listing.Label("GodHand.Settings.Assistant.ConcurrentTasks".Translate(settings.maxGodAssistantTasks));
            settings.maxGodAssistantTasks = (int)listing.Slider(settings.maxGodAssistantTasks, 1, 10);
            listing.Gap();

            // 平衡模式
            listing.CheckboxLabeled("GodHand.Settings.EnableSurgeryQuiz".Translate(), ref settings.enableSurgeryQuiz);
            listing.Gap();

            // 视觉相关 (后续可添加更多)
            listing.Label("GodHand.Settings.Assistant.VisualNote".Translate());
        }

        public override float GetViewHeight(GodHandSettings settings) => 400f;

        public override void Reset(GodHandSettings settings)
        {
            settings.maxGodAssistantTasks = 3;
            settings.enableSurgeryQuiz = false;
        }

        public override int Order => 20;
    }
}
