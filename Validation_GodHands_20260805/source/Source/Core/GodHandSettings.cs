using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using GodHandMod.SettingsPages;
using System;


namespace GodHandMod
{
    // 模组设置类
    public class GodHandSettings : ModSettings
    {
        // 运行时状态
        public static bool isRimTalkAvailable = false;

        // 滚动位置
        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 navScrollPosition = Vector2.zero;
        private float lastContentHeight = 1000f;
        private List<ISettingsPage> pages;
        private ISettingsPage currentPageObj;

        // 甩飞参数
        public float throwDistanceFactor = 1.0f;
        public float baseDamage = 5f;
        public float damagePerCell = 2f;
        public int maxThrowCells = 15;
        public float minThrowSpeed = 3f;

        // 神之爱抚参数
        public int caressMoodBonus = 6;
        public int caressMaxStages = 5;
        public bool caressEnablePrisonerResistanceReduction = true;
        public float caressPrisonerResistanceReduction = 0.05f;
        public bool caressEnableEnemySurrender = true;
        public float caressEnemySurrenderChance = 0.1f;
        public bool caressEnableRemoveLoyalty = true;
        public float caressRemoveLoyaltyChance = 0.1f;

        // 神之清洁参数
        public bool cleaningEnableHealing = true;
        public float cleaningHealAmount = 3f;
        public bool cleaningEnablePlantGrowth = true;
        public float cleaningPlantGrowthBonus = 0.05f;
        public bool cleaningPlayMusic = true;
        public bool cleaningEnableRepair = true;
        public bool cleaningRemoveScars = true;
        public bool cleaningCureAddiction = true;
        public bool cleaningEnableTending = true;
        public bool cleaningEnableRegrowth = true;
        public bool cleaningEnableFoodPreservation = true;

        // 神之裁决参数
        public float judgmentCrushDamageMultiplier = 1.0f;
        public float judgmentShockwaveDamageMultiplier = 1.0f;
        public float judgmentCrushRadiusMultiplier = 1.0f;
        public float judgmentShockwaveRadiusMultiplier = 1.0f;
        public bool judgmentReviewMode = false;
        public int currentJudgmentMode = 0; // 裁决模式索引

        // 神之庇护参数
        public float protectionDomeRadius = 10f;
        public bool protectionDomeInfiniteDuration = true;
        public int protectionDurationTicks = 60000; // 穹顶时长
        public int protectionIndividualDurationTicks = 60000; // 庇护时长
        public int currentProtectionMode = 0; // 庇护模式索引

        // 功能开关
        public bool enableGodHand = true;
        public bool enableGodWrench = true;
        public bool enableCaress = true;
        public bool enableCleaning = true;
        public bool enableJudgment = true;
        public bool enablePoke = true;
        public bool enableThrow = true;
        public bool enableRimTalkIntegration = true;
        public bool enableRimTalkEventReaction = true;
        public bool enableAssistant = true; // 神之助手开关
        public bool enableProtection = true;
        public bool enableGeneratorGodHand = true;
        public bool disableNSFW = true; // 默认关闭NSFW
        public bool disableEndRodGenerator = false; // 关闭永动机

        // 神之扳手子功能开关
        public bool godWrenchEnablePrecisionMode = true;
        public int turretHeadMaxTurrets = 5;

        // 神之手子功能开关
        public bool godHandEnableShake = true;
        public bool godHandEnableMeleeMode = true;
        public bool godHandEnableShootingMode = true;
        public bool godHandEnableForceIngest = true;
        public float godHandGrabRadius = 3.0f;
        public bool godWrenchEnableBulkScoop = true;

        // 神之脑瓜崩子功能开关
        public bool pokeEnableResurrection = true;
        public bool pokeEnableMentalBreakInterrupt = true;
        public bool pokeEnableInspiration = true;
        public bool pokeEnableFlick = true;
        public string playerName = "GodHand.Settings.PlayerName.Default";
        public string playerPersona = "GodHand.Settings.PlayerPersona.Default";

