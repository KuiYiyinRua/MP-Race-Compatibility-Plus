using Verse;
using UnityEngine;

namespace GodHandMod.SettingsPages
{
    // 玩家信息设置页面
    public class PlayerInfoSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.PlayerInfo".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            CheckAndTranslateDefaults(settings);

            listing.Label((string)"GodHand.Settings.Page.PlayerInfo".Translate(), -1f, tooltip: "GodHand.Settings.PlayerInfoDescription".Translate());
            listing.GapLine();

            listing.Label("GodHand.Settings.PlayerName".Translate());
            settings.playerName = listing.TextEntry(settings.playerName);
            listing.Label("GodHand.Settings.PlayerPersona".Translate());
            settings.playerPersona = listing.TextEntry(settings.playerPersona);
            listing.Label("GodHand.Settings.PlayerInfo.AgeGender".Translate());
            settings.playerAgeGender = listing.TextEntry(settings.playerAgeGender);
            listing.Label("GodHand.Settings.PlayerInfo.Title".Translate());
            settings.playerTitle = listing.TextEntry(settings.playerTitle);
            listing.Label("GodHand.Settings.PlayerInfo.Race".Translate());
            settings.playerRace = listing.TextEntry(settings.playerRace);
            listing.Label("GodHand.Settings.PlayerInfo.BackstoryChildhood".Translate());
            settings.playerBackstoryChildhood = listing.TextEntry(settings.playerBackstoryChildhood);
            listing.Label("GodHand.Settings.PlayerInfo.BackstoryAdulthood".Translate());
            settings.playerBackstoryAdulthood = listing.TextEntry(settings.playerBackstoryAdulthood);
            listing.Label("GodHand.Settings.PlayerInfo.Traits".Translate());
            settings.playerTraits = listing.TextEntry(settings.playerTraits);
            listing.Label("GodHand.Settings.PlayerInfo.Skills".Translate());
            settings.playerSkills = listing.TextEntry(settings.playerSkills);
            listing.Label("GodHand.Settings.PlayerInfo.Health".Translate());
            settings.playerHealth = listing.TextEntry(settings.playerHealth);
            listing.Label("GodHand.Settings.PlayerInfo.Mood".Translate());
            settings.playerMood = listing.TextEntry(settings.playerMood);
            listing.Label("GodHand.Settings.PlayerInfo.Thoughts".Translate());
            settings.playerThoughts = listing.TextEntry(settings.playerThoughts);
            listing.Label("GodHand.Settings.PlayerInfo.Relations".Translate());
            settings.playerRelations = listing.TextEntry(settings.playerRelations);
            listing.Label("GodHand.Settings.PlayerInfo.Ideology".Translate());
            settings.playerIdeology = listing.TextEntry(settings.playerIdeology);
            listing.Label("GodHand.Settings.PlayerInfo.Equipment".Translate());
            settings.playerEquipment = listing.TextEntry(settings.playerEquipment);
            listing.Label("GodHand.Settings.PlayerInfo.Personality".Translate());
            settings.playerPersonality = listing.TextEntry(settings.playerPersonality);

