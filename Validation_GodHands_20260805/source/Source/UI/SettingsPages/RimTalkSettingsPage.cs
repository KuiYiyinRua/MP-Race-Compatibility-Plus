using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace GodHandMod.SettingsPages
{
    // RimTalk 联动设置页面
    public class RimTalkSettingsPage : BaseSettingsPage
    {
        public override string Label => "GodHand.Settings.Page.RimTalk".Translate();

        protected override void DrawContent(Listing_Standard listing, GodHandSettings settings)
        {
            if (discoveredPromptKeys.Count == 0 && discoveredMemoryKeys.Count == 0) DiscoverKeys();
            listing.Label((string)"GodHand.Settings.Page.RimTalk".Translate(), -1f, tooltip: "GodHand.Settings.RimTalkDescription".Translate());
            listing.GapLine();
            listing.CheckboxLabeled("GodHand.Settings.EnableRimTalkIntegration".Translate(), ref settings.enableRimTalkIntegration);
            if (settings.enableRimTalkIntegration) listing.CheckboxLabeled("GodHand.Settings.EnableRimTalkEventReaction".Translate(), ref settings.enableRimTalkEventReaction);
            if (settings.enableRimTalkIntegration)
            {
                GUI.color = GodHandSettings.isRimTalkAvailable ? Color.green : Color.red;
                listing.Label(GodHandSettings.isRimTalkAvailable ? "GodHand.Settings.RimTalkDetected".Translate() : "GodHand.Settings.RimTalkNotDetected".Translate());
                GUI.color = Color.white;

                if (settings.enableRimTalkEventReaction)
                {
                    listing.Gap();
                    listing.Label("GodHand.Settings.RimTalkCooldowns".Translate());

                    listing.Label("GodHand.Settings.RimTalkGlobalCooldown".Translate(settings.rimTalkGlobalCooldown.ToString("F1")), -1f, "GodHand.Settings.RimTalkGlobalCooldown.Tooltip".Translate());
                    settings.rimTalkGlobalCooldown = listing.Slider(settings.rimTalkGlobalCooldown, 0f, 60f);

                    listing.Label("GodHand.Settings.RimTalkIndividualCooldown".Translate(settings.rimTalkIndividualCooldown.ToString("F0")), -1f, "GodHand.Settings.RimTalkIndividualCooldown.Tooltip".Translate());
                    settings.rimTalkIndividualCooldown = listing.Slider(settings.rimTalkIndividualCooldown, 0f, 300f);

                    listing.Gap();
                    listing.Label("GodHand.Settings.RimTalkChances".Translate());

                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.Thrown", ref settings.rimTalkChanceThrown);
                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.Poked", ref settings.rimTalkChancePoked);
                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.Caressed", ref settings.rimTalkChanceCaressed);
                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.Grabbed", ref settings.rimTalkChanceGrabbed);
                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.Healed", ref settings.rimTalkChanceHealed);
                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.Resurrected", ref settings.rimTalkChanceResurrected);
                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.ForcedIngest", ref settings.rimTalkChanceForcedIngest);
                    DrawChanceSlider(listing, "GodHand.Settings.RimTalkChance.JudgmentShockwave", ref settings.rimTalkChanceJudgmentShockwave);

                    listing.GapLine();
                    listing.Label("GodHand.Settings.RimTalkCustomization".Translate());
                    listing.Label("GodHand.Settings.RimTalkCustomHint".Translate());

                    if (listing.ButtonText("自动发现可用键值")) DiscoverKeys();

                    if (discoveredPromptKeys.Count > 0)
                    {
                        listing.Gap();
                        listing.Label("GodHand.Settings.RimTalkCustomPrompts".Translate());
                        foreach (string key in discoveredPromptKeys) DrawCustomTextField(listing, key, settings.customPrompts);
                    }

                    if (discoveredMemoryKeys.Count > 0)
                    {
                        listing.Gap();
                        listing.Label("GodHand.Settings.RimTalkCustomMemories".Translate());
                        foreach (string key in discoveredMemoryKeys) DrawCustomTextField(listing, key, settings.customMemories);
                    }
                }
            }
        }

        private List<string> discoveredPromptKeys = new List<string>();
        private List<string> discoveredMemoryKeys = new List<string>();

        private void DiscoverKeys()
        {
            discoveredPromptKeys.Clear();
            discoveredMemoryKeys.Clear();
            var dict = LanguageDatabase.activeLanguage.keyedReplacements;
            foreach (var key in dict.Keys)
            {
                if (key.StartsWith("GodHand.RimTalk.Prompt.") || key.StartsWith("GodHand.RimTalk.System."))
                    discoveredPromptKeys.Add(key);
                else if (key.StartsWith("GodHand.Memory.") && !key.EndsWith(".Tooltip") && !key.Contains("Prefix"))
                    discoveredMemoryKeys.Add(key);
            }
            discoveredPromptKeys.Sort();
            discoveredMemoryKeys.Sort();
        }

        private void DrawCustomTextField(Listing_Standard listing, string key, Dictionary<string, string> dict)
        {
            string current = dict.ContainsKey(key) ? dict[key] : "";
            // 淡黄键名
            GUI.color = new Color(1f, 0.9f, 0.6f);
            listing.Label(key + ":");
            GUI.color = Color.white;

            // 获取默认文本作为占位参考
            string defaultValue = key.CanTranslate() ? (string)key.Translate() : "";

            // 显示默认提示
            string buffer = current;
            if (string.IsNullOrEmpty(buffer) && !defaultValue.NullOrEmpty())
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                buffer = defaultValue;
            }

            string next = listing.TextEntry(buffer, 2);
            GUI.color = Color.white;

            // 修改处理
            if (next != buffer)
            {
                // 清空移除覆盖
                if (string.IsNullOrEmpty(next) || next == defaultValue) dict.Remove(key);
                else dict[key] = next;
            }
        }

        private void DrawChanceSlider(Listing_Standard listing, string labelKey, ref float value)
        {
            listing.Label(labelKey.Translate((value * 100f).ToString("F0")));
            value = listing.Slider(value, 0f, 1f);
        }

        public override float GetViewHeight(GodHandSettings settings)
        {
            if (!settings.enableRimTalkIntegration) return 300f;

            float h = 1200f; // 基础高度
            if (settings.enableRimTalkEventReaction)
            {
                h += 100f; // 按钮和间距 Buffer
                int totalKeys = discoveredPromptKeys.Count + discoveredMemoryKeys.Count;
                // 文本框高度
                h += totalKeys * 120f;

                if (discoveredPromptKeys.Count > 0) h += 50f;
                if (discoveredMemoryKeys.Count > 0) h += 50f;
            }
            return h;
        }

        public override void Reset(GodHandSettings settings)
        {
            settings.enableRimTalkIntegration = true;
            settings.enableRimTalkEventReaction = true;
            settings.rimTalkGlobalCooldown = 5f;
            settings.rimTalkIndividualCooldown = 30f;
            settings.rimTalkChanceThrown = 1.0f;
            settings.rimTalkChancePoked = 1.0f;
            settings.rimTalkChanceCaressed = 1.0f;
            settings.rimTalkChanceGrabbed = 0.3f;
            settings.rimTalkChanceHealed = 0.8f;
            settings.rimTalkChanceResurrected = 1.0f;
            settings.rimTalkChanceForcedIngest = 1.0f;
            settings.rimTalkChanceJudgmentShockwave = 0.5f;
            settings.customPrompts.Clear();
            settings.customMemories.Clear();
        }

        public override int Order => 100;
    }
}
