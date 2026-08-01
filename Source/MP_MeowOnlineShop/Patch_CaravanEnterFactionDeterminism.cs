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
    /// Desync-153/154/155/156: when a caravan arrives at an existing colony /
    /// map parent, `CaravanArrivalAction_Enter.Arrived` decides
    /// `dropInventoryMode` from `map.IsPlayerHome`, `draftColonists` from
    /// `mapParent.Faction.HostileTo(Faction.OfPlayer)`, and whether to send the
    /// arrival letter from `mapParent.Faction == Faction.OfPlayer`. In
    /// multifaction/async sessions those player-relative checks can evaluate
    /// differently on the two peers, so the entering pawns register in
    /// different map TickLists and the map/world Rand streams drift.
    ///
    /// The 153-156 bundles are one repeated session: the client joins map 12
    /// with 94 pending commands, immediately desyncs (`last valid tick -1`),
    /// and every rejoin desyncs again shortly after the caravan-arrival state.
    /// Fix: in multiplayer, run `CaravanArrivalAction_Enter.Arrived` under the
    /// caravan's player-faction context, so `IsPlayerHome`/draft/letter/enter
    /// decisions are peer-identical. Singleplayer is untouched.
    /// </summary>
    internal static class Patch_CaravanEnterFactionDeterminism
    {
        private static bool _applied;
        private static bool _loggedActive;
        private static bool _loggedFailure;
        private static FieldInfo _ofPlayerField;

        private sealed class EnterScopeState
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
                    typeof(CaravanArrivalAction_Enter),
                    "Arrived",
                    new[] { typeof(Caravan) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_CaravanEnterFactionDeterminism),
                    nameof(ArrivedPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_CaravanEnterFactionDeterminism),
                    nameof(ArrivedFinalizer));
                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");

                if (target == null || prefix == null || finalizer == null ||
                    _ofPlayerField == null ||
                    _ofPlayerField.FieldType != typeof(Faction))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Caravan enter faction determinism " +
                        "target resolution failed; colony arrivals can still desync.");
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
                    "[MP-MeowOnlineShop] Caravan enter faction determinism active: " +
                    "Arrived runs under the caravan's player-faction context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan enter faction determinism apply " +
                    "failed: " + e.Message);
            }
        }

        private static void ArrivedPrefix(
            Caravan caravan,
            ref EnterScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || caravan == null)
                return;

            try
            {
                Faction faction = caravan.Faction;
                FactionManager factionManager = Find.FactionManager;
                if (faction?.def?.isPlayer != true || factionManager == null ||
                    _ofPlayerField == null)
                {
                    return;
                }

                Faction previous = _ofPlayerField.GetValue(
                    factionManager) as Faction;
                if (ReferenceEquals(previous, faction))
                    return;

                _ofPlayerField.SetValue(factionManager, faction);
                __state = new EnterScopeState
                {
                    Active = true,
                    SavedFaction = previous
                };

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Caravan enter Arrived faction context: " +
                        $"caravanFaction={faction.Name}, drop/draft/letter decisions " +
                        "now peer-identical.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Caravan enter faction context prefix " +
                        "failed open: " + e.Message);
                }
            }
        }

        private static Exception ArrivedFinalizer(
            Exception __exception,
            EnterScopeState __state)
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
                            "[MP-MeowOnlineShop] Caravan enter faction context " +
                            "restore failed: " + e.Message);
                    }
                }
            }

            return __exception;
        }
    }
}
