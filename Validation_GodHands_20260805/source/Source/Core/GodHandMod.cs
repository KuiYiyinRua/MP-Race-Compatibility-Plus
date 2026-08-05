using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod
{
    // 模组入口
    public class GodHandModMain : Mod
    {
        private static string _modVersion = null;

        // 模组版本
        public static string MOD_VERSION
        {
            get
            {
                if (_modVersion == null)
                {
                    _modVersion = "Unknown";
                }
                return _modVersion;
            }
        }

        public static GodHandSettings Settings { get; private set; }

        public GodHandModMain(ModContentPack content) : base(content)
        {
            // 初始化版本
            _modVersion = content.ModMetaData?.ModVersion ?? "1.0.1";
            Settings = GetSettings<GodHandSettings>();

            // 检查更新
            if (Settings.lastRunVersion != MOD_VERSION)
            {
                Log.Message($"[神之手] 版本更新 {Settings.lastRunVersion} 到 {MOD_VERSION} 重置提示");
                ResetHelpTags();
                Settings.lastRunVersion = MOD_VERSION;
                Settings.Write();
            }

            // 应用补丁
            var harmony = new Harmony("Palpha.godhands");
            harmony.PatchAll();

            // 检查补丁状态
            CheckPatches();

            // 联动检查
            CheckRimTalkAvailability();

            // 异步生成图标
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                GodHandGeneratorController.GenerateAndApplyIcon();
            });

            Log.Message("[神之手] 已加载");
            RefreshBuildingVisibility();
        }

        // 重置帮助标记
        private void ResetHelpTags()
        {
            Settings.hasSeenHelp_GodHand = false;
            Settings.hasSeenHelp_GodWrench = false;
            Settings.hasSeenHelp_Caress = false;
            Settings.hasSeenHelp_Cleaning = false;
            Settings.hasSeenHelp_Judgment = false;
            Settings.hasSeenHelp_Poke = false;
            Settings.hasSeenHelp_Magnifier = false;
            Settings.hasSeenHelp_Generator = false;
        }

        // 检查关键补丁
        private void CheckPatches()
        {
            var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(Thing), "DynamicDrawPhaseAt"));
            if (patches != null)
            {
                DebugLog($"[神之手] 绘制补丁已挂载 {patches.Postfixes.Count} 个");
            }
        }

        // 调试日志
        public static void DebugLog(string message)
        {
            if (Settings?.enableDebugLog == true)
            {
                Log.Message(message);
            }
        }

        // 检测联动
        private void CheckRimTalkAvailability()
        {
            try
            {
                var type = AccessTools.TypeByName("RimTalk.Service.PromptService");
                GodHandSettings.isRimTalkAvailable = (type != null);
            }
            catch
            {
                GodHandSettings.isRimTalkAvailable = false;
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Settings != null)
            {
                lastGeneratorEnabledState = Settings.enableHandCrankGenerator;
                lastEndRodEnabledState = Settings.disableEndRodGenerator;
                lastNSFWState = Settings.disableNSFW;
            }
            Settings.DoSettingsWindowContents(inRect);
        }

        private bool lastGeneratorEnabledState = true;
        private bool lastEndRodEnabledState = false;
        private bool lastNSFWState = false;

        public override void WriteSettings()
        {
            base.WriteSettings();

            // 刷新译名缓存显示
            CaressGodHandController.UpdateThoughtDefTranslations();

            // 检查发电机启用状态变化
            if (lastGeneratorEnabledState && !Settings.enableHandCrankGenerator)
            {
                if (Current.ProgramState == ProgramState.Playing)
                    GeneratorCleanupUtility.CleanupAllGenerators(true);
            }

            // 检查永动机启用状态变化
            if (lastEndRodEnabledState != Settings.disableEndRodGenerator)
            {
                if (Settings.disableEndRodGenerator && Current.ProgramState == ProgramState.Playing)
                    EndRodCleanupUtility.CleanupAllEndRods(true);

                RefreshBuildingVisibility();
            }

            // 检查 NSFW 状态变化 (开启禁用)
            if (!lastNSFWState && Settings.disableNSFW)
            {
                if (Current.ProgramState == ProgramState.Playing)
                    CleanupAllNSFWContent();
            }

            lastEndRodEnabledState = Settings.disableEndRodGenerator;
            lastNSFWState = Settings.disableNSFW;
        }

        private void CleanupAllNSFWContent()
        {
            // 静态精准清理
            foreach (var p in CompPawnCaptureOnTouch.allCapturedPawns)
            {
                if (p == null || p.health?.hediffSet == null) continue;
                var h = p.health.hediffSet.GetFirstHediffOfDef(GodHandDefOf.GodHand_OrificeStretchingStatus);
                if (h != null) p.health.RemoveHediff(h);
            }

            // 刷新所有被捕获者的状态
            CompPawnCaptureOnTouch.RefreshAll();
        }

        public static void RefreshBuildingVisibility()
        {
            ThingDef endRodDef = DefDatabase<ThingDef>.GetNamedSilentFail("GodHand_EndRodGenerator");
            if (endRodDef != null)
            {
                // 禁用时从菜单移除
                endRodDef.designationCategory = Settings.disableEndRodGenerator ? null : DefDatabase<DesignationCategoryDef>.GetNamed("Security");
                // 刷新 UI 缓存
                Settings.RefreshAllDesignators();
            }
        }

        public override string SettingsCategory()
        {
            return "GodHand.Label".Translate();
        }
    }
}