        public string PlayerNameTranslated => TranslateIfKey(playerName);
        public string PlayerPersonaTranslated => TranslateIfKey(playerPersona);

        // 结构化玩家信息
        public string playerAgeGender = "GodHand.Settings.PlayerInfo.AgeGender.Default";
        public string playerTitle = "GodHand.Settings.PlayerInfo.Title.Default";
        public string playerRace = "GodHand.Settings.PlayerInfo.Race.Default";
        public string playerBackstoryChildhood = "GodHand.Settings.PlayerInfo.Childhood.Default";
        public string playerBackstoryAdulthood = "GodHand.Settings.PlayerInfo.Adulthood.Default";
        public string playerTraits = "GodHand.Settings.PlayerInfo.Traits.Default";
        public string playerSkills = "GodHand.Settings.PlayerInfo.Skills.Default";
        public string playerHealth = "GodHand.Settings.PlayerInfo.Health.Default";
        public string playerMood = "GodHand.Settings.PlayerInfo.Mood.Default";
        public string playerThoughts = "GodHand.Settings.PlayerInfo.Thoughts.Default";
        public string playerRelations = "GodHand.Settings.PlayerInfo.Relations.Default";
        public string playerIdeology = "GodHand.Settings.PlayerInfo.Ideology.Default";
        public string playerEquipment = "GodHand.Settings.PlayerInfo.Equipment.Default";
        public string playerPersonality = "GodHand.Settings.PlayerInfo.Personality.Default";

        public string PlayerAgeGenderTranslated => TranslateIfKey(playerAgeGender);
        public string PlayerTitleTranslated => TranslateIfKey(playerTitle);
        public string PlayerRaceTranslated => TranslateIfKey(playerRace);
        public string PlayerBackstoryChildhoodTranslated => TranslateIfKey(playerBackstoryChildhood);
        public string PlayerBackstoryAdulthoodTranslated => TranslateIfKey(playerBackstoryAdulthood);
        public string PlayerTraitsTranslated => TranslateIfKey(playerTraits);
        public string PlayerSkillsTranslated => TranslateIfKey(playerSkills);
        public string PlayerHealthTranslated => TranslateIfKey(playerHealth);
        public string PlayerMoodTranslated => TranslateIfKey(playerMood);
        public string PlayerThoughtsTranslated => TranslateIfKey(playerThoughts);
        public string PlayerRelationsTranslated => TranslateIfKey(playerRelations);
        public string PlayerIdeologyTranslated => TranslateIfKey(playerIdeology);
        public string PlayerEquipmentTranslated => TranslateIfKey(playerEquipment);
        public string PlayerPersonalityTranslated => TranslateIfKey(playerPersonality);

        public bool playMemeSound = false;
        public bool showFlavorError = true;

        // RimTalk 触发概率
        public float rimTalkChanceThrown = 1.0f;
        public float rimTalkChancePoked = 1.0f;
        public float rimTalkChanceCaressed = 1.0f;
        public float rimTalkChanceGrabbed = 0.3f;
        public float rimTalkChanceHealed = 0.8f;
        public float rimTalkChanceResurrected = 1.0f;
        public float rimTalkChanceForcedIngest = 1.0f;
        public float rimTalkChanceJudgmentShockwave = 0.5f;

        // RimTalk 冷却时间
        public float rimTalkGlobalCooldown = 5f;
        public float rimTalkIndividualCooldown = 30f;

        // 助手参数
        public int maxGodAssistantTasks = 3; // 助手数量上限
        public bool enableSurgeryQuiz = false; // 手术答题模式

