using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.RavenIndustryOptimization
{
    public sealed class IndustryPreferences : ModSettings
    {
        public bool enableForNewGames;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableForNewGames, "ravenIndustryMpForNewGames", false);
        }
    }

    public sealed class IndustryMod : Mod
    {
        internal static IndustryMod Instance;
        internal static IndustryPreferences Preferences;
        public IndustryMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Preferences = GetSettings<IndustryPreferences>();
        }
    }

    [StaticConstructorOnStartup]
    internal static class IndustryStartup
    {
        static IndustryStartup() => LongEventHandler.ExecuteWhenFinished(IndustryBootstrap.Install);
    }

    // The choice is a game property, never a per-client simulation preference.
    public sealed class IndustryGameState : GameComponent
    {
        internal static IndustryGameState CurrentState;
        internal readonly Game Owner;
        public bool Enabled;

        public IndustryGameState(Game game)
        {
            Owner = game;
            CurrentState = this;
        }

        internal static IndustryGameState ForCurrentGame =>
            CurrentState != null && ReferenceEquals(CurrentState.Owner, Current.Game)
                ? CurrentState : Current.Game?.GetComponent<IndustryGameState>();

        public override void StartedNewGame()
        {
            Enabled = IndustryMod.Preferences?.enableForNewGames ?? false;
            CurrentState = this;
        }

        public override void ExposeData()
        {
            // Missing settings in an old save have the SAME false value on all peers.
            // Never import the joining computer's local preference here.
            Scribe_Values.Look(ref Enabled, "ravenIndustryMpEnabled", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars) CurrentState = this;
        }

        public override void LoadedGame() => CurrentState = this;
    }

    internal static class IndustrySettings
    {
        internal static bool Active => IndustryBootstrap.Ready && MP.IsInMultiplayer &&
            IndustryGameState.ForCurrentGame?.Enabled == true;

        internal static void Install(Harmony harmony)
        {
            if (MP.enabled) MP.RegisterSyncMethod(typeof(IndustrySettings), nameof(SetEnabled)).SetHostOnly();
            var draw = AccessTools.DeclaredMethod(
                AccessTools.TypeByName("MP_MeowOnlineShop.CompatibilityPatchCategories"), "DrawSettings");
            if (draw == null) throw new System.MissingMethodException("Meow settings entry");
            harmony.Patch(draw, postfix: new HarmonyMethod(typeof(IndustrySettings), nameof(Draw)));
        }

        // A world sync command can change the option without advancing any map.
        // Flush every map's exact current coordinates BEFORE disabling the fast path.
        internal static void SetEnabled(bool enabled)
        {
            var state = IndustryGameState.ForCurrentGame;
            if (state == null || state.Enabled == enabled) return;
            ConveyorMotion.FlushAll();
            CapacityIndex.Clear();
            state.Enabled = enabled;
        }

        private static void Draw(Listing_Standard listing)
        {
            listing.GapLine();
            listing.Label("渡鸦工业流水线优化（仅联机）");
            if (!IndustryBootstrap.Ready)
                listing.Label("当前不可用：" + IndustryBootstrap.Status);

            var state = IndustryGameState.ForCurrentGame;
            bool value = state != null ? state.Enabled : IndustryMod.Preferences.enableForNewGames;
            bool before = value;
            bool gui = GUI.enabled;
            try
            {
                GUI.enabled = gui && IndustryBootstrap.Ready && (!MP.IsInMultiplayer || MP.IsHosting);
                listing.CheckboxLabeled(state == null ? "为新游戏启用渡鸦工业联机优化" : "启用本存档的渡鸦工业联机优化",
                    ref value, "独立于其他优化预设。单人模式不执行优化。联机中由房主同步切换，开关随存档保存；关闭时恢复原运输路径。默认关闭。");
            }
            finally { GUI.enabled = gui; }
            if (before != value)
            {
                if (state != null) SetEnabled(value);
                else
                {
                    IndustryMod.Preferences.enableForNewGames = value;
                    IndustryMod.Preferences.Write();
                }
            }
            listing.Label(MP.IsInMultiplayer
                ? "房主切换后由同步命令生效；客户端自动跟随。"
                : "单人模式保持原算法；存档选项在进入联机后生效。");
            listing.Label("优化容量查询、无效调度事件、普通传送带整段推进及堵塞移动；保留生产时刻、分流、换料和液体周期。尚未进行游戏内验证。");
            listing.Gap(8f);
        }
    }
}
