using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using Verse;
using UnityEngine;

namespace GodHandMod
{
    // 接管玩家对话逻辑
    [StaticConstructorOnStartup]
    public static class GodHandRimTalkCustomDialoguePatch
    {
        #region 缓存反射对象

        private static MethodInfo cacheGetMethod;
        private static MethodInfo cacheGetPlayerMethod;
        private static MethodInfo canDisplayTalkMethod;
        private static MethodInfo addTalkRequestMethod;
        private static MethodInfo addUserHistoryMethod;
        private static FieldInfo talkResponsesField;
        private static PropertyInfo apiLogIdProp;
        private static PropertyInfo talkResponseIdProp;
        private static PropertyInfo spokenTickProp;
        private static MethodInfo notifyLogUpdatedMethod;
        private static Type talkResponseType;
        private static object talkTypeUser;
        private static PropertyInfo hour12HStringProp;
        private static PropertyInfo dateStringProp;
        private static PropertyInfo seasonStringProp;
        private static PropertyInfo weatherStringProp;
        private static MethodInfo getPawnLocationStatusMethod;
        private static MethodInfo getInGameDataMethod;
        private static MethodInfo buildContextMethod;
        private static MethodInfo updateContextMethod;

        private static bool reflectionInitialized = false;

        #endregion