        public bool enableHandCrankGenerator = true; // 启用手摇发电
        public float generatorEfficiency = 1.0f; // 发电效率
        public float generatorExponent = 1.5f; // 发电指数
        public float handCrankPowerMultiplier = 1.0f; // 人力发电倍率
        public float godCrankPowerMultiplier = 1.0f; // 神力发电倍率
        public bool showSimplifiedFormula = false; // 显示简化公式
        public bool enableFTLCrank = false; // 启用FTL模式
        public float ftlRPMThreshold = 150f; // FTL转速阈值
        public bool enableAbsurdRotation = false; // 荒谬旋转模式

        // 自定义文本覆盖 (Key -> Value)
        public Dictionary<string, string> customPrompts = new Dictionary<string, string>();
        public Dictionary<string, string> customMemories = new Dictionary<string, string>();

        public string GetCustomPrompt(string key, string p1, string p2)
        {
            if (customPrompts.TryGetValue(key, out string val) && !val.NullOrEmpty())
                return val.Replace("{0}", p1).Replace("{1}", p2);

            string translated = TranslateIfKey(key);
            if (translated != key)
                return translated.Formatted(p1, p2);

            // 显式从英语语言包获取回退
            var english = LanguageDatabase.AllLoadedLanguages.FirstOrDefault(l => l.folderName == "English");
            if (english != null && english.keyedReplacements.TryGetValue(key, out var kr))
            {
                return kr.value.Formatted(p1, p2);
            }

            return key.Translate(p1, p2);
        }

        public string TranslateIfKey(string value)
        {
            if (value.NullOrEmpty()) return value;
            if (value.StartsWith("GodHand.") && value.CanTranslate())
                return value.Translate();
            return value;
        }

        public string GetCustomMemory(string key, string p1 = null)
        {
            if (customMemories.TryGetValue(key, out string val) && !val.NullOrEmpty())
            {
                string result = (p1 != null) ? val.Replace("{0}", p1) : val;
                return result;
            }

            if (key.CanTranslate())
                return p1 != null ? key.Translate(p1) : key.Translate();

            // 显式从英语语言包获取回退
            var english = LanguageDatabase.AllLoadedLanguages.FirstOrDefault(l => l.folderName == "English");
            if (english != null && english.keyedReplacements.TryGetValue(key, out var kr))
            {
                return p1 != null ? kr.value.Formatted(p1) : (string)kr.value;
            }

            return p1 != null ? key.Translate(p1) : key.Translate();
        }

        // 过滤选项
        public bool canGrabFriendly = true;
        public bool canGrabHostile = true;
        public bool canGrabNeutral = true;

        // 帮助页面记录
        public bool hasSeenHelp_GodHand = false;
        public bool hasSeenHelp_GodWrench = false;
        public bool hasSeenHelp_Caress = false;
        public bool hasSeenHelp_Cleaning = false;
        public bool hasSeenHelp_Judgment = false;
        public bool hasSeenHelp_Poke = false;
        public bool hasSeenHelp_Protection = false;
        public bool hasSeenHelp_Magnifier = false;
        public bool hasSeenHelp_Assistant = false;
        public bool hasSeenHelp_Generator = false;

        // 调试选项
        public bool enableDebugLog = false;
        public bool enableDebugVisuals = false;
        public string lastRunVersion = "0.0.0";

        // 实际甩飞距离系数
        public float ActualThrowDistanceFactor => throwDistanceFactor * 3f;



        // 保存加载设置
        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastRunVersion, "lastRunVersion", "0.0.0");

            // 甩飞参数
            Scribe_Values.Look(ref throwDistanceFactor, "throwDistanceFactor", 1.0f);
            Scribe_Values.Look(ref baseDamage, "baseDamage", 5f);
            Scribe_Values.Look(ref damagePerCell, "damagePerCell", 2f);
            Scribe_Values.Look(ref maxThrowCells, "maxThrowCells", 15);
            Scribe_Values.Look(ref minThrowSpeed, "minThrowSpeed", 3f);

