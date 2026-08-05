using Verse;
using UnityEngine;

namespace GodHandMod.SettingsPages
{
    // 神之手核心设置页面
    public class GodHandSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.GodHand".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Label((string)"GodHand.Settings.Page.GodHand".Translate(), -1f, tooltip: "GodHand.Settings.GodHandDescription".Translate());
            listing.GapLine();

            listing.Label("GodHand.Settings.GodHandGrabRadius".Translate(settings.godHandGrabRadius.ToString("F1")));
            settings.godHandGrabRadius = listing.Slider(settings.godHandGrabRadius, 1.0f, 15.0f);

            listing.CheckboxLabeled("GodHand.Settings.GodHandEnableShake".Translate(), ref settings.godHandEnableShake);

            listing.Gap();
            listing.CheckboxLabeled("GodHand.Settings.CanGrabFriendly".Translate(), ref settings.canGrabFriendly);
            listing.CheckboxLabeled("GodHand.Settings.CanGrabHostile".Translate(), ref settings.canGrabHostile);
            listing.CheckboxLabeled("GodHand.Settings.CanGrabNeutral".Translate(), ref settings.canGrabNeutral);

            listing.Gap();
            listing.Label("GodHand.Settings.GodHandSubFunctions".Translate());
            listing.CheckboxLabeled("GodHand.Settings.GodHandEnableMeleeMode".Translate(), ref settings.godHandEnableMeleeMode, "GodHand.Settings.GodHandEnableMeleeMode.Tooltip".Translate());
            listing.CheckboxLabeled("GodHand.Settings.GodHandEnableShootingMode".Translate(), ref settings.godHandEnableShootingMode, "GodHand.Settings.GodHandEnableShootingMode.Tooltip".Translate());
            listing.CheckboxLabeled("GodHand.Settings.GodHandEnableForceIngest".Translate(), ref settings.godHandEnableForceIngest, "GodHand.Settings.GodHandEnableForceIngest.Tooltip".Translate());

            listing.GapLine();
            listing.CheckboxLabeled("GodHand.Settings.EnableThrow".Translate(), ref settings.enableThrow);
            if (settings.enableThrow)
            {
                listing.Label("GodHand.Settings.ThrowDistanceFactor".Translate(settings.throwDistanceFactor.ToString("F1")));
                settings.throwDistanceFactor = listing.Slider(settings.throwDistanceFactor, 0.1f, 5.0f);
                listing.Label("GodHand.Settings.ThrowMaxCellsValue".Translate(settings.maxThrowCells), -1f, tooltip: "GodHand.Settings.MaxThrowCells.Tooltip".Translate());
                settings.maxThrowCells = (int)listing.Slider(settings.maxThrowCells, 5, 50);
                listing.Label("GodHand.Settings.ThrowBaseDamageValue".Translate(settings.baseDamage.ToString("F1")));
                settings.baseDamage = listing.Slider(settings.baseDamage, 0f, 50f);
            }

            listing.GapLine();
            listing.CheckboxLabeled("GodHand.Settings.PlayMemeSound".Translate(), ref settings.playMemeSound);
        }

        public override float GetViewHeight(GodHandSettings settings) => 800f;

        public override void Reset(GodHandSettings settings)
        {
            settings.godHandGrabRadius = 3.0f;
            settings.godHandEnableShake = true;
            settings.canGrabFriendly = true;
            settings.canGrabHostile = true;
            settings.canGrabNeutral = true;
            settings.godHandEnableMeleeMode = true;
            settings.godHandEnableShootingMode = true;
            settings.godHandEnableForceIngest = true;
            settings.enableThrow = true;
            settings.throwDistanceFactor = 1.0f;
            settings.maxThrowCells = 15;
            settings.baseDamage = 5f;
            settings.playMemeSound = false;
        }

        public override int Order => 10;
    }
}
