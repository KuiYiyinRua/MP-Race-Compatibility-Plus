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
        private static readonly Dictionary<TickList, TickListRuntimeState> RuntimeStates =
            new Dictionary<TickList, TickListRuntimeState>();
        private const int ReconcileSafetyInterval = 600;
        private static readonly HashSet<int> LoggedStaleMemberMaps =
            new HashSet<int>();
        private static readonly HashSet<int> LoggedMissingNormalThingMaps =
            new HashSet<int>();
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

        private sealed class TickListRuntimeState
        {
            public Map Owner;
            public bool Initialized;
            public bool Dirty;
            public int LastReconcileTick;
        }

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

            MethodInfo register = AccessTools.Method(
                typeof(TickList),
                nameof(TickList.RegisterThing),
                new[] { typeof(Thing) });
            MethodInfo deregister = AccessTools.Method(
                typeof(TickList),
                nameof(TickList.DeregisterThing),
                new[] { typeof(Thing) });
            MethodInfo registerPostfix = AccessTools.Method(
                typeof(Patch_DeterministicTickList),
                nameof(RegisterThingPostfix));
            MethodInfo deregisterPostfix = AccessTools.Method(
                typeof(Patch_DeterministicTickList),
                nameof(DeregisterThingPostfix));
            if (register != null && registerPostfix != null)
            {
                harmony.Patch(
                    register,
                    postfix: new HarmonyMethod(registerPostfix)
                    {
                        priority = Priority.Last
                    });
            }
            if (deregister != null && deregisterPostfix != null)
            {
                harmony.Patch(
                    deregister,
                    postfix: new HarmonyMethod(deregisterPostfix)
                    {
                        priority = Priority.Last
                    });
            }

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

        private static TickListRuntimeState GetOrCreateState(TickList tickList)
        {
            TickListRuntimeState state;
            if (!RuntimeStates.TryGetValue(tickList, out state))
            {
                state = new TickListRuntimeState();
                RuntimeStates[tickList] = state;
            }
            return state;
        }

        private static void BindAsyncTickList(TickList tickList, Map map)
        {
            if (tickList == null || map == null)
                return;

            var state = GetOrCreateState(tickList);
            if (state.Owner == map)
                return;

            state.Owner = map;
            state.Initialized = true;
            state.Dirty = true;
        }

        private static void SetInitialized(TickList tickList, bool value)
        {
            if (tickList != null)
                GetOrCreateState(tickList).Initialized = value;
        }

        private static void MarkTickListDirty(TickList tickList)
        {
            if (tickList != null)
                GetOrCreateState(tickList).Dirty = true;
        }

        private static void RegisterThingPostfix(TickList __instance)
        {
            MarkTickListDirty(__instance);
        }

        private static void DeregisterThingPostfix(TickList __instance)
        {
            MarkTickListDirty(__instance);
        }

        private static bool IsSafetyReconcileDue(
            TickListRuntimeState state,
            int tick)
        {
            return tick - state.LastReconcileTick >= ReconcileSafetyInterval;
        }

        private static bool ShouldReconcile(
            TickListRuntimeState state,
            int tick)
        {
            if (state.Dirty)
            {
                state.Dirty = false;
                state.LastReconcileTick = tick;
                return true;
            }

            if (tick - state.LastReconcileTick >= ReconcileSafetyInterval)
            {
                state.LastReconcileTick = tick;
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
                SetInitialized(normal, false);
                SetInitialized(rare, false);
                SetInitialized(longTicks, false);

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
            SetInitialized(tickList, true);
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

            TickListRuntimeState state;
            if (!RuntimeStates.TryGetValue(__instance, out state))
            {
                if (_asyncTickPatchActive)
                    return;

                Map runtimeOwner = ResolveTickListOwner(__instance);
                if (runtimeOwner == null)
                    return;
                state = GetOrCreateState(__instance);
                state.Owner = runtimeOwner;
                state.Initialized = true;
                state.Dirty = true;
            }

            Map ownerMap = state.Owner;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            bool firstTick = !state.Initialized;
            if (!firstTick &&
                !state.Dirty &&
                !IsSafetyReconcileDue(state, currentTick))
            {
                return;
            }

            state.Initialized = true;

            var buckets = ThingListsRef(__instance);
            var pending = ThingsToRegisterRef(__instance);
            if (buckets == null || pending == null || buckets.Count == 0)
                return;

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
                state.Dirty = true;
            }

            if (ShouldReconcile(state, currentTick))
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

            {
            List<Thing> expected = CollectExpectedBucket(
                ownerMap,
                tickType,
                buckets.Count,
                bucketIndex);
            var oldSet = new HashSet<Thing>(bucket);
            var expectedSet = new HashSet<Thing>(expected);
            int innerAdded = 0;
            List<string> innerAddedIds = null;
            for (int i = 0; i < expected.Count; i++)
            {
                Thing thing = expected[i];
                if (oldSet.Contains(thing))
                    continue;
                innerAdded++;
                if (innerAddedIds == null)
                    innerAddedIds = new List<string>();
                if (innerAddedIds.Count < 8)
                    innerAddedIds.Add(thing.ThingID ?? "<null>");
            }

            int innerRemoved = 0;
            List<string> innerRemovedIds = null;
            for (int i = 0; i < bucket.Count; i++)
            {
                Thing thing = bucket[i];
                if (expectedSet.Contains(thing))
                    continue;
                innerRemoved++;
                if (innerRemovedIds == null)
                    innerRemovedIds = new List<string>();
                if (innerRemovedIds.Count < 8)
                    innerRemovedIds.Add(thing?.ThingID ?? "<null>");
            }

            bucket.Clear();
            bucket.AddRange(expected);
            if (expected.Count > 1)
                bucket.Sort(CompareStableThings);

            if (innerRemoved > 0 && LoggedStaleMemberMaps.Add(ownerMap.uniqueID))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Removed stale, cross-map, duplicate, or wrong-bucket " +
                    "TickList members before " +
                    $"execution: map={ownerMap.uniqueID}, bucket={bucketIndex}, " +
                    $"removed={innerRemoved}, ids={string.Join(",", innerRemovedIds ?? new List<string>())}.");
            }

            if (innerAdded > 0 && LoggedMissingNormalThingMaps.Add(ownerMap.uniqueID))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Restored missing spawned TickList " +
                    $"members before execution: map={ownerMap.uniqueID}, " +
                    $"bucket={bucketIndex}, added={innerAdded}, " +
                    $"ids={string.Join(",", innerAddedIds ?? new List<string>())}.");
            }

            return;

            }

        }

        private static List<Thing> CollectExpectedBucket(
            Map ownerMap,
            TickerType tickType,
            int bucketCount,
            int bucketIndex)
        {
            var result = new List<Thing>();
            List<Thing> allThings = ownerMap?.listerThings?.AllThings;
            if (allThings == null)
                return result;

            var seen = new HashSet<Thing>();
            for (int i = 0; i < allThings.Count; i++)
            {
                Thing thing = allThings[i];
                if (IsInvalidForOwner(thing, ownerMap) ||
                    !BelongsToTickList(thing, tickType) ||
                    StableHash(thing) % bucketCount != bucketIndex ||
                    !seen.Add(thing))
                {
                    continue;
                }

                result.Add(thing);
            }

            result.Sort(CompareStableThings);
            return result;
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
