using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod.SettingsPages
{
    // 神之裁决设置页面
    public class JudgmentSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.Judgment".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label((string)"GodHand.Settings.Page.Judgment".Translate());
            listing.GapLine();
            listing.Gap();

            listing.Label("GodHand.Settings.JudgmentCrushDamage".Translate((100f * settings.judgmentCrushDamageMultiplier).ToString("F0"), settings.judgmentCrushDamageMultiplier.ToString("F2")));
            settings.judgmentCrushDamageMultiplier = listing.Slider(settings.judgmentCrushDamageMultiplier, 0.1f, 10.0f);

            listing.Label("GodHand.Settings.JudgmentCrushRadiusValue".Translate(settings.judgmentCrushRadiusMultiplier.ToString("F2")));
            settings.judgmentCrushRadiusMultiplier = listing.Slider(settings.judgmentCrushRadiusMultiplier, 0.5f, 5.0f);

            listing.GapLine();

            listing.Label("GodHand.Settings.JudgmentShockwaveDamage".Translate((100f * settings.judgmentShockwaveDamageMultiplier).ToString("F0"), settings.judgmentShockwaveDamageMultiplier.ToString("F2")));
            settings.judgmentShockwaveDamageMultiplier = listing.Slider(settings.judgmentShockwaveDamageMultiplier, 0.1f, 10.0f);

            listing.Label("GodHand.Settings.JudgmentShockwaveRadiusValue".Translate(settings.judgmentShockwaveRadiusMultiplier.ToString("F2")));
            settings.judgmentShockwaveRadiusMultiplier = listing.Slider(settings.judgmentShockwaveRadiusMultiplier, 0.5f, 5.0f);

            listing.GapLine();

            listing.Gap();
            bool oldHarmonyMode = settings.judgmentReviewMode;
            listing.CheckboxLabeled("GodHand.Settings.JudgmentReviewMode".Translate(), ref settings.judgmentReviewMode, "GodHand.Settings.JudgmentReviewMode.Tooltip".Translate());
            if (oldHarmonyMode != settings.judgmentReviewMode) settings.RefreshAllDesignators();
        }

        public override float GetViewHeight(GodHandSettings settings) => 400f;

        public override void Reset(GodHandSettings settings)
        {
            settings.judgmentCrushDamageMultiplier = 1.0f;
            settings.judgmentShockwaveDamageMultiplier = 1.0f;
            settings.judgmentCrushRadiusMultiplier = 1.0f;
            settings.judgmentShockwaveRadiusMultiplier = 1.0f;
            settings.judgmentReviewMode = false;
        }

        public override int Order => 70;
    }
}
