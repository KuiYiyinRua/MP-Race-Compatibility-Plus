using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Repairs a loaded multifaction save when it is hosted as a single-faction
    /// game. HostUtil.SetupGameFromSingleplayer creates MP faction records for
    /// every old player faction, but the single-faction path only changes the
    /// player identity. That leaves old map managers and owned objects alive.
    ///
    /// The most visible failure is AreaSource.DataForArea: a queued path request
    /// still points at an Area from an old faction manager while the active map
    /// manager only contains the host faction's areas.
    /// </summary>
    internal static class Patch_MultifactionSingleFactionMigration
    {
        private const string HarmonyId = "mp.meowonlineshop.singlefactionmigration";
        private const int PeriodicCheckIntervalTicks = 1000;

        private static readonly Type MultiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
        private static readonly Type WorldDataType = AccessTools.TypeByName("Multiplayer.Client.FactionWorldData");

        private static bool applied;
        private static int nextPeriodicCheckTick = -1;
        private static bool automaticPatchLogged;
        private static bool postReloadMigrationLogged;
        private static bool resolutionLogged;
        private static bool runtimeFactionStateLogged;

        public static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled)
                return;

            MethodInfo setGameState = AccessTools.Method(
                AccessTools.TypeByName("Multiplayer.Client.HostUtil"),
                "SetGameState");
            MethodInfo saveAndReload = AccessTools.Method(
                AccessTools.TypeByName("Multiplayer.Client.SaveLoad"),
                "SaveAndReload",
                Type.EmptyTypes);

            if (setGameState == null && saveAndReload == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Single-faction migration disabled: " +
                    "neither HostUtil.SetGameState nor SaveLoad.SaveAndReload was found.");
                return;
            }

            bool setGameStatePatched = false;
            if (setGameState != null)
            {
                try
                {
                    harmony.Patch(
                        setGameState,
                        postfix: new HarmonyMethod(
                            typeof(Patch_MultifactionSingleFactionMigration),
                            nameof(SetGameStatePostfix)));
                    setGameStatePatched = true;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] SetGameState Harmony registration failed: " +
                        exception.Message);
                }
            }

            bool saveAndReloadPatched = false;
            if (saveAndReload != null)
            {
                try
                {
                    harmony.Patch(
                        saveAndReload,
                        postfix: new HarmonyMethod(
                            typeof(Patch_MultifactionSingleFactionMigration),
                            nameof(SaveAndReloadPostfix)));
                    saveAndReloadPatched = true;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] SaveAndReload Harmony registration failed: " +
                        exception.Message);
                }
            }

            applied = setGameStatePatched || saveAndReloadPatched;
            if (!applied)
                return;

            Log.Message(
                "[MP-MeowOnlineShop] Single-faction migration active: " +
                "post-reload conversion=" + saveAndReloadPatched +
                ", periodic stale-faction repair=true.");
        }

        private static void SetGameStatePostfix(object settings)
        {
            try
            {
                FieldInfo multifactionField = settings == null
                    ? null
                    : AccessTools.Field(settings.GetType(), "multifaction");
                if (multifactionField == null || !(multifactionField.GetValue(settings) is bool multifaction) || multifaction)
                    return;

                if (!automaticPatchLogged)
                {
                    automaticPatchLogged = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Single-faction host conversion detected; " +
                        "faction repair deferred until SaveAndReload has rebuilt the game state.");
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MP-MeowOnlineShop] Single-faction host snapshot migration failed: " +
                    exception);
            }
        }

        private static void SaveAndReloadPostfix()
        {
            try
            {
                if (!MP.IsInMultiplayer ||
                    !MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) ||
                    multifaction)
                {
                    return;
                }

                Faction hostFaction = GetHostFaction();
                LogRuntimeFactionState(hostFaction, "post SaveAndReload");
                MigrationReport report = MigrateAllPlayerFactionsToHost("post SaveAndReload");
                if (!postReloadMigrationLogged || report.AnyChanges)
                {
                    postReloadMigrationLogged = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Single-faction post-reload migration completed: " +
                        report.ToLogString());
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MP-MeowOnlineShop] Single-faction post-reload migration failed: " +
                    exception);
            }
        }

        /// <summary>
        /// Runs on an old loaded game as a deterministic, idempotent safety net.
        /// It intentionally runs on every peer in a lockstep session: the scan
        /// has no random input and every mutation is performed in stable order.
        /// </summary>
        public sealed class SingleFactionMigrationComponent : GameComponent
        {
            public SingleFactionMigrationComponent(Game game)
            {
            }

            public override void GameComponentTick()
            {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("multifaction")) return;
                if (!MP.IsInMultiplayer ||
                    !MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) ||
                    multifaction ||
                    Find.TickManager == null)
                {
                    return;
                }

                int tick = Find.TickManager.TicksGame;
                if (nextPeriodicCheckTick >= 0 && tick < nextPeriodicCheckTick)
                    return;

                nextPeriodicCheckTick = tick + PeriodicCheckIntervalTicks;
                Faction hostFaction = GetHostFaction();
                if (hostFaction == null || !HasPotentialStaleFactionState(hostFaction))
                    return;

                MigrationReport report = MigrateAllPlayerFactionsToHost("periodic check");
                if (report.AnyChanges)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Periodic single-faction repair completed: " +
                        report.ToLogString());
                }
            }
        }

        [DebugAction(
            category = "Multiplayer",
            name = "Repair all player factions to host faction",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        public static void RepairAllPlayerFactionsToHostFaction()
        {
            if (!CanRunDeveloperRepair())
                return;

            MigrationReport report = MigrateAllPlayerFactionsToHost("developer action");
            ReportDeveloperResult(report);
        }

        [DebugAction(
            category = "Multiplayer",
            name = "Repair selected player faction to host faction",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        public static void RepairSelectedPlayerFactionToHostFaction()
        {
            if (!CanRunDeveloperRepair())
                return;

            Faction hostFaction = GetHostFaction();
            if (hostFaction == null)
                return;

            HashSet<int> sourceFactionIds = GetMigrationSourceFactionIds(hostFaction);
            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading?
                .Where(faction => sourceFactionIds.Contains(faction.loadID))
                .OrderBy(faction => faction.loadID)
                .ToList();

            if (factions.NullOrEmpty())
            {
                ReportDeveloperResult(new MigrationReport());
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "All non-NPC player factions",
                    () => ReportDeveloperResult(
                        MigrateAllPlayerFactionsToHost("developer selection: all")))
            };

            foreach (Faction faction in factions)
            {
                Faction selectedFaction = faction;
                options.Add(
                    new FloatMenuOption(
                        selectedFaction.Name,
                        () => ReportDeveloperResult(
                            MigratePlayerFactionToHost(selectedFaction, "developer selection"))));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static bool CanRunDeveloperRepair()
        {
            if (!Prefs.DevMode)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Player-faction migration is a developer-mode action. " +
                    "Enable the official developer mode first.");
                return false;
            }

            if (MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) && multifaction)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Player-faction migration was refused while " +
                    "Multiplayer multifaction mode is active.");
                return false;
            }

            return true;
        }

        private static void ReportDeveloperResult(MigrationReport report)
        {
            Log.Message("[MP-MeowOnlineShop] Developer faction migration: " + report.ToLogString());
            if (!MP.IsInMultiplayer || MpRuntimeInfo.IsHostAuthority())
            {
                Messages.Message(
                    "Player-faction migration complete: " + report.ToLogString(),
                    MessageTypeDefOf.TaskCompletion,
                    false);
            }
        }

        private static MigrationReport MigrateAllPlayerFactionsToHost(string reason)
        {
            Faction hostFaction = GetHostFaction();
            if (hostFaction == null)
                return new MigrationReport();

            LogRuntimeFactionState(hostFaction, reason);
            HashSet<int> sourceFactionIds = GetMigrationSourceFactionIds(hostFaction);

            return MigrateToHost(hostFaction, sourceFactionIds, reason);
        }

        private static MigrationReport MigratePlayerFactionToHost(Faction sourceFaction, string reason)
        {
            Faction hostFaction = GetHostFaction();
            if (hostFaction == null || sourceFaction == null || sourceFaction == hostFaction)
                return new MigrationReport();

            return MigrateToHost(
                hostFaction,
                new HashSet<int> { sourceFaction.loadID },
                reason);
        }

        private static MigrationReport MigrateToHost(
            Faction hostFaction,
            HashSet<int> sourceFactionIds,
            string reason)
        {
            MigrationReport report = new MigrationReport();
            if (hostFaction == null || sourceFactionIds == null || sourceFactionIds.Count == 0)
                return report;

            int hostFactionId = hostFaction.loadID;
            sourceFactionIds.Remove(hostFactionId);

            foreach (WorldObject worldObject in (Find.WorldObjects?.AllWorldObjects ?? Enumerable.Empty<WorldObject>())
                         .Where(worldObject => IsSourceFaction(worldObject.Faction, sourceFactionIds))
                         .OrderBy(worldObject => worldObject.ID)
                         .ToList())
            {
                try
                {
                    worldObject.SetFaction(hostFaction);
                    report.worldObjects++;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Failed to move world object " +
                        worldObject.ToStringSafe() + ": " + exception.Message);
                }
            }

            foreach (Map map in (Find.Maps ?? Enumerable.Empty<Map>()).OrderBy(map => map.uniqueID).ToList())
            {
                if (IsSourceFaction(map.ParentFaction, sourceFactionIds))
                {
                    try
                    {
                        map.Parent.SetFaction(hostFaction);
                        report.worldObjects++;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Failed to move map parent for map " +
                            map.uniqueID + ": " + exception.Message);
                    }
                }

                MigrateMapFactionData(map, hostFaction, sourceFactionIds, report);
                MigrateMapThings(map, hostFaction, sourceFactionIds, report);
            }

            MigrateWorldPawns(hostFaction, sourceFactionIds, report);
            MigrateWorldFactionData(hostFaction, sourceFactionIds, report);

            if (!resolutionLogged)
            {
                resolutionLogged = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Single-faction migration resolved for " +
                    "host=" + hostFaction.loadID + ", sourceFactionIds=" +
                    string.Join(",", sourceFactionIds.OrderBy(id => id)) +
                    ", reason=" + reason + ".");
            }

            return report;
        }

        private static void MigrateMapThings(
            Map map,
            Faction hostFaction,
            HashSet<int> sourceFactionIds,
            MigrationReport report)
        {
            if (map?.listerThings == null)
                return;

            HashSet<Thing> visited = new HashSet<Thing>();
            List<Thing> roots = map.listerThings.AllThings
                .Where(thing => thing != null)
                .OrderBy(thing => thing.thingIDNumber)
                .ToList();

            foreach (Thing root in roots)
            {
                foreach (Thing thing in EnumerateThingTree(root, visited))
                {
                    if (!IsSourceFaction(thing.Faction, sourceFactionIds) || !thing.def.CanHaveFaction)
                        continue;

                    try
                    {
                        thing.SetFaction(hostFaction);
                        report.things++;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Failed to move thing " +
                            thing.ToStringSafe() + ": " + exception.Message);
                    }
                }
            }
        }

        private static IEnumerable<Thing> EnumerateThingTree(Thing root, HashSet<Thing> visited)
        {
            if (root == null || !visited.Add(root))
                yield break;

            yield return root;

            if (!(root is IThingHolder holder))
                yield break;

            ThingOwner directlyHeldThings = holder.GetDirectlyHeldThings();
            if (directlyHeldThings == null)
                yield break;

            foreach (Thing heldThing in directlyHeldThings
                         .Where(thing => thing != null)
                         .OrderBy(thing => thing.thingIDNumber)
                         .ToList())
            {
                foreach (Thing nestedThing in EnumerateThingTree(heldThing, visited))
                    yield return nestedThing;
            }
        }

        private static void MigrateWorldPawns(
            Faction hostFaction,
            HashSet<int> sourceFactionIds,
            MigrationReport report)
        {
            List<Pawn> pawns = Find.WorldPawns?.AllPawnsAliveOrDead?
                .Where(pawn => pawn != null && IsSourceFaction(pawn.Faction, sourceFactionIds))
                .OrderBy(pawn => pawn.thingIDNumber)
                .ToList();

            if (pawns.NullOrEmpty())
                return;

            foreach (Pawn pawn in pawns)
            {
                try
                {
                    pawn.SetFaction(hostFaction);
                    report.pawns++;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Failed to move world pawn " +
                        pawn.ToStringSafe() + ": " + exception.Message);
                }
            }
        }

        private static void MigrateMapFactionData(
            Map map,
            Faction hostFaction,
            HashSet<int> requestedSourceFactionIds,
            MigrationReport report)
        {
            if (!MpRuntimeInfo.TryGetMapComp(map, out object mapComp))
                return;

            FieldInfo factionDataField = AccessTools.Field(mapComp.GetType(), "factionData");
            FieldInfo customFactionDataField = AccessTools.Field(mapComp.GetType(), "customFactionData");
            FieldInfo currentFactionIdField = AccessTools.Field(mapComp.GetType(), "currentFactionId");
            if (!(factionDataField?.GetValue(mapComp) is IDictionary factionData))
                return;

            int spectatorFactionId = GetSpectatorFactionId();
            List<int> sourceIds = factionData.Keys
                .OfType<int>()
                .Where(id => id != hostFaction.loadID &&
                             id != spectatorFactionId &&
                             requestedSourceFactionIds.Contains(id))
                .OrderBy(id => id)
                .ToList();

            object hostData = factionData.Contains(hostFaction.loadID)
                ? factionData[hostFaction.loadID]
                : CreateFactionMapDataFromCurrentMap(factionData, map, hostFaction.loadID);

            if (hostData == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Map " + map.uniqueID +
                    " has no usable host FactionMapData; leaving its MP data untouched.");
                return;
            }

            SetMapFaction(mapComp, hostFaction);

            foreach (int sourceId in sourceIds)
            {
                object sourceData = factionData[sourceId];
                if (sourceData == null || ReferenceEquals(sourceData, hostData))
                    continue;

                MergeFactionMapData(hostData, sourceData, map, report);
            }

            if (!factionData.Contains(hostFaction.loadID))
                factionData[hostFaction.loadID] = hostData;

            foreach (int sourceId in sourceIds)
            {
                if (factionData.Contains(sourceId))
                {
                    factionData.Remove(sourceId);
                    report.mapFactionRecords++;
                }
            }

            if (customFactionDataField?.GetValue(mapComp) is IDictionary customFactionData)
            {
                foreach (int sourceId in customFactionData.Keys.OfType<int>()
                             .Where(requestedSourceFactionIds.Contains)
                             .OrderBy(id => id)
                             .ToList())
                {
                    if (customFactionData.Contains(sourceId))
                    {
                        customFactionData.Remove(sourceId);
                        report.customMapFactionRecords++;
                    }
                }
            }

            currentFactionIdField?.SetValue(mapComp, hostFaction.loadID);
            SetMapFaction(mapComp, hostFaction);

            try
            {
                object pathFinderMapData = map.pathFinder?.MapData;
                MethodInfo computeAll = pathFinderMapData == null
                    ? null
                    : AccessTools.Method(pathFinderMapData.GetType(), "ComputeAll");
                computeAll?.Invoke(pathFinderMapData, new object[] { Enumerable.Empty<PathRequest>() });
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Map " + map.uniqueID +
                    " AreaSource rebuild after faction migration failed: " +
                    exception.Message);
            }
        }

        private static object CreateFactionMapDataFromCurrentMap(
            IDictionary factionData,
            Map map,
            int factionId)
        {
            Type dataType = factionData.GetType().GetGenericArguments().Length > 1
                ? factionData.GetType().GetGenericArguments()[1]
                : null;
            MethodInfo newFromMap = dataType == null
                ? null
                : AccessTools.Method(dataType, "NewFromMap", new[] { typeof(Map), typeof(int) });

            try
            {
                return newFromMap?.Invoke(null, new object[] { map, factionId });
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Could not create host FactionMapData for map " +
                    map.uniqueID + ": " + exception.Message);
                return null;
            }
        }

        private static void MergeFactionMapData(
            object hostData,
            object sourceData,
            Map map,
            MigrationReport report)
        {
            FieldInfo areaField = AccessTools.Field(hostData.GetType(), "areaManager");
            FieldInfo zoneField = AccessTools.Field(hostData.GetType(), "zoneManager");
            FieldInfo planField = AccessTools.Field(hostData.GetType(), "planManager");
            FieldInfo designationField = AccessTools.Field(hostData.GetType(), "designationManager");

            AreaManager hostAreas = areaField?.GetValue(hostData) as AreaManager;
            AreaManager sourceAreas = areaField?.GetValue(sourceData) as AreaManager;
            if (hostAreas != null && sourceAreas != null && !ReferenceEquals(hostAreas, sourceAreas))
            {
                foreach (Area area in sourceAreas.AllAreas.ToList())
                {
                    Area matchingCoreArea = IsCoreArea(area)
                        ? hostAreas.AllAreas.FirstOrDefault(candidate => candidate.GetType() == area.GetType())
                        : null;

                    if (matchingCoreArea != null)
                    {
                        foreach (IntVec3 cell in area.ActiveCells
                                     .OrderBy(cell => cell.z)
                                     .ThenBy(cell => cell.x))
                        {
                            matchingCoreArea[cell] = true;
                        }
                    }

                    // Keep the original Area object as well as merging the
                    // built-in area cells. PathRequests already queued before
                    // repair may still hold the old object reference; removing
                    // it would turn the current KeyNotFoundException into a
                    // delayed failure when that request is finalized.
                    if (!hostAreas.AllAreas.Contains(area))
                    {
                        area.areaManager = hostAreas;
                        hostAreas.AllAreas.Add(area);
                    }

                    sourceAreas.AllAreas.Remove(area);
                    report.areas++;
                }
            }

            ZoneManager hostZones = zoneField?.GetValue(hostData) as ZoneManager;
            ZoneManager sourceZones = zoneField?.GetValue(sourceData) as ZoneManager;
            if (hostZones != null && sourceZones != null && !ReferenceEquals(hostZones, sourceZones))
            {
                foreach (Zone zone in sourceZones.AllZones.ToList())
                {
                    try
                    {
                        sourceZones.DeregisterZone(zone);
                        zone.zoneManager = hostZones;
                        hostZones.RegisterZone(zone);
                        report.zones++;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Failed to merge zone on map " +
                            map.uniqueID + ": " + exception.Message);
                    }
                }
            }

            PlanManager hostPlans = planField?.GetValue(hostData) as PlanManager;
            PlanManager sourcePlans = planField?.GetValue(sourceData) as PlanManager;
            if (hostPlans != null && sourcePlans != null && !ReferenceEquals(hostPlans, sourcePlans))
            {
                foreach (Plan plan in sourcePlans.AllPlans.ToList())
                {
                    try
                    {
                        sourcePlans.DeregisterPlan(plan);
                        plan.planManager = hostPlans;
                        hostPlans.RegisterPlan(plan);
                        report.plans++;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Failed to merge plan on map " +
                            map.uniqueID + ": " + exception.Message);
                    }
                }
            }

            DesignationManager hostDesignations = designationField?.GetValue(hostData) as DesignationManager;
            DesignationManager sourceDesignations = designationField?.GetValue(sourceData) as DesignationManager;
            if (hostDesignations != null && sourceDesignations != null &&
                !ReferenceEquals(hostDesignations, sourceDesignations))
            {
                foreach (Designation designation in sourceDesignations.AllDesignations
                             .Where(designation => designation != null)
                             .OrderBy(designation => designation.def?.defName ?? string.Empty)
                             .ThenBy(designation => designation.target.Cell.z)
                             .ThenBy(designation => designation.target.Cell.x)
                             .ToList())
                {
                    try
                    {
                        sourceDesignations.RemoveDesignation(designation);
                        hostDesignations.AddDesignation(designation);
                        report.designations++;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Failed to merge designation on map " +
                            map.uniqueID + ": " + exception.Message);
                    }
                }
            }
        }

        private static void MigrateWorldFactionData(
            Faction hostFaction,
            HashSet<int> requestedSourceFactionIds,
            MigrationReport report)
        {
            if (MultiplayerType == null)
                return;

            try
            {
                FieldInfo gameField = AccessTools.Field(MultiplayerType, "game");
                object game = gameField?.GetValue(null);
                object worldComp = game == null
                    ? null
                    : AccessTools.Field(game.GetType(), "worldComp")?.GetValue(game);
                FieldInfo factionDataField = worldComp == null
                    ? null
                    : AccessTools.Field(worldComp.GetType(), "factionData");
                if (!(factionDataField?.GetValue(worldComp) is IDictionary factionData))
                    return;

                int spectatorFactionId = GetSpectatorFactionId(worldComp);
                if (!factionData.Contains(hostFaction.loadID))
                {
                    Type dataType = factionData.GetType().GetGenericArguments().Length > 1
                        ? factionData.GetType().GetGenericArguments()[1]
                        : WorldDataType;
                    MethodInfo fromCurrent = dataType == null
                        ? null
                        : AccessTools.Method(dataType, "FromCurrent", new[] { typeof(int) });
                    object hostData = fromCurrent?.Invoke(null, new object[] { hostFaction.loadID });
                    if (hostData != null)
                        factionData[hostFaction.loadID] = hostData;
                }

                foreach (int sourceId in factionData.Keys.OfType<int>()
                             .Where(id => id != hostFaction.loadID &&
                                          id != spectatorFactionId &&
                                          requestedSourceFactionIds.Contains(id))
                             .OrderBy(id => id)
                             .ToList())
                {
                    if (factionData.Contains(sourceId))
                    {
                        factionData.Remove(sourceId);
                        report.worldFactionRecords++;
                    }
                }

                MethodInfo setFaction = AccessTools.Method(
                    worldComp.GetType(),
                    "SetFaction",
                    new[] { typeof(Faction) });
                setFaction?.Invoke(worldComp, new object[] { hostFaction });
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] World faction data migration failed: " +
                    exception.Message);
            }
        }

        private static void SetMapFaction(object mapComp, Faction faction)
        {
            try
            {
                MethodInfo setFaction = AccessTools.Method(
                    mapComp.GetType(),
                    "SetFaction",
                    new[] { typeof(Faction) });
                setFaction?.Invoke(mapComp, new object[] { faction });
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Map faction binding failed: " +
                    exception.Message);
            }
        }

        private static bool HasPotentialStaleFactionState(Faction hostFaction)
        {
            HashSet<int> sourceIds = GetMigrationSourceFactionIds(hostFaction);
            if (sourceIds.Count == 0)
                return false;

            if ((Find.WorldObjects?.AllWorldObjects ?? Enumerable.Empty<WorldObject>())
                .Any(worldObject => IsSourceFaction(worldObject.Faction, sourceIds)))
            {
                return true;
            }

            if ((Find.WorldPawns?.AllPawnsAliveOrDead ?? Enumerable.Empty<Pawn>())
                .Any(pawn => IsSourceFaction(pawn.Faction, sourceIds)))
            {
                return true;
            }

            foreach (Map map in Find.Maps ?? Enumerable.Empty<Map>())
            {
                if (IsSourceFaction(map.ParentFaction, sourceIds))
                    return true;

                if (MpRuntimeInfo.TryMapCompContainsAnyPlayerFaction(
                        map,
                        sourceIds,
                        out bool factionDataMatched,
                        out bool customFactionDataMatched) &&
                    (factionDataMatched || customFactionDataMatched))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<int> GetMigrationSourceFactionIds(Faction hostFaction)
        {
            HashSet<int> sourceIds = new HashSet<int>(
                Find.FactionManager?.AllFactionsListForReading?
                    .Where(faction => IsSelectablePlayerFaction(faction, hostFaction))
                    .Select(faction => faction.loadID) ?? Enumerable.Empty<int>());

            int spectatorFactionId = GetSpectatorFactionId();
            foreach (int factionId in GetMultiplayerFactionDataIds()
                         .Where(id => id != hostFaction?.loadID && id != spectatorFactionId)
                         .OrderBy(id => id))
            {
                Faction faction = Find.FactionManager?.AllFactionsListForReading?
                    .FirstOrDefault(candidate => candidate.loadID == factionId);
                if (faction != null)
                    sourceIds.Add(factionId);
            }

            return sourceIds;
        }

        private static IEnumerable<int> GetMultiplayerFactionDataIds()
        {
            object worldComp = GetWorldComp();
            FieldInfo factionDataField = worldComp == null
                ? null
                : AccessTools.Field(worldComp.GetType(), "factionData");
            if (!(factionDataField?.GetValue(worldComp) is IDictionary factionData))
                return Enumerable.Empty<int>();

            return factionData.Keys.OfType<int>().ToList();
        }

        private static void LogRuntimeFactionState(Faction hostFaction, string reason)
        {
            if (runtimeFactionStateLogged)
                return;

            runtimeFactionStateLogged = true;
            try
            {
                List<Faction> factions = Find.FactionManager?.AllFactionsListForReading?
                    .Where(faction => faction != null)
                    .OrderBy(faction => faction.loadID)
                    .ToList() ?? new List<Faction>();
                string relevantFactions = string.Join(
                    " | ",
                    factions.Where(faction =>
                            faction == hostFaction ||
                            faction.IsPlayer ||
                            (faction.def?.defName?.IndexOf("PlayerFaction", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                        .Select(faction =>
                            faction.Name + "#" + faction.loadID +
                            ", def=" + (faction.def?.defName ?? "null") +
                            ", isPlayer=" + faction.IsPlayer +
                            ", hidden=" + faction.Hidden));
                string mpFactionIds = string.Join(",", GetMultiplayerFactionDataIds().OrderBy(id => id));
                Log.Message(
                    "[MP-MeowOnlineShop] Runtime faction scan: reason=" + reason +
                    ", host=" + (hostFaction == null ? "null" : hostFaction.Name + "#" + hostFaction.loadID) +
                    ", allFactionCount=" + factions.Count +
                    ", relevant=" + (relevantFactions.NullOrEmpty() ? "<none>" : relevantFactions) +
                    ", multiplayerFactionDataIds=" + (mpFactionIds.NullOrEmpty() ? "<none>" : mpFactionIds));
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Runtime faction scan failed: " + exception.Message);
            }
        }

        private static bool IsSelectablePlayerFaction(Faction faction, Faction hostFaction)
        {
            return faction != null &&
                   faction != hostFaction &&
                   faction.IsPlayer &&
                   !faction.Hidden;
        }

        private static bool IsSourceFaction(Faction faction, HashSet<int> sourceFactionIds)
        {
            return faction != null &&
                   sourceFactionIds != null &&
                   sourceFactionIds.Contains(faction.loadID);
        }

        private static bool IsCoreArea(Area area)
        {
            string typeName = area?.GetType().FullName;
            return typeName == "RimWorld.Area_Home" ||
                   typeName == "RimWorld.Area_BuildRoof" ||
                   typeName == "RimWorld.Area_NoRoof" ||
                   typeName == "RimWorld.Area_SnowOrSandClear" ||
                   typeName == "RimWorld.Area_PollutionClear";
        }

        private static Faction GetHostFaction()
        {
            try
            {
                PropertyInfo realPlayerFaction = AccessTools.Property(MultiplayerType, "RealPlayerFaction");
                if (realPlayerFaction?.GetValue(null, null) is Faction faction && faction.IsPlayer)
                    return faction;
            }
            catch
            {
                // Faction.OfPlayer is the correct fallback outside an MP session.
            }

            return Faction.OfPlayer;
        }

        private static int GetSpectatorFactionId(object worldComp = null)
        {
            try
            {
                worldComp ??= GetWorldComp();
                Faction spectator = worldComp == null
                    ? null
                    : AccessTools.Field(worldComp.GetType(), "spectatorFaction")?.GetValue(worldComp) as Faction;
                return spectator?.loadID ?? int.MinValue;
            }
            catch
            {
                return int.MinValue;
            }
        }

        private static object GetWorldComp()
        {
            try
            {
                object game = AccessTools.Field(MultiplayerType, "game")?.GetValue(null);
                return game == null
                    ? null
                    : AccessTools.Field(game.GetType(), "worldComp")?.GetValue(game);
            }
            catch
            {
                return null;
            }
        }

        private sealed class MigrationReport
        {
            public int worldObjects;
            public int things;
            public int pawns;
            public int areas;
            public int zones;
            public int plans;
            public int designations;
            public int mapFactionRecords;
            public int customMapFactionRecords;
            public int worldFactionRecords;

            public bool AnyChanges =>
                worldObjects != 0 || things != 0 || pawns != 0 || areas != 0 ||
                zones != 0 || plans != 0 || designations != 0 ||
                mapFactionRecords != 0 || customMapFactionRecords != 0 ||
                worldFactionRecords != 0;

            public string ToLogString()
            {
                return "worldObjects=" + worldObjects +
                       ", things=" + things +
                       ", pawns=" + pawns +
                       ", areas=" + areas +
                       ", zones=" + zones +
                       ", plans=" + plans +
                       ", designations=" + designations +
                       ", mapFactionRecords=" + mapFactionRecords +
                       ", customMapFactionRecords=" + customMapFactionRecords +
                       ", worldFactionRecords=" + worldFactionRecords;
            }
        }
    }
}
