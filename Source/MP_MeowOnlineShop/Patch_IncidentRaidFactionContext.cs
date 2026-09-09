using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Map raids, including IncidentWorker_RaidFriendly aid raids, reach the
    /// standard IncidentWorker_Raid.TryExecuteWorker -> TryGenerateRaidInfo ->
    /// TryResolveRaidFaction -> GeneratePawns path. In a multifaction session
    /// TryResolveRaidFaction evaluates Faction.OfPlayer through
    /// UsableFactions/FactionCanBeGroupSource, and custom PawnGenerator
    /// postfixes read Find.CurrentMap. A host and a client can therefore pick a
    /// different faction or generate different pawns even though the incident
    /// target map is shared.
    ///
    /// Fix: patch the base IncidentWorker_Raid boundary (which covers the
    /// vanilla RaidFriendly inheritance path) and every declared raid-worker
    /// override under the incident target map's parent player faction, while
    /// setting Find.CurrentMap to that same target map. A failed optional
    /// derived worker must not prevent the base and other workers from being
    /// patched. Singleplayer is untouched.
    /// </summary>
    internal static class Patch_IncidentRaidFactionContext
    {
        private const string LogTag = "[MP-MeowOnlineShop] IncidentRaidFactionContext";

        private static FieldInfo _ofPlayerField;
        private static FieldInfo _currentMapIndexField;
        private static MethodInfo _pushFactionMethod;
        private static MethodInfo _popFactionMethod;
        private static bool _applied;
        private static bool _loggedFactionApiFailure;
        private static readonly HashSet<string> LoggedWorkers =
            new HashSet<string>();

        private sealed class ScopeState
        {
            internal bool FactionActive;
            internal Faction SavedFaction;
            internal bool FactionContextActive;
            internal Map FactionContextMap;
            internal bool MapActive;
            internal Map SavedMap;
            internal sbyte SavedMapIndex;
            internal bool EliteRaidScopeActive;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            Type raidType = AccessTools.TypeByName(
                "RimWorld.IncidentWorker_Raid");
            Type raidFriendlyType = AccessTools.TypeByName(
                "RimWorld.IncidentWorker_RaidFriendly");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_IncidentRaidFactionContext),
                nameof(TryExecuteWorkerPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_IncidentRaidFactionContext),
                nameof(TryExecuteWorkerFinalizer));

            _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
            _currentMapIndexField = AccessTools.Field(typeof(Game), "currentMapIndex");
            Type factionExtensionsType = AccessTools.TypeByName(
                "Multiplayer.Client.Factions.FactionExtensions");
            _pushFactionMethod = factionExtensionsType == null
                ? null
                : AccessTools.Method(
                    factionExtensionsType,
                    "PushFaction",
                    new[] { typeof(Map), typeof(Faction), typeof(bool) });
            _popFactionMethod = factionExtensionsType == null
                ? null
                : AccessTools.Method(
                    factionExtensionsType,
                    "PopFaction",
                    new[] { typeof(Map) });

            if (raidType == null || prefix == null || finalizer == null)
            {
                Log.Warning(
                    LogTag + " target resolution failed; map raids can still desync.");
                return;
            }

            try
            {
                var patchedMethods = new HashSet<RuntimeMethodHandle>();
                var workerTypes = new List<Type> { raidType };
                var discoveredTypes = new List<Type>();

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    if (types == null)
                        continue;

                    foreach (Type workerType in types)
                    {
                        if (workerType == null || workerType == raidType ||
                            workerType.IsAbstract ||
                            !raidType.IsAssignableFrom(workerType))
                        {
                            continue;
                        }
                        discoveredTypes.Add(workerType);
                    }
                }

                discoveredTypes.Sort((left, right) => string.Compare(
                    left.FullName, right.FullName, StringComparison.Ordinal));
                workerTypes.AddRange(discoveredTypes);

                int patchedCount = 0;
                int derivedCount = 0;
                int failedCount = 0;
                var derivedNames = new List<string>();
                bool basePatched = false;
                bool friendlyPatched = false;

                foreach (Type workerType in workerTypes)
                {
                    MethodInfo target = AccessTools.DeclaredMethod(
                        workerType,
                        "TryExecuteWorker",
                        new[] { typeof(IncidentParms) });
                    if (target == null ||
                        patchedMethods.Contains(target.MethodHandle))
                    {
                        if (workerType == raidFriendlyType && target == null)
                            friendlyPatched = basePatched;
                        continue;
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
                        patchedMethods.Add(target.MethodHandle);
                        patchedCount++;

                        if (workerType == raidType)
                            basePatched = true;
                        else
                        {
                            derivedCount++;
                            if (derivedNames.Count < 8)
                                derivedNames.Add(workerType.FullName);
                        }

                        if (workerType == raidFriendlyType)
                            friendlyPatched = true;
                    }
                    catch (Exception e)
                    {
                        failedCount++;
                        Log.Warning(
                            LogTag + " patch failed on " + workerType.FullName +
                            ": " + e.Message);
                    }
                }

                if (patchedCount == 0)
                {
                    Log.Warning(
                        LogTag + " found no Raid TryExecuteWorker methods; " +
                        "map raids can still desync.");
                    return;
                }

                if (raidFriendlyType != null && !friendlyPatched)
                {
                    MethodInfo friendlyDeclared = AccessTools.DeclaredMethod(
                        raidFriendlyType,
                        "TryExecuteWorker",
                        new[] { typeof(IncidentParms) });
                    if (friendlyDeclared == null)
                        friendlyPatched = basePatched;
                }

                Log.Message(
                    LogTag + " active: patched=" + patchedCount +
                    ", derived=" + derivedCount +
                    ", failed=" + failedCount +
                    ", raidBase=" + basePatched +
                    ", raidFriendly=" + friendlyPatched +
                    ", factionContextApi=" +
                    (_pushFactionMethod != null && _popFactionMethod != null) +
                    ", derivedExamples=" + string.Join(",", derivedNames) +
                    "; Raid execution runs under the incident map's player faction " +
                    "and target-map context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " apply failed: " + e.Message);
            }
        }

        private static void TryExecuteWorkerPrefix(
            IncidentParms parms,
            MethodBase __originalMethod,
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
            state.EliteRaidScopeActive =
                Patch_EliteRaidDeterminism.BeginRaidCompressionScope();
            BeginFactionScope(map, state);
            BeginTargetMapScope(map, state);

            if (state.FactionActive || state.FactionContextActive ||
                state.MapActive || state.EliteRaidScopeActive)
            {
                string workerName = __originalMethod?.DeclaringType?.FullName ??
                    "<unknown>";
                if (LoggedWorkers.Add(workerName))
                {
                    string playerFaction = map.ParentFaction?.def?.defName ??
                        "<none>";
                    string incidentFaction = parms.faction?.def?.defName ??
                        "<pending>";
                    string strategy = parms.raidStrategy?.defName ?? "<pending>";
                    string arrival = parms.raidArrivalMode?.defName ?? "<pending>";
                    Log.Message(
                        LogTag + " executing worker=" + workerName +
                        ", map=" + map.uniqueID +
                        ", playerFaction=" + playerFaction +
                        ", incidentFaction=" + incidentFaction +
                        ", strategy=" + strategy +
                        ", arrival=" + arrival +
                        ", points=" + parms.points + ".");
                }
            }
        }

        private static void BeginFactionScope(Map map, ScopeState state)
        {
            Faction faction = map.ParentFaction;
            if (faction?.def?.isPlayer != true)
                return;

            if (_pushFactionMethod != null && _popFactionMethod != null)
            {
                try
                {
                    // Use Multiplayer's stack-aware API. The old direct field
                    // write updates only FactionManager.ofPlayer and leaves
                    // WorldComp/MapComp out of sync for incidents that execute
                    // outside AsyncTimeComp.PreContext.
                    _pushFactionMethod.Invoke(
                        null,
                        new object[] { map, faction, true });
                    state.FactionContextActive = true;
                    state.FactionContextMap = map;
                    return;
                }
                catch (Exception e)
                {
                    if (!_loggedFactionApiFailure)
                    {
                        _loggedFactionApiFailure = true;
                        Log.Warning(
                            LogTag + " Multiplayer faction context API failed; " +
                            "falling back to FactionManager.ofPlayer: " + e.Message);
                    }
                }
            }

            if (_ofPlayerField == null || Find.FactionManager == null)
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
                if (__state.EliteRaidScopeActive)
                    Patch_EliteRaidDeterminism.EndRaidCompressionScope();
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

                if (__state.FactionContextActive &&
                    _popFactionMethod != null)
                {
                    _popFactionMethod.Invoke(
                        null,
                        new object[] { __state.FactionContextMap });
                }
                else if (__state.FactionActive && _ofPlayerField != null &&
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
