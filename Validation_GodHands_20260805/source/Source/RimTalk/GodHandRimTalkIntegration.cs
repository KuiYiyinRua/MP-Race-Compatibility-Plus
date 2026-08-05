using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod
{
    // RimTalk 联动集成
    [StaticConstructorOnStartup]
    public static class GodHandRimTalkIntegration
    {
        private static bool integrationActive = false;
        private static Harmony harmony;

        // 次级缓存
        private static Type tPromptService;
        private static Type tTalkService;
        private static Type tTalkRequest;
        private static Type tTalkType;
        private static Type tCache;
        private static Type tRimTalkAPI;
        private static Type tPawnUtil;
        private static Type tContextHelper;

        private static MethodInfo mCreatePawnContext;
        private static MethodInfo mGenerateTalk;
        private static MethodInfo mGetPlayer;
        private static Type tSettings;
        private static Type tRimTalkSettings;

        // 冷却记录
        private static Dictionary<int, int> lastPawnTriggerTick = new Dictionary<int, int>();
        private static int lastGlobalTriggerTick = -1;

        static GodHandRimTalkIntegration()
        {
            Initialize();
        }

        // 初始化联动
        public static void Initialize()
        {
            try
            {
                // 查找核心类
                tPromptService = AccessTools.TypeByName("RimTalk.Service.PromptService");
                tTalkService = AccessTools.TypeByName("RimTalk.Service.TalkService");
                tTalkRequest = AccessTools.TypeByName("RimTalk.Data.TalkRequest");
                tTalkType = AccessTools.TypeByName("RimTalk.Source.Data.TalkType");
                tCache = AccessTools.TypeByName("RimTalk.Data.Cache");
                tRimTalkAPI = AccessTools.TypeByName("RimTalk.API.RimTalkPromptAPI");
                tPawnUtil = AccessTools.TypeByName("RimTalk.Util.PawnUtil");
                tContextHelper = AccessTools.TypeByName("RimTalk.Util.ContextHelper");

                if (tPromptService == null || tTalkService == null || tCache == null) return;

                // 准备 Harmony
                harmony = new Harmony("com.godhand.rimtalk.integration");

                // 缓存常用方法
                mCreatePawnContext = AccessTools.Method(tPromptService, "CreatePawnContext");
                mGenerateTalk = AccessTools.Method(tTalkService, "GenerateTalk");
                mGetPlayer = AccessTools.Method(tCache, "GetPlayer");
                tSettings = AccessTools.TypeByName("RimTalk.Settings");
                tRimTalkSettings = AccessTools.TypeByName("RimTalk.RimTalkSettings");

                // 应用补丁
                ApplyPatches();

                integrationActive = true;
                SyncPlayerName();

                Log.Message("[神之手] RimTalk 联动初始化完成");
            }
            catch (Exception e)
            {
                Log.Warning($"[神之手] RimTalk 联动失败: {e.Message}");
            }
        }

        // 应用 Harmony 补丁
        private static void ApplyPatches()
        {
            // 补丁注入玩家人设
            var buildContext = AccessTools.Method(tPromptService, "BuildContext");
            if (buildContext != null)
                harmony.Patch(buildContext, prefix: new HarmonyMethod(typeof(GodHandRimTalkIntegration), nameof(Prefix_BuildContext)));

            var decoratePrompt = AccessTools.Method(tPromptService, "DecoratePrompt");
            if (decoratePrompt != null)
                harmony.Patch(decoratePrompt, postfix: new HarmonyMethod(typeof(GodHandRimTalkIntegration), nameof(Postfix_DecoratePrompt)));

            // 拦截物理属性提取
            if (tPawnUtil != null)
            {
                var getRole = AccessTools.Method(tPawnUtil, "GetRole");
                if (getRole != null) harmony.Patch(getRole, prefix: new HarmonyMethod(typeof(GodHandRimTalkIntegration), nameof(Prefix_GetRole)));

                var getTitle = AccessTools.Method(tPawnUtil, "GetTitle");
                if (getTitle != null) harmony.Patch(getTitle, prefix: new HarmonyMethod(typeof(GodHandRimTalkIntegration), nameof(Prefix_GetTitle)));

                var getActivity = AccessTools.Method(tPawnUtil, "GetActivity");
                if (getActivity != null) harmony.Patch(getActivity, prefix: new HarmonyMethod(typeof(GodHandRimTalkIntegration), nameof(Prefix_GetActivity)));
            }

            if (tContextHelper != null)
            {
                var getDecoratedName = AccessTools.Method(tContextHelper, "GetDecoratedName");
                if (getDecoratedName != null) harmony.Patch(getDecoratedName, prefix: new HarmonyMethod(typeof(GodHandRimTalkIntegration), nameof(Prefix_GetDecoratedName)));
            }
        }

        // 同步设置到人设
        public static void SyncPlayerName()
        {
            if (!integrationActive || tCache == null || !GodHandModMain.Settings.enableRimTalkIntegration) return;
            try
            {
                // 同步设置到原生字段
                var settings = AccessTools.Method(tSettings, "Get")?.Invoke(null, null);
                if (settings != null && tRimTalkSettings != null)
                {
                    AccessTools.Field(tRimTalkSettings, "PlayerName")?.SetValue(settings, GodHandModMain.Settings.PlayerNameTranslated);
                }

                // 重建玩家 Pawn
                AccessTools.Method(tCache, "InitializePlayerPawn")?.Invoke(null, null);

                Pawn playerPawn = GetPlayerPawn();
                if (playerPawn != null)
                {
                    var s = GodHandModMain.Settings;
                    // 同步物理属性
                    playerPawn.gender = s.PlayerAgeGenderTranslated.Contains("女") ? Gender.Female : Gender.Male;
                    string ageStr = new string(s.PlayerAgeGenderTranslated.Where(char.IsDigit).ToArray());
                    if (int.TryParse(ageStr, out int age))
                        playerPawn.ageTracker.AgeBiologicalTicks = (long)age * 3600000;
                    else
                        playerPawn.ageTracker.AgeBiologicalTicks = 5000L * 3600000;

                    // 同步到状态对象
                    var state = AccessTools.Method(tCache, "Get")?.Invoke(null, new object[] { playerPawn });
                    if (state != null)
                    {
                        AccessTools.Field(state.GetType(), "Personality")?.SetValue(state, s.PlayerPersonalityTranslated);
                    }
                }
            }
            catch (Exception ex) { Log.Warning($"[神之手] 同步 RimTalk 数据失败: {ex.Message}"); }
        }

        // 注入玩家人设
        public static bool Prefix_BuildContext(List<Pawn> pawns, ref string __result)
        {
            if (!GodHandModMain.Settings.enableRimTalkIntegration) return true;

            Pawn playerPawn = GetPlayerPawn();
            if (playerPawn == null || !pawns.Contains(playerPawn)) return true;

            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    sb.AppendLine($"[P{i + 1}]");

                    if (p == playerPawn)
                    {
                        sb.AppendLine(GetPlayerContext(p));
                    }
                    else if (mCreatePawnContext != null)
                    {
                        // 提取背景描述
                        int level = (i == 0) ? 1 : 0;
                        string ctx = (string)mCreatePawnContext.Invoke(null, new object[] { p, level });
                        sb.AppendLine(ctx);
                    }
                }
                __result = ReplaceCommonTerms(sb.ToString().TrimEnd());
                return false;
            }
            catch (Exception e)
            {
                Log.Error($"[神之手] BuildContext 注入失败: {e}");
                return true;
            }
        }

        // 注入记忆引导
        public static void Postfix_DecoratePrompt(object talkRequest, List<Pawn> pawns)
        {
            if (!GodHandModMain.Settings.enableRimTalkIntegration) return;
            if (talkRequest == null || pawns == null) return;

            // 检查神之手互动记忆
            bool hasMemory = pawns.Any(p => GodHandMemoryTracker.HasRecentMemory(p, 2500));
            if (!hasMemory) return;

            try
            {
                var traverse = Traverse.Create(talkRequest);
                string currentPrompt = traverse.Property("Prompt").GetValue<string>();
                if (currentPrompt != null)
                {
                    string injection = "\n" + "GodHand.Memory.HighPriorityPrompt".Translate();
                    traverse.Property("Prompt").SetValue(ReplaceCommonTerms(currentPrompt + injection));
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[神之手] DecoratePrompt 引导注入失败: {e.Message}", 9128374);
            }
        }

        // 触发对话接口
        public static void TryTriggerConversation(Pawn initiator, string promptKey)
        {
            if (!GodHandModMain.Settings.enableRimTalkIntegration || !GodHandModMain.Settings.enableRimTalkEventReaction) return;
            if (initiator == null || initiator.Dead || !integrationActive) return;

            var settings = GodHandModMain.Settings;
            int currentTick = Find.TickManager.TicksGame;

            // 冷却检查
            if (currentTick - lastGlobalTriggerTick < settings.rimTalkGlobalCooldown * 60) return;
            if (lastPawnTriggerTick.TryGetValue(initiator.thingIDNumber, out int lastTick))
            {
                if (currentTick - lastTick < settings.rimTalkIndividualCooldown * 60) return;
            }

            // 触发概率检查
            float chance = GetTriggerChance(promptKey, settings);
            if (!Rand.Chance(chance)) return;

            try
            {
                lastGlobalTriggerTick = currentTick;
                lastPawnTriggerTick[initiator.thingIDNumber] = currentTick;

                string persona = settings.PlayerPersonaTranslated;
                string playerName = settings.PlayerNameTranslated;

                // 转换带覆盖提示词
                string basePrompt = settings.GetCustomPrompt(promptKey, initiator.LabelShort, playerName);

                // 引导模型内心活动
                StringBuilder promptBuilder = new StringBuilder();
                promptBuilder.AppendLine(settings.GetCustomPrompt("GodHand.RimTalk.System.Background", "", ""));
                promptBuilder.AppendLine(settings.GetCustomPrompt("GodHand.RimTalk.System.Persona", persona, ""));
                promptBuilder.AppendLine(settings.GetCustomPrompt("GodHand.RimTalk.System.Content", basePrompt, ""));
                promptBuilder.AppendLine(settings.GetCustomPrompt("GodHand.RimTalk.System.Instruction", "", ""));

                string fullPrompt = promptBuilder.ToString();
                object talkTypeVal = Enum.Parse(tTalkType, "Thought");

                // 构造对话请求
                object request = Activator.CreateInstance(tTalkRequest, new object[] { fullPrompt, initiator, null, talkTypeVal });
                FieldInfo fIsMonologue = tTalkRequest.GetField("IsMonologue");
                if (fIsMonologue != null) fIsMonologue.SetValue(request, true);

                // 发送对话生成请求
                mGenerateTalk?.Invoke(null, new object[] { request });
            }
            catch (Exception ex)
            {
                Log.Warning($"[神之手] 触发 RimTalk 对话失败: {ex.Message}");
            }
        }

        // 替换常见代称文本
        private static string ReplaceCommonTerms(string text)
        {
            if (text.NullOrEmpty()) return text;
            string playerName = GodHandModMain.Settings.PlayerNameTranslated;
            if (playerName.NullOrEmpty()) return text;

            return text.Replace("archotech intelligence", playerName)
                       .Replace("archotech presence", playerName)
                       .Replace("archotech touch", playerName)
                       .Replace("archotech", playerName)
                       .Replace("Archotech", playerName);
        }

        // 获取触发概率
        private static float GetTriggerChance(string promptKey, GodHandSettings s)
        {
            if (promptKey.Contains("Thrown")) return s.rimTalkChanceThrown;
            if (promptKey.Contains("Poked")) return s.rimTalkChancePoked;
            if (promptKey.Contains("Caressed")) return s.rimTalkChanceCaressed;
            if (promptKey.Contains("Grabbed")) return s.rimTalkChanceGrabbed;
            if (promptKey.Contains("Healed")) return s.rimTalkChanceHealed;
            if (promptKey.Contains("Resurrected")) return s.rimTalkChanceResurrected;
            if (promptKey.Contains("ForcedIngest")) return s.rimTalkChanceForcedIngest;
            if (promptKey.Contains("JudgmentShockwave")) return s.rimTalkChanceJudgmentShockwave;
            return 1.0f;
        }

        // 拦截获取 Role
        public static bool Prefix_GetRole(Pawn pawn, ref string __result)
        {
            if (!GodHandModMain.Settings.enableRimTalkIntegration) return true;
            if (integrationActive && pawn != null && pawn == GetPlayerPawn())
            {
                __result = GodHandModMain.Settings.PlayerPersonaTranslated;
                return false;
            }
            return true;
        }

        // 拦截获取 Title
        public static bool Prefix_GetTitle(Pawn pawn, ref string __result)
        {
            if (!GodHandModMain.Settings.enableRimTalkIntegration) return true;
            if (integrationActive && pawn != null && pawn == GetPlayerPawn())
            {
                __result = GodHandModMain.Settings.PlayerTitleTranslated;
                return false;
            }
            return true;
        }

        // 拦截获取 Activity
        public static bool Prefix_GetActivity(Pawn pawn, ref string __result)
        {
            if (!GodHandModMain.Settings.enableRimTalkIntegration) return true;
            if (integrationActive && pawn != null && pawn == GetPlayerPawn())
            {
                // 设置观察状态
                __result = "GodHand.Settings.PlayerInfo.Thoughts.Default".Translate();
                return false;
            }
            return true;
        }

        // 拦截获取装饰名称
        public static bool Prefix_GetDecoratedName(Pawn pawn, ref string __result)
        {
            if (!GodHandModMain.Settings.enableRimTalkIntegration) return true;
            if (integrationActive && pawn != null && pawn == GetPlayerPawn())
            {
                var s = GodHandModMain.Settings;
                string title = !s.playerTitle.NullOrEmpty() ? $"{s.PlayerTitleTranslated}/" : "";
                __result = $"{s.PlayerNameTranslated}({title}{s.PlayerAgeGenderTranslated}/{s.PlayerRaceTranslated})";
                return false;
            }
            return true;
        }

        // 获取玩家虚拟 Pawn
        private static Pawn GetPlayerPawn()
        {
            return mGetPlayer?.Invoke(null, null) as Pawn;
        }

        // 生成玩家人设文本
        private static string GetPlayerContext(Pawn pawn)
        {
            var s = GodHandModMain.Settings;
            StringBuilder sb = new StringBuilder();

            // 遵循标准格式
            string title = !s.playerTitle.NullOrEmpty() ? $"({s.PlayerTitleTranslated}) " : "";
            sb.AppendLine($"{s.PlayerNameTranslated} {title}({s.PlayerAgeGenderTranslated})");

            // 提取字段映射
            if (!s.playerPersona.NullOrEmpty()) sb.AppendLine($"Role: {s.PlayerPersonaTranslated}");
            if (!s.playerPersonality.NullOrEmpty()) sb.AppendLine($"Personality: {s.PlayerPersonalityTranslated}");
            if (!s.playerRace.NullOrEmpty()) sb.AppendLine($"Race: {s.PlayerRaceTranslated}");
            if (!s.playerIdeology.NullOrEmpty()) sb.AppendLine($"Ideology: {s.PlayerIdeologyTranslated}");

            // 填充背景描述
            if (!s.playerBackstoryChildhood.NullOrEmpty()) sb.AppendLine($"Childhood: {s.PlayerBackstoryChildhoodTranslated}");
            if (!s.playerBackstoryAdulthood.NullOrEmpty()) sb.AppendLine($"Adulthood: {s.PlayerBackstoryAdulthoodTranslated}");

            if (!s.playerTraits.NullOrEmpty()) sb.AppendLine($"Traits: {s.PlayerTraitsTranslated}");
            if (!s.playerSkills.NullOrEmpty()) sb.AppendLine($"Skills: {s.PlayerSkillsTranslated}");
            if (!s.playerHealth.NullOrEmpty()) sb.AppendLine($"Health: {s.PlayerHealthTranslated}");
            if (!s.playerMood.NullOrEmpty()) sb.AppendLine($"Mood: {s.PlayerMoodTranslated}");
            if (!s.playerThoughts.NullOrEmpty()) sb.AppendLine($"Memory: {s.PlayerThoughtsTranslated}"); // 对齐记忆标签
            if (!s.playerRelations.NullOrEmpty()) sb.AppendLine($"Relations: {s.PlayerRelationsTranslated}");
            if (!s.playerEquipment.NullOrEmpty()) sb.AppendLine($"Equipment: {s.PlayerEquipmentTranslated}");

            return sb.ToString().TrimEnd();
        }
    }
}
