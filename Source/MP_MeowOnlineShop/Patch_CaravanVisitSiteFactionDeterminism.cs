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
    /// Desync-141/142: the caravan-arrival synchronous LongEvent for
    /// `CaravanArrivalAction_VisitSite.DoEnter` runs outside the tick/command
    /// boundary on at least one peer, so `Faction.OfPlayer` can be the
    /// spectator faction there while the other peer still sees the real player
    /// faction. `DoEnter` derives `draftColonists` from
    /// `site.Faction.HostileTo(Faction.OfPlayer)`, and the arrival letter /
    /// relations / caravan entry all follow that context.
    ///
    /// With divergent `Faction.OfPlayer`, one peer drafts the entering
    /// colonists and the other does not. The very first map tick then takes a
    /// different think-tree path for the same pawn:
    /// `JobGiver_Orders` (drafted) vs `JobGiver_OptimizeApparel` (not
    /// drafted), consuming a different Rand/job-ID sequence and desyncing the
    /// site map (Desync-141 trace record 1669).
    ///
    /// Fix: in multiplayer, wrap the whole `DoEnter` body in the caravan's
    /// player-faction context. Both peers then evaluate the same faction, the
    /// same `draftColonists`, and the same site arrival side effects.
    /// Singleplayer is untouched; failures fail open.
    /// </summary>
    internal static class Patch_CaravanVisitSiteFactionDeterminism
    {
        private static bool _applied;
        private static bool _loggedActive;
        private static bool _loggedFailure;
        private static FieldInfo _ofPlayerField;

        private sealed class VisitSiteScopeState
        {
            internal bool Active;
            internal Faction SavedFaction;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(CaravanArrivalAction_VisitSite),
                    "DoEnter",
                    new[] { typeof(Caravan), typeof(Site) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_CaravanVisitSiteFactionDeterminism),
                    nameof(DoEnterPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_CaravanVisitSiteFactionDeterminism),
                    nameof(DoEnterFinalizer));

                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");

                if (target == null || prefix == null || finalizer == null ||
                    _ofPlayerField == null ||
                    _ofPlayerField.FieldType != typeof(Faction))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Caravan visit-site faction " +
                        "determinism target resolution failed; the site map " +
                        "arrival can still desync.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(finalizer)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Caravan visit-site faction " +
                    "determinism active: DoEnter runs under the caravan's " +
                    "player-faction context on every peer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan visit-site faction " +
                    "determinism apply failed: " + e.Message);
            }
        }

        private static void DoEnterPrefix(
            Caravan caravan,
            ref VisitSiteScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || caravan == null)
                return;

            Faction faction = caravan.Faction;
            FactionManager factionManager = Find.FactionManager;
            if (faction?.def?.isPlayer != true || factionManager == null ||
                _ofPlayerField == null)
            {
                return;
            }

            try
            {
                Faction previous = _ofPlayerField.GetValue(
                    factionManager) as Faction;
                if (ReferenceEquals(previous, faction))
                    return;

                _ofPlayerField.SetValue(factionManager, faction);
                __state = new VisitSiteScopeState
                {
                    Active = true,
                    SavedFaction = previous
                };

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Caravan visit-site DoEnter " +
                        $"faction context: caravanFaction={faction.Name}, " +
                        "draft/letter/relations decisions now peer-identical.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Caravan visit-site faction " +
                        "context prefix failed open: " + e.Message);
                }
            }
        }

        private static Exception DoEnterFinalizer(
            Exception __exception,
            VisitSiteScopeState __state)
        {
            if (__state?.Active == true && _ofPlayerField != null)
            {
                try
                {
                    FactionManager factionManager = Find.FactionManager;
                    if (factionManager != null)
                        _ofPlayerField.SetValue(
                            factionManager,
                            __state.SavedFaction);
                }
                catch (Exception e)
                {
                    if (!_loggedFailure)
                    {
                        _loggedFailure = true;
                        Log.Warning(
                            "[MP-MeowOnlineShop] Caravan visit-site faction " +
                            "context restore failed: " + e.Message);
                    }
                }
            }

            return __exception;
        }
    }
}