            // 神之爱抚参数
            Scribe_Values.Look(ref caressMoodBonus, "caressMoodBonus", 6);
            Scribe_Values.Look(ref caressMaxStages, "caressMaxStages", 5);
            Scribe_Values.Look(ref caressEnablePrisonerResistanceReduction, "caressEnablePrisonerResistanceReduction", true);
            Scribe_Values.Look(ref caressPrisonerResistanceReduction, "caressPrisonerResistanceReduction", 0.05f);
            Scribe_Values.Look(ref caressEnableEnemySurrender, "caressEnableEnemySurrender", true);
            Scribe_Values.Look(ref caressEnemySurrenderChance, "caressEnemySurrenderChance", 0.1f);
            Scribe_Values.Look(ref caressEnableRemoveLoyalty, "caressEnableRemoveLoyalty", true);
            Scribe_Values.Look(ref caressRemoveLoyaltyChance, "caressRemoveLoyaltyChance", 0.1f);

            // 神之清洁参数
            Scribe_Values.Look(ref cleaningEnableHealing, "cleaningEnableHealing", true);
            Scribe_Values.Look(ref cleaningHealAmount, "cleaningHealAmount", 3f);
            Scribe_Values.Look(ref cleaningEnablePlantGrowth, "cleaningEnablePlantGrowth", true);
            Scribe_Values.Look(ref cleaningPlantGrowthBonus, "cleaningPlantGrowthBonus", 0.05f);
            Scribe_Values.Look(ref cleaningPlayMusic, "cleaningPlayMusic", true);
            Scribe_Values.Look(ref cleaningEnableRepair, "cleaningEnableRepair", true);
            Scribe_Values.Look(ref cleaningRemoveScars, "cleaningRemoveScars", true);
            Scribe_Values.Look(ref cleaningCureAddiction, "cleaningCureAddiction", true);
            Scribe_Values.Look(ref cleaningEnableTending, "cleaningEnableTending", true);
            Scribe_Values.Look(ref cleaningEnableRegrowth, "cleaningEnableRegrowth", true);
            Scribe_Values.Look(ref cleaningEnableFoodPreservation, "cleaningEnableFoodPreservation", true);

            // 神之裁决参数
            Scribe_Values.Look(ref judgmentCrushDamageMultiplier, "judgmentCrushDamageMultiplier", 1.0f);
            Scribe_Values.Look(ref judgmentShockwaveDamageMultiplier, "judgmentShockwaveDamageMultiplier", 1.0f);
            Scribe_Values.Look(ref judgmentCrushRadiusMultiplier, "judgmentCrushRadiusMultiplier", 1.0f);
            Scribe_Values.Look(ref judgmentShockwaveRadiusMultiplier, "judgmentShockwaveRadiusMultiplier", 1.0f);
            Scribe_Values.Look(ref judgmentReviewMode, "judgmentReviewMode", false);
            Scribe_Values.Look(ref currentJudgmentMode, "currentJudgmentMode", 0);

            // 神之庇护参数保存
            Scribe_Values.Look(ref protectionDomeRadius, "protectionDomeRadius", 10f);
            Scribe_Values.Look(ref protectionDomeInfiniteDuration, "protectionDomeInfiniteDuration", true);
            Scribe_Values.Look(ref protectionDurationTicks, "protectionDurationTicks", 60000);
            Scribe_Values.Look(ref protectionIndividualDurationTicks, "protectionIndividualDurationTicks", 60000);
            Scribe_Values.Look(ref currentProtectionMode, "currentProtectionMode", 0);

