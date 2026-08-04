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
    /// </summary>
    internal static class Patch_DeterministicTickList
    {
        private static readonly FieldInfo ThingListsField =
            AccessTools.Field(typeof(TickList), "thingLists");
        private static readonly FieldInfo ThingsToRegisterField =
            AccessTools.Field(typeof(TickList), "thingsToRegister");
        private static readonly FieldInfo ThingsToDeregisterField =
            AccessTools.Field(typeof(TickList), "thingsToDeregister");
        private static readonly FieldInfo TickTypeField =
            AccessTools.Field(typeof(TickList), "tickType");
        private static readonly HashSet<TickList> InitializedLists =
            new HashSet<TickList>();
        private static readonly Dictionary<TickList, Map> ListOwners =
            new Dictionary<TickList, Map>();
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
        private static readonly HashSet<int> LoggedRuntimeOwnerMaps =
            new HashSet<int>();
        private static bool _loggedComplexMapCoverage;
        private static bool _loggedOwnerFallback;
        private static FieldInfo _asyncMapField;
        private static FieldInfo _asyncTickingMapField;
        private static FieldInfo _asyncNormalField;
        private static FieldInfo _asyncRareField;
        private static FieldInfo _asyncLongField;

        internal static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(TickList), nameof(TickList.Tick));
            var prefix = AccessTools.Method(
                typeof(Patch_DeterministicTickList),
                nameof(TickPrefix));
            if (target == null || prefix == null ||
                ThingListsField == null || ThingsToRegisterField == null ||
                ThingsToDeregisterField == null || TickTypeField == null)
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

            if (target == null || postfix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Async-time TickList membership rebuild skipped: " +
                    "Multiplayer.Client.AsyncTimeComp.FinalizeInit was not resolved.");
                return;
            }

            _asyncMapField = AccessTools.Field(asyncTimeType, "map");
            _asyncTickingMapField = AccessTools.Field(asyncTimeType, "tickingMap");
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

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(postfix)
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Async-time TickList snapshot membership rebuild active.");
        }

        private static void AsyncTimeFinalizeInitPostfix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            try
            {
                var map = _asyncMapField.GetValue(__instance) as Map;
                var normal = _asyncNormalField.GetValue(__instance) as TickList;
                var rare = _asyncRareField.GetValue(__instance) as TickList;
                var longTicks = _asyncLongField.GetValue(__instance) as TickList;
                if (map == null || normal == null || rare == null || longTicks == null)
                    return;

                ResetTickList(normal);
                ResetTickList(rare);
                ResetTickList(longTicks);
                ListOwners[normal] = map;
                ListOwners[rare] = map;
                ListOwners[longTicks] = map;

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
                        thing.Map != map ||
                        thing.def == null || !seen.Add(thing))
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
            ListOwners[tickList] = map;
            RebuildSingleTickList(tickList, map, out _);
            InitializedLists.Add(tickList);
        }

        private static void ResetTickList(TickList tickList)
        {
            var buckets = ThingListsField.GetValue(tickList) as List<List<Thing>>;
            if (buckets != null)
            {
                for (int i = 0; i < buckets.Count; i++)
                    buckets[i].Clear();
            }

            (ThingsToRegisterField.GetValue(tickList) as List<Thing>)?.Clear();
            (ThingsToDeregisterField?.GetValue(tickList) as List<Thing>)?.Clear();
        }

        private static void AddDirect(TickList tickList, Thing thing)
        {
            var buckets = ThingListsField.GetValue(tickList) as List<List<Thing>>;
            if (buckets == null || buckets.Count == 0)
                return;

            int hash = StableHash(thing);
            buckets[hash % buckets.Count].Add(thing);
        }

        private static void TickPrefix(TickList __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            if (MpRuntimeInfo.RequiresVanillaPerMapPipelines(out string reason) &&
                !_loggedComplexMapCoverage)
            {
                _loggedComplexMapCoverage = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Deterministic TickList guard covers each Multiplayer " +
                    $"per-map TickList independently ({reason}); pending registrations are merged " +
                    "and stable thing-ID order is restored before the first map tick.");
            }

            var buckets = ThingListsField.GetValue(__instance) as List<List<Thing>>;
            var pending = ThingsToRegisterField.GetValue(__instance) as List<Thing>;
            if (buckets == null || pending == null || buckets.Count == 0)
                return;

            bool firstTick = InitializedLists.Add(__instance);
            ListOwners.TryGetValue(__instance, out Map ownerMap);

            // FinalizeInit is guaranteed for a cold-loading client, but a
            // long-running host can retain or replace an AsyncTimeComp/TickList
            // without this compatibility patch observing that initialization.
            // AsyncTimeComp sets its static tickingMap immediately before all
            // three per-map TickLists execute, so it is the authoritative owner
            // at this boundary. If that field is unavailable, fall back to
            // matching the live TickList instance against every map's
            // AsyncTimeComp. Rebind on every execution rather than allowing
            // only the rejoining client to repair a missing spawned pawn.
            Map runtimeOwner = _asyncTickingMapField?.GetValue(null) as Map;
            bool usedOwnerFallback = false;
            if (runtimeOwner == null)
            {
                runtimeOwner = ResolveTickListOwner(__instance);
                usedOwnerFallback = runtimeOwner != null;
            }
            if (runtimeOwner != null && ownerMap != runtimeOwner)
            {
                ownerMap = runtimeOwner;
                ListOwners[__instance] = runtimeOwner;
                if (LoggedRuntimeOwnerMaps.Add(runtimeOwner.uniqueID))
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Bound active async TickLists from " +
                        (usedOwnerFallback
                            ? "the live AsyncTimeComp owner scan"
                            : "Multiplayer tickingMap") +
                        $": map={runtimeOwner.uniqueID}. " +
                        "Runtime membership reconciliation now applies equally " +
                        "to long-running hosts and joining clients.");
                }
                if (usedOwnerFallback && !_loggedOwnerFallback)
                {
                    _loggedOwnerFallback = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Deterministic TickList owner " +
                        "fallback is active; Multiplayer tickingMap was not " +
                        "resolved from this runtime.");
                }
            }

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
                    if (thing == null ||
                        (ownerMap != null && IsInvalidForOwner(thing, ownerMap)))
                        continue;

                    RemoveAllOccurrences(buckets, thing);
                    int hash = StableHash(thing);
                    buckets[hash % buckets.Count].Add(thing);
                }
                pending.Clear();
            }

            if (ownerMap != null)
                ReconcileCurrentBucket(
                    buckets,
                    ownerMap,
                    GetTickType(__instance));

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
                if (!invalid && !duplicate && !wrongBucket)
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
            if (tickList == null || TickTypeField == null)
                return TickerType.Never;
            return (TickerType)TickTypeField.GetValue(tickList);
        }

        private static bool BelongsToTickList(Thing thing, TickerType tickType)
        {
            if (thing?.def == null)
                return false;

            if (tickType == TickerType.Normal)
                return thing is IThingHolder || thing.def.tickerType == TickerType.Normal;
            return thing.def.tickerType == tickType;
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
