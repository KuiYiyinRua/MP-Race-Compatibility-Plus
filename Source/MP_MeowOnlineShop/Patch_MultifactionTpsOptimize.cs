using System;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Legacy settings remain readable, but whole-map throttling is retired.
    /// MapPostTick includes power, environment and fleck simulation whose native
    /// per-tick schedules cannot be safely sampled using a map-ID offset.
    /// </summary>
    internal static class Patch_MultifactionTpsOptimize
    {
        private static bool _applied;

        public static void Apply()
        {
            if (_applied) return;
            _applied = true;
            // Install no MapPostTick prefix. Multiplayer remains the sole owner
            // of map/world dispatch, so native managers run once per map tick.
            Log.Message("[MP-MeowOnlineShop] Whole-map tick throttling retired: native power, environment and fleck updates preserved on every map tick. Legacy scheduler settings are ignored. Core=" +
                typeof(Patch_MultifactionTpsOptimize).Assembly.GetName().Version);
        }

        internal static void NotifyModSettingsUpdated()
        {
            // Kept for existing settings/hot-sync callers. There is deliberately
            // no process-static scheduler cache to survive loading another game.
        }
    }

    /// <summary>联机会话存档态：保留调度参数，并兼容读取旧版开关字段。</summary>
    public class MultifactionTpsOptimizeComponent : GameComponent
    {
        public bool tpsOptimizeEnabled = true;
        public bool experimentalTickSchedulerEnabled = false;
        public int schedulerMode = 0;
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

        public MultifactionTpsOptimizeComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            bool legacyEnabled = false;
            bool legacyForceEnable = false;
            Scribe_Values.Look(ref legacyEnabled, "mp_meow_multifaction_tps_opt_enabled", false);
            Scribe_Values.Look(ref legacyForceEnable, "mp_meow_multifaction_tps_opt_force_enabled", false);
            Scribe_Values.Look(ref tpsOptimizeEnabled, "mp_meow_multifaction_tps_opt_single_enabled", true);
            Scribe_Values.Look(ref experimentalTickSchedulerEnabled, "mp_meow_multifaction_tps_opt_experimental_enabled", false);
            Scribe_Values.Look(ref schedulerMode, "mp_meow_multifaction_tps_opt_mode", 0);
            Scribe_Values.Look(ref asyncFallbackCooldownTicks, "mp_meow_multifaction_tps_opt_async_fallback_cooldown", 420);
            Scribe_Values.Look(ref aggressiveCombatIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_combat_interval", 4);
            Scribe_Values.Look(ref aggressiveWatchIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_watch_interval", 6);
            Scribe_Values.Look(ref aggressiveAwakeIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_awake_interval", 8);
            Scribe_Values.Look(ref aggressiveIdleBaseIntervalTicks, "mp_meow_multifaction_tps_opt_aggressive_idle_base_interval", 160);
            Scribe_Values.Look(ref aggressiveIdlePerMapFactorTicks, "mp_meow_multifaction_tps_opt_aggressive_idle_map_factor", 26);
            Scribe_Values.Look(ref eventDrivenCombatIntervalTicks, "mp_meow_multifaction_tps_opt_event_combat_interval", 4);
            Scribe_Values.Look(ref eventDrivenWatchIntervalTicks, "mp_meow_multifaction_tps_opt_event_watch_interval", 8);
            Scribe_Values.Look(ref eventDrivenAwakeIntervalTicks, "mp_meow_multifaction_tps_opt_event_awake_interval", 14);
            Scribe_Values.Look(ref eventDrivenSparseBaseIntervalTicks, "mp_meow_multifaction_tps_opt_event_sparse_base_interval", 220);
            Scribe_Values.Look(ref eventDrivenSparsePerMapFactorTicks, "mp_meow_multifaction_tps_opt_event_sparse_map_factor", 34);
        }
    }

    /// <summary>
    /// 独立于 MpMeowOnlineShopBootstrap：确保尽早安装调度补丁，不依赖 Host UI 生命周期。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MultifactionTpsOptimizeBootstrap
    {
        static MultifactionTpsOptimizeBootstrap()
        {
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("performance")) return;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Patch_MultifactionTpsOptimize.Apply();
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Multifaction TPS optimize bootstrap failed: " + e.Message);
                }
            });
        }
    }
}