            // 功能开关
            Scribe_Values.Look(ref enableGodHand, "enableGodHand", true);
            Scribe_Values.Look(ref enableGodWrench, "enableGodWrench", true);
            Scribe_Values.Look(ref enableCaress, "enableCaress", true);
            Scribe_Values.Look(ref enableCleaning, "enableCleaning", true);
            Scribe_Values.Look(ref enableJudgment, "enableJudgment", true);
            Scribe_Values.Look(ref enablePoke, "enablePoke", true);
            Scribe_Values.Look(ref enableThrow, "enableThrow", true);
            Scribe_Values.Look(ref enableRimTalkIntegration, "enableRimTalkIntegration", true);
            Scribe_Values.Look(ref enableRimTalkEventReaction, "enableRimTalkEventReaction", true);
            Scribe_Values.Look(ref enableAssistant, "enableAssistant", true);
            Scribe_Values.Look(ref enableProtection, "enableProtection", true);
            Scribe_Values.Look(ref enableGeneratorGodHand, "enableGeneratorGodHand", true);
            Scribe_Values.Look(ref disableNSFW, "disableNSFW", true);
            Scribe_Values.Look(ref disableEndRodGenerator, "disableEndRodGenerator", false);

            // 神之扳手子功能开关
            Scribe_Values.Look(ref godWrenchEnablePrecisionMode, "godWrenchEnablePrecisionMode", true);
            Scribe_Values.Look(ref turretHeadMaxTurrets, "turretHeadMaxTurrets", 5);

            // 神之手子功能开关
            Scribe_Values.Look(ref godHandEnableShake, "godHandEnableShake", true);
            Scribe_Values.Look(ref godHandEnableMeleeMode, "godHandEnableMeleeMode", true);
            Scribe_Values.Look(ref godHandEnableShootingMode, "godHandEnableShootingMode", true);
            Scribe_Values.Look(ref godHandEnableForceIngest, "godHandEnableForceIngest", true);
            Scribe_Values.Look(ref godHandGrabRadius, "godHandGrabRadius", 3.0f);
            Scribe_Values.Look(ref godWrenchEnableBulkScoop, "godWrenchEnableBulkScoop", true);

            // 神之脑瓜崩子功能开关
            Scribe_Values.Look(ref pokeEnableResurrection, "pokeEnableResurrection", true);
            Scribe_Values.Look(ref pokeEnableMentalBreakInterrupt, "pokeEnableMentalBreakInterrupt", true);
            Scribe_Values.Look(ref pokeEnableInspiration, "pokeEnableInspiration", true);
            Scribe_Values.Look(ref pokeEnableFlick, "pokeEnableFlick", true);
            Scribe_Values.Look(ref playerName, "playerName", "GodHand.Settings.PlayerName.Default");
            Scribe_Values.Look(ref playerPersona, "playerPersona", "GodHand.Settings.PlayerPersona.Default");

            // 结构化玩家信息
            Scribe_Values.Look(ref playerAgeGender, "playerAgeGender", "GodHand.Settings.PlayerInfo.AgeGender.Default");
            Scribe_Values.Look(ref playerTitle, "playerTitle", "GodHand.Settings.PlayerInfo.Title.Default");
            Scribe_Values.Look(ref playerRace, "playerRace", "GodHand.Settings.PlayerInfo.Race.Default");
            Scribe_Values.Look(ref playerBackstoryChildhood, "playerBackstoryChildhood", "GodHand.Settings.PlayerInfo.Childhood.Default");
            Scribe_Values.Look(ref playerBackstoryAdulthood, "playerBackstoryAdulthood", "GodHand.Settings.PlayerInfo.Adulthood.Default");
            Scribe_Values.Look(ref playerTraits, "playerTraits", "GodHand.Settings.PlayerInfo.Traits.Default");
            Scribe_Values.Look(ref playerSkills, "playerSkills", "GodHand.Settings.PlayerInfo.Skills.Default");
            Scribe_Values.Look(ref playerHealth, "playerHealth", "GodHand.Settings.PlayerInfo.Health.Default");
            Scribe_Values.Look(ref playerMood, "playerMood", "GodHand.Settings.PlayerInfo.Mood.Default");
            Scribe_Values.Look(ref playerThoughts, "playerThoughts", "GodHand.Settings.PlayerInfo.Thoughts.Default");
            Scribe_Values.Look(ref playerRelations, "playerRelations", "GodHand.Settings.PlayerInfo.Relations.Default");
            Scribe_Values.Look(ref playerIdeology, "playerIdeology", "GodHand.Settings.PlayerInfo.Ideology.Default");
            Scribe_Values.Look(ref playerEquipment, "playerEquipment", "GodHand.Settings.PlayerInfo.Equipment.Default");
            Scribe_Values.Look(ref playerPersonality, "playerPersonality", "GodHand.Settings.PlayerInfo.Personality.Default");

