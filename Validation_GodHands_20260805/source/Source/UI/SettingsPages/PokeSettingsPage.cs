using Verse;

namespace GodHandMod.SettingsPages
{
    // 神之脑瓜崩设置页面
    public class PokeSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.Poke".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label((string)"GodHand.Settings.Page.Poke".Translate(), -1f, tooltip: "GodHand.Settings.PokeDescription".Translate());
            listing.GapLine();

            listing.Label("GodHand.Settings.PokeSubFunctions".Translate());
            listing.CheckboxLabeled("GodHand.Settings.PokeEnableResurrection".Translate(), ref settings.pokeEnableResurrection);
            listing.CheckboxLabeled("GodHand.Settings.PokeEnableMentalBreakInterrupt".Translate(), ref settings.pokeEnableMentalBreakInterrupt);
            listing.CheckboxLabeled("GodHand.Settings.PokeEnableInspiration".Translate(), ref settings.pokeEnableInspiration);
            listing.CheckboxLabeled("GodHand.Settings.PokeEnableFlick".Translate(), ref settings.pokeEnableFlick);
        }

        public override float GetViewHeight(GodHandSettings settings) => 400f;

        public override void Reset(GodHandSettings settings)
        {
            settings.pokeEnableResurrection = true;
            settings.pokeEnableMentalBreakInterrupt = true;
            settings.pokeEnableInspiration = true;
            settings.pokeEnableFlick = true;
        }

        public override int Order => 40;
    }
}
