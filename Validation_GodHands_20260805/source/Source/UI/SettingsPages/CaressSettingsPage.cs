using Verse;

namespace GodHandMod.SettingsPages
{
    // 神之爱抚设置页面
    public class CaressSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.Caress".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label((string)"GodHand.Settings.Page.Caress".Translate(), -1f, tooltip: "GodHand.Settings.CaressDescription".Translate());
            listing.GapLine();

            listing.Label("GodHand.Settings.CaressMoodBonus".Translate(settings.caressMoodBonus.ToString()));
            settings.caressMoodBonus = (int)listing.Slider(settings.caressMoodBonus, 1f, 30f);

            listing.Label("GodHand.Settings.CaressMaxStages".Translate(settings.caressMaxStages.ToString()));
            settings.caressMaxStages = (int)listing.Slider(settings.caressMaxStages, 1f, 10f);

            listing.GapLine();
            listing.CheckboxLabeled("GodHand.Settings.CaressEnablePrisonerResistanceReduction".Translate(), ref settings.caressEnablePrisonerResistanceReduction);
            if (settings.caressEnablePrisonerResistanceReduction)
            {
                listing.Label("GodHand.Settings.CaressResistanceReductionPerTickValue".Translate(settings.caressPrisonerResistanceReduction.ToString("P1")));
                settings.caressPrisonerResistanceReduction = listing.Slider(settings.caressPrisonerResistanceReduction, 0.01f, 0.5f);
            }

            listing.Gap();
            listing.CheckboxLabeled("GodHand.Settings.CaressEnableEnemySurrender".Translate(), ref settings.caressEnableEnemySurrender);
            if (settings.caressEnableEnemySurrender)
            {
                listing.Label("GodHand.Settings.CaressSurrenderChanceValue".Translate(settings.caressEnemySurrenderChance.ToString("P1")));
                settings.caressEnemySurrenderChance = listing.Slider(settings.caressEnemySurrenderChance, 0f, 0.5f);
            }

            listing.Gap();
            listing.CheckboxLabeled("GodHand.Settings.CaressEnableRemoveLoyalty".Translate(), ref settings.caressEnableRemoveLoyalty);
            if (settings.caressEnableRemoveLoyalty)
            {
                listing.Label("GodHand.Settings.CaressRemoveLoyaltyChanceValue".Translate(settings.caressRemoveLoyaltyChance.ToString("P1")));
                settings.caressRemoveLoyaltyChance = listing.Slider(settings.caressRemoveLoyaltyChance, 0f, 0.5f);
            }
        }

        public override float GetViewHeight(GodHandSettings settings) => 600f;

        public override void Reset(GodHandSettings settings)
        {
            settings.caressMoodBonus = 6;
            settings.caressMaxStages = 5;
            settings.caressEnablePrisonerResistanceReduction = true;
            settings.caressPrisonerResistanceReduction = 0.05f;
            settings.caressEnableEnemySurrender = true;
            settings.caressEnemySurrenderChance = 0.1f;
            settings.caressEnableRemoveLoyalty = true;
            settings.caressRemoveLoyaltyChance = 0.1f;
        }

        public override int Order => 50;
    }
}
