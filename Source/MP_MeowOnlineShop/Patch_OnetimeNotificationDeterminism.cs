using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-524/525/526（2026-08-20，同一会话连续掉线）：
    /// 首个分叉 trace 全部落在原版
    /// `GameComponent_OnetimeNotification.GameComponentTick` —— 即 EPOE/原版的
    /// "人格核心 (AIPersonaCore) 出售信件"组件。两端在同一共享 tick 上，
    /// host 只消费 `Rand.Chance(0.05f)`（失败），local 却进入
    /// `RandomNonHostileFaction + ReceiveLetter`（成功并发信），世界 Rand 流
    /// 错位，最终报 `Wrong random state for the world`。
    ///
    /// 根因：该组件用 `Find.TickManager.TicksGame % 2000` 作为门。async time 下
    /// 地图 tick 会把 `ticksGameInt` 覆盖成各端自己的 mapTicks，因此 `TicksGame`
    /// 是每端私有的；一旦世界 Rand 因任何上游 JobID/命令错位产生漂移，这个组件
    /// 就会在两端走不同分支（发信/不发信），并继续消费/错开世界 Rand。
    ///
    /// 修复：MP 下用 Multiplayer 的共享时钟（TickPatch.Timer，两端对齐）替代
    /// `TicksGame % 2000` 门，并把整个组件体放进 `DeterministicRandScope`
    /// （种子=共享 tick 窗口），使 Chance/RandomNonHostileFaction 全部消费
    /// 确定性的独立随机，不再触碰共享世界 Rand。两端同种子 → 同分支、同信件。
    /// 单机完全不变；MP 符号解析失败时 fail-open 回原版。
    /// </summary>
    internal static class Patch_OnetimeNotificationDeterminism
    {
        private const int OnetimeSeedOffset = 0x4F4E4554; // "ONET"
        private const int OnetimeWorldSeedOffset = 0x4F4E4557; // "ONEW"
        private const int GateIntervalTicks = 2000;

        private static bool _applied;
        private static bool _loggedFailure;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(GameComponent_OnetimeNotification),
                    "GameComponentTick",
                    Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_OnetimeNotificationDeterminism),
                    nameof(GameComponentTickPrefix));

                if (target == null || prefix == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Onetime notification (AIPersonaCore offer) " +
                        "determinism target resolution failed; the offer can still desync.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                Log.Message(
                    "[MP-MeowOnlineShop] Onetime notification (AIPersonaCore offer) " +
                    "determinism active: gated on the shared MP clock and isolated Rand scope.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Onetime notification determinism apply failed: " +
                    e.Message);
            }
        }

        private static bool GameComponentTickPrefix(GameComponent_OnetimeNotification __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            int syncTick;
            if (!MpRuntimeInfo.TryGetMpTimerTick(out syncTick) || syncTick < 0)
            {
                // 拿不到共享时钟时 fail-open 回原版（宁可维持原行为，也不引入端间不一致的种子）。
                LogOnceFailure(
                    "[MP-MeowOnlineShop] Onetime notification determinism: MP timer " +
                    "unavailable, falling back to vanilla tick.");
                return true;
            }

            int state = 0;
            Map mapForPop = null;
            try
            {
                if (syncTick % GateIntervalTicks != 0)
                    return false;

                // 每个 2000-tick 窗口一个确定性种子，替代原版 TicksGame%2000 门 +
                // 共享世界 Rand 的 5% Chance。两端同窗口 → 同结果。
                int window = syncTick / GateIntervalTicks;
                int seed = Gen.HashCombineInt(OnetimeSeedOffset, window);

                if (!DeterministicRandScope.Begin(
                        null,
                        seed,
                        OnetimeWorldSeedOffset,
                        ref state,
                        out mapForPop,
                        ignoreGate: true))
                {
                    LogOnceFailure(
                        "[MP-MeowOnlineShop] Onetime notification determinism: " +
                        "deterministic scope unavailable, falling back to vanilla tick.");
                    return true;
                }

                if (Rand.Chance(0.05f) &&
                    __instance.sendAICoreRequestReminder &&
                    ResearchProjectTagDefOf.ShipRelated.CompletedProjects() >= 2 &&
                    !PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(ThingDefOf.AIPersonaCore) &&
                    !PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(ThingDefOf.Ship_ComputerCore))
                {
                    Faction faction = Find.FactionManager.RandomNonHostileFaction();
                    if (faction != null && faction.leader != null)
                    {
                        Find.LetterStack.ReceiveLetter(
                            "LetterLabelAICoreOffer".Translate(),
                            "LetterAICoreOffer".Translate(
                                faction.leader.LabelDefinite(),
                                faction.NameColored,
                                faction.leader.Named("PAWN")).Resolve().CapitalizeFirst(),
                            LetterDefOf.NeutralEvent,
                            GlobalTargetInfo.Invalid,
                            faction);
                        __instance.sendAICoreRequestReminder = false;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                LogOnceFailure(
                    "[MP-MeowOnlineShop] Onetime notification determinism prefix " +
                    "failed open: " + e.Message);
                return false;
            }
            finally
            {
                if (state != 0)
                    DeterministicRandScope.End(state, mapForPop);
            }
        }

        private static void LogOnceFailure(string message)
        {
            if (_loggedFailure)
                return;
            _loggedFailure = true;
            Log.Warning(message);
        }
    }
}


