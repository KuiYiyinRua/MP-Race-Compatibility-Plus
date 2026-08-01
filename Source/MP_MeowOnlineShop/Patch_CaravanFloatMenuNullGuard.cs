using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-129: replaying a synced `WorldObject.GetFloatMenuOptions` command
    /// on the client threw
    /// `NullReferenceException` in
    /// `CaravanVisitUtility.SettlementVisitedNow` because the command's
    /// `Caravan` argument resolved to null. `SyncAction.Handle` regenerates the
    /// full settlement menu before matching the clicked option, so a null
    /// caravan aborts the whole world command on that peer only. The other peer
    /// then starts the caravan trade alone and the world desyncs 44-60 ticks
    /// later (`Wrong random state for the world`).
    ///
    /// This guard short-circuits the two vanilla call sites that dereference
    /// the caravan during that regeneration:
    /// - `Settlement.GetFloatMenuOptions(Caravan)` returns an empty menu;
    /// - `CaravanVisitUtility.SettlementVisitedNow(Caravan)` returns null.
    ///
    /// When a null caravan is seen inside a synced command, one bounded log
    /// records the resolution state so the next bundle can prove whether the
    /// command was sent with a null caravan or the caravan is missing on one
    /// peer after a cold join. Singleplayer behavior is unchanged.
    /// </summary>
    internal static class Patch_CaravanFloatMenuNullGuard
    {
        private static bool _applied;
        private static bool _loggedReplayGuard;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                int patched = 0;

                MethodInfo settlementMenu = AccessTools.Method(
                    typeof(Settlement),
                    nameof(Settlement.GetFloatMenuOptions),
                    new[] { typeof(Caravan) });
                MethodInfo settlementPrefix = AccessTools.Method(
                    typeof(Patch_CaravanFloatMenuNullGuard),
                    nameof(SettlementFloatMenuPrefix));
                if (settlementMenu != null && settlementPrefix != null)
                {
                    harmony.Patch(
                        settlementMenu,
                        prefix: new HarmonyMethod(settlementPrefix)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                MethodInfo visitedNow = AccessTools.Method(
                    typeof(CaravanVisitUtility),
                    nameof(CaravanVisitUtility.SettlementVisitedNow),
                    new[] { typeof(Caravan) });
                MethodInfo visitedPrefix = AccessTools.Method(
                    typeof(Patch_CaravanFloatMenuNullGuard),
                    nameof(SettlementVisitedPrefix));
                if (visitedNow != null && visitedPrefix != null)
                {
                    harmony.Patch(
                        visitedNow,
                        prefix: new HarmonyMethod(visitedPrefix)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Caravan float-menu null-caravan guard " +
                        "target resolution failed; a synced float-menu command can " +
                        "still NRE during replay.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Caravan float-menu null-caravan guard active " +
                    $"(targets patched={patched}).");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan float-menu null-caravan guard failed: " +
                    e.Message);
            }
        }

        private static bool SettlementFloatMenuPrefix(
            Caravan caravan,
            ref IEnumerable<FloatMenuOption> __result)
        {
            if (caravan != null)
                return true;

            LogReplayGuardOnce("Settlement.GetFloatMenuOptions");
            __result = Enumerable.Empty<FloatMenuOption>();
            return false;
        }

        private static bool SettlementVisitedPrefix(
            Caravan caravan,
            ref Settlement __result)
        {
            if (caravan != null)
                return true;

            LogReplayGuardOnce("CaravanVisitUtility.SettlementVisitedNow");
            __result = null;
            return false;
        }

        private static void LogReplayGuardOnce(string source)
        {
            if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand ||
                _loggedReplayGuard)
            {
                return;
            }
            _loggedReplayGuard = true;

            string worldObjectCount = "?";
            string caravanCount = "?";
            string tick = "?";
            try
            {
                tick = Find.TickManager?.TicksGame.ToString() ?? "?";
                WorldObjectsHolder holder = Find.World?.worldObjects;
                if (holder != null)
                {
                    worldObjectCount = holder.AllWorldObjects.Count.ToString();
                    caravanCount = holder.Caravans.Count.ToString();
                }
            }
            catch
            {
                // Diagnostic only; never let this affect command replay.
            }

            Log.Warning(
                "[MP-MeowOnlineShop][CaravanMenuGuard] synced caravan float-menu " +
                $"command skipped: caravan resolved to null during replay; source={source}; " +
                $"tick={tick}; worldObjects={worldObjectCount}; caravans={caravanCount}. " +
                "Capture host and client logs for this tick to confirm whether the command " +
                "was sent with a null caravan or one peer is missing the caravan.");
        }
    }
}
