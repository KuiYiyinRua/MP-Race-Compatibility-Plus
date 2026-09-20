using System;
using System.Collections.Generic;
using Verse;

namespace MP_MeowOnlineShop
{
    // Capture once, before any patch registration. Never read mutable UI settings
    // from a running patch: Harmony and Multiplayer registrations require restart.
    public static class CompatibilityPatchCategories
    {
        private static HashSet<string> disabledAtStartup;
        private static bool enabledAtStartup;

        private static readonly string[] Ids =
        {
            "core", "transport", "multifaction", "performance", "visual", "gameplay",
            "meow", "milira", "axolotl", "rjw", "light350", "ratkin", "wolfein",
            "nivarian", "raven", "races", "melee"
        };

        private static readonly string[] Labels =
        {
            "基础稳定性与存档修复", "商队、运输与奥德赛飞船", "多派系、任务与事件",
            "性能优化与调度", "视听效果与随机隔离", "通用玩法、建筑与操作",
            "喵呜网店与通信台", "米莉拉、米莉安与 Ancot 系列", "墨萝与相关扩展",
            "RJW 与相关扩展", "Light350 与秘书系统", "鼠族、地下鼠与异常扩展",
            "沃芬与相关扩展", "涅瓦莲与相关扩展", "渡鸦与服装缓存",
            "其他种族与扩展", "近战动画"
        };

        internal static void Capture(MpMeowOnlineShopSettings settings)
        {
            if (disabledAtStartup != null) return;
            enabledAtStartup = settings.compatibilityPatchesEnabled;
            disabledAtStartup = new HashSet<string>(settings.disabledCompatibilityCategories ?? new List<string>(), StringComparer.Ordinal);
            Log.Message("[MP-MeowOnlineShop] Compatibility categories at startup: master=" +
                enabledAtStartup + "; disabled=" + string.Join(",", settings.disabledCompatibilityCategories ?? new List<string>()));
        }

        public static bool IsEnabled(string category)
        {
            if (Array.IndexOf(Ids, category) < 0)
                throw new ArgumentException("Unknown compatibility category: " + category);
            if (disabledAtStartup == null)
                throw new InvalidOperationException("Compatibility settings must be captured before patch initialization.");
            return enabledAtStartup && !disabledAtStartup.Contains(category);
        }

        internal static void Apply(string category, Action install)
        {
            if (IsEnabled(category)) install();
        }

        internal static void DrawSettings(Listing_Standard listing, MpMeowOnlineShopSettings settings)
        {
            listing.Label("联机兼容补丁开关（重启游戏后生效）");
            listing.Label("默认全部开启。关闭大类会跳过该类补丁的安装与同步注册；不关闭原模组。主客机必须使用相同开关并全部重启，再开房、加入或观看重放。关闭兼容补丁可能恢复原有失步问题。");
            listing.CheckboxLabeled("启用本 mod 的联机兼容补丁（总开关）", ref settings.compatibilityPatchesEnabled);
            listing.Label("总开关关闭时，各分类选择仍会保留。下方优化预设不会修改这些分类开关。");
            if (listing.ButtonText("恢复所有联机补丁为开启"))
            {
                settings.compatibilityPatchesEnabled = true;
                settings.disabledCompatibilityCategories.Clear();
            }
            bool changed = settings.compatibilityPatchesEnabled != enabledAtStartup;
            for (int i = 0; i < Ids.Length; i++)
            {
                string id = Ids[i];
                bool enabled = !settings.disabledCompatibilityCategories.Contains(id);
                listing.CheckboxLabeled(Labels[i] + "（本次启动：" + (IsEnabled(id) ? "开启" : "关闭") + "）", ref enabled);
                settings.disabledCompatibilityCategories.RemoveAll(value => value == id);
                if (!enabled) settings.disabledCompatibilityCategories.Add(id);
                changed |= enabled == disabledAtStartup.Contains(id);
            }
            if (changed) listing.Label("设置已更改：重启游戏后生效，当前已安装的补丁保持本次启动状态。");
            listing.GapLine();
        }
    }
}
