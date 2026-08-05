using Verse;
using UnityEngine;

namespace GodHandMod.SettingsPages
{
    // 神之庇护设置页面
    public class ProtectionSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.Protection".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label((string)"GodHand.Settings.Page.Protection".Translate(), -1f, tooltip: "GodHand.Settings.ProtectionDescription".Translate());
            listing.GapLine();

            bool oldState = settings.enableProtection;
            listing.CheckboxLabeled("GodHand.Settings.EnableProtection".Translate(), ref settings.enableProtection);
            if (oldState != settings.enableProtection) settings.RefreshAllDesignators();
            if (settings.enableProtection)
            {
                listing.Gap();

                // 穹顶设置
                listing.Label("GodHand.Settings.ProtectionDomeRadius".Translate(settings.protectionDomeRadius.ToString("F1")));
                settings.protectionDomeRadius = listing.Slider(settings.protectionDomeRadius, 5f, 50f);

                listing.CheckboxLabeled("GodHand.Settings.ProtectionDomeInfinite".Translate(), ref settings.protectionDomeInfiniteDuration);
                if (!settings.protectionDomeInfiniteDuration)
                {
                    listing.Label("GodHand.Settings.ProtectionDomeDuration".Translate((settings.protectionDurationTicks / 2500f).ToString("F1")));
                    settings.protectionDurationTicks = (int)listing.Slider(settings.protectionDurationTicks, 2500, 300000);
                }

                // 单体设置
                listing.Gap();
                listing.Label("GodHand.Settings.ProtectionIndividualDuration".Translate((settings.protectionIndividualDurationTicks / 2500f).ToString("F1")));
                settings.protectionIndividualDurationTicks = (int)listing.Slider(settings.protectionIndividualDurationTicks, 2500, 300000);
            }

        }

        public override float GetViewHeight(GodHandSettings settings) => 600f;

        public override void Reset(GodHandSettings settings)
        {
            settings.enableProtection = true;
            settings.protectionDomeRadius = 10f;
            settings.protectionDomeInfiniteDuration = true;
            settings.protectionDurationTicks = 60000;
            settings.protectionIndividualDurationTicks = 60000;
        }

        public override int Order => 80;
    }
}
