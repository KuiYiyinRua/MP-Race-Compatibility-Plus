using Verse;
using RimWorld;

namespace GodHandMod.SettingsPages
{
    // 通用设置页面
    public class GeneralSettingsPage : ISettingsPage
    {
        public string Label => "GodHand.Settings.Page.General".Translate();

        public void Draw(Listing_Standard listing, GodHandSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label((string)"PA's God Hands v" + GodHandModMain.MOD_VERSION, -1f, tooltip: "GodHand.Settings.Page.General".Translate());
            Text.Font = GameFont.Small;
            listing.GapLine();

            listing.Label("GodHand.Settings.FunctionSwitches".Translate());
            bool changed = false;
            bool oldGH = settings.enableGodHand;
            listing.CheckboxLabeled("GodHand.Settings.EnableGodHand".Translate(), ref settings.enableGodHand);
            if (oldGH != settings.enableGodHand) changed = true;

            bool oldGW = settings.enableGodWrench;
            listing.CheckboxLabeled("GodHand.Settings.EnableGodWrench".Translate(), ref settings.enableGodWrench);
            if (oldGW != settings.enableGodWrench) changed = true;

            bool oldCaress = settings.enableCaress;
            listing.CheckboxLabeled("GodHand.Settings.EnableCaress".Translate(), ref settings.enableCaress);
            if (oldCaress != settings.enableCaress) changed = true;

            bool oldClean = settings.enableCleaning;
            listing.CheckboxLabeled("GodHand.Settings.EnableCleaning".Translate(), ref settings.enableCleaning);
            if (oldClean != settings.enableCleaning) changed = true;

            bool oldJudg = settings.enableJudgment;
            listing.CheckboxLabeled("GodHand.Settings.EnableJudgment".Translate(), ref settings.enableJudgment);
            if (oldJudg != settings.enableJudgment) changed = true;

            bool oldPoke = settings.enablePoke;
            listing.CheckboxLabeled("GodHand.Settings.EnablePoke".Translate(), ref settings.enablePoke);
            if (oldPoke != settings.enablePoke) changed = true;

            bool oldAsst = settings.enableAssistant;
            listing.CheckboxLabeled("GodHand.Settings.EnableAssistant".Translate(), ref settings.enableAssistant);
            if (oldAsst != settings.enableAssistant) changed = true;

            bool oldProtection = settings.enableProtection;
            listing.CheckboxLabeled("GodHand.Settings.EnableProtection".Translate(), ref settings.enableProtection);
            if (oldProtection != settings.enableProtection) changed = true;

            listing.CheckboxLabeled("GodHand.Settings.Generator.EnableHandCrank".Translate(), ref settings.enableHandCrankGenerator);

            bool oldGenTool = settings.enableGeneratorGodHand;
            listing.CheckboxLabeled("GodHand.Settings.Generator.EnableGodHandCrankTool".Translate(), ref settings.enableGeneratorGodHand);
            if (oldGenTool != settings.enableGeneratorGodHand) changed = true;


            listing.GapLine();
            listing.Label("GodHand.Settings.GlobalMisc".Translate());
            listing.CheckboxLabeled("GodHand.Settings.ShowFlavorError".Translate(), ref settings.showFlavorError);
            listing.CheckboxLabeled("GodHand.Settings.EnableDebugLog".Translate(), ref settings.enableDebugLog);
            listing.CheckboxLabeled("GodHand.Settings.DisableNSFW".Translate(), ref settings.disableNSFW);
            bool oldEndRod = settings.disableEndRodGenerator;
            listing.CheckboxLabeled("GodHand.Settings.DisableEndRodGenerator".Translate(), ref settings.disableEndRodGenerator);
            if (oldEndRod != settings.disableEndRodGenerator) changed = true;
            listing.CheckboxLabeled("显示 JFA 轮廓诊断面片", ref settings.enableDebugVisuals);

            if (changed) GodHandModMain.RefreshBuildingVisibility();

            listing.GapLine();


            if (listing.ButtonText("GodHand.Settings.ResetToDefaults".Translate())) settings.ResetToDefaults();
        }


        public float GetViewHeight(GodHandSettings settings) => 500f;

        public void Reset(GodHandSettings settings)
        {
            settings.enableGodHand = true;
            settings.enableGodWrench = true;
            settings.enableCaress = true;
            settings.enableCleaning = true;
            settings.enableJudgment = true;
            settings.enablePoke = true;
            settings.enableAssistant = true;
            settings.enableProtection = true;
            settings.enableHandCrankGenerator = true;
            settings.enableGeneratorGodHand = true;
            settings.showFlavorError = true;
            settings.enableDebugLog = false;
            settings.disableNSFW = true;
            settings.disableEndRodGenerator = false;
            settings.enableDebugVisuals = false;
        }

        public int Order => 0;
    }
}