            // 同步联动数据
            if (GUI.changed) GodHandRimTalkIntegration.SyncPlayerName();
        }

        public override float GetViewHeight(GodHandSettings settings) => 1200f;

        public override void Reset(GodHandSettings settings)
        {
            settings.playerName = "GodHand.Settings.PlayerName.Default";
            settings.playerPersona = "GodHand.Settings.PlayerPersona.Default";

            settings.playerAgeGender = "GodHand.Settings.PlayerInfo.AgeGender.Default";
            settings.playerTitle = "GodHand.Settings.PlayerInfo.Title.Default";
            settings.playerRace = "GodHand.Settings.PlayerInfo.Race.Default";
            settings.playerBackstoryChildhood = "GodHand.Settings.PlayerInfo.Childhood.Default";
            settings.playerBackstoryAdulthood = "GodHand.Settings.PlayerInfo.Adulthood.Default";
            settings.playerTraits = "GodHand.Settings.PlayerInfo.Traits.Default";
            settings.playerSkills = "GodHand.Settings.PlayerInfo.Skills.Default";
            settings.playerHealth = "GodHand.Settings.PlayerInfo.Health.Default";
            settings.playerMood = "GodHand.Settings.PlayerInfo.Mood.Default";
            settings.playerThoughts = "GodHand.Settings.PlayerInfo.Thoughts.Default";
            settings.playerRelations = "GodHand.Settings.PlayerInfo.Relations.Default";
            settings.playerIdeology = "GodHand.Settings.PlayerInfo.Ideology.Default";
            settings.playerEquipment = "GodHand.Settings.PlayerInfo.Equipment.Default";
            settings.playerPersonality = "GodHand.Settings.PlayerInfo.Personality.Default";
        }

        public override int Order => 110;

        private void CheckAndTranslateDefaults(GodHandSettings settings)
        {
            if (settings.playerName == "GodHand.Settings.PlayerName.Default") settings.playerName = "GodHand.Settings.PlayerName.Default".Translate();
            if (settings.playerPersona == "GodHand.Settings.PlayerPersona.Default") settings.playerPersona = "GodHand.Settings.PlayerPersona.Default".Translate();

            if (settings.playerAgeGender == "GodHand.Settings.PlayerInfo.AgeGender.Default") settings.playerAgeGender = "GodHand.Settings.PlayerInfo.AgeGender.Default".Translate();
            if (settings.playerTitle == "GodHand.Settings.PlayerInfo.Title.Default") settings.playerTitle = "GodHand.Settings.PlayerInfo.Title.Default".Translate();
            if (settings.playerRace == "GodHand.Settings.PlayerInfo.Race.Default") settings.playerRace = "GodHand.Settings.PlayerInfo.Race.Default".Translate();
            if (settings.playerBackstoryChildhood == "GodHand.Settings.PlayerInfo.Childhood.Default") settings.playerBackstoryChildhood = "GodHand.Settings.PlayerInfo.Childhood.Default".Translate();
            if (settings.playerBackstoryAdulthood == "GodHand.Settings.PlayerInfo.Adulthood.Default") settings.playerBackstoryAdulthood = "GodHand.Settings.PlayerInfo.Adulthood.Default".Translate();
            if (settings.playerTraits == "GodHand.Settings.PlayerInfo.Traits.Default") settings.playerTraits = "GodHand.Settings.PlayerInfo.Traits.Default".Translate();
            if (settings.playerSkills == "GodHand.Settings.PlayerInfo.Skills.Default") settings.playerSkills = "GodHand.Settings.PlayerInfo.Skills.Default".Translate();
            if (settings.playerHealth == "GodHand.Settings.PlayerInfo.Health.Default") settings.playerHealth = "GodHand.Settings.PlayerInfo.Health.Default".Translate();
            if (settings.playerMood == "GodHand.Settings.PlayerInfo.Mood.Default") settings.playerMood = "GodHand.Settings.PlayerInfo.Mood.Default".Translate();
            if (settings.playerThoughts == "GodHand.Settings.PlayerInfo.Thoughts.Default") settings.playerThoughts = "GodHand.Settings.PlayerInfo.Thoughts.Default".Translate();
            if (settings.playerRelations == "GodHand.Settings.PlayerInfo.Relations.Default") settings.playerRelations = "GodHand.Settings.PlayerInfo.Relations.Default".Translate();
            if (settings.playerIdeology == "GodHand.Settings.PlayerInfo.Ideology.Default") settings.playerIdeology = "GodHand.Settings.PlayerInfo.Ideology.Default".Translate();
            if (settings.playerEquipment == "GodHand.Settings.PlayerInfo.Equipment.Default") settings.playerEquipment = "GodHand.Settings.PlayerInfo.Equipment.Default".Translate();
            if (settings.playerPersonality == "GodHand.Settings.PlayerInfo.Personality.Default") settings.playerPersonality = "GodHand.Settings.PlayerInfo.Personality.Default".Translate();
        }
    }
}
