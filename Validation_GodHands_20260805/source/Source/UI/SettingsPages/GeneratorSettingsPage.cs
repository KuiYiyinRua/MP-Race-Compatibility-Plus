using Verse;
using UnityEngine;
using GodHandMod;

namespace GodHandMod.SettingsPages
{
    public class GeneratorSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.Generator".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label("GodHand.Settings.Page.Generator".Translate());
            listing.GapLine();

            // 基础功能开关
            listing.CheckboxLabeled("GodHand.Settings.Generator.EnableHandCrank".Translate(), ref settings.enableHandCrankGenerator);
            listing.CheckboxLabeled("GodHand.Settings.Generator.EnableGodHandCrankTool".Translate(), ref settings.enableGeneratorGodHand);
            listing.Gap();
            listing.GapLine();

            // 发电参数设置
            listing.Label("GodHand.Settings.Generator.PhysicsSettings".Translate());

            if (settings.enableHandCrankGenerator)
            {
                listing.Label("GodHand.Settings.Generator.HandCrankMultiplier".Translate(settings.handCrankPowerMultiplier.ToString("F1")));
                settings.handCrankPowerMultiplier = listing.Slider(settings.handCrankPowerMultiplier, 0.1f, 5.0f);
            }

            if (settings.enableGeneratorGodHand)
            {
                listing.Label("GodHand.Settings.Generator.GodHandInteractionPower".Translate(settings.godCrankPowerMultiplier.ToString("F1")));
                settings.godCrankPowerMultiplier = listing.Slider(settings.godCrankPowerMultiplier, 0.1f, 10.0f);
            }

            listing.Label("GodHand.Settings.Generator.Exponent".Translate(settings.generatorExponent.ToString("F2")));
            settings.generatorExponent = listing.Slider(settings.generatorExponent, 1.0f, 3.0f);

            listing.CheckboxLabeled("GodHand.Settings.Generator.EnableFTLCrank".Translate(), ref settings.enableFTLCrank, "GodHand.Settings.Generator.EnableFTLCrank.Tooltip".Translate());

            if (settings.enableFTLCrank)
            {
                listing.Label("GodHand.Settings.Generator.FTLThreshold".Translate(settings.ftlRPMThreshold.ToString("F0")));
                settings.ftlRPMThreshold = listing.Slider(settings.ftlRPMThreshold, 50f, 500f);
            }

            listing.CheckboxLabeled("GodHand.Settings.Generator.EnableAbsurdRotation".Translate(), ref settings.enableAbsurdRotation, "GodHand.Settings.Generator.EnableAbsurdRotation.Tooltip".Translate());

            listing.CheckboxLabeled("GodHand.Settings.Generator.ShowSimplifiedFormula".Translate(), ref settings.showSimplifiedFormula, "GodHand.Settings.Generator.ShowSimplifiedFormula.Tooltip".Translate());
            listing.Gap();
            GUI.color = Color.gray;
            listing.Label("GodHand.Settings.Generator.Formula".Translate());
            string formulaKey = settings.showSimplifiedFormula ? "GodHand.Settings.Generator.FormulaTextSimple" : "GodHand.Settings.Generator.FormulaText";
            listing.Label("  " + formulaKey.Translate());
            GUI.color = Color.white;
        }

        public override float GetViewHeight(GodHandSettings settings) => 550f;

        public override void Reset(GodHandSettings settings)
        {
            settings.enableHandCrankGenerator = true;
            settings.enableGeneratorGodHand = true;
            settings.handCrankPowerMultiplier = 1.0f;
            settings.godCrankPowerMultiplier = 1.0f;
            settings.generatorExponent = 1.5f;
            settings.enableFTLCrank = false;
            settings.ftlRPMThreshold = 150f;
            settings.showSimplifiedFormula = false;
            settings.enableAbsurdRotation = false;
        }

        public override int Order => 90;
    }
}
