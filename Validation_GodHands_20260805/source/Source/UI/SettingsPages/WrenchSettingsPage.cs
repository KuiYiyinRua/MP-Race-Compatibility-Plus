using Verse;
using GodHandMod;

namespace GodHandMod.SettingsPages
{
    // 神之扳手设置页面
    public class WrenchSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.GodWrench".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label((string)"GodHand.Settings.Page.GodWrench".Translate(), -1f, tooltip: "GodHand.Settings.GodWrenchDescription".Translate());
            listing.GapLine();

            listing.Label("GodHand.Settings.GodWrenchDescription".Translate());
            listing.Gap();

            listing.CheckboxLabeled("GodHand.Settings.GodWrenchEnableBulkScoop".Translate(), ref settings.godWrenchEnableBulkScoop, "GodHand.Settings.GodWrenchEnableBulkScoop.Tooltip".Translate());
            listing.CheckboxLabeled("GodHand.Settings.GodWrenchEnablePrecisionMode".Translate(), ref settings.godWrenchEnablePrecisionMode, "GodHand.Settings.GodWrenchEnablePrecisionMode.Tooltip".Translate());

            listing.GapLine();
            listing.Label("GodHand.Settings.TurretHeadMaxTurrets".Translate(settings.turretHeadMaxTurrets));
            settings.turretHeadMaxTurrets = (int)listing.Slider(settings.turretHeadMaxTurrets, 1, 30);
        }

        public override float GetViewHeight(GodHandSettings settings) => 400f;

        public override void Reset(GodHandSettings settings)
        {
            settings.godWrenchEnableBulkScoop = true;
            settings.godWrenchEnablePrecisionMode = true;
            settings.turretHeadMaxTurrets = 5;
        }

        public override int Order => 30;
    }
}