        static GodHandRimTalkCustomDialoguePatch()
        {
            // 检查RimTalk存在
            Type customDialogueServiceType = AccessTools.TypeByName("RimTalk.Service.CustomDialogueService");
            if (customDialogueServiceType == null)
            {
                GodHandModMain.DebugLog("[神之手] RimTalk未安装 跳过");
                return;
            }

            try
            {
                var harmony = new Harmony("GodHand.RimTalkCustomDialoguePatch");

                // 初始化反射对象
                InitializeReflection();

                // 更新对话上下文
                MethodInfo executeDialogueMethod = AccessTools.Method(customDialogueServiceType, "ExecuteDialogue");
                if (executeDialogueMethod != null)
                {
                    MethodInfo prefixMethod = AccessTools.Method(typeof(GodHandRimTalkCustomDialoguePatch), nameof(Prefix_ExecuteDialogue));
                    harmony.Patch(executeDialogueMethod, prefix: new HarmonyMethod(prefixMethod));
                    Log.Message("[神之手] 成功 Patch CustomDialogueService.ExecuteDialogue (Context注入)");
                }

                // 拦截 PromptService 为玩家对话添加环境
                Type promptServiceType = AccessTools.TypeByName("RimTalk.Service.PromptService");
                if (promptServiceType != null)
                {
                    MethodInfo decoratePromptMethod = AccessTools.Method(promptServiceType, "DecoratePrompt");
                    if (decoratePromptMethod != null)
                    {
                        MethodInfo prefixMethod = AccessTools.Method(typeof(GodHandRimTalkCustomDialoguePatch), nameof(Prefix_DecoratePrompt));
                        harmony.Patch(decoratePromptMethod, prefix: new HarmonyMethod(prefixMethod));
                        Log.Message("[神之手] 成功 Patch PromptService.DecoratePrompt");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[神之手] 对话系统补丁失败 {ex.Message}");
            }
        }

        private static void InitializeReflection()
        {
            GodHandModMain.DebugLog("[神之手CustomPatch] 开始初始化反射对象...");
            try
            {
                Type cacheType = AccessTools.TypeByName("RimTalk.Data.Cache");
                cacheGetMethod = AccessTools.Method(cacheType, "Get", new Type[] { typeof(Pawn) });
                cacheGetPlayerMethod = AccessTools.Method(cacheType, "GetPlayer");
                GodHandModMain.DebugLog($"[神之手CustomPatch] Cache.Get方法: {cacheGetMethod != null}");
                GodHandModMain.DebugLog($"[神之手CustomPatch] Cache.GetPlayer方法: {cacheGetPlayerMethod != null}");

                Type pawnStateType = AccessTools.TypeByName("RimTalk.Data.PawnState");
                canDisplayTalkMethod = AccessTools.Method(pawnStateType, "CanDisplayTalk");
                addTalkRequestMethod = AccessTools.Method(pawnStateType, "AddTalkRequest");
                talkResponsesField = AccessTools.Field(pawnStateType, "TalkResponses");
                GodHandModMain.DebugLog($"[神之手CustomPatch] PawnState方法: {canDisplayTalkMethod != null}, {addTalkRequestMethod != null}");

                // 注册用户历史记录方法
                Type apiHistoryType = AccessTools.TypeByName("RimTalk.Data.ApiHistory");
                addUserHistoryMethod = AccessTools.Method(apiHistoryType, "AddUserHistory",
                    new Type[] { typeof(Pawn), typeof(Pawn), typeof(string) });
                GodHandModMain.DebugLog($"[神之手CustomPatch] ApiHistory.AddUserHistory方法: {addUserHistoryMethod != null}");

                Type apiLogType = AccessTools.TypeByName("RimTalk.Data.ApiLog");
                if (apiLogType != null)
                {
                    apiLogIdProp = AccessTools.Property(apiLogType, "Id");
                    spokenTickProp = AccessTools.Property(apiLogType, "SpokenTick");
                }

                talkResponseType = AccessTools.TypeByName("RimTalk.Data.TalkResponse");
                if (talkResponseType != null)
                {
                    talkResponseIdProp = AccessTools.Property(talkResponseType, "Id");
                }

                Type talkTypeEnum = AccessTools.TypeByName("RimTalk.Source.Data.TalkType");
                if (talkTypeEnum != null)
                {
                    talkTypeUser = Enum.Parse(talkTypeEnum, "User");
                }

                Type overlayType = AccessTools.TypeByName("RimTalk.UI.Overlay");
                notifyLogUpdatedMethod = AccessTools.Method(overlayType, "NotifyLogUpdated");

                // 环境信息相关的反射
                Type commonUtilType = AccessTools.TypeByName("RimTalk.Util.CommonUtil");
                getInGameDataMethod = AccessTools.Method(commonUtilType, "GetInGameData");

                Type inGameDataType = AccessTools.TypeByName("RimTalk.Util.CommonUtil+InGameData");
                if (inGameDataType != null)
                {
                    hour12HStringProp = AccessTools.Property(inGameDataType, "Hour12HString");
                    dateStringProp = AccessTools.Property(inGameDataType, "DateString");
                    seasonStringProp = AccessTools.Property(inGameDataType, "SeasonString");
                    weatherStringProp = AccessTools.Property(inGameDataType, "WeatherString");
                }
                else
                {
                    Log.Warning("[神之手] Reflection: InGameData Type not found");
                }

                // PromptService
                Type promptServiceType = AccessTools.TypeByName("RimTalk.Service.PromptService");
                if (promptServiceType != null)
                {
                    getPawnLocationStatusMethod = AccessTools.Method(promptServiceType, "GetPawnLocationStatus");
                    buildContextMethod = AccessTools.Method(promptServiceType, "BuildContext");
                }

                // AIService
                Type aiServiceType = AccessTools.TypeByName("RimTalk.Service.AIService");
                if (aiServiceType != null)
                {
                    updateContextMethod = AccessTools.Method(aiServiceType, "UpdateContext");
                    GodHandModMain.DebugLog($"[神之手CustomPatch] BuildContext和UpdateContext方法: {buildContextMethod != null}, {updateContextMethod != null}");
                }

                reflectionInitialized = true;
                GodHandModMain.DebugLog("[神之手CustomPatch] 反射对象初始化完成！");
            }
            catch (Exception ex)
            {
                Log.Error($"[神之手] 反射初始化异常: {ex}");
                reflectionInitialized = false;
            }
        }

        // 拦截 DecoratePrompt 注入环境信息
        public static bool Prefix_DecoratePrompt(object talkRequest, System.Collections.Generic.List<Pawn> pawns, string status)
        {
            try
            {
                if (talkRequest == null) return true;
                if (!GodHandModMain.Settings.enableRimTalkIntegration) return true;

                if (!reflectionInitialized) InitializeReflection();

                var traverse = Traverse.Create(talkRequest);
                string currentPrompt = traverse.Property("Prompt")?.GetValue<string>();

                // 检查 TalkType 是否为 User
                bool isUserTalk = false;
                object talkTypeObj = traverse.Property("TalkType")?.GetValue();
                if (talkTypeObj != null && talkTypeObj.ToString() == "User")
                {
                    isUserTalk = true;
                }

                // 只拦截玩家对话
                bool isPlayerDialogue = false;
                if (isUserTalk && cacheGetPlayerMethod != null)
                {
                    Pawn initiator = traverse.Property("Initiator")?.GetValue<Pawn>();
                    Pawn playerPawn = (Pawn)cacheGetPlayerMethod.Invoke(null, null);
                    if (playerPawn != null)
                    {
                        isPlayerDialogue = (initiator == playerPawn);
                    }
                }

                // 检查自定义标记
                if (isPlayerDialogue || (currentPrompt != null && (currentPrompt.Contains("[System Instruction: The user") || currentPrompt.Contains("GodHand.Memory.HighPriorityPrompt"))))
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    // 模仿 RimTalk 原版 User 格式
                    if (isPlayerDialogue && pawns != null && pawns.Count >= 2)
                    {
                        string playerName = GodHandModMain.Settings.playerName;
                        string playerRole = GodHandModMain.Settings.playerPersona;

                        // 识别接收者与玩家
                        Pawn recipientPawn = pawns[0];
                        string recipientName = ((Verse.Entity)recipientPawn).LabelShort;

                        // 动态获取 recipient 的 Role
                        string recipientRole = "Colonist";
                        try
                        {
                            Type pawnServiceType = AccessTools.TypeByName("RimTalk.Service.PawnService");
                            if (pawnServiceType != null)
                            {
                                MethodInfo getRoleMethod = AccessTools.Method(pawnServiceType, "GetRole", new Type[] { typeof(Pawn), typeof(bool) });
                                if (getRoleMethod != null)
                                {
                                    recipientRole = (string)getRoleMethod.Invoke(null, new object[] { recipientPawn, false });
                                }
                            }
                        }
                        catch
                        {
                            // 使用默认值
                        }

                        // 从 currentPrompt 提取原始消息
                        string rawMessage = currentPrompt;
                        int colonIndex = currentPrompt.IndexOf(": \"");
                        if (colonIndex >= 0)
                        {
                            rawMessage = currentPrompt.Substring(colonIndex + 3);
                            if (rawMessage.EndsWith("\""))
                            {
                                rawMessage = rawMessage.Substring(0, rawMessage.Length - 1);
                            }
                        }

                        // 构造原版风格的 Prompt
                        string wrappedPrompt = $"{playerName}({playerRole}) said to '{recipientName}({recipientRole}): {rawMessage}'.";
                        sb.Append(wrappedPrompt);
                        sb.Append($"\nGenerate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {recipientName}");

                        GodHandModMain.DebugLog($"[神之手DecoratePrompt] 玩家对话Prompt重新包装: {wrappedPrompt}");
                    }
                    else
                    {
                        // 回退方案
                        sb.Append(currentPrompt);
                        GodHandModMain.DebugLog($"[神之手DecoratePrompt] 使用回退Prompt: {currentPrompt}");
                    }

                    sb.Append("\n" + status);

                    // 获取环境信息
                    if (getPawnLocationStatusMethod != null && pawns != null && pawns.Count > 0)
                    {
                        string locationStatus = (string)getPawnLocationStatusMethod.Invoke(null, new object[] { pawns[0] });
                        if (!string.IsNullOrEmpty(locationStatus))
                        {
                            sb.Append("\nLocation: " + locationStatus);
                        }
                    }

                    if (getInGameDataMethod != null)
                    {
                        object gameData = getInGameDataMethod.Invoke(null, null);
                        if (gameData != null)
                        {
                            if (hour12HStringProp != null) sb.Append("\nTime: " + hour12HStringProp.GetValue(gameData));
                            if (dateStringProp != null) sb.Append("\nToday: " + dateStringProp.GetValue(gameData));
                            if (seasonStringProp != null) sb.Append("\nSeason: " + seasonStringProp.GetValue(gameData));
                            if (weatherStringProp != null) sb.Append("\nWeather: " + weatherStringProp.GetValue(gameData));
                        }
                    }

                    // 更新 Prompt
                    traverse.Property("Prompt")?.SetValue(ReplaceCommonTerms(sb.ToString()));

                    return false; // 阻止原方法执行
                }

                return true; // 其他情况执行原方法
            }
            catch (Exception ex)
            {
                Log.Warning($"[神之手] Prefix_DecoratePrompt 异常: {ex}");
                return true;
            }
        }

        private static string ReplaceCommonTerms(string text)
        {
            try
            {
                // 获取 GodHandRimTalkIntegration 中的同一个方法 (解耦或直接调用)
                var method = AccessTools.Method(typeof(GodHandRimTalkIntegration), "ReplaceCommonTerms");
                if (method != null)
                {
                    return (string)method.Invoke(null, new object[] { text });
                }
            }
            catch { }
            return text;
        }

        // 执行对话前注入 Context
        public static bool Prefix_ExecuteDialogue(Pawn initiator, Pawn recipient, string message)
        {
            // 检查是否启用RimTalk集成
            if (!GodHandModMain.Settings.enableRimTalkIntegration)
            {
                return true; // 使用原版逻辑
            }

            GodHandModMain.DebugLog($"[神之手CustomPatch] ExecuteDialogue被调用: {initiator?.LabelShort} -> {recipient?.LabelShort}, message: {message}");

            try
            {
                if (!reflectionInitialized)
                {
                    InitializeReflection();
                    if (!reflectionInitialized)
                    {
                        return true; // 初始化失败则回退
                    }
                }

                // 判断是否是玩家对话
                bool isPlayerDialogue = false;
                Pawn playerPawn = null;

                if (cacheGetPlayerMethod != null)
                {
                    playerPawn = (Pawn)cacheGetPlayerMethod.Invoke(null, null);
                    isPlayerDialogue = (initiator == playerPawn);
                    GodHandModMain.DebugLog($"[神之手CustomDialogue] 检查: initiator={initiator?.LabelShort}, recipient={recipient?.LabelShort}, playerPawn={playerPawn?.LabelShort}, isPlayer={isPlayerDialogue}");
                }

                if (!isPlayerDialogue)
                {
                    // 使用原版对话逻辑
                    GodHandModMain.DebugLog($"[神之手CustomDialogue] Pawn对话 ({initiator?.LabelShort} -> {recipient?.LabelShort})，使用原版逻辑");
                    return true;
                }

                // 玩家对话更新 System Context
                GodHandModMain.DebugLog($"[神之手CustomDialogue] 玩家对话: {initiator?.LabelShort} -> {recipient?.LabelShort}");

                try
                {
                    if (buildContextMethod != null && updateContextMethod != null)
                    {
                        // 构建对话角色列表
                        // 这与 RimTalk 原版的顺序一致
                        System.Collections.Generic.List<Pawn> pawns = new System.Collections.Generic.List<Pawn> { recipient, initiator };

                        GodHandModMain.DebugLog($"[神之手CustomDialogue] 调用BuildContext，pawns: {recipient.LabelShort}, {initiator.LabelShort}");

                        // 重新生成对话上下文
                        string newContext = (string)buildContextMethod.Invoke(null, new object[] { pawns });

                        GodHandModMain.DebugLog($"[神之手CustomDialogue] 已构建Context，长度: {newContext?.Length ?? 0}");

                        // 更新 AIService
                        updateContextMethod.Invoke(null, new object[] { newContext });
                        GodHandModMain.DebugLog($"[神之手CustomDialogue] 已更新AIService Context");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[神之手] Context构建异常: {ex.Message}");
                }

                // 执行后续对话处理
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[神之手] Prefix_ExecuteDialogue 运行时错误: {ex.Message}\n{ex.StackTrace}");
                return true; // 出错则回退
            }
        }
    }
}
