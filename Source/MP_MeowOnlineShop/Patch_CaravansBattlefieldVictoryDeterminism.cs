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
    /// Desync-128: the moment the ambush battle is won,
    /// `CaravansBattlefield.CheckWonBattle` calls
    /// `base.Map.mapPawns.FreeColonists.RandomElement()` and
    /// `TaleRecorder.RecordTale(CaravanAmbushDefeated, ...)`.
    ///
    /// The client's `FreeColonists` list is empty at the victory tick
    /// ("Getting random element from empty collection"), so RandomElement
    /// returns null without consuming Rand and
    /// `TaleData_Pawn.GenerateFrom(null)` throws NRE. The host's list is
    /// non-empty, so it consumes one world Rand draw and records the tale.
    /// The tale path also consumes Rand inside `TaleRecorder.RecordTale`
    /// (ignoreChance draws), so only one peer advances the world stream.
    ///
    /// Fix: in multiplayer, replace `CheckWonBattle` with a deterministic
    /// settlement: notify the detection comp, send the victory letter, mark
    /// `wonBattle`, and skip the `RandomElement`/`RecordTale` path entirely.
    /// The tale is cosmetic history; suppressing it removes the per-peer
    /// pawn-list dependence and the Rand/ID divergence. Singleplayer is
    /// unchanged.
    /// </summary>
    internal static class Patch_CaravansBattlefieldVictoryDeterminism
    {
        private static FieldInfo _wonBattleField;
        private static bool _applied;
        private static bool _loggedActive;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type battlefieldType = AccessTools.TypeByName(
                    "RimWorld.Planet.CaravansBattlefield");
                MethodInfo checkWonBattle = battlefieldType?.GetMethod(
                    "CheckWonBattle",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_CaravansBattlefieldVictoryDeterminism),
                    nameof(CheckWonBattlePrefix));

                _wonBattleField = battlefieldType?.GetField(
                    "wonBattle",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (checkWonBattle == null || prefix == null ||
                    _wonBattleField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] CaravansBattlefield victory determinism " +
                        "target resolution failed; tale/Random path can desync.");
                    return;
                }

                harmony.Patch(
                    checkWonBattle,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] CaravansBattlefield victory settlement is " +
                    "deterministic in multiplayer (tale/RandomElement path removed).");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] CaravansBattlefield victory determinism failed: " +
                    e.Message);
            }
        }

        private static bool CheckWonBattlePrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            CaravansBattlefield battlefield = __instance as CaravansBattlefield;
            Map map = battlefield?.Map;
            if (battlefield == null || map == null)
                return false;

            if ((bool)_wonBattleField.GetValue(battlefield) ||
                GenHostility.AnyHostileActiveThreatToPlayer(map))
            {
                return false;
            }

            try
            {
                TimedDetectionRaids detection =
                    battlefield.GetComponent<TimedDetectionRaids>();
                if (detection != null)
                {
                    detection.SetNotifiedSilently();
                    Find.LetterStack.ReceiveLetter(
                        "LetterLabelCaravansBattlefieldVictory".Translate(),
                        "LetterCaravansBattlefieldVictory".Translate(
                            detection.DetectionCountdownTimeLeftString),
                        LetterDefOf.PositiveEvent,
                        battlefield);
                }

                _wonBattleField.SetValue(battlefield, true);

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] CaravansBattlefield victory settled " +
                        "deterministically (no RandomElement/tale draw).");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] CaravansBattlefield victory replacement " +
                    $"failed: {e.Message}");
            }

            return false;
        }
    }
}
