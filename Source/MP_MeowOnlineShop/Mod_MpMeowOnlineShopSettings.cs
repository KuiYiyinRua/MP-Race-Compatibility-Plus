using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    public sealed class MpMeowOnlineShopSettings : ModSettings
    {
        public const int SchedulerModeConservative = 0;
        public const int SchedulerModeAggressive = 1;
        public const int SchedulerModeEventDriven = 2;

        /// <summary>一键优化预设档位（仅用于 UI/逻辑标识，数值以各字段为准）。</summary>
        public const int OptimizationPresetNone = 0;
        public const int OptimizationPresetLight = 1;
        public const int OptimizationPresetMedium = 2;
        public const int OptimizationPresetHigh = 3;
        public const int OptimizationPresetAggressive = 4;
        public const int OptimizationPresetVeryAggressive = 5;
        public const int OptimizationPresetExtreme = 6;

        public bool tpsOptimizeEnabled = true;
        public bool enableOptimizationTelemetry = true;
        public int optimizationTelemetryIntervalTicks = 300;
        public bool enableUiActionBatching = true;
        public bool enableDeterministicRandRefactor = true;
        public bool enableTickListFullReconcile = true;
        public bool enableThirdPartyPerfCleanup = true;
        public bool enableMpServerLagThrottle = true;
        public bool enableProjectileLauncherDeterminism = false;
        public bool enableMiliraWeaponModeCompat = true;
        public bool enableMpSafeAlertThrottling = true;
        public bool enableMpMissileGirlMothballPort = true;
        public bool enableMpMissileGirlBeautyPort = true;
        public bool enableMpMissileGirlTimetableFix = true;
        public bool blockGodHandsWhilePaused;
        public int mediumAlertRecheckIntervalTicks = 180;
        public bool enableExperimentalTickScheduler = false;
        public int schedulerMode = SchedulerModeAggressive;
        public int asyncFallbackCooldownTicks = 420;
        public int aggressiveCombatIntervalTicks = 4;
        public int aggressiveWatchIntervalTicks = 6;
        public int aggressiveAwakeIntervalTicks = 8;
        public int aggressiveIdleBaseIntervalTicks = 160;
        public int aggressiveIdlePerMapFactorTicks = 26;
        public int eventDrivenCombatIntervalTicks = 4;
        public int eventDrivenWatchIntervalTicks = 8;
        public int eventDrivenAwakeIntervalTicks = 14;
        public int eventDrivenSparseBaseIntervalTicks = 220;
        public int eventDrivenSparsePerMapFactorTicks = 34;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tpsOptimizeEnabled, "mp_meow_modcfg_tps_opt_enabled", true);
            Scribe_Values.Look(ref enableOptimizationTelemetry, "mp_meow_modcfg_opt_telemetry_enabled", true);
            Scribe_Values.Look(ref optimizationTelemetryIntervalTicks, "mp_meow_modcfg_opt_telemetry_interval", 300);
            Scribe_Values.Look(ref enableUiActionBatching, "mp_meow_modcfg_opt_ui_batching_enabled", true);
            Scribe_Values.Look(ref enableDeterministicRandRefactor, "mp_meow_modcfg_opt_rand_refactor_enabled", true);
            Scribe_Values.Look(ref enableTickListFullReconcile, "mp_meow_modcfg_opt_ticklist_full_reconcile_enabled", true);
            Scribe_Values.Look(ref enableThirdPartyPerfCleanup, "mp_meow_modcfg_opt_thirdparty_perf_cleanup_enabled", true);
            Scribe_Values.Look(ref enableMpServerLagThrottle, "mp_meow_modcfg_opt_server_lag_throttle_enabled", true);
            Scribe_Values.Look(ref enableProjectileLauncherDeterminism, "mp_meow_modcfg_opt_projectile_launcher_determinism_enabled", false);
            Scribe_Values.Look(ref enableMiliraWeaponModeCompat, "mp_meow_modcfg_opt_milira_weapon_mode_compat_enabled", true);
            Scribe_Values.Look(ref enableMpSafeAlertThrottling, "mp_meow_modcfg_opt_alert_throttling_enabled", true);
            Scribe_Values.Look(ref enableMpMissileGirlMothballPort, "mp_meow_modcfg_missilegirl_mothball_port", true);
            Scribe_Values.Look(ref enableMpMissileGirlBeautyPort, "mp_meow_modcfg_missilegirl_beauty_port", true);
            Scribe_Values.Look(ref enableMpMissileGirlTimetableFix, "mp_meow_modcfg_missilegirl_timetable_fix", true);
            Scribe_Values.Look(ref blockGodHandsWhilePaused, "mp_meow_modcfg_godhands_pause_block_enabled", false);
            Scribe_Values.Look(ref mediumAlertRecheckIntervalTicks, "mp_meow_modcfg_opt_alert_interval_ticks", 180);
            Scribe_Values.Look(ref enableExperimentalTickScheduler, "mp_meow_modcfg_opt_exp_tick_scheduler_enabled", false);
            Scribe_Values.Look(ref schedulerMode, "mp_meow_modcfg_tps_opt_mode", SchedulerModeAggressive);
            Scribe_Values.Look(ref asyncFallbackCooldownTicks, "mp_meow_modcfg_tps_opt_async_fallback_cooldown", 420);
            Scribe_Values.Look(ref aggressiveCombatIntervalTicks, "mp_meow_modcfg_tps_opt_aggressive_combat_interval", 4);
            Scribe_Values.Look(ref aggressiveWatchIntervalTicks, "mp_meow_modcfg_tps_opt_aggressive_watch_interval", 6);
            Scribe_Values.Look(ref aggressiveAwakeIntervalTicks, "mp_meow_modcfg_tps_opt_aggressive_awake_interval", 8);
            Scribe_Values.Look(ref aggressiveIdleBaseIntervalTicks, "mp_meow_modcfg_tps_opt_aggressive_idle_base_interval", 160);
            Scribe_Values.Look(ref aggressiveIdlePerMapFactorTicks, "mp_meow_modcfg_tps_opt_aggressive_idle_map_factor", 26);
            Scribe_Values.Look(ref eventDrivenCombatIntervalTicks, "mp_meow_modcfg_tps_opt_event_combat_interval", 4);
            Scribe_Values.Look(ref eventDrivenWatchIntervalTicks, "mp_meow_modcfg_tps_opt_event_watch_interval", 8);
            Scribe_Values.Look(ref eventDrivenAwakeIntervalTicks, "mp_meow_modcfg_tps_opt_event_awake_interval", 14);
            Scribe_Values.Look(ref eventDrivenSparseBaseIntervalTicks, "mp_meow_modcfg_tps_opt_event_sparse_base_interval", 220);
            Scribe_Values.Look(ref eventDrivenSparsePerMapFactorTicks, "mp_meow_modcfg_tps_opt_event_sparse_map_factor", 34);

            ClampValues();
        }

        public void ResetToDefaults()
        {
            ApplyOptimizationPreset(OptimizationPresetHigh);
        }

        /// <summary>
        /// 按预设档位批量写入配置（会覆盖调度模式与各间隔参数）。
        /// 无优化：仅关闭 TPS/实验调度；UI 批处理与确定性 Rand 仍建议保持开启以降低联机失步风险。
        /// </summary>
        public void ApplyOptimizationPreset(int preset)
        {
            enableMpSafeAlertThrottling = preset != OptimizationPresetNone;
            enableMpMissileGirlMothballPort = false;
            enableMpMissileGirlBeautyPort = false;
            enableMpMissileGirlTimetableFix = true;
            mediumAlertRecheckIntervalTicks =
                preset == OptimizationPresetLight ? 90 :
                preset == OptimizationPresetMedium ? 120 :
                preset == OptimizationPresetHigh ? 180 :
                preset == OptimizationPresetAggressive ? 240 :
                preset == OptimizationPresetVeryAggressive ? 300 :
                preset == OptimizationPresetExtreme ? 360 :
                180;
            enableTickListFullReconcile = true;
            enableThirdPartyPerfCleanup = true;
            enableMpServerLagThrottle = true;
            enableProjectileLauncherDeterminism = false;
            enableMiliraWeaponModeCompat = true;

            switch (preset)
            {
                case OptimizationPresetNone:
                    tpsOptimizeEnabled = false;
                    enableOptimizationTelemetry = false;
                    optimizationTelemetryIntervalTicks = 600;
                    enableUiActionBatching = true;
                    enableDeterministicRandRefactor = true;
                    enableExperimentalTickScheduler = false;
                    schedulerMode = SchedulerModeConservative;
                    asyncFallbackCooldownTicks = 2400;
                    aggressiveCombatIntervalTicks = 10;
                    aggressiveWatchIntervalTicks = 14;
                    aggressiveAwakeIntervalTicks = 18;
                    aggressiveIdleBaseIntervalTicks = 280;
                    aggressiveIdlePerMapFactorTicks = 44;
                    eventDrivenCombatIntervalTicks = 10;
                    eventDrivenWatchIntervalTicks = 18;
                    eventDrivenAwakeIntervalTicks = 24;
                    eventDrivenSparseBaseIntervalTicks = 320;
                    eventDrivenSparsePerMapFactorTicks = 52;
                    break;

                case OptimizationPresetLight:
                    tpsOptimizeEnabled = true;
                    enableOptimizationTelemetry = true;
                    optimizationTelemetryIntervalTicks = 600;
                    enableUiActionBatching = true;
                    enableDeterministicRandRefactor = true;
                    enableExperimentalTickScheduler = false;
                    schedulerMode = SchedulerModeConservative;
                    asyncFallbackCooldownTicks = 600;
                    aggressiveCombatIntervalTicks = 8;
                    aggressiveWatchIntervalTicks = 12;
                    aggressiveAwakeIntervalTicks = 16;
                    aggressiveIdleBaseIntervalTicks = 240;
                    aggressiveIdlePerMapFactorTicks = 40;
                    eventDrivenCombatIntervalTicks = 8;
                    eventDrivenWatchIntervalTicks = 16;
                    eventDrivenAwakeIntervalTicks = 22;
                    eventDrivenSparseBaseIntervalTicks = 300;
                    eventDrivenSparsePerMapFactorTicks = 50;
                    break;

                case OptimizationPresetMedium:
                    tpsOptimizeEnabled = true;
                    enableOptimizationTelemetry = true;
                    optimizationTelemetryIntervalTicks = 420;
                    enableUiActionBatching = true;
                    enableDeterministicRandRefactor = true;
                    enableExperimentalTickScheduler = false;
                    schedulerMode = SchedulerModeAggressive;
                    asyncFallbackCooldownTicks = 480;
                    aggressiveCombatIntervalTicks = 6;
                    aggressiveWatchIntervalTicks = 9;
                    aggressiveAwakeIntervalTicks = 12;
                    aggressiveIdleBaseIntervalTicks = 200;
                    aggressiveIdlePerMapFactorTicks = 32;
                    eventDrivenCombatIntervalTicks = 6;
                    eventDrivenWatchIntervalTicks = 12;
                    eventDrivenAwakeIntervalTicks = 18;
                    eventDrivenSparseBaseIntervalTicks = 260;
                    eventDrivenSparsePerMapFactorTicks = 40;
                    break;

                case OptimizationPresetHigh:
                    tpsOptimizeEnabled = true;
                    enableOptimizationTelemetry = true;
                    optimizationTelemetryIntervalTicks = 300;
                    enableUiActionBatching = true;
                    enableDeterministicRandRefactor = true;
                    enableExperimentalTickScheduler = false;
                    schedulerMode = SchedulerModeAggressive;
                    asyncFallbackCooldownTicks = 420;
                    aggressiveCombatIntervalTicks = 4;
                    aggressiveWatchIntervalTicks = 6;
                    aggressiveAwakeIntervalTicks = 8;
                    aggressiveIdleBaseIntervalTicks = 160;
                    aggressiveIdlePerMapFactorTicks = 26;
                    eventDrivenCombatIntervalTicks = 4;
                    eventDrivenWatchIntervalTicks = 8;
                    eventDrivenAwakeIntervalTicks = 14;
                    eventDrivenSparseBaseIntervalTicks = 220;
                    eventDrivenSparsePerMapFactorTicks = 34;
                    break;

                case OptimizationPresetAggressive:
                    tpsOptimizeEnabled = true;
                    enableOptimizationTelemetry = true;
                    optimizationTelemetryIntervalTicks = 240;
                    enableUiActionBatching = true;
                    enableDeterministicRandRefactor = true;
                    enableExperimentalTickScheduler = false;
                    schedulerMode = SchedulerModeAggressive;
                    asyncFallbackCooldownTicks = 360;
                    aggressiveCombatIntervalTicks = 3;
                    aggressiveWatchIntervalTicks = 5;
                    aggressiveAwakeIntervalTicks = 6;
                    aggressiveIdleBaseIntervalTicks = 120;
                    aggressiveIdlePerMapFactorTicks = 20;
                    eventDrivenCombatIntervalTicks = 3;
                    eventDrivenWatchIntervalTicks = 7;
                    eventDrivenAwakeIntervalTicks = 11;
                    eventDrivenSparseBaseIntervalTicks = 180;
                    eventDrivenSparsePerMapFactorTicks = 28;
                    break;

                case OptimizationPresetVeryAggressive:
                    tpsOptimizeEnabled = true;
                    enableOptimizationTelemetry = true;
                    optimizationTelemetryIntervalTicks = 180;
                    enableUiActionBatching = true;
                    enableDeterministicRandRefactor = true;
                    enableExperimentalTickScheduler = false;
                    schedulerMode = SchedulerModeEventDriven;
                    asyncFallbackCooldownTicks = 240;
                    aggressiveCombatIntervalTicks = 3;
                    aggressiveWatchIntervalTicks = 4;
                    aggressiveAwakeIntervalTicks = 5;
                    aggressiveIdleBaseIntervalTicks = 100;
                    aggressiveIdlePerMapFactorTicks = 16;
                    eventDrivenCombatIntervalTicks = 2;
                    eventDrivenWatchIntervalTicks = 5;
                    eventDrivenAwakeIntervalTicks = 8;
                    eventDrivenSparseBaseIntervalTicks = 140;
                    eventDrivenSparsePerMapFactorTicks = 22;
                    break;

                case OptimizationPresetExtreme:
                    tpsOptimizeEnabled = true;
                    enableOptimizationTelemetry = false;
                    optimizationTelemetryIntervalTicks = 3600;
                    enableUiActionBatching = true;
                    enableDeterministicRandRefactor = true;
                    enableExperimentalTickScheduler = true;
                    schedulerMode = SchedulerModeEventDriven;
                    asyncFallbackCooldownTicks = 120;
                    aggressiveCombatIntervalTicks = 1;
                    aggressiveWatchIntervalTicks = 2;
                    aggressiveAwakeIntervalTicks = 2;
                    aggressiveIdleBaseIntervalTicks = 60;
                    aggressiveIdlePerMapFactorTicks = 8;
                    eventDrivenCombatIntervalTicks = 1;
                    eventDrivenWatchIntervalTicks = 2;
                    eventDrivenAwakeIntervalTicks = 2;
                    eventDrivenSparseBaseIntervalTicks = 30;
                    eventDrivenSparsePerMapFactorTicks = 8;
                    break;

                default:
                    ApplyOptimizationPreset(OptimizationPresetHigh);
                    return;
            }

            ClampValues();
        }

        public static string GetOptimizationPresetLabel(int preset)
        {
            switch (preset)
            {
                case OptimizationPresetNone: return "无优化";
                case OptimizationPresetLight: return "轻度优化";
                case OptimizationPresetMedium: return "中度优化";
                case OptimizationPresetHigh: return "高度优化";
                case OptimizationPresetAggressive: return "激进";
                case OptimizationPresetVeryAggressive: return "极度激进";
                case OptimizationPresetExtreme: return "极致优化";
                default: return "自定义";
            }
        }

        public static List<FloatMenuOption> BuildOptimizationPresetMenu(MpMeowOnlineShopSettings target)
        {
            void apply(int p)
            {
                target.ApplyOptimizationPreset(p);
                Patch_MultifactionTpsOptimize.NotifyModSettingsUpdated();
            }

            return new List<FloatMenuOption>
            {
                new FloatMenuOption(GetOptimizationPresetLabel(OptimizationPresetNone) + "：关闭 TPS/实验调度", () => apply(OptimizationPresetNone)),
                new FloatMenuOption(GetOptimizationPresetLabel(OptimizationPresetLight) + "：保守策略、宽松间隔", () => apply(OptimizationPresetLight)),
                new FloatMenuOption(GetOptimizationPresetLabel(OptimizationPresetMedium) + "：激进模式、中等间隔", () => apply(OptimizationPresetMedium)),
                new FloatMenuOption(GetOptimizationPresetLabel(OptimizationPresetHigh) + "：与模组默认一致", () => apply(OptimizationPresetHigh)),
                new FloatMenuOption(GetOptimizationPresetLabel(OptimizationPresetAggressive) + "：更短间隔（激进模式）", () => apply(OptimizationPresetAggressive)),
                new FloatMenuOption(GetOptimizationPresetLabel(OptimizationPresetVeryAggressive) + "：事件驱动、更短间隔", () => apply(OptimizationPresetVeryAggressive)),
                new FloatMenuOption(GetOptimizationPresetLabel(OptimizationPresetExtreme) + "：实验 Tick + 事件驱动 + 最短间隔", () => apply(OptimizationPresetExtreme)),
            };
        }

        public void ClampValues()
        {
            optimizationTelemetryIntervalTicks = Mathf.Clamp(optimizationTelemetryIntervalTicks, 60, 3600);
            mediumAlertRecheckIntervalTicks = Mathf.Clamp(mediumAlertRecheckIntervalTicks, 30, 600);
            schedulerMode = Mathf.Clamp(schedulerMode, SchedulerModeConservative, SchedulerModeEventDriven);
            asyncFallbackCooldownTicks = Mathf.Clamp(asyncFallbackCooldownTicks, 120, 2400);
            aggressiveCombatIntervalTicks = Mathf.Clamp(aggressiveCombatIntervalTicks, 1, 30);
            aggressiveWatchIntervalTicks = Mathf.Clamp(aggressiveWatchIntervalTicks, 2, 60);
            aggressiveAwakeIntervalTicks = Mathf.Clamp(aggressiveAwakeIntervalTicks, 2, 90);
            aggressiveIdleBaseIntervalTicks = Mathf.Clamp(aggressiveIdleBaseIntervalTicks, 30, 3600);
            aggressiveIdlePerMapFactorTicks = Mathf.Clamp(aggressiveIdlePerMapFactorTicks, 1, 200);
            eventDrivenCombatIntervalTicks = Mathf.Clamp(eventDrivenCombatIntervalTicks, 1, 30);
            eventDrivenWatchIntervalTicks = Mathf.Clamp(eventDrivenWatchIntervalTicks, 2, 90);
            eventDrivenAwakeIntervalTicks = Mathf.Clamp(eventDrivenAwakeIntervalTicks, 2, 120);
            eventDrivenSparseBaseIntervalTicks = Mathf.Clamp(eventDrivenSparseBaseIntervalTicks, 30, 4800);
            eventDrivenSparsePerMapFactorTicks = Mathf.Clamp(eventDrivenSparsePerMapFactorTicks, 1, 240);
        }
    }

    public sealed class MpMeowOnlineShopMod : Mod
    {
        private static MpMeowOnlineShopSettings _settings;
        private Vector2 _settingsScrollPos;

        public static MpMeowOnlineShopSettings Settings => _settings;

        public MpMeowOnlineShopMod(ModContentPack content) : base(content)
        {
            Patch_RimJobWorld.ApplyEarly();
            _settings = GetSettings<MpMeowOnlineShopSettings>();
        }

        public override string SettingsCategory()
        {
            return "[MP] Meow Online Shop Compat";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            _settings.ClampValues();
            var viewRect = new Rect(0f, 0f, inRect.width - 20f, 1950f);
            Widgets.BeginScrollView(inRect, ref _settingsScrollPos, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);
            listing.CheckboxLabeled(
                "暂停时禁用神之手（联机）",
                ref _settings.blockGodHandsWhilePaused,
                "勾选后，联机暂停时无法使用神之手及该模组的其他手功能；恢复运行后才可使用。");
            listing.Gap(8f);
            listing.Label("一键优化预设（会覆盖下方调度模式与各间隔参数；无优化仍保留 UI 批处理与确定性 Rand 以降低失步风险）");
            if (listing.ButtonText("选择并应用预设档位…"))
            {
                Find.WindowStack.Add(new FloatMenu(MpMeowOnlineShopSettings.BuildOptimizationPresetMenu(_settings)));
            }

            listing.Gap(8f);
            listing.CheckboxLabeled(
                "启用联机TPS优化（主机权威）",
                ref _settings.tpsOptimizeEnabled,
                "默认开启。联机时以主机设置为准，客户端会自动跟随主机状态。");
            listing.Gap(4f);
            listing.CheckboxLabeled(
                "启用实验性 Tick 调度（高风险，默认关闭）",
                ref _settings.enableExperimentalTickScheduler,
                "关闭时强制回退 vanilla MapPostTick。");
            listing.Gap(8f);
            listing.Label("调度策略模式");
            _settings.schedulerMode = Mathf.Clamp(
                (int)listing.Slider(_settings.schedulerMode, 0f, 2f),
                MpMeowOnlineShopSettings.SchedulerModeConservative,
                MpMeowOnlineShopSettings.SchedulerModeEventDriven);
            string modeLabel = _settings.schedulerMode == MpMeowOnlineShopSettings.SchedulerModeConservative
                ? "当前：保守（稳定优先）"
                : _settings.schedulerMode == MpMeowOnlineShopSettings.SchedulerModeEventDriven
                    ? "当前：事件驱动（省Tick优先）"
                    : "当前：激进（性能优先）";
            listing.Label(modeLabel);
            listing.Label("Async 回退冷却 Tick（120-2400）");
            _settings.asyncFallbackCooldownTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.asyncFallbackCooldownTicks, 120f, 2400f),
                120,
                2400);

            listing.GapLine();
            listing.Label("激进模式参数");
            listing.Label($"战斗地图间隔（1-30）：{_settings.aggressiveCombatIntervalTicks}");
            _settings.aggressiveCombatIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.aggressiveCombatIntervalTicks, 1f, 30f),
                1,
                30);
            listing.Label($"警戒地图间隔（2-60）：{_settings.aggressiveWatchIntervalTicks}");
            _settings.aggressiveWatchIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.aggressiveWatchIntervalTicks, 2f, 60f),
                2,
                60);
            listing.Label($"唤醒地图间隔（2-90）：{_settings.aggressiveAwakeIntervalTicks}");
            _settings.aggressiveAwakeIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.aggressiveAwakeIntervalTicks, 2f, 90f),
                2,
                90);
            listing.Label($"空闲基础间隔（30-3600）：{_settings.aggressiveIdleBaseIntervalTicks}");
            _settings.aggressiveIdleBaseIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.aggressiveIdleBaseIntervalTicks, 30f, 3600f),
                30,
                3600);
            listing.Label($"空闲地图系数（1-200）：{_settings.aggressiveIdlePerMapFactorTicks}");
            _settings.aggressiveIdlePerMapFactorTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.aggressiveIdlePerMapFactorTicks, 1f, 200f),
                1,
                200);

            listing.GapLine();
            listing.Label("事件驱动模式参数");
            listing.Label($"战斗地图间隔（1-30）：{_settings.eventDrivenCombatIntervalTicks}");
            _settings.eventDrivenCombatIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.eventDrivenCombatIntervalTicks, 1f, 30f),
                1,
                30);
            listing.Label($"警戒地图间隔（2-90）：{_settings.eventDrivenWatchIntervalTicks}");
            _settings.eventDrivenWatchIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.eventDrivenWatchIntervalTicks, 2f, 90f),
                2,
                90);
            listing.Label($"唤醒地图间隔（2-120）：{_settings.eventDrivenAwakeIntervalTicks}");
            _settings.eventDrivenAwakeIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.eventDrivenAwakeIntervalTicks, 2f, 120f),
                2,
                120);
            listing.Label($"稀疏基础间隔（30-4800）：{_settings.eventDrivenSparseBaseIntervalTicks}");
            _settings.eventDrivenSparseBaseIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.eventDrivenSparseBaseIntervalTicks, 30f, 4800f),
                30,
                4800);
            listing.Label($"稀疏地图系数（1-240）：{_settings.eventDrivenSparsePerMapFactorTicks}");
            _settings.eventDrivenSparsePerMapFactorTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.eventDrivenSparsePerMapFactorTicks, 1f, 240f),
                1,
                240);

            listing.GapLine();
            listing.CheckboxLabeled(
                "启用优化遥测日志（低频）",
                ref _settings.enableOptimizationTelemetry,
                "记录优化命中与回退原因，默认开启。");
            listing.Label("遥测间隔 Tick（60-3600）");
            _settings.optimizationTelemetryIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.optimizationTelemetryIntervalTicks, 60f, 3600f),
                60,
                3600);
            listing.CheckboxLabeled(
                "启用 UI 操作批处理（默认）",
                ref _settings.enableUiActionBatching,
                "仅作用于界面层，降低重复触发。");
            listing.CheckboxLabeled(
                "启用确定性 Rand 公共作用域（默认）",
                ref _settings.enableDeterministicRandRefactor,
                "统一随机作用域实现，降低重复反射与分叉风险。");
            listing.CheckboxLabeled(
                "Enable async TickList full-bucket reconcile (default on)",
                ref _settings.enableTickListFullReconcile,
                "Turns on the periodic full-list safety sweep; disable to keep the conservative first-tick rebuild and pending registration merge.");
            listing.CheckboxLabeled(
                "Enable runtime cleanup of third-party performance patches (default on)",
                ref _settings.enableThirdPartyPerfCleanup,
                "Unpatches Slower Pawn Tick Rate / TPS Optimalizer / PerformanceEsmolas when entering multiplayer. Leave off unless desync appears; runtime unpatching can crash native detour finalizers.");
            listing.CheckboxLabeled(
                "Enable Multiplayer server lag log throttle (default on)",
                ref _settings.enableMpServerLagThrottle,
                "Suppresses repeated server pause messages and changes ServerPlayer.ExtrapolatedTicksBehind before the first keepalive. Disabled by default to keep Multiplayer server behavior vanilla.");
            listing.CheckboxLabeled(
                "Enable Milira weapon-mode compat patch (default on)",
                ref _settings.enableMiliraWeaponModeCompat,
                "Adds Milira weapon gizmo/command interception and sustained-fire sync. Disabled by default to restore vanilla Milira firing behavior.");
            listing.Gap(8f);
            if (listing.ButtonText("重置为默认配置"))
            {
                _settings.ResetToDefaults();
                _settings.ClampValues();
                Patch_MultifactionTpsOptimize.NotifyModSettingsUpdated();
            }
            listing.GapLine();
            listing.CheckboxLabeled(
                "\u542f\u7528\u8054\u673a\u5b89\u5168\u7684\u4e2d\u4f18\u5148\u7ea7\u8b66\u62a5\u964d\u9891",
                ref _settings.enableMpSafeAlertThrottling,
                "\u4ec5\u5ef6\u540e\u4e2d\u4f18\u5148\u7ea7\u754c\u9762\u8b66\u62a5\u7684\u91cd\u7b97\uff1b\u9ad8\u3001\u4e25\u91cd\u8b66\u62a5\u3001\u5f3a\u5236\u79fb\u9664\u548c\u540c\u6b65\u547d\u4ee4\u59cb\u7ec8\u4f7f\u7528\u539f\u7248\u884c\u4e3a\u3002");
            listing.Label($"\u4e2d\u4f18\u5148\u7ea7\u8b66\u62a5\u590d\u67e5\u95f4\u9694\uff0830-600 Tick\uff09\uff1a{_settings.mediumAlertRecheckIntervalTicks}");
            _settings.mediumAlertRecheckIntervalTicks = Mathf.Clamp(
                (int)listing.Slider(_settings.mediumAlertRecheckIntervalTicks, 30f, 600f),
                30,
                600);

            listing.GapLine();
            listing.CheckboxLabeled(
                "\u79fb\u690d MissileGirl\uff1a\u6210\u763e/\u8010\u53d7 Hediff \u5141\u8bb8\u4f11\u7720\uff08\u9ed8\u8ba4\u5f00\uff09",
                ref _settings.enableMpMissileGirlMothballPort,
                "\u6309 HediffDef \u786e\u5b9a\u6027\u653e\u5bbd\u6210\u763e/\u8010\u53d7\u75c7\u7684\u4e16\u754c Pawn \u4f11\u7720\u5224\u5b9a\u3002");
            listing.CheckboxLabeled(
                "\u79fb\u690d MissileGirl\uff1a\u52a8\u6001\u7f8e\u89c2\u91c7\u6837\u534a\u5f84\uff08\u9ed8\u8ba4\u5f00\uff09",
                ref _settings.enableMpMissileGirlBeautyPort,
                "\u6839\u636e Pawn \u662f\u5426\u5728\u5e8a/\u5012\u5730/\u79fb\u52a8\u8fc7\u7a0b\u786e\u5b9a\u6027\u8c03\u6574\u7f8e\u89c2\u91c7\u6837\u6570\u3002");
            listing.CheckboxLabeled(
                "\u79fb\u690d MissileGirl\uff1a\u65f6\u95f4\u8868\u7f3a\u5931\u9632\u5fa1\uff08\u9ed8\u8ba4\u5f00\uff09",
                ref _settings.enableMpMissileGirlTimetableFix,
                "\u65f6\u95f4\u8868\u5b9a\u4e49\u7f3a\u5931\u65f6\u56de\u9000\u4e3a Anything\uff0c\u907f\u514d\u5355\u7aef\u5f02\u5e38\u3002");

            listing.End();
            Widgets.EndScrollView();
        }

        public override void WriteSettings()
        {
            _settings.ClampValues();
            base.WriteSettings();
            Patch_MultifactionTpsOptimize.NotifyModSettingsUpdated();
        }
    }
}