            Scribe_Values.Look(ref playMemeSound, "playMemeSound", false);
            Scribe_Values.Look(ref showFlavorError, "showFlavorError", true);

            // RimTalk 概率配置
            Scribe_Values.Look(ref rimTalkChanceThrown, "rimTalkChanceThrown", 1.0f);
            Scribe_Values.Look(ref rimTalkChancePoked, "rimTalkChancePoked", 1.0f);
            Scribe_Values.Look(ref rimTalkChanceCaressed, "rimTalkChanceCaressed", 1.0f);
            Scribe_Values.Look(ref rimTalkChanceGrabbed, "rimTalkChanceGrabbed", 0.3f);
            Scribe_Values.Look(ref rimTalkChanceHealed, "rimTalkChanceHealed", 0.8f);
            Scribe_Values.Look(ref rimTalkChanceResurrected, "rimTalkChanceResurrected", 1.0f);
            Scribe_Values.Look(ref rimTalkChanceForcedIngest, "rimTalkChanceForcedIngest", 1.0f);
            Scribe_Values.Look(ref rimTalkChanceJudgmentShockwave, "rimTalkChanceJudgmentShockwave", 0.5f);

            // RimTalk 冷却配置
            Scribe_Values.Look(ref rimTalkGlobalCooldown, "rimTalkGlobalCooldown", 5f);
            Scribe_Values.Look(ref rimTalkIndividualCooldown, "rimTalkIndividualCooldown", 30f);

            Scribe_Values.Look(ref maxGodAssistantTasks, "maxGodAssistantTasks", 3);
            Scribe_Values.Look(ref enableSurgeryQuiz, "enableSurgeryQuiz", false);

            Scribe_Values.Look(ref generatorEfficiency, "generatorEfficiency", 1.0f);
            Scribe_Values.Look(ref generatorExponent, "generatorExponent", 1.5f);
            Scribe_Values.Look(ref enableHandCrankGenerator, "enableHandCrankGenerator", true);
            Scribe_Values.Look(ref handCrankPowerMultiplier, "handCrankPowerMultiplier", 1.0f);
            Scribe_Values.Look(ref godCrankPowerMultiplier, "godCrankPowerMultiplier", 1.0f);
            Scribe_Values.Look(ref showSimplifiedFormula, "showSimplifiedFormula", false);
            Scribe_Values.Look(ref enableFTLCrank, "enableFTLCrank", false);
            Scribe_Values.Look(ref ftlRPMThreshold, "ftlRPMThreshold", 150f);
            Scribe_Values.Look(ref enableAbsurdRotation, "enableAbsurdRotation", false);

            Scribe_Collections.Look(ref customPrompts, "customPrompts", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref customMemories, "customMemories", LookMode.Value, LookMode.Value);

            if (customPrompts == null) customPrompts = new Dictionary<string, string>();
            if (customMemories == null) customMemories = new Dictionary<string, string>();

            // 过滤选项
            Scribe_Values.Look(ref canGrabFriendly, "canGrabFriendly", true);
            Scribe_Values.Look(ref canGrabHostile, "canGrabHostile", true);
            Scribe_Values.Look(ref canGrabNeutral, "canGrabNeutral", true);

