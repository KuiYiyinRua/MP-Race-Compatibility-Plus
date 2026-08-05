using Verse;

namespace GodHandMod.SettingsPages
{
    // 神之清洁设置页面
    public class CleaningSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.Cleaning".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label((string)"GodHand.Settings.Page.Cleaning".Translate(), -1f, tooltip: "GodHand.Settings.CleaningDescription".Translate());
            listing.GapLine();

            listing.CheckboxLabeled("GodHand.Settings.CleaningEnableHealing".Translate(), ref settings.cleaningEnableHealing);
            if (settings.cleaningEnableHealing)
            {
                listing.Label("GodHand.Settings.CleaningHealAmountValue".Translate(settings.cleaningHealAmount.ToString("F1")));
                settings.cleaningHealAmount = listing.Slider(settings.cleaningHealAmount, 0.1f, 20f);
                listing.CheckboxLabeled("GodHand.Settings.CleaningRemoveScars".Translate(), ref settings.cleaningRemoveScars);
                listing.CheckboxLabeled("GodHand.Settings.CleaningCureAddiction".Translate(), ref settings.cleaningCureAddiction);
                listing.CheckboxLabeled("GodHand.Settings.CleaningEnableTending".Translate(), ref settings.cleaningEnableTending);
                listing.CheckboxLabeled("GodHand.Settings.CleaningEnableRegrowth".Translate(), ref settings.cleaningEnableRegrowth);
            }

            listing.GapLine();
            listing.CheckboxLabeled("GodHand.Settings.CleaningEnablePlantGrowth".Translate(), ref settings.cleaningEnablePlantGrowth);
            if (settings.cleaningEnablePlantGrowth)
            {
                listing.Label("GodHand.Settings.CleaningPlantGrowthBonusValue".Translate(settings.cleaningPlantGrowthBonus.ToString("P0")));
                settings.cleaningPlantGrowthBonus = listing.Slider(settings.cleaningPlantGrowthBonus, 0.01f, 1.0f);
            }

            listing.Gap();
            listing.CheckboxLabeled("GodHand.Settings.CleaningEnableRepair".Translate(), ref settings.cleaningEnableRepair);
            listing.CheckboxLabeled("GodHand.Settings.CleaningEnableFoodPreservation".Translate(), ref settings.cleaningEnableFoodPreservation);
            listing.CheckboxLabeled("GodHand.Settings.CleaningPlayMusic".Translate(), ref settings.cleaningPlayMusic);
        }

        public override float GetViewHeight(GodHandSettings settings) => 700f;

        public override void Reset(GodHandSettings settings)
        {
            settings.cleaningEnableHealing = true;
            settings.cleaningHealAmount = 3f;
            settings.cleaningRemoveScars = true;
            settings.cleaningCureAddiction = true;
            settings.cleaningEnableTending = true;
            settings.cleaningEnableRegrowth = true;
            settings.cleaningEnablePlantGrowth = true;
            settings.cleaningPlantGrowthBonus = 0.05f;
            settings.cleaningEnableRepair = true;
            settings.cleaningEnableFoodPreservation = true;
            settings.cleaningPlayMusic = true;
        }

        public override int Order => 60;
    }
}
