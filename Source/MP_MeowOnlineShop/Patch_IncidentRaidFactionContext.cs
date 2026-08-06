using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-245/246/247: a Skyfeather Church / Milira quest raid reaches the
    /// standard IncidentWorker_RaidEnemy.TryExecuteWorker ->
    /// TryGenerateRaidInfo -> TryResolveRaidFaction -> GeneratePawns path.
    /// In a multifaction session TryResolveRaidFaction evaluates
    /// Faction.OfPlayer through UsableFactions/FactionCanBeGroupSource, and
    /// Milira/Milian PawnGenerator postfixes read Find.CurrentMap. A host and a
    /// client can therefore pick a different enemy faction and generate
    /// different raider pawns even though the incident target map is shared.
    ///
    /// Fix: run the whole RaidEnemy execution under the incident target map's
    /// parent player faction and set Find.CurrentMap to that same target map.
    /// This is the same faction-context pattern already proven for caravan
    /// arrivals, applied at the stable raid executor boundary. Singleplayer is
    /// untouched.
    /// </summary>
    internal static class Patch_IncidentRaidFactionContext
    {
        private const string LogTag = "[MP-MeowOnlineShop] IncidentRaidFactionContext";

        private static FieldInfo _ofPlayerField;
        private static FieldInfo _currentMapIndexField;
        private static bool _applied;
        private static bool _loggedActive;

        private sealed class ScopeState
        {
            internal bool FactionActive;
            internal Faction SavedFaction;
            internal bool MapActive;
            internal Map SavedMap;
            internal sbyte SavedMapIndex;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            Type raidEnemyType = AccessTools.TypeByName(
                "RimWorld.IncidentWorker_RaidEnemy");
            MethodInfo target = raidEnemyType == null
                ? null
                : AccessTools.Method(
                    raidEnemyType,
                    "TryExecuteWorker",
                    new[] { typeof(IncidentParms) });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_IncidentRaidFactionContext),
                nameof(TryExecuteWorkerPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_IncidentRaidFactionContext),
                nameof(TryExecuteWorkerFinalizer));

            _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
            _currentMapIndexField = AccessTools.Field(typeof(Game), "currentMapIndex");

            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning(
                    LogTag + " target resolution failed; map raids can still desync.");
                return;
            }

            try
            {
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
                    LogTag + " active: RaidEnemy execution runs under the " +
                    "incident map's player faction and target-map context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " apply failed: " + e.Message);
            }
        }

        private static void TryExecuteWorkerPrefix(
            IncidentParms parms,
            ref ScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || parms?.target == null)
                return;

            Map map = parms.target as Map;
            if (map == null)
                return;

            ScopeState state = new ScopeState();
            __state = state;
            BeginFactionScope(map, state);
            BeginTargetMapScope(map, state);

            if (!_loggedActive && (state.FactionActive || state.MapActive))
            {
                _loggedActive = true;
                Log.Message(
                    LogTag + " executing RaidEnemy with incident-map faction " +
                    "and target-map context: map=" + map.uniqueID + ".");
            }
        }

        private static void BeginFactionScope(Map map, ScopeState state)
        {
            if (_ofPlayerField == null || Find.FactionManager == null)
                return;

            Faction faction = map.ParentFaction;
            if (faction?.def?.isPlayer != true)
                return;

            Faction previous = _ofPlayerField.GetValue(
                Find.FactionManager) as Faction;
            if (ReferenceEquals(previous, faction))
                return;

            _ofPlayerField.SetValue(Find.FactionManager, faction);
            state.FactionActive = true;
            state.SavedFaction = previous;
        }

        private static void BeginTargetMapScope(Map map, ScopeState state)
        {
            if (_currentMapIndexField == null || Current.Game == null ||
                Find.Maps == null)
            {
                return;
            }

            int index = Find.Maps.IndexOf(map);
            if (index < 0 || index > sbyte.MaxValue)
                return;

            sbyte previous = (sbyte)_currentMapIndexField.GetValue(Current.Game);
            sbyte next = (sbyte)index;
            if (previous == next)
                return;

            _currentMapIndexField.SetValue(Current.Game, next);
            state.MapActive = true;
            state.SavedMap = map;
            state.SavedMapIndex = previous;
        }

        private static Exception TryExecuteWorkerFinalizer(
            Exception __exception,
            ScopeState __state)
        {
            if (__state == null)
                return __exception;

            try
            {
                if (__state.MapActive && _currentMapIndexField != null &&
                    Current.Game != null)
                {
                    sbyte restoreIndex = __state.SavedMapIndex;
                    if (__state.SavedMap != null && Find.Maps != null)
                    {
                        int currentIndex = Find.Maps.IndexOf(__state.SavedMap);
                        if (currentIndex >= 0 && currentIndex <= sbyte.MaxValue)
                            restoreIndex = (sbyte)currentIndex;
                    }
                    _currentMapIndexField.SetValue(Current.Game, restoreIndex);
                }

                if (__state.FactionActive && _ofPlayerField != null &&
                    Find.FactionManager != null)
                {
                    _ofPlayerField.SetValue(
                        Find.FactionManager,
                        __state.SavedFaction);
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " context restore failed: " + e.Message);
            }

            return __exception;
        }
    }
}
