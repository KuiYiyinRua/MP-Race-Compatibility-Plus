using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MP_MeowOnlineShop
{
    /// <summary>本模组调试开关：设为 true 时输出对应类别日志，便于排查联机掉线。</summary>
    internal static class ModDebug
    {
        /// <summary>true 时输出通讯台 GetGizmos / FloatMenu 等日志（默认 false，避免刷屏）。</summary>
        public static readonly bool EnableCommsVerbose = false;
        /// <summary>true 时在棺材 Fall/Recover/WorkGiver 关键路径打日志（默认 true，便于排查恢复棺材掉线）。</summary>
        public static readonly bool EnableRigorMortisTrace = false;
        /// <summary>true 时输出喵喵电商购买同步关键路径日志（默认 false，诊断掉线时可临时打开）。</summary>
        public static readonly bool EnableMeowPurchaseTrace = false;
        /// <summary>true 时 TryLaunch 首次执行时打 Rand 包裹结果。</summary>
        public static readonly bool EnableTryLaunchLog = false;
        /// <summary>true 时输出 DropPodUtility.DropThingsNear 相关 Rand 包裹/种子日志。</summary>
        public static readonly bool EnableDropPodTrace = false;
        /// <summary>true 时输出 VoiceroidAsAnimal 驯服/训练联机补丁诊断日志（默认 false）。</summary>
        public static readonly bool EnableVoiceroidTameTrace = false;
        /// <summary>true 时输出文化DLC/任务联机补丁诊断日志（默认 false）。</summary>
        public static readonly bool EnableQuestIdeologyTrace = false;
        /// <summary>true 时输出矿物发现事件联机补丁诊断日志（默认 false）。</summary>
        public static readonly bool EnableMiningDiscoveryTrace = false;
        /// <summary>true 时输出污染类事件（空气污染器等）TryExecuteWorker 地图解析与 Find.CurrentMap 对照日志（默认 false）。</summary>
        public static readonly bool EnablePollutionIncidentTrace = false;
        /// <summary>true 时在 IncidentWorker 作用域中同步包裹 UnityEngine.Random.state（默认 true，用于第三方事件使用 Unity 随机）。</summary>
        public static readonly bool EnableIncidentUnityRandomScope = true;
        /// <summary>true 时输出远行队/世界 UI Rand 稳定补丁命中与异常日志（默认 false）。</summary>
        public static readonly bool EnableCaravanUiRandTrace = false;
        /// <summary>true 时输出 Axolotl 修炼功法切换 send/replay 与派生数值快照日志（默认 false）。</summary>
        public static readonly bool EnableAxolotlCultivationToggleTrace = false;
        /// <summary>true 时输出通讯台 Gizmo/FloatMenu 三层 Rand 包裹日志（默认 false）。</summary>
        public static readonly bool EnableCommsRandTrace = false;
        /// <summary>true 时输出 World/Zone Rand Push-Pop 配对计数日志（默认 false）。</summary>
        public static readonly bool EnableWorldRandScopeTrace = false;
    }

    /// <summary>
    /// 用于修复 Meow Online Shop 的"弹射出售器(SellSlingshot)"在联机下点击发射导致掉线的问题。
    ///
    /// 根因：
    /// 1) 原 Mod 通过确认弹窗的回调执行实际状态变更；联机下该回调容易丢失 map/transition 上下文。
    /// 2) 联机下多端同时执行 onConfirm 会导致 QuestPart.ArgumentNullException 与 Letter 未 deep-save。
    /// 3) 使用 [HarmonyPatch] + TargetMethod() 可能触发 Multiplayer 的 GetHarmonyVersions/ToDictionaryConsistent 异常。
    /// 4) TryLaunch 内调用的 DropCellFinder.TradeDropSpot、DropPodUtility.DropThingsNear 等会使用 Rand；
    ///    进入新地图时玩家阵营切换导致 Rand 状态在主机/客户端不一致，引发 "Random state from commands doesn't match" 掉线。
    /// 5) 联机校验可能报 "Wrong random state for the world"：刷新访客/通讯台或切换世界视图时，“当前 Rand”在主机为 map.Rand、在客户端为 World.rand，若只包一层静态 Rand 会单边消耗 World 随机状态。
    ///
    /// 方案：
    /// 1) 将 CompSellSlingshot.TryLaunch 注册为同步方法。
    /// 2) 使用手动 Harmony.Patch 而非 PatchAll，避免 Harmony 版本收集异常。
    /// 3) 联机时跳过确认弹窗，直接执行回调，让 TryLaunch 通过已注册的同步方法来处理。
    /// 4) 对 TryLaunch 显式包裹 静态 Rand + Map.Rand + World.rand 的 PushState(确定性种子)/PopState，双端一致。
    /// 5) 通用稳定（见 ApplyWorldRandStabilizerPatches）：World.Tick 内整 tick 用确定性 Rand，避免 “last valid tick -1”/加入即掉线；Zone_Growing/Zone_Fishing.GetGizmos 用确定性 Rand，避免点开区域时 world 随机单边消耗。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MpMeowOnlineShopBootstrap
    {
        /// <summary>喵呜框架 packageId；仅当该模组在列表中启用时才应用网店/通信台/购买等 Meow 生态补丁。</summary>
        private const string MeowFrameworkPackageId = "EoralMilk.MeowFramework";

        private const string HarmonyId = "mp.meowonlineshop.sellslingshot";
        private static readonly Harmony Harmony = new Harmony(HarmonyId);

        static MpMeowOnlineShopBootstrap()
        {
            if (!MP.enabled)
                return;

            // 延迟至所有模组加载完成后再 Patch，确保 Meow_SellSlingshot 类型已存在
            LongEventHandler.ExecuteWhenFinished(ApplyPatch);
        }

        private static void ApplyPatch()
        {
            try
            {
                LogBuildIdentity();

                // 与 Meow Framework 无关的联机补丁：始终尝试应用（内部按类型是否存在再决定是否打补丁）
                ApplyWorldRandStabilizerPatches();
                Patch_GravshipAbandonQueue.Apply(Harmony);
                ApplyCaravanFormingDiagnostics();
                ApplyVehicleFrameworkCaravanProxyGuard();
                Patch_MpDesyncTraceBudget.Apply(Harmony);
                Patch_AsyncRandStateDiagnostic.Apply(Harmony);
                Patch_PerformanceOptimizerMp.Apply(Harmony);
                Patch_SlowerPawnTickRateMp.Apply(Harmony);
                Patch_ThirdPartyPerformanceMp.Apply(Harmony);
                Patch_MultiplayerVtrContextGuard.Apply(Harmony);
                Patch_DateNotifierMultifactionDeterminism.Apply(Harmony);
                Patch_StoragePriorityMp.Apply(Harmony);
                Patch_MultiplayerAsyncQuestSnapshot.Apply(Harmony);
                Patch_AcceptJoinerWorldCommand.Apply(Harmony);
                Patch_AncientAmorphousThreatMp.Apply(Harmony);
                Patch_PerspectiveShiftMp.Apply(Harmony);
                Patch_MpSafeAlertThrottling.Apply(Harmony);
                Patch_NaturalGoodwillMultifactionDeterminism.Apply(Harmony);
                Patch_DubsMintMenusPlant.Apply(Harmony);
                Patch_MiliraActiveDropPod.Apply(Harmony);
                Patch_MiliraFallenAngelQuest.Apply(Harmony);
                Patch_MiliraTaleStorytellerDeterminism.Apply(Harmony);
                Patch_MiliraSupplyMp.Apply(Harmony);
                Patch_MiliraMultifactionRelations.Apply(Harmony);
                Patch_AudioRandIsolation.Apply(Harmony);
                Patch_RjwPeVoiceRandIsolation.Apply(Harmony);
                Patch_BallzAutoOrganRandIsolation.Apply(Harmony);
                Patch_KemomimiHouseAutoSpawnRand.Apply(Harmony);
                Patch_NiceBillTabLoad.Apply(Harmony);
                Patch_UniqueIdSimulationBoundary.Apply(Harmony);
                Patch_MutantAbilityCacheMp.Apply(Harmony);
                Patch_InsectGirlPermanentWoundMp.Apply(Harmony);
                Patch_DeterministicTickList.Apply(Harmony);
                Patch_AsyncTimeMapLoadSafety.Apply(Harmony);
                Patch_NudityMattersMoreMp.Apply(Harmony);
                Patch_DeterministicWorldPawns.Apply(Harmony);
                Patch_HospitalityInteractions.Apply(Harmony);
                Patch_HarbingerTreeSpawnDeterminism.Apply(Harmony);
                Patch_BloodAnimationsMp.Apply(Harmony);
                Patch_CaravanFloatMenuDiagnostic.Apply(Harmony);
                Patch_CaravanFloatMenuNullGuard.Apply(Harmony);
                Patch_CaravansBattlefieldVictoryDeterminism.Apply(Harmony);
                Patch_RpgDialogMp.Apply(Harmony);
                Patch_FishShadowRandIsolation.Apply(Harmony);
                Patch_SimpleFxSplashesRandIsolation.Apply(Harmony);
                Patch_StaticQualityDeterminism.Apply(Harmony);
                Patch_InspirationScheduleMp.Apply(Harmony);
                Patch_ChildcareRandMp.Apply(Harmony);
                Patch_RitualVisualEffectRandIsolation.Apply(Harmony);
                Patch_EliteRaidDeterminism.Apply(Harmony);
                Patch_KiiroStoryEventsMp.Apply(Harmony);
                Patch_SearchAndDestroyMp.Apply(Harmony);
                Patch_DefensivePositionsMp.Apply(Harmony);
                Patch_RatkinWeaponsMp.Apply();
                ApplyMpConfigHotSyncPatch();
                Patch_QuestAndIdeologyMp.Apply();
                Patch_MiningDiscoveryMp.Apply();
                Patch_WorkSiteQuestDeterminism.Apply(Harmony);
                Patch_CaravanVisitSiteFactionDeterminism.Apply(Harmony);
                Patch_CaravanEnterFactionDeterminism.Apply(Harmony);
                Patch_StorageGroupSync.Apply(Harmony);
                Patch_TechprintMpDiagnostic.Apply(Harmony);
                Patch_OberoniaBirthdayMp.Apply(Harmony);
                Patch_StorytellerRandomQuestDeterminism.Apply(Harmony);
                Patch_StorytellerIntervalDeterminism.Apply(Harmony);
                Patch_MilianDressMp.Apply(Harmony);
                Patch_HarbingerTreeSpawnExecutionDeterminism.Apply(Harmony);
                Patch_DisableHarbingerTreeSpawn.Apply(Harmony);
                Patch_RitualObligationDateDeterminism.Apply(Harmony);
                Patch_GoodwillRecalcMultifactionDeterminism.Apply(Harmony);
                Patch_TraderStockDeterminism.Apply(Harmony);
                Patch_InsectGirlSpawnFactionDeterminism.Apply(Harmony);
                Patch_RWBeheadingRandIsolation.Apply(Harmony);
                Patch_PollutionIncidentMp.Apply();
                Patch_BattleReferenceSaveFix.Apply(Harmony);
                ApplyDesignatorShapesCompatPatches();
                ApplyMultifactionTpsOptimizePatches();
                ApplyRigorMortisPatches();
                Patch_AncotCommandMode.Apply(Harmony);
                ApplyMiliraWeaponModePatches();
                ApplyMiliraShieldModePatches();
                ApplyMiliraFlightModePatches();
                ApplyMiliraFlyPatches();
                ApplyVoiceroidAsAnimalPatches();
                ApplyRpgInventoryDropPatch();
                ApplyRimJobWorldPatches();
                Patch_RjwMenstruation.Apply(Harmony);
                Patch_SecretaryNexusMp.Apply(Harmony);
                Patch_FacialAnimationMp.Apply(Harmony);
                ApplyAxolotlWeaponModePatches();
                ApplyAxolotlCultivationReadPatches();
                ApplyAxolotlCombatStabilityPatches();
                ApplyAxolotlJumpLandingPatches();
                ApplyAxolotlFlyerCarrySaveFixPatches();
                ApplyAxolotlVerbSaveFixPatches();
                ApplyAxolotlCommsPatches();

                if (!ModsConfig.IsActive(MeowFrameworkPackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Meow Framework 未启用：已跳过 SellSlingshot / 通信台 / 网店购买 等 Meow 生态补丁；World·DropPod·RigorMortis·Milira 等仍已尝试应用。");
                    return;
                }

                ApplySellSlingshotPatches();
                ApplyCommsConsolePatch();
                ApplyMeowPurchasePatch();

                Log.Message("[MP-MeowOnlineShop] MP 兼容启动完成（含 Meow Framework 相关补丁）。");
            }
            catch (Exception e)
            {
                Log.Error($"[MP-MeowOnlineShop] Failed to apply MP patch: {e}");
            }
        }

        /// <summary>RPG Style Inventory Revamped：联机下丢下装备/物品同步与空引用防护。</summary>
        private static void LogBuildIdentity()
        {
            try
            {
                Assembly assembly = typeof(MpMeowOnlineShopBootstrap).Assembly;
                string informational = "unknown";
                var informationalAttribute = Attribute.GetCustomAttribute(
                    assembly,
                    typeof(AssemblyInformationalVersionAttribute))
                    as AssemblyInformationalVersionAttribute;
                if (informationalAttribute != null)
                    informational = informationalAttribute.InformationalVersion;

                string sha256 = "unavailable";
                string location = assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    using (var stream = File.OpenRead(location))
                    using (var hasher = SHA256.Create())
                    {
                        sha256 = BitConverter.ToString(hasher.ComputeHash(stream))
                            .Replace("-", string.Empty);
                    }
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Loaded build identity: " +
                    $"assembly={assembly.GetName().Version}, informational={informational}, sha256={sha256}.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Failed to record loaded build identity: " + e.Message);
            }
        }

        private static void ApplyRpgInventoryDropPatch()
        {
            try
            {
                Patch_RpgInventoryDrop.Apply(Harmony);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RPG inventory drop patch failed: {e.Message}");
            }
        }

        /// <summary>RimJobWorld: sync uncovered RMB callbacks and stabilize baby trait inheritance RNG.</summary>
        private static void ApplyRimJobWorldPatches()
        {
            try
            {
                Patch_RimJobWorld.Apply(Harmony);
                Patch_RjwAddons.Apply(Harmony);
                Patch_RjwSerializationRandIsolation.Apply(Harmony);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop][RJW] compatibility patch failed: {e}");
            }
        }

        /// <summary>
        /// Designator Shapes：联机命令回放期间不记录本地撤销历史，避免非建造 designation 进入其历史栈引发存档噪音。
        /// </summary>
        private static void ApplyDesignatorShapesCompatPatches()
        {
            try
            {
                Patch_DesignatorShapesMpCompat.Apply(Harmony);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] DesignatorShapes compat patch failed: {e.Message}");
            }
        }

        /// <summary>Axolotl 通讯台：联机下替换嵌套 Dialog_Negotiation 为独立 MP 窗口，并同步交易动作。</summary>
        private static void ApplyAxolotlCommsPatches()
        {
            try
            {
                Patch_AxolotlComms.Apply(Harmony);
                Log.Message("[MP-MeowOnlineShop] Axolotl comms patch bootstrap done (hooks: CommFloatMenuOption/GiveUseCommsJob/TryOpenComms; trade sync path unchanged).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl comms patch failed: {e.Message}");
            }
        }

        /// <summary>Axolotl 螈力武器切换形态：联机下同步 Gizmo 点击，并稳定 Rand 状态。</summary>
        private static void ApplyAxolotlWeaponModePatches()
        {
            try
            {
                var harmonyAxolotlWeaponMode = new Harmony(Patch_AxolotlWeaponMode.HarmonyId);
                Patch_AxolotlWeaponMode.Apply(harmonyAxolotlWeaponMode);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl weapon mode patch failed: {e.Message}");
            }
        }

        /// <summary>Axolotl 修炼阅读：稳定阅读朝向随机与功法书列表顺序，避免选书后联机随机状态分叉。</summary>
        private static void ApplyAxolotlCultivationReadPatches()
        {
            try
            {
                var harmonyAxolotlCultivation = new Harmony("mp.meowonlineshop.axolotl.cultivation");
                Patch_AxolotlCultivationRead.Apply(harmonyAxolotlCultivation);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl cultivation read patch failed: {e.Message}");
            }
        }

        /// <summary>Axolotl 强化攻击链路（近战附效）局部随机稳定。</summary>
        private static void ApplyAxolotlCombatStabilityPatches()
        {
            try
            {
                var harmonyAxolotlCombat = new Harmony("mp.meowonlineshop.axolotl.combat");
                Patch_AxolotlCombatStability.Apply(harmonyAxolotlCombat);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl combat stability patch failed: {e.Message}");
            }
        }

        /// <summary>Axolotl 跳跃落地：联机下接管 FallOnGroundDo，空安全执行 Notify_EndJump，避免落地刷错。</summary>
        private static void ApplyAxolotlJumpLandingPatches()
        {
            try
            {
                var harmonyAxolotlJumpLanding = new Harmony("mp.meowonlineshop.axolotl.jumpLanding");
                Patch_AxolotlJumpLanding.Apply(harmonyAxolotlJumpLanding);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl jump landing patch failed: {e.Message}");
            }
        }

        /// <summary>Axolotl 飞行搬运存档修复：保存时剥离 stale carriedThing 引用，避免 not deep-saved。</summary>
        private static void ApplyAxolotlFlyerCarrySaveFixPatches()
        {
            try
            {
                var harmonyAxolotlFlyerCarrySave = new Harmony("mp.meowonlineshop.axolotl.flyerCarrySaveFix");
                Patch_AxolotlFlyerCarrySaveFix.Apply(harmonyAxolotlFlyerCarrySave);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl flyer carry-save fix patch failed: {e.Message}");
            }
        }

        /// <summary>Axolotl 武器 Verb 存档修复：保存前去重重复 Verb，避免 join point deep-save 冲突。</summary>
        private static void ApplyAxolotlVerbSaveFixPatches()
        {
            try
            {
                var harmonyAxolotlVerbSave = new Harmony("mp.meowonlineshop.axolotl.verbSaveFix");
                Patch_AxolotlVerbSaveFix.Apply(harmonyAxolotlVerbSave);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl verb save fix patch failed: {e.Message}");
            }
        }

        /// <summary>弹射出售器等：仅当类型存在时打补丁；不因缺失而中断其它补丁。</summary>
        private static void ApplySellSlingshotPatches()
        {
            try
            {
                var compType = AccessTools.TypeByName("Meow_SellSlingshot.CompSellSlingshot");
                if (compType == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] 未找到 Meow_SellSlingshot.CompSellSlingshot（未装对应网店扩展或加载顺序过早），SellSlingshot 补丁跳过。");
                    return;
                }

                var confirmIfMethod = AccessTools.Method(compType, "ConfirmIf");
                if (confirmIfMethod == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] 未找到 ConfirmIf，SellSlingshot 补丁跳过。");
                    return;
                }

                var tryLaunchMethod = AccessTools.Method(compType, "TryLaunch");
                if (tryLaunchMethod == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] 未找到 TryLaunch，SellSlingshot 补丁跳过。");
                    return;
                }

                MP.RegisterSyncMethod(compType, "TryLaunch");

                var confirmIfPrefix = typeof(Patch_SellSlingshot_ConfirmIf).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
                Harmony.Patch(confirmIfMethod, prefix: new HarmonyMethod(confirmIfPrefix));

                var tryLaunchPrefix = typeof(Patch_SellSlingshot_TryLaunch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
                var tryLaunchFinalizer = typeof(Patch_SellSlingshot_TryLaunch).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic);
                var tryLaunchTranspiler = typeof(Patch_SellSlingshot_TryLaunch).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
                Harmony.Patch(tryLaunchMethod,
                    prefix: new HarmonyMethod(tryLaunchPrefix),
                    finalizer: new HarmonyMethod(tryLaunchFinalizer),
                    transpiler: new HarmonyMethod(tryLaunchTranspiler));

                Log.Message("[MP-MeowOnlineShop] SellSlingshot 联机补丁已应用。");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SellSlingshot 补丁失败: {e}");
            }
        }

        /// <summary>应用世界/区域 Rand 稳定补丁，减轻 "Wrong random state for the world" 与 tick -1 掉线。</summary>
        private static void ApplyWorldRandStabilizerPatches()
        {
            try
            {
                var worldType = AccessTools.TypeByName("Verse.World");
                if (worldType != null)
                {
                    var worldTick = AccessTools.Method(worldType, "Tick");
                    if (worldTick != null)
                    {
                        var wtPrefix = typeof(Patch_WorldRandStabilizer.Patch_WorldTick).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                        var wtFinalizer = typeof(Patch_WorldRandStabilizer.Patch_WorldTick).GetMethod("Finalizer", BindingFlags.Public | BindingFlags.Static);
                        if (wtPrefix != null && wtFinalizer != null)
                        {
                            Harmony.Patch(worldTick,
                                prefix: new HarmonyMethod(wtPrefix),
                                finalizer: new HarmonyMethod(wtFinalizer));
                        }
                    }
                }

                ApplyZoneGetGizmosPatch("RimWorld.Zone_Growing");
                ApplyZoneGetGizmosPatch("RimWorld.Zone_Fishing");
                ApplyZoneGetGizmosPatch("RimWorld.Zone_Stockpile");
                ApplyZoneGetGizmosPatch("Verse.Zone_Growing");
                ApplyZoneGetGizmosPatch("Verse.Zone_Fishing");
                ApplyZoneGetGizmosPatch("Verse.Zone_Stockpile");
                ApplyCaravanWorldUiRandPatches();
                // 不对 Zone_Stockpile.GetInspectTabs 打补丁，避免与 AdaptiveStorage 等 mod 同补该方法时触发 Multiplayer 的 ToDictionaryConsistent 重复键异常导致掉线
                // ApplyZoneGetInspectTabsPatch("RimWorld.Zone_Stockpile");
                // ApplyZoneGetInspectTabsPatch("Verse.Zone_Stockpile");

                // 为所有通过 DropPodUtility.DropThingsNear 落地的空投包裹确定性 Rand，上下文与 SellSlingshot/RigorMortis 一致，
                // 解决诸如“喵喵电商购买 -> 空投落地解包瞬间 Wrong random state on map X”这类问题。
                try
                {
                    var dropType = typeof(DropPodUtility);

                    // 统一用“按前 3 个参数匹配”的方式适配不同 RimWorld 版本 / MP 修改过的重载：
                    // IntVec3, Map, IEnumerable<Thing>, ... 若后面还有 int/bool/Faction 之类的参数无所谓。
                    var candidates = dropType
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == "DropThingsNear")
                        .Where(m =>
                        {
                            var ps = m.GetParameters();
                            if (ps.Length < 3) return false;
                            return ps[0].ParameterType == typeof(IntVec3)
                                   && ps[1].ParameterType == typeof(Map)
                                   && typeof(IEnumerable<Thing>).IsAssignableFrom(ps[2].ParameterType);
                        })
                        .OrderByDescending(m => m.GetParameters().Length) // 先长后短，仅影响日志顺序
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        var dpPrefixShort = typeof(Patch_DropPodRandStabilizer).GetMethod("PrefixShort", BindingFlags.Public | BindingFlags.Static);
                        var dpPrefixOpenDelay = typeof(Patch_DropPodRandStabilizer).GetMethod("PrefixWithOpenDelay", BindingFlags.Public | BindingFlags.Static);
                        var dpFinalizer = typeof(Patch_DropPodRandStabilizer).GetMethod("Finalizer", BindingFlags.Public | BindingFlags.Static);
                        if (dpPrefixShort != null && dpFinalizer != null)
                        {
                            foreach (var dropMethod in candidates)
                            {
                                try
                                {
                                    var ps = dropMethod.GetParameters();
                                    var dpPrefix = (ps.Length >= 4 && ps[3].ParameterType == typeof(int) && dpPrefixOpenDelay != null)
                                        ? dpPrefixOpenDelay
                                        : dpPrefixShort;
                                    Harmony.Patch(dropMethod,
                                        prefix: new HarmonyMethod(dpPrefix),
                                        finalizer: new HarmonyMethod(dpFinalizer));
                                    Log.Message($"[MP-MeowOnlineShop] Patched DropPodUtility.{dropMethod.Name} ({dropMethod.GetParameters().Length} params) for Rand stabilization.");
                                }
                                catch (Exception exInner)
                                {
                                    Log.Warning($"[MP-MeowOnlineShop] Failed to patch DropPodUtility.{dropMethod.Name} ({dropMethod.GetParameters().Length} params): {exInner.Message}");
                                }
                            }
                        }
                        else
                        {
                            Log.Warning("[MP-MeowOnlineShop] DropPod Rand stabilizer: Prefix/Finalizer methods not found, patch skipped.");
                        }
                    }
                    else
                    {
                        // 某些 MP / RimWorld 版本可能完全重写了 DropThingsNear，找不到就静默跳过；
                        // 只有在显式打开 DropPodTrace 时才提示一次，避免误导为严重错误。
                        if (ModDebug.EnableDropPodTrace)
                            Log.Warning("[MP-MeowOnlineShop] DropPod Rand stabilizer: Could not find any DropPodUtility.DropThingsNear overload to patch (no matching signature). Rand for generic drop pods will not be stabilized.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MP-MeowOnlineShop] DropPod Rand stabilizer patch failed: {ex}");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] WorldRandStabilizer patch failed (some patches may be missing): {e}");
            }
        }

        /// <summary>
        /// Multiplayer 加入窗口：仅配置不一致时自动热同步并继续加入，避免强制重启。
        /// </summary>
        private static void ApplyMpConfigHotSyncPatch()
        {
            try
            {
                Patch_MpConfigHotSync.Apply(Harmony);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] MP config hot sync patch failed: {e.Message}");
            }
        }

        /// <summary>
        /// Multifaction: TPS 优化（主机设置旁开关 + 多基地确定性轮转调度）。
        /// 该补丁不依赖 Meow Framework。
        /// </summary>
        private static void ApplyMultifactionTpsOptimizePatches()
        {
            try
            {
                Patch_MultifactionTpsOptimize.Apply();
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Multifaction TPS optimize patch failed: {e.Message}");
            }
        }

        /// <summary>联机下将喵喵电商“确认购买”改为同步执行，避免 Thing inaccessible / Letter 未 deep-save 导致掉线。</summary>
        private static void ApplyMeowPurchasePatch()
        {
            try
            {
                MP.RegisterSyncMethod(typeof(Patch_MeowPurchase), nameof(Patch_MeowPurchase.ApplyMeowPurchase));
                try
                {
                    var withMap = AccessTools.Method(typeof(Patch_MeowPurchase), nameof(Patch_MeowPurchase.ApplyMeowPurchaseWithMap));
                    if (withMap != null)
                        Patch_MeowPurchase.SyncApplyMeowPurchaseWithMap = MP.RegisterSyncMethod(withMap, null);
                }
                catch (Exception exReg)
                {
                    Log.Warning($"[MP-MeowOnlineShop] MeowPurchase RegisterSyncMethod(ApplyMeowPurchaseWithMap) failed: {exReg.Message}");
                    MP.RegisterSyncMethod(typeof(Patch_MeowPurchase), nameof(Patch_MeowPurchase.ApplyMeowPurchaseWithMap));
                }

                MethodInfo targetMethod = null;
                Type targetType = null;

                // 1) 优先按原始类型名 Meow.Dialog_OnlineShop + OnAcceptKeyPressed
                var dialogType = AccessTools.TypeByName("Meow.Dialog_OnlineShop");
                if (dialogType != null)
                {
                    var onAccept = AccessTools.Method(dialogType, "OnAcceptKeyPressed");
                    if (onAccept != null)
                    {
                        targetType = dialogType;
                        targetMethod = onAccept;
                    }
                }

                // 2) 如果没找到，扫描所有加载的程序集，尽量猜出真正的 OnlineShop 对话框类型和确认方法
                if (targetMethod == null)
                {
                    try
                    {
                        var asmList = AppDomain.CurrentDomain.GetAssemblies();
                        var candidateTypes = asmList
                            .Where(a =>
                            {
                                var n = a.GetName().Name ?? "";
                                return n.IndexOf("Meow", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("OnlineShop", StringComparison.OrdinalIgnoreCase) >= 0;
                            })
                            .SelectMany(a =>
                            {
                                try { return a.GetTypes(); }
                                catch { return Array.Empty<Type>(); }
                            })
                            .Where(t =>
                            {
                                var n = t.FullName ?? t.Name ?? "";
                                return n.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0
                                       && n.IndexOf("OnlineShop", StringComparison.OrdinalIgnoreCase) >= 0;
                            })
                            .ToList();

                        foreach (var t in candidateTypes)
                        {
                            // 尝试常见的确认方法名
                            var m = AccessTools.Method(t, "OnAcceptKeyPressed")
                                ?? AccessTools.Method(t, "ConfirmPurchase")
                                ?? AccessTools.Method(t, "DoPurchase");
                            if (m != null && m.GetParameters().Length == 0)
                            {
                                targetType = t;
                                targetMethod = m;
                                break;
                            }
                        }

                        if (targetMethod != null)
                        {
                            Log.Message($"[MP-MeowOnlineShop] MeowPurchase patch: patched {targetType.FullName}.{targetMethod.Name}() for purchase sync.");
                        }
                        else
                        {
                            Log.Warning("[MP-MeowOnlineShop] MeowPurchase patch: could not find Dialog_OnlineShop confirm method. Purchase sync disabled.");
                        }
                    }
                    catch (Exception scanEx)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] MeowPurchase patch: failed while scanning assemblies: {scanEx}");
                    }
                }
                else
                {
                    Log.Message($"[MP-MeowOnlineShop] MeowPurchase patch: patched Meow.Dialog_OnlineShop.OnAcceptKeyPressed() for purchase sync.");
                }

                if (targetMethod != null)
                {
                    var prefix = typeof(Patch_MeowPurchase).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prefix != null)
                    {
                        var hm = new HarmonyMethod(prefix)
                        {
                            // 确保在任何其他前缀（包括 Multiplayer 自带补丁）之前运行，
                            // 这样我们可以优先拦截 OnAcceptKeyPressed，阻止原始 Meow 购买逻辑执行。
                            priority = Priority.First
                        };
                        Harmony.Patch(targetMethod, prefix: hm);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] MeowPurchase patch failed: {e}");
            }
        }

        /// <summary>联机下通讯台联系喵喵电商时改为同步方法打开界面，避免 UseCommsConsole Job 序列化 InvalidCastException。</summary>
        private static void ApplyCommsConsolePatch()
        {
            try
            {
                try
                {
                    var open = AccessTools.Method(typeof(Patch_CommsConsole), nameof(Patch_CommsConsole.OpenMeowCommsShop));
                    if (open != null)
                        MP.RegisterSyncMethod(open, null);
                }
                catch (Exception exReg)
                {
                    Log.Warning($"[MP-MeowOnlineShop] CommsConsole RegisterSyncMethod(OpenMeowCommsShop) failed: {exReg.Message}");
                    MP.RegisterSyncMethod(typeof(Patch_CommsConsole), nameof(Patch_CommsConsole.OpenMeowCommsShop));
                }

                var gizmosPostfix = typeof(Patch_CommsConsole_GetGizmos).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (gizmosPostfix != null)
                {
                    var getGizmosThing = AccessTools.Method(typeof(Thing), "GetGizmos");
                    if (getGizmosThing != null)
                    {
                        try { Harmony.Patch(getGizmosThing, postfix: new HarmonyMethod(gizmosPostfix)); }
                        catch (Exception ex) { Log.Warning($"[MP-MeowOnlineShop] Thing.GetGizmos patch failed: {ex}"); }
                    }
                    // 联机下 Building/通讯台实际可能走 ThingWithComps.GetGizmos，若不 Patch 则底部 Gizmo 栏无喵喵电商按钮
                    var thingWithCompsType = AccessTools.TypeByName("Verse.ThingWithComps");
                    if (thingWithCompsType != null)
                    {
                        var getGizmosTwc = AccessTools.Method(thingWithCompsType, "GetGizmos");
                        if (getGizmosTwc != null && getGizmosTwc.DeclaringType == thingWithCompsType)
                        {
                            try { Harmony.Patch(getGizmosTwc, postfix: new HarmonyMethod(gizmosPostfix)); }
                            catch (Exception ex) { Log.Warning($"[MP-MeowOnlineShop] ThingWithComps.GetGizmos patch failed: {ex}"); }
                        }
                    }
                }

                var getCommTargets = AccessTools.Method(typeof(Building_CommsConsole), "GetCommTargets");
                if (getCommTargets != null)
                {
                    var getCommPostfix = typeof(Patch_CommsConsole_GetCommTargets).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (getCommPostfix != null)
                    {
                        try { Harmony.Patch(getCommTargets, postfix: new HarmonyMethod(getCommPostfix)); }
                        catch (Exception ex) { Log.Warning($"[MP-MeowOnlineShop] GetCommTargets patch failed: {ex}"); }
                    }
                }

                var givePrefix = typeof(Patch_CommsConsole_GiveUseCommsJob).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (givePrefix != null)
                {
                    var giveHm = new HarmonyMethod(givePrefix) { priority = int.MinValue };
                    var giveUseCommsJob = AccessTools.Method(typeof(Building_CommsConsole), "GiveUseCommsJob", new[] { typeof(Pawn), typeof(ICommunicable) });
                    if (giveUseCommsJob != null)
                        Harmony.Patch(giveUseCommsJob, prefix: giveHm);
                    foreach (var t in GenTypes.AllTypes)
                    {
                        if (t == null || t == typeof(Building_CommsConsole) || !typeof(Building_CommsConsole).IsAssignableFrom(t)) continue;
                        var m = AccessTools.Method(t, "GiveUseCommsJob", new[] { typeof(Pawn), typeof(ICommunicable) });
                        if (m != null && m.DeclaringType == t)
                        {
                            try { Harmony.Patch(m, prefix: giveHm); } catch { }
                        }
                    }
                }

                var commFloatPrefix = typeof(Patch_CommsConsole_CommFloatMenuOption).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (commFloatPrefix != null)
                {
                    var commFloatHm = new HarmonyMethod(commFloatPrefix) { priority = int.MinValue };
                    var patched = false;
                    var commShopType = AccessTools.TypeByName("Meow.CommsShop");
                    if (commShopType != null)
                    {
                        var m = AccessTools.Method(commShopType, "CommFloatMenuOption", new[] { typeof(Building_CommsConsole), typeof(Pawn) });
                        if (m != null)
                        {
                            try { Harmony.Patch(m, prefix: commFloatHm); patched = true; }
                            catch (Exception ex) { Log.Warning($"[MP-MeowOnlineShop] CommFloatMenuOption patch failed: {ex}"); }
                        }
                    }
                    if (!patched)
                    {
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                foreach (var t in asm.GetTypes())
                                {
                                    if (t == null || t.IsInterface || t.FullName?.IndexOf("CommsShop", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    var mm = AccessTools.Method(t, "CommFloatMenuOption", new[] { typeof(Building_CommsConsole), typeof(Pawn) });
                                    if (mm != null && mm.DeclaringType == t)
                                    {
                                        try { Harmony.Patch(mm, prefix: commFloatHm); patched = true; break; }
                                        catch { }
                                    }
                                }
                            }
                            catch { }
                            if (patched) break;
                        }
                    }
                }

                var trackerType = AccessTools.TypeByName("Verse.AI.Pawn_JobTracker");
                if (trackerType != null)
                {
                    var tryPrefix = typeof(Patch_CommsConsole_TryTakeOrderedJob).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var tryFinalizer = typeof(Patch_CommsConsole_TryTakeOrderedJob).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (tryPrefix != null && tryFinalizer != null)
                    {
                        var hm = new HarmonyMethod(tryPrefix) { priority = 1000 };
                        var hf = new HarmonyMethod(tryFinalizer) { priority = 1000 };
                        foreach (var m in trackerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (m.Name != "TryTakeOrderedJob") continue;
                            try { Harmony.Patch(m, prefix: hm, finalizer: hf); } catch { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] CommsConsole patch failed: {e}");
            }
        }

        private static void ApplyZoneGetGizmosPatch(string typeName)
        {
            var zoneType = AccessTools.TypeByName(typeName);
            if (zoneType == null) return;
            var getGizmos = AccessTools.Method(zoneType, "GetGizmos");
            if (getGizmos == null) return;
            var prefix = typeof(Patch_WorldRandStabilizer.Patch_ZoneGetGizmos).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
            var finalizer = typeof(Patch_WorldRandStabilizer.Patch_ZoneGetGizmos).GetMethod("Finalizer", BindingFlags.Public | BindingFlags.Static);
            if (prefix == null || finalizer == null) return;
            try
            {
                Harmony.Patch(getGizmos, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
            }
            catch { /* 可能已被其他 mod 补丁，忽略 */ }
        }

        private static void ApplyZoneGetInspectTabsPatch(string typeName)
        {
            var zoneType = AccessTools.TypeByName(typeName);
            if (zoneType == null) return;
            var getInspectTabs = AccessTools.Method(zoneType, "GetInspectTabs");
            if (getInspectTabs == null) return;
            var prefix = typeof(Patch_WorldRandStabilizer.Patch_ZoneGetInspectTabs).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
            var finalizer = typeof(Patch_WorldRandStabilizer.Patch_ZoneGetInspectTabs).GetMethod("Finalizer", BindingFlags.Public | BindingFlags.Static);
            if (prefix == null || finalizer == null) return;
            try
            {
                Harmony.Patch(getInspectTabs, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
            }
            catch { /* 可能已被其他 mod 补丁，忽略 */ }
        }

        private static readonly HashSet<MethodBase> PatchedCaravanWorldUiMethods = new HashSet<MethodBase>();

        private static void ApplyCaravanFormingDiagnostics()
        {
            try
            {
                var sessionType = AccessTools.TypeByName("Multiplayer.Client.CaravanFormingSession");
                var addItems = AccessTools.Method(sessionType, "AddItems");
                var postfix = AccessTools.Method(typeof(MpMeowOnlineShopBootstrap), nameof(CaravanFormingAddItems_Postfix));
                if (sessionType == null || addItems == null || postfix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Caravan forming diagnostics unresolved; session AddItems hook skipped.");
                    return;
                }

                Harmony.Patch(addItems, postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] Caravan forming diagnostics active: Multiplayer.Client.CaravanFormingSession.AddItems resolved.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Caravan forming diagnostics init failed: " + e.Message);
            }
        }

        private static void CaravanFormingAddItems_Postfix(object __instance)
        {
            if (__instance == null)
                return;

            try
            {
                var type = __instance.GetType();
                var transferables = AccessTools.Field(type, "transferables")?.GetValue(__instance) as System.Collections.IEnumerable;
                int transferableCount = transferables?.Cast<object>().Count() ?? -1;
                if (transferableCount > 0)
                    return;

                var map = AccessTools.Field(type, "map")?.GetValue(__instance) as Map;
                var faction = AccessTools.Field(type, "faction")?.GetValue(__instance) as Faction;
                int allPawns = map?.mapPawns?.AllPawnsSpawned?.Count ?? -1;
                int factionPawns = map?.mapPawns?.AllPawnsSpawned?.Count(p => p?.Faction == faction) ?? -1;
                int allThings = map?.listerThings?.AllThings?.Count ?? -1;

                Log.Warning(
                    "[MP-MeowOnlineShop] Empty caravan forming session detected: " +
                    $"map={map?.Index.ToString() ?? "null"}, mapParentFaction={map?.ParentFaction?.GetUniqueLoadID() ?? "null"}, " +
                    $"sessionFaction={faction?.GetUniqueLoadID() ?? "null"}, allPawns={allPawns}, factionPawns={factionPawns}, " +
                    $"allThings={allThings}, transferables={transferableCount}.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Caravan forming diagnostics failed: " + e.Message);
            }
        }

        private static bool _loggedVehicleCaravanProxyGuard;
        private static bool _loggedVehicleCaravanProxyTabsRepair;
        private static bool _loggedVehicleCaravanProxyVehicleTabRestored;
        private static bool _loggedEmptyColonyCaravanCancelRepair;
        private static FieldInfo _vehicleCaravanSelectedTabField;
        private static FieldInfo _vehicleCaravanVehiclesTransferField;
        private static FieldInfo _caravanFormingProxyDrawingField;
        private static Type _vehiclePawnType;
        private static object _vehicleCaravanAssignedSeats;
        private static MethodInfo _vehicleCaravanAssignedSeatsClearMethod;
        private static bool _loggedVehicleCaravanAssignedSeatsReset;
        private static bool _loggedVehicleCaravanAssignedSeatsResetFailure;

        private static void ApplyVehicleFrameworkCaravanProxyGuard()
        {
            try
            {
                var patchType = AccessTools.TypeByName("Vehicles.Patch_FormCaravanDialog");
                if (patchType == null)
                {
                    Log.Message("[MP-MeowOnlineShop] Vehicle Framework caravan proxy guard not required: patch type not found.");
                    return;
                }

                _vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
                var caravanFormingProxyType = AccessTools.TypeByName(
                    "Multiplayer.Client.CaravanFormingProxy");
                _caravanFormingProxyDrawingField = caravanFormingProxyType == null
                    ? null
                    : AccessTools.Field(caravanFormingProxyType, "drawing");

                // Vehicle Framework keeps seat choices in a process-wide UI-only
                // singleton. Multiplayer's persistent CaravanFormingProxy does not
                // expose VF's vehicle/seat UI, but VF's global mass transpilers still
                // consult that singleton while MP reconstructs a dummy dialog for a
                // synced command. A stale per-client assignment can therefore make
                // one peer fail CheckForErrors (MassUsage > MassCapacity) while the
                // other peer continues into random exit-cell selection.
                var caravanHelperType = AccessTools.TypeByName("Vehicles.CaravanHelper");
                var assignedSeatsField = caravanHelperType == null
                    ? null
                    : AccessTools.Field(caravanHelperType, "assignedSeats");
                _vehicleCaravanAssignedSeats = assignedSeatsField?.GetValue(null);
                _vehicleCaravanAssignedSeatsClearMethod = _vehicleCaravanAssignedSeats == null
                    ? null
                    : AccessTools.Method(_vehicleCaravanAssignedSeats.GetType(), "Clear");

                var caravanSessionType = AccessTools.TypeByName(
                    "Multiplayer.Client.CaravanFormingSession");
                var resetAssignmentsPrefix = AccessTools.Method(
                    typeof(MpMeowOnlineShopBootstrap),
                    nameof(VehicleCaravanProxyResetAssignments_Prefix));
                var sessionDialogBoundaries = caravanSessionType == null
                    ? new MethodInfo[0]
                    : new[]
                    {
                        AccessTools.Method(caravanSessionType, "AddItems"),
                        AccessTools.Method(caravanSessionType, "PrepareDummyDialog")
                    };
                if (_vehicleCaravanAssignedSeats != null &&
                    _vehicleCaravanAssignedSeatsClearMethod != null &&
                    resetAssignmentsPrefix != null &&
                    sessionDialogBoundaries.Length == 2 &&
                    sessionDialogBoundaries.All(method => method != null))
                {
                    foreach (var method in sessionDialogBoundaries)
                    {
                        Harmony.Patch(
                            method,
                            prefix: new HarmonyMethod(resetAssignmentsPrefix)
                            {
                                priority = Priority.First
                            });
                    }
                    Log.Message(
                        "[MP-MeowOnlineShop] Vehicle Framework Multiplayer caravan seat-state reset active.");
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Vehicle Framework Multiplayer caravan seat-state reset unresolved; " +
                        "stale local seat assignments may affect caravan mass validation.");
                }

                var target = patchType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "TryAndSendWithVehicles", StringComparison.Ordinal))
                            return false;
                        var parameters = method.GetParameters();
                        return parameters.Length == 1 &&
                               typeof(Dialog_FormCaravan).IsAssignableFrom(parameters[0].ParameterType);
                    });
                var prefix = AccessTools.Method(
                    typeof(MpMeowOnlineShopBootstrap),
                    nameof(VehicleTryAndSendWithVehicles_Prefix));
                _vehicleCaravanSelectedTabField = AccessTools.Field(patchType, "selectedTab");
                _vehicleCaravanVehiclesTransferField = AccessTools.Field(patchType, "vehiclesTransfer");
                var createTabs = AccessTools.Method(patchType, "CreateTabListPostOpen");
                var createTabsPrefix = AccessTools.Method(
                    typeof(MpMeowOnlineShopBootstrap),
                    nameof(VehicleCreateTabListPostOpen_Prefix));
                if (createTabs != null && createTabsPrefix != null && _vehicleCaravanSelectedTabField != null)
                {
                    Harmony.Patch(
                        createTabs,
                        prefix: new HarmonyMethod(createTabsPrefix) { priority = Priority.First });
                    Log.Message("[MP-MeowOnlineShop] Vehicle Framework Multiplayer caravan tab repair active.");
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Vehicle Framework caravan proxy tab creation unresolved; " +
                        "pawns/items/travel supplies tabs may remain unavailable.");
                }
                var vehicleWidgetType = AccessTools.TypeByName(
                    "Vehicles.World.TransferableVehicleWidget");
                var vehicleWidgetOnGui = vehicleWidgetType == null
                    ? null
                    : AccessTools.Method(vehicleWidgetType, "OnGUI");
                var vehicleWidgetPrefix = AccessTools.Method(
                    typeof(MpMeowOnlineShopBootstrap),
                    nameof(VehicleTransferableWidgetOnGUI_Prefix));
                var vehicleWidgetPostfix = AccessTools.Method(
                    typeof(MpMeowOnlineShopBootstrap),
                    nameof(VehicleTransferableWidgetOnGUI_Postfix));
                if (vehicleWidgetOnGui != null && vehicleWidgetPrefix != null &&
                    vehicleWidgetPostfix != null)
                {
                    Harmony.Patch(
                        vehicleWidgetOnGui,
                        prefix: new HarmonyMethod(vehicleWidgetPrefix),
                        postfix: new HarmonyMethod(vehicleWidgetPostfix));
                    Log.Message(
                        "[MP-MeowOnlineShop] Vehicle Framework Multiplayer vehicle tab read-only guard active.");
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Vehicle Framework vehicle tab read-only guard unresolved; " +
                        "the proxy vehicle tab may remain interactive.");
                }
                var postClose = AccessTools.Method(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.PostClose));
                var postClosePostfix = AccessTools.Method(
                    typeof(MpMeowOnlineShopBootstrap),
                    nameof(VehicleCaravanProxyPostClose_Postfix));
                if (_vehicleCaravanSelectedTabField != null && postClose != null && postClosePostfix != null)
                {
                    Harmony.Patch(
                        postClose,
                        postfix: new HarmonyMethod(postClosePostfix) { priority = Priority.Last });
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Vehicle Framework caravan proxy tab reset unresolved; " +
                        "the second caravan window may reopen on an empty vehicle tab.");
                }

                if (target != null && prefix != null)
                {
                    Harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                    Log.Message("[MP-MeowOnlineShop] Vehicle Framework caravan proxy guard active: TryAndSendWithVehicles resolved.");
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Vehicle Framework TryAndSendWithVehicles guard unresolved; " +
                        "proxy tab reset remains active.");
                }

                var showCancelButton = AccessTools.PropertyGetter(
                    typeof(Dialog_FormCaravan), "ShowCancelButton");
                var showCancelPrefix = AccessTools.Method(
                    typeof(MpMeowOnlineShopBootstrap),
                    nameof(VehicleCaravanProxyShowCancelButton_Prefix));
                if (_vehiclePawnType != null && showCancelButton != null && showCancelPrefix != null)
                {
                    Harmony.Patch(
                        showCancelButton,
                        prefix: new HarmonyMethod(showCancelPrefix) { priority = Priority.First });
                    Log.Message(
                        "[MP-MeowOnlineShop] Vehicle Framework empty-colony caravan cancel guard active.");
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Vehicle Framework empty-colony caravan cancel guard unresolved; " +
                        "a vehicle-only map may hide the cancel button.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Vehicle Framework caravan proxy guard init failed: " + e.Message);
            }
        }

        private static bool VehicleCaravanProxyShowCancelButton_Prefix(
            Dialog_FormCaravan __instance,
            ref bool __result)
        {
            if (__instance == null ||
                !string.Equals(
                    __instance.GetType().FullName,
                    "Multiplayer.Client.CaravanFormingProxy",
                    StringComparison.Ordinal) ||
                _vehiclePawnType == null)
            {
                return true;
            }

            // Vanilla hides Cancel while a map-about-to-be-removed contains an
            // able-bodied colonist. VehiclePawn also satisfies Pawn.IsColonist,
            // although the MP proxy intentionally cannot expose VF's uninitialized
            // vehicle tab. Ignore vehicle pawns for this visibility decision so a
            // vehicle-only/empty colony can always leave the persistent session via
            // Multiplayer's existing Cancel button -> Session.Cancel sync path.
            bool hasOrdinaryAbleColonist = __instance.transferables != null &&
                __instance.transferables.Any(transferable =>
                {
                    var pawn = transferable?.AnyThing as Pawn;
                    return pawn != null &&
                           pawn.IsColonist &&
                           !pawn.Downed &&
                           !_vehiclePawnType.IsInstanceOfType(pawn);
                });

            if (hasOrdinaryAbleColonist)
                return true;

            __result = true;
            if (!_loggedEmptyColonyCaravanCancelRepair)
            {
                _loggedEmptyColonyCaravanCancelRepair = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Forced the Multiplayer caravan Cancel button visible " +
                    "for an empty or vehicle-only colony.");
            }
            return false;
        }

        private static void VehicleCaravanProxyResetAssignments_Prefix()
        {
            if (_vehicleCaravanAssignedSeats == null ||
                _vehicleCaravanAssignedSeatsClearMethod == null)
            {
                return;
            }

            try
            {
                // The MP proxy cannot author VF seat assignments, so retaining this
                // process-local state across proxy sessions is always stale. Clear it
                // before both the initial transferable calculation and every dummy
                // dialog reconstruction, including synced TryFormAndSendCaravan.
                _vehicleCaravanAssignedSeatsClearMethod.Invoke(
                    _vehicleCaravanAssignedSeats,
                    null);

                if (!_loggedVehicleCaravanAssignedSeatsReset)
                {
                    _loggedVehicleCaravanAssignedSeatsReset = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Cleared Vehicle Framework local seat assignments " +
                        "before rebuilding a Multiplayer caravan proxy.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedVehicleCaravanAssignedSeatsResetFailure)
                {
                    _loggedVehicleCaravanAssignedSeatsResetFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Failed to clear Vehicle Framework local caravan " +
                        "seat assignments: " + e.Message);
                }
            }
        }

        private static bool VehicleTryAndSendWithVehicles_Prefix(Dialog_FormCaravan __0, ref bool __result)
        {
            // The target is Vehicle Framework's static Harmony prefix. Harmony's
            // special __instance is therefore always null; __0 is its first real
            // argument (the Dialog_FormCaravan being sent).
            if (__0 == null ||
                !string.Equals(
                    __0.GetType().FullName,
                    "Multiplayer.Client.CaravanFormingProxy",
                    StringComparison.Ordinal))
                return true;

            // This method is itself Vehicle Framework's Harmony prefix for
            // Dialog_FormCaravan.TrySend. Returning true as its result tells
            // Harmony to continue the original Multiplayer proxy flow.
            __result = true;
            if (!_loggedVehicleCaravanProxyGuard)
            {
                _loggedVehicleCaravanProxyGuard = true;
                Log.Message("[MP-MeowOnlineShop] Bypassed Vehicle Framework TryAndSendWithVehicles for Multiplayer CaravanFormingProxy.");
            }
            return false;
        }

        private static bool VehicleCreateTabListPostOpen_Prefix(
            Dialog_FormCaravan __0,
            List<TabRecord> __2)
        {
            if (__0 == null ||
                !string.Equals(
                    __0.GetType().FullName,
                    "Multiplayer.Client.CaravanFormingProxy",
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (__2 == null || _vehicleCaravanSelectedTabField == null)
                return false;

            // Multiplayer deliberately opens a fresh proxy with
            // thisWindowInstanceEverOpened=true because its session already built
            // the transfer widgets. Vehicle Framework treats that as a reused
            // vanilla dialog and skips all tab creation; its PostClose patch also
            // clears the shared list and leaves selectedTab on Vehicles (10).
            // Rebuild the three widgets owned by Multiplayer and add a read-only
            // Vehicles tab whose TransferableVehicleWidget was already created by
            // VF's CreateCaravanTransferableWidgets postfix. The widget is made
            // non-interactive for the proxy because VF's CaravanFormation.formation
            // (needed for seat assignment) is not owned by Multiplayer's session.
            __2.Clear();
            if (_vehicleCaravanVehiclesTransferField != null &&
                _vehicleCaravanVehiclesTransferField.GetValue(null) != null)
            {
                AddVehicleFrameworkProxyTab(__2, "VF_Vehicles", 10);
                if (!_loggedVehicleCaravanProxyVehicleTabRestored)
                {
                    _loggedVehicleCaravanProxyVehicleTabRestored = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Restored read-only Vehicles tab for Multiplayer CaravanFormingProxy.");
                }
            }
            AddVehicleFrameworkProxyTab(__2, "PawnsTab", 0);
            AddVehicleFrameworkProxyTab(__2, "ItemsTab", 1);
            AddVehicleFrameworkProxyTab(__2, "TravelSupplies", 2);
            _vehicleCaravanSelectedTabField.SetValue(null, 0);

            if (!_loggedVehicleCaravanProxyTabsRepair)
            {
                _loggedVehicleCaravanProxyTabsRepair = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Restored pawns/items/travel supplies tabs for Multiplayer CaravanFormingProxy.");
            }

            // Replace Vehicle Framework's method only for the Multiplayer proxy.
            return false;
        }

        private static void VehicleTransferableWidgetOnGUI_Prefix(ref bool __state)
        {
            __state = GUI.enabled;
            if (_caravanFormingProxyDrawingField == null ||
                _caravanFormingProxyDrawingField.GetValue(null) == null)
            {
                return;
            }

            // The proxy cannot author VF seat assignments because
            // CaravanFormation.formation is not initialized for it. Keep the
            // vehicle cards fully visible for mass/capacity inspection, but block
            // checkbox/sorter clicks that would dereference that missing state.
            GUI.enabled = false;
        }

        private static void VehicleTransferableWidgetOnGUI_Postfix(bool __state)
        {
            GUI.enabled = __state;
        }

        private static void AddVehicleFrameworkProxyTab(List<TabRecord> tabs, string labelKey, int tab)
        {
            tabs.Add(new TabRecord(
                labelKey.Translate(),
                () => _vehicleCaravanSelectedTabField.SetValue(null, tab),
                () => (int)_vehicleCaravanSelectedTabField.GetValue(null) == tab));
        }

        private static void VehicleCaravanProxyPostClose_Postfix(Dialog_FormCaravan __instance)
        {
            if (__instance == null ||
                !string.Equals(
                    __instance.GetType().FullName,
                    "Multiplayer.Client.CaravanFormingProxy",
                    StringComparison.Ordinal) ||
                _vehicleCaravanSelectedTabField == null)
            {
                return;
            }

            // Vehicle Framework's PostClose postfix sets its process-wide selectedTab
            // to the vehicle tab (10). Multiplayer deliberately constructs a reused
            // CaravanFormingProxy with thisWindowInstanceEverOpened=true, so VF skips
            // its PostOpen tab initialization on the second opening. The stale value
            // then draws the empty vehicle widget instead of pawns/items.
            _vehicleCaravanSelectedTabField.SetValue(null, 0);
            if (ModDebug.EnableCaravanUiRandTrace)
                Log.Message("[MP-MeowOnlineShop] Reset Vehicle Framework caravan tab after closing Multiplayer proxy.");
        }

        private static void ApplyCaravanWorldUiRandPatches()
        {
            var prefix = typeof(Patch_WorldRandStabilizer.Patch_CaravanAndWorldUiRand).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
            var finalizer = typeof(Patch_WorldRandStabilizer.Patch_CaravanAndWorldUiRand).GetMethod("Finalizer", BindingFlags.Public | BindingFlags.Static);
            if (prefix == null || finalizer == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Caravan/world UI Rand stabilizer methods not found, patch skipped.");
                return;
            }

            // 远行队弹窗主流程
            // Multiplayer owns Dialog_FormCaravan through CaravanFormingProxy and
            // CaravanFormingSession. Wrapping the vanilla dialog lifecycle here is
            // redundant and unsafe while MP reconstructs map/faction command context.
            // Leave caravan transfer calculation and drawing entirely to Multiplayer.
            Log.Message("[MP-MeowOnlineShop] Caravan UI Rand patch skipped: Multiplayer native CaravanFormingProxy/Session retained.");
            // 世界目标器与路线规划（尽量覆盖实例方法，避免静态上下文无 __instance）
            ApplyTypeMethodNamesPatch("RimWorld.WorldTargeter", new[] { "TargeterOnGUI", "ProcessInputEvents", "StopTargeting", "WorldTargeterUpdate" }, prefix, finalizer);
            ApplyTypeMethodNamesPatch("RimWorld.WorldRoutePlanner", new[] { "DoRoutePlannerButton", "get_ShouldStop", "Stop" }, prefix, finalizer);
            // 世界对象菜单（远行队目的地右键菜单常经过此入口）
            ApplyTypeMethodNamesPatch("RimWorld.WorldObject", new[] { "GetFloatMenuOptions" }, prefix, finalizer);
            ApplyDerivedWorldObjectFloatMenuPatches(prefix, finalizer);
        }

        private static void ApplyTypeMethodNamesPatch(string typeName, IEnumerable<string> methodNames, MethodInfo prefix, MethodInfo finalizer)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null || methodNames == null) return;
            foreach (var name in methodNames)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                        continue;
                    TryPatchCaravanWorldMethod(method, prefix, finalizer);
                }
            }
        }

        private static void ApplyDerivedWorldObjectFloatMenuPatches(MethodInfo prefix, MethodInfo finalizer)
        {
            try
            {
                foreach (var type in GenTypes.AllTypes)
                {
                    if (type == null || type.IsAbstract || !typeof(WorldObject).IsAssignableFrom(type))
                        continue;
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (!string.Equals(method.Name, "GetFloatMenuOptions", StringComparison.Ordinal))
                            continue;
                        if (method.DeclaringType != type)
                            continue;
                        TryPatchCaravanWorldMethod(method, prefix, finalizer);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Failed while patching derived WorldObject.GetFloatMenuOptions: {e.Message}");
            }
        }

        private static void TryPatchCaravanWorldMethod(MethodInfo method, MethodInfo prefix, MethodInfo finalizer)
        {
            if (method == null || method.IsStatic)
                return;
            if (!PatchedCaravanWorldUiMethods.Add(method))
                return;
            try
            {
                Harmony.Patch(method, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                if (ModDebug.EnableCaravanUiRandTrace)
                    Log.Message($"[MP-MeowOnlineShop] Patched caravan/world UI Rand method: {method.DeclaringType?.FullName}.{method.Name}");
            }
            catch (Exception e)
            {
                if (ModDebug.EnableCaravanUiRandTrace)
                    Log.Warning($"[MP-MeowOnlineShop] Failed to patch caravan/world UI Rand method {method.DeclaringType?.FullName}.{method.Name}: {e.Message}");
            }
        }

        /// <summary>为 Rigor Mortis（僵死模组 3454053400）应用联机 Rand 稳定补丁；使用独立 Harmony 实例减轻 Multiplayer 收集补丁时的 ToDictionaryConsistent 重复键风险。</summary>
        private static void ApplyRigorMortisPatches()
        {
            Harmony harmonyRM = null;
            try
            {
                harmonyRM = new Harmony("mp.meowonlineshop.rigormortis");
                Log.Message("[MP-MeowOnlineShop] Applying Rigor Mortis multiplayer patches...");
                try
                {
                    var zombieType = AccessTools.TypeByName("RigorMortis.ZombieTraderComms");
                    var rmUtilityType = AccessTools.TypeByName("RigorMortis.RMUtility");
                    var rmDefOfType = AccessTools.TypeByName("RigorMortis.RMDefOf");
                    var hasComponentAccessor = false;
                    var hasTraderDef = false;

                    if (rmUtilityType != null)
                    {
                        var componentProp = AccessTools.Property(rmUtilityType, "TheComponent");
                        // Startup runs before Current.Game is guaranteed to exist.
                        // Resolving the accessor is sufficient here; invoking it used
                        // to throw TargetInvocationException on every normal launch.
                        hasComponentAccessor = componentProp?.GetGetMethod(true) != null;
                    }
                    if (rmDefOfType != null)
                    {
                        hasTraderDef = AccessTools.Field(rmDefOfType, "Axolotl_Orbital_ZombieGoods")?.GetValue(null) != null;
                    }

                    Log.Message($"[MP-MeowOnlineShop] RigorMortis precheck: zombieType={(zombieType != null)} rmUtility={(rmUtilityType != null)} rmComponentAccessor={hasComponentAccessor} traderDef={hasTraderDef}");
                }
                catch (Exception exPrecheck)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis precheck failed: {exPrecheck.Message}");
                }

                ApplyRigorMortisSongCommonalityGuard();

                try
                {
                    var useRangedAttack = AccessTools.Method(typeof(FloatMenuUtility), "UseRangedAttack", new[] { typeof(Pawn) });
                    var draftedAttackGuardPrefix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.UseRangedAttackGuardPrefix));
                    if (useRangedAttack != null && draftedAttackGuardPrefix != null)
                    {
                        harmonyRM.Patch(useRangedAttack, prefix: new HarmonyMethod(draftedAttackGuardPrefix));
                        Log.Message("[MP-MeowOnlineShop] Patched FloatMenuUtility.UseRangedAttack (RigorMortis drafted zombie guard).");
                    }
                    else
                    {
                        Log.Warning("[MP-MeowOnlineShop] RigorMortis drafted attack guard skipped: method/prefix not found.");
                    }
                }
                catch (Exception exDraftedGuard)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis drafted attack guard patch failed: {exDraftedGuard.Message}");
                }

                try
                {
                    Log.Message("[MP-MeowOnlineShop] RigorMortis: registering sync method SyncRigorTraderTrade...");
                    var syncTrade = AccessTools.Method(typeof(Patch_RigorMortisComms), nameof(Patch_RigorMortisComms.SyncRigorTraderTrade));
                    if (syncTrade != null)
                    {
                        try
                        {
                            Patch_RigorMortisComms.SyncRigorTraderTradeMethod = MP.RegisterSyncMethod(syncTrade, null);
                            Log.Message("[MP-MeowOnlineShop] RigorMortis: sync method SyncRigorTraderTrade registered.");
                        }
                        catch (Exception exReg)
                        {
                            Log.Warning($"[MP-MeowOnlineShop] RegisterSyncMethod(SyncRigorTraderTrade) failed: {exReg.Message}");
                            Patch_RigorMortisComms.SyncRigorTraderTradeMethod =
                                MP.RegisterSyncMethod(typeof(Patch_RigorMortisComms), nameof(Patch_RigorMortisComms.SyncRigorTraderTrade));
                            Log.Message("[MP-MeowOnlineShop] RigorMortis: fallback sync registration by type+name succeeded.");
                        }
                    }
                    else
                    {
                        Log.Warning("[MP-MeowOnlineShop] RigorMortis: SyncRigorTraderTrade method not found, sync registration skipped.");
                    }
                    Log.Message("[MP-MeowOnlineShop] RigorMortis: applying comms patch suite...");
                    Patch_RigorMortisComms.Apply(harmonyRM);
                    Log.Message("[MP-MeowOnlineShop] RigorMortis: comms patch suite apply returned.");
                }
                catch (Exception exComms)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Rigor Mortis comms MP patch failed: {exComms.Message}");
                }

                try
                {
                    Log.Message("[MP-MeowOnlineShop] RigorMortis: registering CQF story sync methods...");
                    Patch_RigorMortis.RegisterStorySyncMethods();

                    // Synchronize the real CQF option selection. CQF RealWork actions then execute
                    // once under that command; intercepting every RealWork would duplicate actions
                    // that are already part of deterministic quest simulation.
                    Log.Message("[MP-MeowOnlineShop] RigorMortis: applying CQF real-option sync patches...");
                Patch_RigorMortisStoryDialogs.Apply(harmonyRM);
                Patch_RigorMortisUtilityWindows.Apply(harmonyRM);
                }
                catch (Exception exStory)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis story CQF patch failed: {exStory.Message}");
                }

                try
                {
                    Patch_RigorMortis.LogCooldownPatchStartupSummary();
                    var transpilerMethod = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.ReplaceTicksGameWithGlobalNowTranspiler));
                    if (transpilerMethod == null)
                        throw new MissingMethodException(typeof(Patch_RigorMortis).FullName, nameof(Patch_RigorMortis.ReplaceTicksGameWithGlobalNowTranspiler));

                    var transpiler = new HarmonyMethod(transpilerMethod);

                    void PatchGlobalCooldownMethod(Type type, string methodName, Type[] args = null, bool isPropertyGetter = false)
                    {
                        if (type == null || string.IsNullOrEmpty(methodName))
                            return;

                        MethodInfo method = isPropertyGetter
                            ? AccessTools.PropertyGetter(type, methodName)
                            : AccessTools.Method(type, methodName, args);

                        if (method == null)
                        {
                            Log.Warning($"[MP-MeowOnlineShop] RigorMortis global cooldown patch skipped: method not found {type.FullName}.{methodName}.");
                            return;
                        }

                        harmonyRM.Patch(method, transpiler: transpiler);
                        Log.Message($"[MP-MeowOnlineShop] Patched {type.FullName}.{method.Name} to use global cooldown clock.");
                    }

                    var bellType = AccessTools.TypeByName("RigorMortis.CompBell");
                    PatchGlobalCooldownMethod(bellType, "CooldownTicks", isPropertyGetter: true);
                    PatchGlobalCooldownMethod(bellType, "Lock", new[] { typeof(LocalTargetInfo) });
                    PatchGlobalCooldownMethod(bellType, "Attack", new[] { typeof(LocalTargetInfo) });

                    var lightningType = AccessTools.TypeByName("RigorMortis.CompLightningSword");
                    PatchGlobalCooldownMethod(lightningType, "CooldownTicks", isPropertyGetter: true);
                    PatchGlobalCooldownMethod(lightningType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    PatchGlobalCooldownMethod(lightningType, "AbilityDisabled", new[] { typeof(string).MakeByRefType() });

                    var baguaType = AccessTools.TypeByName("RigorMortis.CompBaguaMirror");
                    PatchGlobalCooldownMethod(baguaType, "CooldownTicks", isPropertyGetter: true);
                    PatchGlobalCooldownMethod(baguaType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    PatchGlobalCooldownMethod(baguaType, "AbilityDisabled", new[] { typeof(string).MakeByRefType() });

                    var flagType = AccessTools.TypeByName("RigorMortis.CompFlagWindRain");
                    PatchGlobalCooldownMethod(flagType, "CooldownTicks", isPropertyGetter: true);
                    PatchGlobalCooldownMethod(flagType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });

                    var inkType = AccessTools.TypeByName("RigorMortis.CompInkFountain");
                    if (inkType != null)
                    {
                        Log.Message("[MP-MeowOnlineShop] RigorMortis.CompInkFountain uses lastUseTick but has no TicksGame cooldown logic in 1.6; no transpiler target needed.");
                    }

                    // Aggressive mode: force disable artifact cooldown by clearing lastUseTick and forcing CooldownTicks getter to 0.
                    var noCooldownPrefix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.DisableCooldownPrefix));
                    var noCooldownPostfix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.DisableCooldownPostfix));
                    var zeroCooldownPostfix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.ForceZeroCooldownPostfix));
                    var noCooldownPrefixHm = noCooldownPrefix != null ? new HarmonyMethod(noCooldownPrefix) : null;
                    var noCooldownPostfixHm = noCooldownPostfix != null ? new HarmonyMethod(noCooldownPostfix) : null;
                    var zeroCooldownPostfixHm = zeroCooldownPostfix != null ? new HarmonyMethod(zeroCooldownPostfix) : null;

                    void PatchNoCooldownMethod(Type type, string methodName, Type[] args = null, bool isPropertyGetter = false, bool usePrefix = true, bool usePostfix = false, bool forceZeroGetter = false)
                    {
                        if (type == null || string.IsNullOrEmpty(methodName))
                            return;

                        MethodInfo method = isPropertyGetter
                            ? AccessTools.PropertyGetter(type, methodName)
                            : AccessTools.Method(type, methodName, args);
                        if (method == null)
                        {
                            Log.Warning($"[MP-MeowOnlineShop] RigorMortis no-cooldown patch skipped: method not found {type.FullName}.{methodName}.");
                            return;
                        }

                        harmonyRM.Patch(
                            method,
                            prefix: usePrefix ? noCooldownPrefixHm : null,
                            postfix: forceZeroGetter ? zeroCooldownPostfixHm : (usePostfix ? noCooldownPostfixHm : null));
                        Log.Message($"[MP-MeowOnlineShop] Patched {type.FullName}.{method.Name} for forced no-cooldown.");
                    }

                    PatchNoCooldownMethod(bellType, "CooldownTicks", isPropertyGetter: true, usePrefix: false, forceZeroGetter: true);
                    PatchNoCooldownMethod(bellType, "AbilityDisabled", new[] { typeof(string).MakeByRefType() }, usePrefix: true);
                    PatchNoCooldownMethod(bellType, "AttackAbilityDisabled", new[] { typeof(string).MakeByRefType() }, usePrefix: true);
                    PatchNoCooldownMethod(bellType, "Lock", new[] { typeof(LocalTargetInfo) }, usePrefix: true, usePostfix: true);
                    PatchNoCooldownMethod(bellType, "Attack", new[] { typeof(LocalTargetInfo) }, usePrefix: true, usePostfix: true);

                    PatchNoCooldownMethod(lightningType, "CooldownTicks", isPropertyGetter: true, usePrefix: false, forceZeroGetter: true);
                    PatchNoCooldownMethod(lightningType, "AbilityDisabled", new[] { typeof(string).MakeByRefType() }, usePrefix: true);
                    PatchNoCooldownMethod(lightningType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) }, usePrefix: true, usePostfix: true);

                    PatchNoCooldownMethod(baguaType, "CooldownTicks", isPropertyGetter: true, usePrefix: false, forceZeroGetter: true);
                    PatchNoCooldownMethod(baguaType, "AbilityDisabled", new[] { typeof(string).MakeByRefType() }, usePrefix: true);
                    PatchNoCooldownMethod(baguaType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) }, usePrefix: true, usePostfix: true);

                    PatchNoCooldownMethod(flagType, "CooldownTicks", isPropertyGetter: true, usePrefix: false, forceZeroGetter: true);
                    PatchNoCooldownMethod(flagType, "AbilityDisabled", new[] { typeof(string).MakeByRefType() }, usePrefix: true);
                    PatchNoCooldownMethod(flagType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) }, usePrefix: true, usePostfix: true);

                    // Ink fountain 1.6 currently doesn't consume cooldown, but keep field cleared for consistency.
                    PatchNoCooldownMethod(inkType, "TryPaint", Type.EmptyTypes, usePrefix: true, usePostfix: true);
                }
                catch (Exception exCooldownShift)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis global cooldown patch failed: {exCooldownShift.Message}");
                }

                var lightningCompType = AccessTools.TypeByName("RigorMortis.CompLightningSword");
                var baguaCompType = AccessTools.TypeByName("RigorMortis.CompBaguaMirror");
                var flagCompType = AccessTools.TypeByName("RigorMortis.CompFlagWindRain");
                Log.Message(
                    "[MP-MeowOnlineShop] RigorMortis artifact sync symbols: " +
                    $"compLightningSword={(lightningCompType != null)}, compBaguaMirror={(baguaCompType != null)}, compFlagWindRain={(flagCompType != null)}.");

                void RegisterArtifactOrderForceTargetSync(Type compType, string compShortName)
                {
                    if (compType == null)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] RigorMortis {compShortName} sync skipped: type not found.");
                        return;
                    }

                    var method = AccessTools.Method(compType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    if (method == null)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] RigorMortis {compShortName} sync skipped: OrderForceTarget(LocalTargetInfo) not found.");
                        return;
                    }

                    try
                    {
                        MP.RegisterSyncMethod(method, null);
                        Log.Message($"[MP-MeowOnlineShop] Registered {compType.FullName}.OrderForceTarget(LocalTargetInfo) as sync method.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] Failed to register {compType.FullName}.OrderForceTarget(LocalTargetInfo): {ex.Message}");
                    }
                }

                RegisterArtifactOrderForceTargetSync(lightningCompType, "CompLightningSword");
                RegisterArtifactOrderForceTargetSync(baguaCompType, "CompBaguaMirror");
                RegisterArtifactOrderForceTargetSync(flagCompType, "CompFlagWindRain");

                void RegisterRigorMutationSync(
                    string typeName,
                    string methodName,
                    Type[] parameterTypes)
                {
                    Type type = AccessTools.TypeByName(typeName);
                    MethodInfo method = type == null
                        ? null
                        : AccessTools.Method(type, methodName, parameterTypes);
                    if (method == null)
                    {
                        Log.Warning(
                            $"[MP-MeowOnlineShop] RigorMortis mutation sync skipped: " +
                            $"{typeName}.{methodName} not found.");
                        return;
                    }

                    try
                    {
                        MP.RegisterSyncMethod(method, null);
                        Log.Message(
                            $"[MP-MeowOnlineShop] Registered RigorMortis mutation boundary " +
                            $"{typeName}.{methodName}.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(
                            $"[MP-MeowOnlineShop] Failed to register RigorMortis mutation boundary " +
                            $"{typeName}.{methodName}: {ex.Message}");
                    }
                }

                RegisterRigorMutationSync(
                    "RigorMortis.CompCompass",
                    "OrderForceTarget",
                    new[] { typeof(LocalTargetInfo) });
                RegisterRigorMutationSync(
                    "RigorMortis.CompWoodGolem",
                    "OrderForceTarget",
                    new[] { typeof(LocalTargetInfo) });
                RegisterRigorMutationSync(
                    "RigorMortis.CompInkFountain",
                    "TryPaint",
                    Type.EmptyTypes);
                RegisterRigorMutationSync(
                    "RigorMortis.CompChangeCorpseApparel",
                    "FinalEffect",
                    new[] { typeof(LocalTargetInfo) });
                RegisterRigorMutationSync(
                    "RigorMortis.CompYinAndMalevolent",
                    "FinalEffect",
                    new[] { typeof(LocalTargetInfo) });

                if (lightningCompType != null)
                {
                    var lightningOrder = AccessTools.Method(lightningCompType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    var lightningOrderPrefix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.LightningOrderPrefix));
                    if (lightningOrder != null && lightningOrderPrefix != null)
                    {
                        harmonyRM.Patch(lightningOrder, prefix: new HarmonyMethod(lightningOrderPrefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompLightningSword.OrderForceTarget (trace).");
                    }
                }

                var casketType = AccessTools.TypeByName("RigorMortis.Building_ZombieCasket");
                if (casketType != null)
                {
                    // 注册 Fall() 为同步方法，确保开发者命令触发时客户端也执行，避免 amissVecs 状态不一致
                    var fallMethod = AccessTools.Method(casketType, "Fall");
                    if (fallMethod != null)
                    {
                        try
                        {
                            MP.RegisterSyncMethod(casketType, "Fall");
                            Log.Message("[MP-MeowOnlineShop] Registered Building_ZombieCasket.Fall as sync method for multiplayer compatibility.");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[MP-MeowOnlineShop] Failed to register Fall as sync method: {ex.Message}");
                        }
                    }

                    var tick = AccessTools.Method(casketType, "Tick");
                    if (tick != null)
                    {
                        var prefix = typeof(Patch_RigorMortis).GetMethod("CasketTickPrefix", BindingFlags.Public | BindingFlags.Static);
                        var finalizer = typeof(Patch_RigorMortis).GetMethod("CasketTickFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (prefix != null && finalizer != null)
                        {
                            harmonyRM.Patch(tick, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.Building_ZombieCasket.Tick (deterministic Rand).");
                        }
                    }
                    var getGizmos = AccessTools.Method(casketType, "GetGizmos");
                    if (getGizmos != null)
                    {
                        var gPrefix = typeof(Patch_RigorMortis).GetMethod("GizmoPrefix", BindingFlags.Public | BindingFlags.Static);
                        var gFinalizer = typeof(Patch_RigorMortis).GetMethod("GizmoFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (gPrefix != null && gFinalizer != null)
                        {
                            harmonyRM.Patch(getGizmos, prefix: new HarmonyMethod(gPrefix), finalizer: new HarmonyMethod(gFinalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.Building_ZombieCasket.GetGizmos (deterministic Rand).");
                        }
                    }
                    var recover = AccessTools.Method(casketType, "Recover");
                    if (recover != null)
                    {
                        var rPrefix = typeof(Patch_RigorMortis).GetMethod("RecoverPrefix", BindingFlags.Public | BindingFlags.Static);
                        var rFinalizer = typeof(Patch_RigorMortis).GetMethod("RecoverFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (rPrefix != null && rFinalizer != null)
                        {
                            harmonyRM.Patch(recover, prefix: new HarmonyMethod(rPrefix), finalizer: new HarmonyMethod(rFinalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.Building_ZombieCasket.Recover (deterministic Rand).");
                        }
                    }
                    var fall = AccessTools.Method(casketType, "Fall");
                    if (fall != null)
                    {
                        var fPrefix = typeof(Patch_RigorMortis).GetMethod("FallPrefix", BindingFlags.Public | BindingFlags.Static);
                        var fFinalizer = typeof(Patch_RigorMortis).GetMethod("FallFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (fPrefix != null && fFinalizer != null)
                        {
                            harmonyRM.Patch(fall, prefix: new HarmonyMethod(fPrefix), finalizer: new HarmonyMethod(fFinalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.Building_ZombieCasket.Fall (deterministic Rand).");
                        }
                    }
                }

                var compYinType = AccessTools.TypeByName("RigorMortis.CompYinAndMalevolent");
                if (compYinType != null)
                {
                    var compTick = AccessTools.Method(compYinType, "CompTick");
                    if (compTick != null)
                    {
                        var prefix = typeof(Patch_RigorMortis).GetMethod("CompYinTickPrefix", BindingFlags.Public | BindingFlags.Static);
                        var finalizer = typeof(Patch_RigorMortis).GetMethod("CompYinTickFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (prefix != null && finalizer != null)
                        {
                            harmonyRM.Patch(compTick, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompYinAndMalevolent.CompTick (deterministic Rand).");
                        }
                    }
                    var compGetGizmos = AccessTools.Method(compYinType, "CompGetGizmosExtra");
                    if (compGetGizmos != null)
                    {
                        var gPrefix = typeof(Patch_RigorMortis).GetMethod("GizmoPrefix", BindingFlags.Public | BindingFlags.Static);
                        var gFinalizer = typeof(Patch_RigorMortis).GetMethod("GizmoFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (gPrefix != null && gFinalizer != null)
                        {
                            harmonyRM.Patch(compGetGizmos, prefix: new HarmonyMethod(gPrefix), finalizer: new HarmonyMethod(gFinalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompYinAndMalevolent.CompGetGizmosExtra (deterministic Rand).");
                        }
                    }
                }

                var compChangeApparelType = AccessTools.TypeByName("RigorMortis.CompChangeCorpseApparel");
                if (compChangeApparelType != null)
                {
                    var compGetGizmos = AccessTools.Method(compChangeApparelType, "CompGetGizmosExtra");
                    if (compGetGizmos != null)
                    {
                        var gPrefix = typeof(Patch_RigorMortis).GetMethod("GizmoPrefix", BindingFlags.Public | BindingFlags.Static);
                        var gFinalizer = typeof(Patch_RigorMortis).GetMethod("GizmoFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (gPrefix != null && gFinalizer != null)
                        {
                            harmonyRM.Patch(compGetGizmos, prefix: new HarmonyMethod(gPrefix), finalizer: new HarmonyMethod(gFinalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompChangeCorpseApparel.CompGetGizmosExtra (deterministic Rand).");
                        }
                    }
                }

                var workGiverType = AccessTools.TypeByName("RigorMortis.WorkGiver_Recover");
                if (workGiverType != null)
                {
                    var potentialWorkThings = AccessTools.Method(workGiverType, "PotentialWorkThingsGlobal");
                    if (potentialWorkThings != null)
                    {
                        var pwPrefix = typeof(Patch_RigorMortis).GetMethod("PotentialWorkThingsPrefix", BindingFlags.Public | BindingFlags.Static);
                        var pwFinalizer = typeof(Patch_RigorMortis).GetMethod("PotentialWorkThingsFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (pwPrefix != null)
                        {
                            if (pwFinalizer != null)
                                harmonyRM.Patch(potentialWorkThings, prefix: new HarmonyMethod(pwPrefix), finalizer: new HarmonyMethod(pwFinalizer));
                            else
                                harmonyRM.Patch(potentialWorkThings, prefix: new HarmonyMethod(pwPrefix));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.WorkGiver_Recover.PotentialWorkThingsGlobal (deterministic order).");
                        }
                    }
                }

                // ----- Soul Chanter Bell (zombie control commands) -----
                var bellCompType = AccessTools.TypeByName("RigorMortis.CompBell");
                if (bellCompType != null)
                {
                    foreach (var methodName in new[] { "OrderForceTarget", "OrderForceAttackTarget", "Goto", "Attack", "Lock" })
                    {
                        try
                        {
                            MP.RegisterSyncMethod(bellCompType, methodName);
                            Log.Message($"[MP-MeowOnlineShop] Registered RigorMortis.CompBell.{methodName} as sync method.");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[MP-MeowOnlineShop] Failed to register CompBell.{methodName} as sync method: {ex}");
                        }
                    }

                    var bellGizmos = AccessTools.Method(bellCompType, "CompGetGizmosExtra");
                    if (bellGizmos != null)
                    {
                        var gPrefix = typeof(Patch_RigorMortis).GetMethod("GizmoPrefix", BindingFlags.Public | BindingFlags.Static);
                        var gFinalizer = typeof(Patch_RigorMortis).GetMethod("GizmoFinalizer", BindingFlags.Public | BindingFlags.Static);
                        if (gPrefix != null && gFinalizer != null)
                        {
                            harmonyRM.Patch(bellGizmos, prefix: new HarmonyMethod(gPrefix), finalizer: new HarmonyMethod(gFinalizer));
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompBell.CompGetGizmosExtra (deterministic Rand).");
                        }
                    }

                    var bellActionPrefix = typeof(Patch_RigorMortis).GetMethod("BellActionPrefix", BindingFlags.Public | BindingFlags.Static);
                    var bellActionFinalizer = typeof(Patch_RigorMortis).GetMethod("BellActionFinalizer", BindingFlags.Public | BindingFlags.Static);
                    var bellStopTargetingPostfix = typeof(Patch_RigorMortis).GetMethod("StopTargetingPostfix", BindingFlags.Public | BindingFlags.Static);
                    var bellOrderGotoPrefix = typeof(Patch_RigorMortis).GetMethod("BellOrderTargetPrefix", BindingFlags.Public | BindingFlags.Static);
                    var bellOrderAttackPrefix = typeof(Patch_RigorMortis).GetMethod("BellOrderAttackTargetPrefix", BindingFlags.Public | BindingFlags.Static);
                    var bellGotoPrefix = typeof(Patch_RigorMortis).GetMethod("BellGotoPrefix", BindingFlags.Public | BindingFlags.Static);
                    var bellAttackPrefix = typeof(Patch_RigorMortis).GetMethod("BellAttackPrefix", BindingFlags.Public | BindingFlags.Static);
                    var bellLockPrefix = typeof(Patch_RigorMortis).GetMethod("BellLockPrefix", BindingFlags.Public | BindingFlags.Static);

                    var orderGoto = AccessTools.Method(bellCompType, "OrderForceTarget");
                    if (orderGoto != null && bellOrderGotoPrefix != null)
                    {
                        harmonyRM.Patch(orderGoto, prefix: new HarmonyMethod(bellOrderGotoPrefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompBell.OrderForceTarget (trace).");
                    }
                    var orderAttack = AccessTools.Method(bellCompType, "OrderForceAttackTarget");
                    if (orderAttack != null && bellOrderAttackPrefix != null)
                    {
                        harmonyRM.Patch(orderAttack, prefix: new HarmonyMethod(bellOrderAttackPrefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompBell.OrderForceAttackTarget (trace).");
                    }

                    var gotoMethod = AccessTools.Method(bellCompType, "Goto");
                    if (gotoMethod != null)
                    {
                        if (bellActionPrefix != null && bellActionFinalizer != null)
                            harmonyRM.Patch(gotoMethod, prefix: new HarmonyMethod(bellActionPrefix), finalizer: new HarmonyMethod(bellActionFinalizer));
                        if (bellGotoPrefix != null)
                            harmonyRM.Patch(gotoMethod, prefix: new HarmonyMethod(bellGotoPrefix));
                        if (bellStopTargetingPostfix != null)
                            harmonyRM.Patch(gotoMethod, postfix: new HarmonyMethod(bellStopTargetingPostfix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompBell.Goto (deterministic Rand + targeter cleanup).");
                    }

                    var attackMethod = AccessTools.Method(bellCompType, "Attack");
                    if (attackMethod != null)
                    {
                        if (bellActionPrefix != null && bellActionFinalizer != null)
                            harmonyRM.Patch(attackMethod, prefix: new HarmonyMethod(bellActionPrefix), finalizer: new HarmonyMethod(bellActionFinalizer));
                        if (bellAttackPrefix != null)
                            harmonyRM.Patch(attackMethod, prefix: new HarmonyMethod(bellAttackPrefix));
                        if (bellStopTargetingPostfix != null)
                            harmonyRM.Patch(attackMethod, postfix: new HarmonyMethod(bellStopTargetingPostfix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompBell.Attack (deterministic Rand + targeter cleanup).");
                    }

                    var lockMethod = AccessTools.Method(bellCompType, "Lock");
                    if (lockMethod != null)
                    {
                        if (bellActionPrefix != null && bellActionFinalizer != null)
                            harmonyRM.Patch(lockMethod, prefix: new HarmonyMethod(bellActionPrefix), finalizer: new HarmonyMethod(bellActionFinalizer));
                        if (bellLockPrefix != null)
                            harmonyRM.Patch(lockMethod, prefix: new HarmonyMethod(bellLockPrefix));
                        if (bellStopTargetingPostfix != null)
                            harmonyRM.Patch(lockMethod, postfix: new HarmonyMethod(bellStopTargetingPostfix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompBell.Lock (deterministic Rand + targeter cleanup).");
                    }
                }

                // ----- Sticky rice projectile (throw_rice) -----
                var stickyRiceBulletType = AccessTools.TypeByName("RigorMortis.Bullet_StickyRice");
                if (stickyRiceBulletType != null)
                {
                    var impact = AccessTools.Method(stickyRiceBulletType, "Impact");
                    var impactPrefix = typeof(Patch_RigorMortis).GetMethod("StickyRiceImpactPrefix", BindingFlags.Public | BindingFlags.Static);
                    var impactFinalizer = typeof(Patch_RigorMortis).GetMethod("StickyRiceImpactFinalizer", BindingFlags.Public | BindingFlags.Static);
                    if (impact != null && impactPrefix != null && impactFinalizer != null)
                    {
                        harmonyRM.Patch(impact, prefix: new HarmonyMethod(impactPrefix), finalizer: new HarmonyMethod(impactFinalizer));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.Bullet_StickyRice.Impact (deterministic Rand).");
                    }
                }

                // ----- Blood usage (insect blood on casket / chicken blood enchant on taoist weapon) -----
                var insectBloodCompType = AccessTools.TypeByName("RigorMortis.CompUseInsectBlood");
                if (insectBloodCompType != null)
                {
                    try
                    {
                        MP.RegisterSyncMethod(insectBloodCompType, "OrderForceTarget");
                        Log.Message("[MP-MeowOnlineShop] Registered RigorMortis.CompUseInsectBlood.OrderForceTarget as sync method.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] Failed to register CompUseInsectBlood.OrderForceTarget as sync method: {ex}");
                    }
                    var order = AccessTools.Method(insectBloodCompType, "OrderForceTarget");
                    var prefix = typeof(Patch_RigorMortis).GetMethod("InsectBloodOrderPrefix", BindingFlags.Public | BindingFlags.Static);
                    if (order != null && prefix != null)
                    {
                        harmonyRM.Patch(order, prefix: new HarmonyMethod(prefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompUseInsectBlood.OrderForceTarget (trace).");
                    }
                }

                var chickenBloodCompType = AccessTools.TypeByName("RigorMortis.CompUseChickenBlood");
                if (chickenBloodCompType != null)
                {
                    try
                    {
                        MP.RegisterSyncMethod(chickenBloodCompType, "OrderForceTarget");
                        Log.Message("[MP-MeowOnlineShop] Registered RigorMortis.CompUseChickenBlood.OrderForceTarget as sync method.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] Failed to register CompUseChickenBlood.OrderForceTarget as sync method: {ex}");
                    }
                    var order = AccessTools.Method(chickenBloodCompType, "OrderForceTarget");
                    var prefix = typeof(Patch_RigorMortis).GetMethod("ChickenBloodOrderPrefix", BindingFlags.Public | BindingFlags.Static);
                    if (order != null && prefix != null)
                    {
                        harmonyRM.Patch(order, prefix: new HarmonyMethod(prefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompUseChickenBlood.OrderForceTarget (trace).");
                    }
                }

                // ----- Taoist incantation (talisman put & execution) -----
                var incantationCompType = AccessTools.TypeByName("RigorMortis.CompIncantation");
                if (incantationCompType != null)
                {
                    try
                    {
                        MP.RegisterSyncMethod(incantationCompType, "OrderForcePutTarget");
                        Log.Message("[MP-MeowOnlineShop] Registered RigorMortis.CompIncantation.OrderForcePutTarget as sync method.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] Failed to register CompIncantation.OrderForcePutTarget as sync method: {ex}");
                    }
                    try
                    {
                        MP.RegisterSyncMethod(incantationCompType, "OrderForceTarget");
                        Log.Message("[MP-MeowOnlineShop] Registered RigorMortis.CompIncantation.OrderForceTarget as sync method.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] Failed to register CompIncantation.OrderForceTarget as sync method: {ex}");
                    }

                    var putOrder = AccessTools.Method(incantationCompType, "OrderForcePutTarget");
                    var putPrefix = typeof(Patch_RigorMortis).GetMethod("IncantationPutPrefix", BindingFlags.Public | BindingFlags.Static);
                    if (putOrder != null && putPrefix != null)
                    {
                        harmonyRM.Patch(putOrder, prefix: new HarmonyMethod(putPrefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompIncantation.OrderForcePutTarget (trace).");
                    }

                    var execOrder = AccessTools.Method(incantationCompType, "OrderForceTarget");
                    var execPrefix = typeof(Patch_RigorMortis).GetMethod("IncantationExecuteOrderPrefix", BindingFlags.Public | BindingFlags.Static);
                    if (execOrder != null && execPrefix != null)
                    {
                        harmonyRM.Patch(execOrder, prefix: new HarmonyMethod(execPrefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.CompIncantation.OrderForceTarget (trace).");
                    }
                }

                var incantationExecuteType = AccessTools.TypeByName("RigorMortis.HediffAbility_IncantationExecute");
                if (incantationExecuteType != null)
                {
                    var apply = AccessTools.Method(incantationExecuteType, "ApplyExecute");
                    var prefix = typeof(Patch_RigorMortis).GetMethod("IncantationExecuteApplyPrefix", BindingFlags.Public | BindingFlags.Static);
                    if (apply != null && prefix != null)
                    {
                        harmonyRM.Patch(apply, prefix: new HarmonyMethod(prefix));
                        Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.HediffAbility_IncantationExecute.ApplyExecute (trace).");
                    }
                }

                // ----- Lantern control (zombie draft ownership) -----
                var lanternCompType = AccessTools.TypeByName("RigorMortis.CompLantern");
                bool lanternOrderSyncRegistered = false;
                if (lanternCompType != null)
                {
                    try
                    {
                        var orderForceTarget = AccessTools.Method(lanternCompType, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                        if (orderForceTarget != null)
                        {
                            MP.RegisterSyncMethod(orderForceTarget, null);
                            lanternOrderSyncRegistered = true;
                            Log.Message("[MP-MeowOnlineShop] Registered RigorMortis.CompLantern.OrderForceTarget as sync method.");
                        }
                        else
                        {
                            Log.Warning("[MP-MeowOnlineShop] RigorMortis lantern sync skipped: OrderForceTarget(LocalTargetInfo) not found.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] Failed to register CompLantern.OrderForceTarget as sync method: {ex.Message}");
                    }
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] RigorMortis lantern sync skipped: CompLantern type not found.");
                }

                // ----- Zombie drafted skill sync coverage (all RM ability defs + custom verbs) -----
                int rmAbilityDefCount = 0;
                int rmCustomVerbTypeCount = 0;
                int rmCustomVerbOrderSyncCount = 0;
                int rmCustomVerbTryStartSyncCount = 0;
                int rmVanillaVerbCount = 0;
                bool passOrderSyncRegistered = false;
                bool passTryStartSyncRegistered = false;
                bool catchOrderSyncRegistered = false;
                bool catchTryStartSyncRegistered = false;

                var coveredVerbTypes = new HashSet<Type>();

                bool TryRegisterVerbSync(MethodInfo method, string label)
                {
                    if (method == null)
                        return false;
                    // Mutant abilities are not serializable by Multiplayer's stock Ability worker:
                    // it only resolves pawn.abilities, while these live in pawn.mutant.
                    // The compatibility proxy serializes Pawn + Ability.Id/DefName instead.
                    Log.Message($"[MP-MeowOnlineShop] Discovered {label}; handled by mutant ability proxy (direct MP registration intentionally skipped).");
                    return false;
                }

                bool TryRegisterPreferredOrderForceTarget(Type verbType, string verbTypeName)
                {
                    var declaredMethods = verbType.GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    var preferred = declaredMethods.FirstOrDefault(m =>
                        string.Equals(m.Name, "OrderForceTarget", StringComparison.Ordinal) &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(LocalTargetInfo));
                    if (preferred != null)
                        return TryRegisterVerbSync(preferred, $"{verbTypeName}.OrderForceTarget(LocalTargetInfo)");

                    foreach (var m in declaredMethods)
                    {
                        if (!string.Equals(m.Name, "OrderForceTarget", StringComparison.Ordinal))
                            continue;
                        var ps = m.GetParameters();
                        if (ps.Length >= 1 && ps[0].ParameterType == typeof(LocalTargetInfo))
                            return TryRegisterVerbSync(m, $"{verbTypeName}.{m.Name}({string.Join(",", ps.Select(p => p.ParameterType.Name))})");
                    }

                    Log.Message($"[MP-MeowOnlineShop] RigorMortis verb sync: {verbTypeName} does not declare OrderForceTarget; Multiplayer's base verb sync remains authoritative.");
                    return false;
                }

                bool TryRegisterPreferredTryStartCastOn(Type verbType, string verbTypeName)
                {
                    MethodInfo preferred = null;
                    foreach (var m in verbType.GetMethods(
                                 BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!string.Equals(m.Name, "TryStartCastOn", StringComparison.Ordinal))
                            continue;

                        var ps = m.GetParameters();
                        if (ps.Length < 1 || ps[0].ParameterType != typeof(LocalTargetInfo))
                            continue;

                        if (ps.Length >= 2 && ps[1].ParameterType == typeof(LocalTargetInfo))
                        {
                            preferred = m;
                            break;
                        }

                        if (preferred == null)
                            preferred = m;
                    }

                    if (preferred == null)
                    {
                        Log.Message($"[MP-MeowOnlineShop] RigorMortis verb sync: {verbTypeName} does not declare TryStartCastOn; Multiplayer's base verb sync remains authoritative.");
                        return false;
                    }

                    var sig = string.Join(",", preferred.GetParameters().Select(p => p.ParameterType.Name));
                    return TryRegisterVerbSync(preferred, $"{verbTypeName}.TryStartCastOn({sig})");
                }

                void RegisterCustomVerbType(Type verbType)
                {
                    if (verbType == null || !coveredVerbTypes.Add(verbType))
                        return;

                    rmCustomVerbTypeCount++;
                    string verbTypeName = verbType.FullName ?? verbType.Name ?? "UnknownVerbType";

                    bool orderRegistered = TryRegisterPreferredOrderForceTarget(verbType, verbTypeName);
                    bool tryStartRegistered = TryRegisterPreferredTryStartCastOn(verbType, verbTypeName);

                    if (orderRegistered)
                        rmCustomVerbOrderSyncCount++;
                    if (tryStartRegistered)
                        rmCustomVerbTryStartSyncCount++;

                    if (verbTypeName == "RigorMortis.Verb_AbilityPass")
                    {
                        passOrderSyncRegistered = passOrderSyncRegistered || orderRegistered;
                        passTryStartSyncRegistered = passTryStartSyncRegistered || tryStartRegistered;
                    }
                    else if (verbTypeName == "RigorMortis.Verb_AbilityCatch")
                    {
                        catchOrderSyncRegistered = catchOrderSyncRegistered || orderRegistered;
                        catchTryStartSyncRegistered = catchTryStartSyncRegistered || tryStartRegistered;
                    }
                }

                try
                {
                    foreach (var def in DefDatabase<AbilityDef>.AllDefsListForReading)
                    {
                        if (def == null)
                            continue;
                        string defName = def.defName ?? "";
                        if (!defName.StartsWith("RM_", StringComparison.OrdinalIgnoreCase))
                            continue;

                        rmAbilityDefCount++;
                        var verbType = def.verbProperties?.verbClass;
                        if (verbType == null)
                            continue;

                        string verbTypeName = verbType.FullName ?? verbType.Name ?? "";
                        if (!verbTypeName.StartsWith("RigorMortis.Verb_", StringComparison.OrdinalIgnoreCase))
                        {
                            rmVanillaVerbCount++;
                            continue;
                        }

                        // Multiplayer already registers the vanilla ability verb entry points.
                        // Register only methods actually declared by RigorMortis verb types.
                        RegisterCustomVerbType(verbType);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis drafted skill sync scan by AbilityDef failed: {ex.Message}");
                }

                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm == null || asm.IsDynamic)
                            continue;
                        var asmName = asm.GetName().Name ?? "";
                        if (asmName.IndexOf("RigorMortis", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        Type[] types;
                        try
                        {
                            types = asm.GetTypes();
                        }
                        catch (ReflectionTypeLoadException rex)
                        {
                            types = rex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
                        }

                        foreach (var type in types)
                        {
                            if (type == null)
                                continue;
                            var fullName = type.FullName ?? "";
                            if (!fullName.StartsWith("RigorMortis.Verb_", StringComparison.OrdinalIgnoreCase))
                                continue;
                            RegisterCustomVerbType(type);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis drafted skill sync scan by assembly failed: {ex.Message}");
                }

                var zombieCatchType = AccessTools.TypeByName("RigorMortis.AxolotlZombieCatch");
                bool zombieCatchRandPatched = false;
                if (zombieCatchType != null)
                {
                    try
                    {
                        var tryCatchPawn = AccessTools.Method(zombieCatchType, "TryCatchPawn", new[] { typeof(Pawn) });
                        var tryCatchPrefix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.ZombieCatchTryCatchPawnPrefix));
                        var tryCatchFinalizer = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.ZombieCatchTryCatchPawnFinalizer));
                        if (tryCatchPawn != null && tryCatchPrefix != null && tryCatchFinalizer != null)
                        {
                            harmonyRM.Patch(tryCatchPawn, prefix: new HarmonyMethod(tryCatchPrefix), finalizer: new HarmonyMethod(tryCatchFinalizer));
                            zombieCatchRandPatched = true;
                            Log.Message("[MP-MeowOnlineShop] Patched RigorMortis.AxolotlZombieCatch.TryCatchPawn (deterministic Rand + trace).");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] Failed to patch AxolotlZombieCatch.TryCatchPawn: {ex.Message}");
                    }
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] RigorMortis zombie catch Rand patch skipped: AxolotlZombieCatch type not found.");
                }

                try
                {
                    var jobTrackerType = typeof(Pawn_JobTracker);
                    var miliraTryTakeOrderedJobPrefix = AccessTools.Method(typeof(Patch_MiliraFly), "TryTakeOrderedJob_Prefix", new[] { typeof(Job), typeof(Pawn_JobTracker) });
                    int patchedTryTakeOrderedJob = 0;
                    if (miliraTryTakeOrderedJobPrefix != null)
                    {
                        foreach (var m in jobTrackerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (!string.Equals(m.Name, "TryTakeOrderedJob", StringComparison.Ordinal))
                                continue;
                            var ps = m.GetParameters();
                            if (ps.Length == 0 || ps[0].ParameterType != typeof(Job))
                                continue;

                            harmonyRM.Patch(m, prefix: new HarmonyMethod(miliraTryTakeOrderedJobPrefix) { priority = 5 });
                            patchedTryTakeOrderedJob++;
                            break;
                        }
                    }

                    if (patchedTryTakeOrderedJob > 0)
                        Log.Message("[MP-MeowOnlineShop] Patched Pawn_JobTracker.TryTakeOrderedJob for CastJump verb repair (RigorMortis zombie abilities).");
                    else
                        Log.Warning("[MP-MeowOnlineShop] RigorMortis CastJump verb repair skipped: TryTakeOrderedJob prefix not applied.");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Failed to patch TryTakeOrderedJob for RigorMortis CastJump repair: {ex.Message}");
                }

                try
                {
                    int patchedOrderForceTarget = 0;
                    var abilityOrderPrefix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.ZombieAbilityOrderForceTargetPrefix));
                    var orderTargets = new HashSet<MethodInfo>();

                    var vanillaAbilityOrder = AccessTools.Method(typeof(Verb_CastAbility), "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    if (vanillaAbilityOrder != null)
                        orderTargets.Add(vanillaAbilityOrder);

                    foreach (var type in coveredVerbTypes)
                    {
                        if (type == null)
                            continue;

                        // Some Rigor Mortis verbs (notably Verb_AbilityPass) inherit
                        // Verb_CastAbilityJump.OrderForceTarget without declaring an
                        // override. Patch the method that reflection actually resolves
                        // for every live verb type, otherwise the targeting callback
                        // bypasses both Verb_CastAbility and the custom-type scan.
                        var resolvedOrder = AccessTools.Method(
                            type,
                            "OrderForceTarget",
                            new[] { typeof(LocalTargetInfo) });
                        if (resolvedOrder != null)
                        {
                            // AccessTools may return an inherited MethodInfo whose
                            // ReflectedType is the derived verb. Harmony rejects that
                            // wrapper as having no implementation. Re-resolve it on
                            // DeclaringType so the actual IL body is patched.
                            var declaredOrder = resolvedOrder.DeclaringType?
                                .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                .FirstOrDefault(m =>
                                    string.Equals(m.Name, resolvedOrder.Name, StringComparison.Ordinal) &&
                                    m.GetParameters().Length == 1 &&
                                    m.GetParameters()[0].ParameterType == typeof(LocalTargetInfo));
                            orderTargets.Add(declaredOrder ?? resolvedOrder);
                        }

                        foreach (var m in type.GetMethods(
                                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (!string.Equals(m.Name, "OrderForceTarget", StringComparison.Ordinal))
                                continue;
                            var ps = m.GetParameters();
                            if (ps.Length != 1)
                                continue;
                            if (ps[0].ParameterType == typeof(LocalTargetInfo))
                                orderTargets.Add(m);
                        }
                    }

                    if (abilityOrderPrefix != null)
                    {
                        foreach (var method in orderTargets)
                        {
                            try
                            {
                                harmonyRM.Patch(
                                    method,
                                    prefix: new HarmonyMethod(abilityOrderPrefix) { priority = Priority.First });
                                patchedOrderForceTarget++;
                            }
                            catch (Exception patchEx)
                            {
                                Log.Warning(
                                    $"[MP-MeowOnlineShop] Failed to patch zombie OrderForceTarget target " +
                                    $"{method?.DeclaringType?.FullName}.{method?.Name}: {patchEx.Message}");
                            }
                        }
                    }

                    if (patchedOrderForceTarget > 0)
                    {
                        Log.Message($"[MP-MeowOnlineShop] Patched {patchedOrderForceTarget} vanilla/custom OrderForceTarget method(s) for zombie mutant ability sync proxy.");
                    }
                    else
                    {
                        Log.Warning("[MP-MeowOnlineShop] RigorMortis zombie ability sync proxy skipped: no OrderForceTarget(*) verb method patched.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Failed to patch verb OrderForceTarget for zombie sync proxy: {ex.Message}");
                }

                try
                {
                    var commandProcessPrefix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(Patch_RigorMortis.ZombieAbilityCommandProcessInputPrefix));
                    var commandAbilityType = AccessTools.TypeByName("RimWorld.Command_Ability");
                    var processInput = commandAbilityType?
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .FirstOrDefault(m => string.Equals(m.Name, "ProcessInput", StringComparison.Ordinal) &&
                                             m.GetParameters().Length == 1);
                    if (processInput != null && commandProcessPrefix != null)
                    {
                        harmonyRM.Patch(
                            processInput,
                            prefix: new HarmonyMethod(commandProcessPrefix) { priority = Priority.First });
                        Log.Message("[MP-MeowOnlineShop] Patched RimWorld.Command_Ability.ProcessInput for zombie mutant self-cast sync.");
                    }
                    else
                        Log.Warning("[MP-MeowOnlineShop] Zombie ability click trace skipped: no Command_Ability.ProcessInput method patched.");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Failed to patch Command_Ability.ProcessInput for zombie trace: {ex.Message}");
                }

                Log.Message(
                    "[MP-MeowOnlineShop] RigorMortis zombie skill startup summary: " +
                    $"lanternOrderSync={lanternOrderSyncRegistered} " +
                    $"rmAbilityDefs={rmAbilityDefCount} rmVanillaVerbDefs={rmVanillaVerbCount} " +
                    $"rmCustomVerbTypes={rmCustomVerbTypeCount} rmCustomOrderSync={rmCustomVerbOrderSyncCount} rmCustomTryStartSync={rmCustomVerbTryStartSyncCount} " +
                    $"passOrderSync={passOrderSyncRegistered} passTryStartSync={passTryStartSyncRegistered} " +
                    $"catchOrderSync={catchOrderSyncRegistered} catchTryStartSync={catchTryStartSyncRegistered} " +
                    $"catchRandPatched={zombieCatchRandPatched}.");

                Log.Message("[MP-MeowOnlineShop] Rigor Mortis multiplayer patches applied.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Rigor Mortis MP patch failed (mod may not be loaded): " + e.Message);
            }
        }

        private static readonly string[] RigorMortisSongDefs =
        {
            "DeadFlowerBlossom",
            "DontLeaveMeAlone",
            "Neighbours",
            "Truth",
            "Becoming",
            "GetItBack",
            "TheHallwayOfBlood"
        };

        private const float MinRigorMortisSongCommonality = 0.01f;

        private static void ApplyRigorMortisSongCommonalityGuard()
        {
            try
            {
                if (AccessTools.TypeByName("RigorMortis.RMUtility") == null)
                    return;

                var fixedSongs = new List<string>();
                foreach (var defName in RigorMortisSongDefs)
                {
                    var songDef = DefDatabase<SongDef>.GetNamedSilentFail(defName);
                    if (songDef == null)
                        continue;
                    if (songDef.commonality > 0f)
                        continue;

                    songDef.commonality = MinRigorMortisSongCommonality;
                    fixedSongs.Add(defName);
                }

                if (fixedSongs.Count > 0)
                {
                    Log.Warning(
                        $"[MP-MeowOnlineShop] RigorMortis song commonality guard fixed {fixedSongs.Count} SongDef(s): {string.Join(", ", fixedSongs)}. " +
                        $"Set commonality to {MinRigorMortisSongCommonality} to avoid totalWeight=0.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis song commonality guard failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 为 Milira/Ancot 武器模式切换（CompRangeWeaponVerbSwitch*）应用联机同步与 Rand 稳定补丁。
        /// 使用独立 HarmonyId，避免与其它补丁在 Harmony 元数据汇总时互相影响。
        /// </summary>
        private static void ApplyMiliraWeaponModePatches()
        {
            try
            {
                var mainType = AccessTools.TypeByName("AncotLibrary.CompRangeWeaponVerbSwitch");
                var energyType = AccessTools.TypeByName("AncotLibrary.CompRangeWeaponVerbSwitch_EnergyPassive");
                if (mainType == null && energyType == null)
                {
                    Log.Message("[MP-MeowOnlineShop] Milira patch: CompRangeWeaponVerbSwitch types not found, skipped.");
                    return;
                }

                var harmonyMilira = new Harmony("mp.meowonlineshop.miliraweaponmode");
                Patch_MiliraWeaponMode.Apply(harmonyMilira);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira weapon mode patch failed: {e}");
            }
        }

        /// <summary>
        /// Milira/Ancot 物理盾牌 Gizmo：同步与 Rand 稳定；仅对有限 Command 类型打 ProcessInput 补丁。
        /// </summary>
        private static void ApplyMiliraShieldModePatches()
        {
            try
            {
                var harmonyShield = new Harmony("mp.meowonlineshop.milirashieldmode");
                Patch_MiliraShieldMode.Apply(harmonyShield);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira shield patch failed: {e.Message}");
            }
        }

        /// <summary>
        /// Milira 种族征召「飞行状态」Gizmo（<c>CompFlightControl</c>）：同步点击与 Rand 稳定。
        /// </summary>
        private static void ApplyMiliraFlightModePatches()
        {
            try
            {
                var harmonyFlight = new Harmony(Patch_MiliraFlightMode.HarmonyId);
                Patch_MiliraFlightMode.Apply(harmonyFlight);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira flight mode patch failed: {e.Message}");
            }
        }

        /// <summary>
        /// Milira 飞行/跳跃能力：<see cref="Job"/> CastJump 在联机同步 <see cref="Pawn_JobTracker.TryTakeOrderedJob"/> 时序列化 <see cref="Job.verbToUse"/> 失败。
        /// 见 <see cref="Patch_MiliraFly"/>。
        /// </summary>
        private static void ApplyMiliraFlyPatches()
        {
            try
            {
                Patch_MiliraFlyBootstrap.Apply();
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira fly MP patch failed: {e}");
            }
        }

        /// <summary>VoiceroidAsAnimal（琴叶等）：言灵 SetKotodamaInt、VAA 技能 Verb 与 WorldManager RandomElement 联机同步。</summary>
        private static void ApplyVoiceroidAsAnimalPatches()
        {
            try
            {
                Patch_VoiceroidAsAnimalBootstrap.Apply(Harmony);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP patch failed: {e}");
            }
        }
    }

    internal static class Patch_SellSlingshot_ConfirmIf
    {
        // ConfirmIf 是静态方法，无 __instance；参数顺序须与 CompSellSlingshot.ConfirmIf(Func<bool>, Func<string>, Action, bool) 一致。
        // 原始逻辑：predicate()=true 显示确认弹窗，predicate()=false 直接执行 onConfirm。联机时跳过弹窗，始终执行 onConfirm。
        private static bool Prefix(Func<bool> predicate, Func<string> confirmStr, Action onConfirm, bool danger)
        {
            // 单机：保持原逻辑（包含确认弹窗）
            if (!MP.IsInMultiplayer)
                return true;

            // 联机：跳过确认弹窗，直接执行 onConfirm。predicate 仅决定是否弹窗，不决定是否执行；无论 predicate 真假都应执行。
            if (onConfirm == null)
                return false;

            try
            {
                onConfirm.Invoke();
            }
            catch (Exception e)
            {
                Log.Error($"[MP-MeowOnlineShop] SellSlingshot ConfirmIf onConfirm failed: {e}");
            }

            // 阻止原始 ConfirmIf 再次运行（避免重复弹窗/重复执行）
            return false;
        }
    }

    /// <summary>
    /// 为 TryLaunch 包裹确定性 Rand 上下文，解决 "Wrong random state on map" / "Wrong random state for the world" 掉线。
    /// 根因：1) DropCellFinder.TradeDropSpot、DropPodUtility.DropThingsNear 使用 map.Rand；
    /// 2) 主机在殖民地时“当前 Rand”为 map.Rand，客户端在世界视图时“当前 Rand”为 World.rand，若只 Push 静态 Rand 会推错实例，导致世界层随机状态单边消耗；
    /// 3) 联机校验报 "Wrong random state for the world" 多为 World.rand 或 map.Rand 在双端消耗不一致。
    /// 方案：同时包裹 1) 静态 Verse.Rand（DropCellFinder/DropPodUtility 实际使用的）、2) map.Rand、3) World.rand，
    /// 三者在双端用同一确定性种子 Push/Pop，避免“当前 Rand”在 UI/同步上下文中为 World 导致 world 状态单边消耗；Transpiler 将 ListFullCopy 替换为按 thingIDNumber 排序的副本。
    /// </summary>
    internal static class Patch_SellSlingshot_TryLaunch
    {
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;
        private static bool _warnedDeterministicScopeDisabled;
        private static bool _warnedDeterministicScopeBeginFailed;
        /// <summary>
        /// 返回按 parent.thingIDNumber 排序的副本，确保主机与客户端 foreach 迭代顺序一致，Rand 消耗序列同步。
        /// </summary>
        public static List<CompTransporter> DeterministicTransportersCopy(List<CompTransporter> list)
        {
            if (list == null)
                return null;
            var copy = GenList.ListFullCopy(list);
            copy.Sort((a, b) => (a?.parent?.thingIDNumber ?? 0).CompareTo(b?.parent?.thingIDNumber ?? 0));
            return copy;
        }

        // 用于 Finalizer 中对 map.Rand 做 PopState（Harmony 仅支持 int __state，无法传 Map）
        [ThreadStatic] private static Map _mapForRandPop;

        // 诊断：仅首次 TryLaunch 时打一次日志，便于确认 map/World Rand 反射是否成功
        private static bool _randWrapLogged;

        // 缓存 World.rand 的 Get（若存在）
        private static readonly System.Func<object> WorldRandGetter = TryGetWorldRandGetter();
        private const int WorldSeedOffset = 0x2A3B; // 与地图种子区分，避免与 map Rand 冲突

        private static string FormatStateBits(int state)
        {
            return $"static={((state & StateStaticRand) != 0)} map={((state & StateMapRand) != 0)} world={((state & StateWorldRand) != 0)}";
        }

        private static System.Func<object> TryGetWorldRandGetter()
        {
            try
            {
                // 先尝试属性，再尝试字段（RimWorld 可能用 rand 字段）；静态初始化时 Find.World 可能为 null，getter 在运行时再取
                var getter = (Func<object>)(() =>
                {
                    var w = Find.World;
                    if (w == null) return null;
                    var t = w.GetType();
                    return AccessTools.Property(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Property(t, "rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "rand")?.GetValue(w);
                });
                return getter;
            }
            catch { return null; }
        }

        /// <summary>对指定 Map 的 Rand 执行 PushState(seed)，返回是否执行过 Push。</summary>
        private static bool PushMapRandIfExists(Map map, int seed)
        {
            if (map == null) return false;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return false;
                var pushMethod = mapRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null) return false;
                pushMethod.Invoke(mapRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        /// <summary>对 World 的 Rand（若存在）执行 PushState(seed)，返回是否执行过 Push。</summary>
        private static bool PushWorldRandIfExists(int seed)
        {
            if (WorldRandGetter == null) return false;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return false;
                var pushMethod = worldRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null) return false;
                pushMethod.Invoke(worldRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        /// <summary>对 World 的 Rand（若存在）执行 PopState。</summary>
        private static void PopWorldRandIfExists()
        {
            if (WorldRandGetter == null) return;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return;
                var popMethod = worldRand.GetType().GetMethod("PopState", Type.EmptyTypes);
                popMethod?.Invoke(worldRand, null);
            }
            catch { /* 忽略 */ }
        }

        /// <summary>对当前缓存的 Map 的 Rand 执行 PopState（由 Prefix 设置 _mapForRandPop）。</summary>
        private static void PopMapRandIfExists()
        {
            var map = _mapForRandPop;
            _mapForRandPop = null;
            if (map == null) return;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return;
                var popMethod = mapRand.GetType().GetMethod("PopState", Type.EmptyTypes);
                popMethod?.Invoke(mapRand, null);
            }
            catch { /* 忽略 */ }
        }

        // __state：bit0=静态 Rand，bit1=map.Rand，bit2=World Rand。Pop 顺序与 Push 相反：World -> map -> 静态
        private static void Prefix(object __instance, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;

            if (!MP.IsInMultiplayer)
                return;

            var comp = __instance as ThingComp;
            if (comp?.parent == null)
                return;

            Map map = comp.parent.Map;
            if (map == null)
                return;

            int mapIndex = map.Index;
            if (mapIndex < 0)
                return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < 0)
                return;
            int seed = Gen.HashCombineInt(Gen.HashCombineInt(comp.parent.thingIDNumber, mapIndex), 0x5E110000);
            if (!MP.IsExecutingSyncCommand)
            {
                if (tick >= 0)
                    seed = Gen.HashCombineInt(seed, tick);
            }

            if (!OptimizationGate.IsDeterministicRandScopeEnabled)
            {
                if (!_warnedDeterministicScopeDisabled)
                {
                    _warnedDeterministicScopeDisabled = true;
                    Log.Warning("[MP-MeowOnlineShop] TryLaunch Rand scope skipped because enableDeterministicRandRefactor=false. SellSlingshot may desync on random state.");
                }
                return;
            }

            Map mapForPop;
            bool entered = DeterministicRandScope.Begin(map, seed, WorldSeedOffset, ref __state, out mapForPop);
            _mapForRandPop = mapForPop;

            if (!entered && !_warnedDeterministicScopeBeginFailed)
            {
                _warnedDeterministicScopeBeginFailed = true;
                Log.Warning($"[MP-MeowOnlineShop] TryLaunch Rand scope Begin returned false: map={mapIndex} tick={tick} sync={MP.IsExecutingSyncCommand} seed={seed}.");
            }

            if (ModDebug.EnableTryLaunchLog && !_randWrapLogged)
            {
                _randWrapLogged = true;
                bool worldOk = (__state & DeterministicRandScope.StateWorldRand) != 0;
                bool mapOk = (__state & DeterministicRandScope.StateMapRand) != 0;
                Log.Message($"[MP-MeowOnlineShop] TryLaunch Rand wrap: static=yes map={mapOk} world={worldOk} (if world=false, World.rand reflection may have failed; desync may persist)");
            }

            if (ModDebug.EnableTryLaunchLog)
            {
                Log.Message($"[MP-MeowOnlineShop] TryLaunch Prefix: map={mapIndex} thing={comp.parent.thingIDNumber} tick={tick} sync={MP.IsExecutingSyncCommand} seed={seed} state=0x{__state:X} ({FormatStateBits(__state)})");
            }
        }

        private static Exception Finalizer(Exception __exception, int __state)
        {
            if (!MP.IsInMultiplayer)
                return __exception;
            try
            {
                DeterministicRandScope.End(__state, _mapForRandPop);
                if (ModDebug.EnableTryLaunchLog)
                    Log.Message($"[MP-MeowOnlineShop] TryLaunch Finalizer: sync={MP.IsExecutingSyncCommand} state=0x{__state:X} ({FormatStateBits(__state)}) popDone=true ex={(__exception != null)}");
            }
            finally
            {
                _mapForRandPop = null;
            }
            return __exception;
        }

        /// <summary>
        /// 将 foreach 中的 GenList.ListFullCopy 替换为 DeterministicTransportersCopy，确保迭代顺序确定性。
        /// </summary>
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var listFullCopyGeneric = typeof(GenList).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "ListFullCopy" && m.IsGenericMethod && m.GetParameters().Length == 1);
            var listFullCopyMethod = listFullCopyGeneric?.MakeGenericMethod(typeof(CompTransporter));
            var replacementMethod = AccessTools.Method(typeof(Patch_SellSlingshot_TryLaunch), "DeterministicTransportersCopy");
            if (listFullCopyMethod == null || replacementMethod == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Transpiler could not resolve methods, deterministic iteration not applied.");
                foreach (var instr in instructions)
                    yield return instr;
                yield break;
            }

            foreach (var instr in instructions)
            {
                if (instr.Calls(listFullCopyMethod))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacementMethod);
                }
                else
                {
                    yield return instr;
                }
            }
        }
    }

    /// <summary>
    /// 为所有通过 DropPodUtility.DropThingsNear 落地的空投包裹确定性 Rand（静态 Rand + map.Rand + World.rand）。
    /// 这样即使喵喵电商/其他模组在联机下通过空投发货，也不会在落地/解包瞬间因为 Rand 消耗不同步而触发
    /// "Wrong random state on map X" 或 "Wrong random state for the world"。
    /// </summary>
    internal static class Patch_DropPodRandStabilizer
    {
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;
        private const int SeedOffsetDropPod = 0x9D21;
        private const int WorldSeedOffset = 0x2A3B;
        private static bool _warnedDeterministicScopeDisabled;
        private static bool _warnedDeterministicScopeBeginFailed;

        [ThreadStatic] private static Map _mapForRandPop;
        [ThreadStatic] private static string _lastPrefixTag;
        [ThreadStatic] private static MethodBase _lastOriginalMethod;

        // 与 Patch_SellSlingshot_TryLaunch 中逻辑一致的工具方法，避免与 RimWorld 版本差异强耦合。
        private static bool PushMapRandIfExists(Map map, int seed)
        {
            if (map == null) return false;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return false;
                var pushMethod = mapRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null) return false;
                pushMethod.Invoke(mapRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();

        private static Func<object> TryGetWorldRandGetter()
        {
            try
            {
                return () =>
                {
                    var w = Find.World;
                    if (w == null) return null;
                    var t = w.GetType();
                    return AccessTools.Property(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Property(t, "rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "rand")?.GetValue(w);
                };
            }
            catch { return null; }
        }

        private static bool PushWorldRandIfExists(int seed)
        {
            if (WorldRandGetter == null) return false;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return false;
                var pushMethod = worldRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null) return false;
                pushMethod.Invoke(worldRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        private static void PopWorldRandIfExists()
        {
            if (WorldRandGetter == null) return;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return;
                var popMethod = worldRand.GetType().GetMethod("PopState", Type.EmptyTypes);
                popMethod?.Invoke(worldRand, null);
            }
            catch { }
        }

        private static void PopMapRandIfExists()
        {
            var map = _mapForRandPop;
            _mapForRandPop = null;
            if (map == null) return;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return;
                var popMethod = mapRand.GetType().GetMethod("PopState", Type.EmptyTypes);
                popMethod?.Invoke(mapRand, null);
            }
            catch { }
        }

        /// <summary>为同 tick 同地图多次空投区分 Rand子流；仅哈希货物 ID（有上限），避免 O(n) 过大。</summary>
        private static int HashDropCargoIds(IEnumerable<Thing> things)
        {
            if (things == null) return 0;
            int h = 0;
            int n = 0;
            const int max = 128;
            try
            {
                foreach (var t in things)
                {
                    if (t != null)
                        h = Gen.HashCombineInt(h, t.thingIDNumber);
                    if (++n >= max) break;
                }
            }
            catch { }
            return Gen.HashCombineInt(h, n);
        }

        /// <summary>3 参数及第 4 参非 int 的 DropThingsNear 重载。</summary>
        public static void PrefixShort(IntVec3 dropCenter, Map map, IEnumerable<Thing> things, ref int __state, MethodBase __originalMethod)
        {
            PrefixCore("short", __originalMethod, dropCenter, map, things, ref __state);
        }

        /// <summary>第 4 参为 int（如 openDelay）的 DropThingsNear 重载。</summary>
        public static void PrefixWithOpenDelay(IntVec3 dropCenter, Map map, IEnumerable<Thing> things, int openDelay, ref int __state, MethodBase __originalMethod)
        {
            PrefixCore("withOpenDelay", __originalMethod, dropCenter, map, things, ref __state);
        }

        // Prefix 仅绑定 DropPodUtility.DropThingsNear 的前几个参数（其余参数在不同 RimWorld 版本/MP 重写中可能变化），
        // 额外的 ref int __state 用于记录 Push 了哪些 Rand。这样可在 1.4~1.6 等不同签名下保持兼容。
        private static void PrefixCore(string overloadTag, MethodBase originalMethod, IntVec3 dropCenter, Map map, IEnumerable<Thing> things, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            _lastPrefixTag = overloadTag;
            _lastOriginalMethod = originalMethod;

            if (!MP.IsInMultiplayer || map == null)
                return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            int mapIndex = map.Index;
            if (tick < 0 || mapIndex < 0)
                return;

            int seed = Gen.HashCombineInt(Gen.HashCombineInt(mapIndex, SeedOffsetDropPod), dropCenter.GetHashCode());
            seed = Gen.HashCombineInt(seed, HashDropCargoIds(things));
            if (!MP.IsExecutingSyncCommand)
                seed = Gen.HashCombineInt(seed, tick);

            if (!OptimizationGate.IsDeterministicRandScopeEnabled)
            {
                if (!_warnedDeterministicScopeDisabled)
                {
                    _warnedDeterministicScopeDisabled = true;
                    Log.Warning("[MP-MeowOnlineShop] DropPod Rand scope skipped because enableDeterministicRandRefactor=false. DropThingsNear may desync on random state.");
                }
                return;
            }

            if (ModDebug.EnableDropPodTrace)
            {
                int thingsCount = 0;
                if (things != null)
                {
                    try
                    {
                        foreach (var _ in things)
                            thingsCount++;
                    }
                    catch { }
                }
                string methodName = originalMethod != null ? $"{originalMethod.DeclaringType?.Name}.{originalMethod.Name}" : "unknown";
                Log.Message($"[MP-MeowOnlineShop] DropPodRand Prefix[{overloadTag}]: method={methodName} map={mapIndex} tick={tick} sync={MP.IsExecutingSyncCommand} center={dropCenter} things={thingsCount} seed={seed}");
            }

            Map mapForPop;
            bool entered = DeterministicRandScope.Begin(map, seed, WorldSeedOffset, ref __state, out mapForPop);
            _mapForRandPop = mapForPop;
            if (!entered && !_warnedDeterministicScopeBeginFailed)
            {
                _warnedDeterministicScopeBeginFailed = true;
                Log.Warning($"[MP-MeowOnlineShop] DropPod Rand scope Begin returned false: map={mapIndex} tick={tick} sync={MP.IsExecutingSyncCommand} seed={seed}.");
            }
        }

        public static Exception Finalizer(Exception __exception, int __state)
        {
            if (!MP.IsInMultiplayer)
                return __exception;

            try
            {
                DeterministicRandScope.End(__state, _mapForRandPop);
                _mapForRandPop = null;

                if (ModDebug.EnableDropPodTrace)
                {
                    string methodName = _lastOriginalMethod != null ? $"{_lastOriginalMethod.DeclaringType?.Name}.{_lastOriginalMethod.Name}" : "unknown";
                    Log.Message($"[MP-MeowOnlineShop] DropPodRand Finalizer[{_lastPrefixTag ?? "unknown"}]: method={methodName} sync={MP.IsExecutingSyncCommand} state=0x{__state:X} popDone=true ex={(__exception != null)}");
                }
            }
            catch
            {
                // 忽略 Pop 异常，避免将真正的逻辑异常吞掉
            }
            finally
            {
                _lastOriginalMethod = null;
                _lastPrefixTag = null;
            }

            return __exception;
        }
    }
}
