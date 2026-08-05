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
    /// TickList is runtime-only state and is reconstructed from SpawnSetup calls
    /// when a joining client loads Multiplayer's snapshot. Large mod lists can
    /// produce a different registration order, and in Desync-44 the client was
    /// also missing several early normal-tick entries. Desync-46 then proved the
    /// inverse: the long-running host retained Axolotl522 in map 0 while the cold
    /// client correctly rebuilt without it. Rebuild every per-map list after load,
    /// then reconcile only the bucket about to execute against its owning map.
    /// Transient visual motes are excluded entirely: they are not part of the
    /// authoritative listerThings registry, and their Rand-consuming
    /// construction/tick timing can otherwise differ between peers after a
    /// gravship landing or rejoin.
    /// </summary>
    internal static class Patch_DeterministicTickList
    {
        private static readonly AccessTools.FieldRef<TickList, List<List<Thing>>> ThingListsRef =
            TryGetThingListsRef();
        private static readonly AccessTools.FieldRef<TickList, List<Thing>> ThingsToRegisterRef =
            TryGetThingsToRegisterRef();
        private static readonly AccessTools.FieldRef<TickList, List<Thing>> ThingsToDeregisterRef =
            TryGetThingsToDeregisterRef();
        private static readonly AccessTools.FieldRef<TickList, TickerType> TickTypeRef =
            TryGetTickTypeRef();
        private static readonly HashSet<TickList> InitializedLists =
            new HashSet<TickList>();
        private static readonly Dictionary<TickList, Map> ListOwners =
            new Dictionary<TickList, Map>();
        private static readonly HashSet<TickList> DirtyTickLists =
            new HashSet<TickList>();
        private static readonly Dictionary<TickList, int> LastReconcileTickByList =
            new Dictionary<TickList, int>();
        private const int ReconcileSafetyInterval = 600;
        private static readonly HashSet<int> LoggedStaleMemberMaps =
            new HashSet<int>();
        private static readonly HashSet<int> LoggedMissingPawnMaps =
            new HashSet<int>();
        private static readonly HashSet<int> LoggedMissingProjectileMaps =
            new HashSet<int>();
        private static readonly HashSet<int> LoggedMissingNormalThingMaps =
            new HashSet<int>();
        private static readonly Dictionary<Map, NormalTickerCandidateCache>
            NormalTickerCandidates =
                new Dictionary<Map, NormalTickerCandidateCache>();
        private const int NormalTickerCacheRefreshInterval = 600;
        private static bool _loggedComplexMapCoverage;
        private static bool _asyncTickPatchActive;
        private static FieldInfo _asyncMapField;
        private static FieldInfo _asyncNormalField;
        private static FieldInfo _asyncRareField;
        private static FieldInfo _asyncLongField;
        private static AccessTools.FieldRef<object, Map> _asyncMapRef;
        private static AccessTools.FieldRef<object, TickList> _asyncNormalRef;
        private static AccessTools.FieldRef<object, TickList> _asyncRareRef;
        private static AccessTools.FieldRef<object, TickList> _asyncLongRef;

        private static AccessTools.FieldRef<TickList, List<List<Thing>>> TryGetThingListsRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<TickList, List<List<Thing>>>("thingLists");
            }
            catch
            {
                return null;
            }
        }

        private static AccessTools.FieldRef<TickList, List<Thing>> TryGetThingsToRegisterRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<TickList, List<Thing>>("thingsToRegister");
            }
            catch
            {
                return null;
            }
        }

        private static AccessTools.FieldRef<TickList, List<Thing>> TryGetThingsToDeregisterRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<TickList, List<Thing>>("thingsToDeregister");
            }
            catch
            {
                return null;
            }
        }

        private static AccessTools.FieldRef<TickList, TickerType> TryGetTickTypeRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<TickList, TickerType>("tickType");
            }
            catch
            {
                return null;
            }
        }

        internal static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(TickList), nameof(TickList.Tick));
            var prefix = AccessTools.Method(
                typeof(Patch_DeterministicTickList),
                nameof(TickPrefix));
            if (target == null || prefix == null ||
                ThingListsRef == null || ThingsToRegisterRef == null ||
                ThingsToDeregisterRef == null || TickTypeRef == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Deterministic TickList guard could not " +
                    "resolve RimWorld TickList fields.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });

            PatchAsyncTimeFinalizeInit(harmony);

            Log.Message(
                "[MP-MeowOnlineShop] Deterministic multiplayer TickList guard active.");
        }

        private static void PatchAsyncTimeFinalizeInit(Harmony harmony)
        {
            Type asyncTimeType = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
            MethodInfo target = asyncTimeType == null
                ? null
                : AccessTools.Method(asyncTimeType, "FinalizeInit");
            MethodInfo postfix = AccessTools.Method(
                typeof(Patch_DeterministicTickList),
                nameof(AsyncTimeFinalizeInitPostfix));

            _asyncMapField = AccessTools.Field(asyncTimeType, "map");
            _asyncNormalField = AccessTools.Field(asyncTimeType, "tickListNormal");
            _asyncRareField = AccessTools.Field(asyncTimeType, "tickListRare");
            _asyncLongField = AccessTools.Field(asyncTimeType, "tickListLong");
            if (_asyncMapField == null || _asyncNormalField == null ||
                _asyncRareField == null || _asyncLongField == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Async-time TickList membership rebuild skipped: " +
                    "required AsyncTimeComp fields were not resolved.");
                return;
            }

            _asyncMapRef = TryGetInstanceFieldRef<Map>(asyncTimeType, "map");
            _asyncNormalRef = TryGetInstanceFieldRef<TickList>(asyncTimeType, "tickListNormal");
            _asyncRareRef = TryGetInstanceFieldRef<TickList>(asyncTimeType, "tickListRare");
            _asyncLongRef = TryGetInstanceFieldRef<TickList>(asyncTimeType, "tickListLong");

            if (target != null && postfix != null)
            {
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(postfix)
                    {
                        priority = Priority.Last
                    });
            }

            MethodInfo tick = AccessTools.Method(asyncTimeType, "Tick", Type.EmptyTypes);
            MethodInfo tickPrefix = AccessTools.Method(
                typeof(Patch_DeterministicTickList),
                nameof(AsyncTimeTickPrefix));
            if (tick != null && tickPrefix != null)
            {
                harmony.Patch(
                    tick,
                    prefix: new HarmonyMethod(tickPrefix)
                    {
                        priority = Priority.First
                    });
                _asyncTickPatchActive = true;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Async-time TickList snapshot membership rebuild active: " +
                $"finalizeInit={target != null}, tickBinding={_asyncTickPatchActive}.");
        }

        private static AccessTools.FieldRef<object, T> TryGetInstanceFieldRef<T>(
            Type type,
            string name) where T : class
        {
            try
            {
                return AccessTools.FieldRefAccess<T>(type, name);
            }
            catch
            {
                return null;
            }
        }

        private static void AsyncTimeTickPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null ||
                _asyncMapRef == null || _asyncNormalRef == null ||
                _asyncRareRef == null || _asyncLongRef == null)
            {
                return;
            }

            try
            {
                Map map = _asyncMapRef(__instance);
                TickList normal = _asyncNormalRef(__instance);
                TickList rare = _asyncRareRef(__instance);
                TickList longTicks = _asyncLongRef(__instance);
                if (map == null || normal == null || rare == null || longTicks == null)
                    return;

                BindAsyncTickList(normal, map);
                BindAsyncTickList(rare, map);
                BindAsyncTickList(longTicks, map);
            }
            catch
            {
                // Binding is best-effort; TickPrefix still has a fallback path.
            }
        }

        private static void BindAsyncTickList(TickList tickList, Map map)
        {
            if (tickList == null || map == null)
                return;

            if (ListOwners.TryGetValue(tickList, out Map existing) && existing == map)
                return;

            ListOwners[tickList] = map;
            InitializedLists.Add(tickList);
            MarkTickListDirty(tickList);
        }

        private static void MarkTickListDirty(TickList tickList)
        {
            if (tickList != null)
                DirtyTickLists.Add(tickList);
        }

        private static bool ShouldReconcile(TickList tickList, int tick)
        {
            if (DirtyTickLists.Remove(tickList))
            {
                LastReconcileTickByList[tickList] = tick;
                return true;
            }

            if (!LastReconcileTickByList.TryGetValue(tickList, out int lastTick))
            {
                LastReconcileTickByList[tickList] = tick;
                return true;
            }

            if (tick - lastTick >= ReconcileSafetyInterval)
            {
                LastReconcileTickByList[tickList] = tick;
                return true;
            }

            return false;
        }

        private static void AsyncTimeFinalizeInitPostfix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            try
            {
                var map = _asyncMapRef != null
                    ? _asyncMapRef(__instance)
                    : _asyncMapField.GetValue(__instance) as Map;
                var normal = _asyncNormalRef != null
                    ? _asyncNormalRef(__instance)
                    : _asyncNormalField.GetValue(__instance) as TickList;
                var rare = _asyncRareRef != null
                    ? _asyncRareRef(__instance)
                    : _asyncRareField.GetValue(__instance) as TickList;
                var longTicks = _asyncLongRef != null
                    ? _asyncLongRef(__instance)
                    : _asyncLongField.GetValue(__instance) as TickList;
                if (map == null || normal == null || rare == null || longTicks == null)
                    return;

                ResetTickList(normal);
                ResetTickList(rare);
                ResetTickList(longTicks);
                BindAsyncTickList(normal, map);
                BindAsyncTickList(rare, map);
                BindAsyncTickList(longTicks, map);

                var things = new List<Thing>(map.listerThings.AllThings);
                things.Sort(CompareStableThings);

                int normalCount = 0;
                int rareCount = 0;
                int longCount = 0;
                var seen = new HashSet<Thing>();
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || !thing.Spawned ||
                        thing.Map != map || thing.def == null ||
                        IsVisualMote(thing) || !seen.Add(thing))
                    {
                        continue;
                    }

                    TickerType tickerType = thing.def.tickerType;
                    if (thing is IThingHolder || tickerType == TickerType.Normal)
                    {
                        AddDirect(normal, thing);
                        normalCount++;
                    }
                    else if (tickerType == TickerType.Rare)
                    {
                        AddDirect(rare, thing);
                        rareCount++;
                    }
                    else if (tickerType == TickerType.Long)
                    {
                        AddDirect(longTicks, thing);
                        longCount++;
                    }
                }

                // FinalizeInit is not the end of snapshot initialization. Some
                // mods run late SpawnSetup/component repair hooks afterwards.
                // Force one more authoritative rebuild at the actual first tick
                // so those process-timing-dependent pending registrations cannot
                // make a rejoining peer tick an extra pawn.
                InitializedLists.Remove(normal);
                InitializedLists.Remove(rare);
                InitializedLists.Remove(longTicks);

                Log.Message(
                    "[MP-MeowOnlineShop] Rebuilt async map TickLists from authoritative " +
                    $"spawned registry: map={map.uniqueID}, normal={normalCount}, " +
                    $"rare={rareCount}, long={longCount}.");
            }
            catch (Exception e)
            {
                Log.Error(
                    "[MP-MeowOnlineShop] Async-time TickList membership rebuild failed: " + e);
            }
        }

        internal static int RebuildAllAsyncTickListsForStableMapLifecycle(
            string boundary)
        {
            if (!MP.IsInMultiplayer || Find.Maps == null)
                return 0;

            int rebuiltMaps = 0;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map == null)
                    continue;

                object asyncTime = GetAsyncTime(map);
                if (asyncTime == null)
                    continue;

                var normal = _asyncNormalField?.GetValue(asyncTime) as TickList;
                var rare = _asyncRareField?.GetValue(asyncTime) as TickList;
                var longTicks = _asyncLongField?.GetValue(asyncTime) as TickList;
                if (normal == null || rare == null || longTicks == null)
                    continue;

                RebuildLifecycleTickList(normal, map);
                RebuildLifecycleTickList(rare, map);
                RebuildLifecycleTickList(longTicks, map);
                rebuiltMaps++;
            }

            return rebuiltMaps;
        }

        private static object GetAsyncTime(Map map)
        {
            try
            {
                Type extensions = AccessTools.TypeByName(
                    "Multiplayer.Client.Extensions");
                MethodInfo asyncTime = AccessTools.Method(
                    extensions, "AsyncTime", new[] { typeof(Map) });
                return asyncTime?.Invoke(null, new object[] { map });
            }
            catch
            {
                return null;
            }
        }

        private static Map ResolveTickListOwner(TickList tickList)
        {
            if (tickList == null || Find.Maps == null)
                return null;

            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map == null)
                    continue;

                object asyncTime = GetAsyncTime(map);
                if (asyncTime == null)
                    continue;

                Type asyncType = asyncTime.GetType();
                FieldInfo normal = _asyncNormalField ??
                    AccessTools.Field(asyncType, "tickListNormal");
                FieldInfo rare = _asyncRareField ??
                    AccessTools.Field(asyncType, "tickListRare");
                FieldInfo longTicks = _asyncLongField ??
                    AccessTools.Field(asyncType, "tickListLong");

                if ((normal != null &&
                     ReferenceEquals(normal.GetValue(asyncTime), tickList)) ||
                    (rare != null &&
                     ReferenceEquals(rare.GetValue(asyncTime), tickList)) ||
                    (longTicks != null &&
                     ReferenceEquals(longTicks.GetValue(asyncTime), tickList)))
                {
                    return map;
                }
            }

            return null;
        }

        private static void RebuildLifecycleTickList(TickList tickList, Map map)
        {
            BindAsyncTickList(tickList, map);
            RebuildSingleTickList(tickList, map, out _);
            InitializedLists.Add(tickList);
            MarkTickListDirty(tickList);
        }

        private static void ResetTickList(TickList tickList)
        {
            var buckets = ThingListsRef(tickList);
            if (buckets != null)
            {
                for (int i = 0; i < buckets.Count; i++)
                    buckets[i].Clear();
            }

            ThingsToRegisterRef(tickList)?.Clear();
            ThingsToDeregisterRef(tickList)?.Clear();
        }

        private static void AddDirect(TickList tickList, Thing thing)
        {
            var buckets = ThingListsRef(tickList);
            if (buckets == null || buckets.Count == 0)
                return;

            int hash = StableHash(thing);
            buckets[hash % buckets.Count].Add(thing);
        }

        private static void TickPrefix(TickList __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            if (!_loggedComplexMapCoverage &&
                MpRuntimeInfo.RequiresVanillaPerMapPipelines(out string reason))
            {
                _loggedComplexMapCoverage = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Deterministic TickList guard covers each Multiplayer " +
                    $"per-map TickList independently ({reason}); pending registrations are merged " +
                    "and stable thing-ID order is restored before the first map tick.");
            }

            if (!_asyncTickPatchActive &&
                !MpRuntimeInfo.RequiresVanillaPerMapPipelines(out _))
            {
                return;
            }

            if (!ListOwners.TryGetValue(__instance, out Map ownerMap))
            {
                if (_asyncTickPatchActive)
                    return;

                Map runtimeOwner = ResolveTickListOwner(__instance);
                if (runtimeOwner == null)
                    return;
                BindAsyncTickList(__instance, runtimeOwner);
                ownerMap = runtimeOwner;
            }

            var buckets = ThingListsRef(__instance);
            var pending = ThingsToRegisterRef(__instance);
            if (buckets == null || pending == null || buckets.Count == 0)
                return;

            bool firstTick = InitializedLists.Add(__instance);

            if (firstTick && ownerMap != null)
            {
                int rebuilt = RebuildSingleTickList(
                    __instance, ownerMap, out uint membershipFingerprint);
                Log.Message(
                    "[MP-MeowOnlineShop] Rebuilt async TickList immediately before " +
                    $"its first execution: map={ownerMap.uniqueID}, " +
                    $"ticker={GetTickType(__instance)}, members={rebuilt}, " +
                    $"fingerprint={membershipFingerprint:X8}.");
            }

            bool hasRegistrations = pending.Count > 0;

            if (hasRegistrations)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var thing = pending[i];
                    if (thing == null || IsVisualMote(thing) ||
                        IsInvalidForOwner(thing, ownerMap))
                        continue;

                    RemoveAllOccurrences(buckets, thing);
                    int hash = StableHash(thing);
                    buckets[hash % buckets.Count].Add(thing);
                }
                pending.Clear();
                MarkTickListDirty(__instance);
            }

            if (ShouldReconcile(__instance, Find.TickManager?.TicksGame ?? 0))
            {
                ReconcileCurrentBucket(
                    buckets,
                    ownerMap,
                    GetTickType(__instance));
            }

            if (!firstTick && !hasRegistrations)
                return;

            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Count > 1)
                    buckets[i].Sort(CompareStableThings);
            }
        }

        private static void ReconcileCurrentBucket(
            List<List<Thing>> buckets,
            Map ownerMap,
            TickerType tickType)
        {
            if (buckets == null || buckets.Count == 0 || ownerMap == null)
                return;

            int bucketIndex = Find.TickManager.TicksGame % buckets.Count;
            if (bucketIndex < 0)
                bucketIndex += buckets.Count;

            List<Thing> bucket = buckets[bucketIndex];
            if (bucket == null)
                return;

            List<string> removedIds = null;
            var seen = new HashSet<Thing>();
            int removed = bucket.RemoveAll(thing =>
            {
                bool invalid = IsInvalidForOwner(thing, ownerMap);
                bool duplicate = !invalid && !seen.Add(thing);
                bool wrongBucket = !invalid && !duplicate &&
                                   StableHash(thing) % buckets.Count != bucketIndex;
                bool visualMote = !invalid && IsVisualMote(thing);
                if (!invalid && !duplicate && !wrongBucket && !visualMote)
                    return false;

                if (!LoggedStaleMemberMaps.Contains(ownerMap.uniqueID) &&
                    removedIds == null)
                {
                    removedIds = new List<string>();
                }
                if (removedIds != null && removedIds.Count < 8)
                    removedIds.Add(thing?.ThingID ?? "<null>");
                return true;
            });

            // Desync-69 proved the inverse of a stale host member: at tick
            // 383041 the host ticked Ratkin102560 and consumed 16 job-selection
            // draws, while the rejoined client skipped that pawn entirely and
            // moved to the next world tick. The serialized map pawn registry is
            // authoritative, so repair only missing spawned pawns in the normal
            // bucket that is about to execute. This avoids scanning tens of
            // thousands of map things and preserves vanilla bucket scheduling.
            int added = 0;
            List<string> addedIds = null;
            if (tickType == TickerType.Normal)
            {
                IReadOnlyList<Pawn> spawnedPawns = ownerMap.mapPawns?.AllPawnsSpawned;
                if (spawnedPawns != null)
                {
                    for (int i = 0; i < spawnedPawns.Count; i++)
                    {
                        Pawn pawn = spawnedPawns[i];
                        if (pawn == null || pawn.Destroyed || !pawn.Spawned ||
                            pawn.Map != ownerMap || pawn.def == null ||
                            StableHash(pawn) % buckets.Count != bucketIndex ||
                            !seen.Add(pawn))
                        {
                            continue;
                        }

                        bucket.Add(pawn);
                        added++;
                        if (!LoggedMissingPawnMaps.Contains(ownerMap.uniqueID))
                        {
                            if (addedIds == null)
                                addedIds = new List<string>();
                            if (addedIds.Count < 8)
                                addedIds.Add(pawn.ThingID ?? "<null>");
                        }
                    }
                }
            }

            if (added > 0)
            {
                bucket.Sort(CompareStableThings);
                if (LoggedMissingPawnMaps.Add(ownerMap.uniqueID))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Restored missing spawned pawn TickList " +
                        $"members before execution: map={ownerMap.uniqueID}, " +
                        $"bucket={bucketIndex}, added={added}, " +
                        $"ids={string.Join(",", addedIds ?? new List<string>())}.");
                }
            }

            // Desync-05 proved that dynamic Normal-TickList members need the
            // same inverse repair as pawns. The cold client rebuilt
            // MiliraBullet_PlasmaPistolCharged40274 from listerThings while the
            // long-running host no longer had that still-spawned projectile in
            // its runtime TickList. The client consequently executed
            // Projectile.CheckForFreeIntercept alone. Scan only RimWorld's
            // dedicated projectile group, rather than every map thing, and
            // restore missing members immediately before the Normal list runs.
            int projectilesAdded = 0;
            List<string> projectileIds = null;
            if (tickType == TickerType.Normal)
            {
                List<Thing> projectiles = ownerMap.listerThings?
                    .ThingsInGroup(ThingRequestGroup.Projectile);
                if (projectiles != null)
                {
                    for (int i = 0; i < projectiles.Count; i++)
                    {
                        Thing projectile = projectiles[i];
                        if (!(projectile is Projectile) || projectile.Destroyed ||
                            !projectile.Spawned || projectile.Map != ownerMap ||
                            projectile.def == null ||
                            StableHash(projectile) % buckets.Count != bucketIndex ||
                            !seen.Add(projectile))
                        {
                            continue;
                        }

                        bucket.Add(projectile);
                        projectilesAdded++;
                        if (!LoggedMissingProjectileMaps.Contains(ownerMap.uniqueID))
                        {
                            if (projectileIds == null)
                                projectileIds = new List<string>();
                            if (projectileIds.Count < 8)
                                projectileIds.Add(projectile.ThingID ?? "<null>");
                        }
                    }
                }
            }

            if (projectilesAdded > 0)
            {
                bucket.Sort(CompareStableThings);
                if (LoggedMissingProjectileMaps.Add(ownerMap.uniqueID))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Restored missing spawned projectile TickList " +
                        $"members before execution: map={ownerMap.uniqueID}, " +
                        $"bucket={bucketIndex}, added={projectilesAdded}, " +
                        $"ids={string.Join(",", projectileIds ?? new List<string>())}. " +
                        "This keeps long-running hosts aligned with cold clients that " +
                        "rebuild projectiles from the authoritative map registry.");
                }
            }

            // Desync-235 through Desync-237 showed that Pawn and Projectile
            // coverage is not sufficient.  A missing normal-ticker building
            // (the Ancot/Milira plasma turret in the bundle) lets one peer
            // advance to an unrelated projectile or pawn, after which every
            // later Rand and UniqueID allocation diverges.  Include every
            // spawned normal-ticker Thing from the authoritative map registry.
            // The candidate list is cached and only rescanned on a map-count
            // change or a bounded refresh, so this repair does not turn every
            // normal tick into a full scan of a large map's item registry.
            int normalThingsAdded = 0;
            List<string> normalThingIds = null;
            if (tickType == TickerType.Normal)
            {
                List<Thing> candidates = GetNormalTickerCandidates(ownerMap);
                if (candidates != null)
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Thing thing = candidates[i];
                        if (IsInvalidForOwner(thing, ownerMap) ||
                            !BelongsToTickList(thing, TickerType.Normal) ||
                            StableHash(thing) % buckets.Count != bucketIndex ||
                            !seen.Add(thing))
                        {
                            continue;
                        }

                        bucket.Add(thing);
                        normalThingsAdded++;
                        if (!LoggedMissingNormalThingMaps.Contains(ownerMap.uniqueID))
                        {
                            if (normalThingIds == null)
                                normalThingIds = new List<string>();
                            if (normalThingIds.Count < 8)
                                normalThingIds.Add(thing.ThingID ?? "<null>");
                        }
                    }
                }
            }

            if (normalThingsAdded > 0)
            {
                bucket.Sort(CompareStableThings);
                if (LoggedMissingNormalThingMaps.Add(ownerMap.uniqueID))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Restored missing spawned normal TickList " +
                        $"members before execution: map={ownerMap.uniqueID}, " +
                        $"bucket={bucketIndex}, added={normalThingsAdded}, " +
                        $"ids={string.Join(",", normalThingIds ?? new List<string>())}. " +
                        "This covers buildings and holders in addition to pawns and projectiles.");
                }
            }

            if (removed <= 0 || !LoggedStaleMemberMaps.Add(ownerMap.uniqueID))
                return;

            Log.Warning(
                "[MP-MeowOnlineShop] Removed stale, cross-map, duplicate, or wrong-bucket " +
                "TickList members before " +
                $"execution: map={ownerMap.uniqueID}, bucket={bucketIndex}, " +
                $"removed={removed}, ids={string.Join(",", removedIds ?? new List<string>())}. " +
                "This prevents a long-running host from ticking entities omitted by a " +
                "cold-joining client's authoritative map rebuild.");
        }

        private static List<Thing> GetNormalTickerCandidates(Map map)
        {
            if (map == null)
                return null;

            List<Thing> allThings = map.listerThings?.AllThings;
            if (allThings == null)
                return null;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!NormalTickerCandidates.TryGetValue(map, out NormalTickerCandidateCache cache) ||
                cache.AllThingsCount != allThings.Count ||
                currentTick - cache.LastRefreshTick >= NormalTickerCacheRefreshInterval)
            {
                cache = new NormalTickerCandidateCache
                {
                    AllThingsCount = allThings.Count,
                    LastRefreshTick = currentTick,
                    Things = new List<Thing>()
                };

                for (int i = 0; i < allThings.Count; i++)
                {
                    Thing thing = allThings[i];
                    if (!IsInvalidForOwner(thing, map) &&
                        BelongsToTickList(thing, TickerType.Normal))
                    {
                        cache.Things.Add(thing);
                    }
                }

                cache.Things.Sort(CompareStableThings);
                NormalTickerCandidates[map] = cache;
            }

            return cache.Things;
        }

        private sealed class NormalTickerCandidateCache
        {
            internal int AllThingsCount;
            internal int LastRefreshTick;
            internal List<Thing> Things;
        }

        private static int RebuildSingleTickList(
            TickList tickList,
            Map ownerMap,
            out uint membershipFingerprint)
        {
            membershipFingerprint = 2166136261u;
            if (tickList == null || ownerMap == null)
                return 0;

            TickerType tickType = GetTickType(tickList);
            ResetTickList(tickList);

            var things = new List<Thing>(ownerMap.listerThings.AllThings);
            things.Sort(CompareStableThings);
            var seen = new HashSet<Thing>();
            int count = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed || !thing.Spawned ||
                    thing.Map != ownerMap || thing.def == null ||
                    IsVisualMote(thing) ||
                    !seen.Add(thing) || !BelongsToTickList(thing, tickType))
                {
                    continue;
                }

                AddDirect(tickList, thing);
                unchecked
                {
                    membershipFingerprint ^= (uint)thing.thingIDNumber;
                    membershipFingerprint *= 16777619u;
                    membershipFingerprint ^= thing.def.shortHash;
                    membershipFingerprint *= 16777619u;
                }
                count++;
            }

            return count;
        }

        private static TickerType GetTickType(TickList tickList)
        {
            if (tickList == null || TickTypeRef == null)
                return TickerType.Never;
            return TickTypeRef(tickList);
        }

        private static bool BelongsToTickList(Thing thing, TickerType tickType)
        {
            if (thing?.def == null)
                return false;
            if (IsVisualMote(thing))
                return false;

            if (tickType == TickerType.Normal)
                return thing is IThingHolder || thing.def.tickerType == TickerType.Normal;
            return thing.def.tickerType == tickType;
        }

        private static bool IsVisualMote(Thing thing)
        {
            return thing is Mote;
        }

        private static void RemoveAllOccurrences(
            List<List<Thing>> buckets,
            Thing thing)
        {
            if (buckets == null || thing == null)
                return;

            for (int i = 0; i < buckets.Count; i++)
            {
                List<Thing> bucket = buckets[i];
                bucket?.RemoveAll(existing => ReferenceEquals(existing, thing));
            }
        }

        private static bool IsInvalidForOwner(Thing thing, Map ownerMap)
        {
            return thing == null ||
                   thing.Destroyed ||
                   !thing.Spawned ||
                   thing.Map != ownerMap;
        }

        private static int StableHash(Thing thing)
        {
            int hash = thing.thingIDNumber >= 0
                ? thing.thingIDNumber
                : thing.GetHashCode();
            if (hash < 0)
                hash = hash == int.MinValue ? int.MaxValue : -hash;
            return hash;
        }

        private static int CompareStableThings(Thing left, Thing right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            int byId = left.thingIDNumber.CompareTo(right.thingIDNumber);
            if (byId != 0)
                return byId;

            return string.CompareOrdinal(left.ThingID, right.ThingID);
        }

    }
}