            // 帮助记录
            Scribe_Values.Look(ref hasSeenHelp_GodHand, "hasSeenHelp_GodHand", false);
            Scribe_Values.Look(ref hasSeenHelp_GodWrench, "hasSeenHelp_GodWrench", false);
            Scribe_Values.Look(ref hasSeenHelp_Caress, "hasSeenHelp_Caress", false);
            Scribe_Values.Look(ref hasSeenHelp_Cleaning, "hasSeenHelp_Cleaning", false);
            Scribe_Values.Look(ref hasSeenHelp_Judgment, "hasSeenHelp_Judgment", false);
            Scribe_Values.Look(ref hasSeenHelp_Poke, "hasSeenHelp_Poke", false);
            Scribe_Values.Look(ref hasSeenHelp_Magnifier, "hasSeenHelp_Magnifier", false);
            Scribe_Values.Look(ref hasSeenHelp_Protection, "hasSeenHelp_Protection", false);
            Scribe_Values.Look(ref hasSeenHelp_Assistant, "hasSeenHelp_Assistant", false);
            Scribe_Values.Look(ref hasSeenHelp_Generator, "hasSeenHelp_Generator", false);

            Scribe_Values.Look(ref enableDebugLog, "enableDebugLog", false);
            Scribe_Values.Look(ref enableDebugVisuals, "enableDebugVisuals", false);

            base.ExposeData();
        }

        private void InitializePages()
        {
            if (pages != null) return;
            pages = new List<ISettingsPage>();

            foreach (var type in typeof(GodHandSettings).Assembly.GetTypes())
            {
                if (!type.IsAbstract && !type.IsInterface && typeof(ISettingsPage).IsAssignableFrom(type) && !type.ContainsGenericParameters)
                {
                    // 始终允许进入身份设置页，以便用户随时修改或清除遗留数据

                    try
                    {
                        var page = (ISettingsPage)Activator.CreateInstance(type);
                        pages.Add(page);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[GodHand] Failed to create settings page {type.Name}: {ex}");
                    }
                }
            }

            pages.SortBy(p => p.Order);
            if (pages.Count > 0) currentPageObj = pages[0];
        }

        // 绘制设置界面
        public void DoSettingsWindowContents(Rect inRect)
        {
            InitializePages();
            float navWidth = 180f;
            float margin = 10f;

            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width - navWidth - margin, inRect.height);
            Rect navRect = new Rect(inRect.xMax - navWidth, inRect.y, navWidth, inRect.height);

            DrawNavigation(navRect);
            DrawCurrentPage(outRect);
        }

        private void DrawNavigation(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect innerRect = rect.ContractedBy(8f);
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, pages.Count * 40f + 20f);
            Widgets.BeginScrollView(innerRect, ref navScrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            foreach (var page in pages)
            {
                if (currentPageObj == page) GUI.color = Color.cyan;
                if (listing.ButtonText(page.Label))
                {
                    currentPageObj = page;
                    scrollPosition = Vector2.zero;
                    lastContentHeight = 0f; // 重置高度记录
                }
                GUI.color = Color.white;
                listing.Gap(4f);
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawCurrentPage(Rect outRect)
        {
            if (currentPageObj == null) return;

            // 获取实际高度
            // 确保视图高度
            float estimatedHeight = currentPageObj.GetViewHeight(this);
            float viewHeight = (lastContentHeight > 100f) ? Mathf.Max(lastContentHeight + 50f, estimatedHeight) : estimatedHeight;

            Rect viewRect = new Rect(0f, 0f, outRect.width - 24f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            currentPageObj.Draw(listing, this);

            // 记录当前实际绘制高度
            lastContentHeight = listing.CurHeight;

            listing.End();
            Widgets.EndScrollView();
        }

        public void RefreshAllDesignators()
        {
            if (Find.World != null)
            {
                var targetCategories = new HashSet<string> { "GodTools", "Security" };
                foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
                {
                    if (targetCategories.Contains(cat.defName))
                    {
                        cat.ResolveReferences();
                    }
                }
            }
        }

        public void ResetToDefaults()
        {
            if (pages == null) InitializePages();

            foreach (var page in pages)
            {
                page.Reset(this);
            }

            // 刷新页面显示
            pages = null;
            InitializePages();
        }
    }
}
