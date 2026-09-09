using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// Transient visual motes stay in the normal tick list so their lifespan
    /// cleanup still runs, but they are preserved separately from the
    /// authoritative listerThings rebuild because motes are not part of that
    /// registry. Their construction and tick Rand is isolated by the mote
    /// patches so per-peer visual timing cannot advance the shared map stream.
    /// </summary>
    internal static class Patch_DeterministicTickList
    {
        // Desync-342/343 prove that rebuilding and reconciling these runtime
        // lists after a client snapshot load changes only the joiner's tick
        // order. Multiplayer already reconstructs its AsyncTimeComp during
        // loading; do not derive a replacement schedule from listerThings
        // unless host/client membership can first be proven identical.
        private static readonly bool RuntimeTickListMutationEnabled = false;

        private static readonly AccessTools.FieldRef<TickList, List<List<Thing>>> ThingListsRef =
            TryGetThingListsRef();
        private static readonly AccessTools.FieldRef<TickList, List<Thing>> ThingsToRegisterRef =
            TryGetThingsToRegisterRef();
        private static readonly AccessTools.FieldRef<TickList, List<Thing>> ThingsToDeregisterRef =
            TryGetThingsToDeregisterRef();
        private static readonly AccessTools.FieldRef<TickList, TickerType> TickTypeRef =
            TryGetTickTypeRef();
        private static readonly ConditionalWeakTable<TickList, TickListRuntimeState> RuntimeStates =
            new ConditionalWeakTable<TickList, TickListRuntimeState>();
        // Full-list reconciliation is opt-in because it scans every bucket of
        // every async TickList on the main thread. The first-execution
        // authoritative rebuild and the per-tick pending-registration merge
        // keep membership deterministic in normal play; the full sweep only
        // closes a stale-bucket safety window when the user enables it.
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
        private static int _cachedMapsCount = int.MinValue;
        private static bool _cachedRequiresPerMapPipelines;

        private sealed class TickListRuntimeState
        {
            public Map Owner;
            public bool Initialized;
            public bool Dirty;
            public int LastReconcileTick;
            public bool[] SortedCache;
            public bool[] SortedKnownCache;
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
            if (!RuntimeTickListMutationEnabled)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Async TickList mutation patch disabled: " +
                    "client-local snapshot rebuild/reconcile can diverge from the host.");
                return;
            }

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

            if (!OptimizationGate.IsTickListFullReconcileEnabled)
            {
                OptimizationGate.LogOnce(
                    "ticklist.full.reconcile",
                    "[MP-MeowOnlineShop] Async TickList full-bucket reconcile is disabled; " +
                    "first-execution authoritative rebuild and pending-registration merge remain active.");
            }
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
            return RuntimeStates.GetOrCreateValue(tickList);
        }

        private static void BindAsyncTickList(TickList tickList, Map map)
        {
            if (tickList == null || map == null)
                return;

            BindAsyncTickList(tickList, map, GetOrCreateState(tickList));
        }

        private static void BindAsyncTickList(
            TickList tickList,
            Map map,
            TickListRuntimeState state)
        {
            if (tickList == null || map == null || state == null)
                return;

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

                var preservedNormalMotes = CaptureMotes(normal);
                var preservedRareMotes = CaptureMotes(rare);
                var preservedLongMotes = CaptureMotes(longTicks);

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

                normalCount += ReaddMotes(normal, map, preservedNormalMotes);
                rareCount += ReaddMotes(rare, map, preservedRareMotes);
                longCount += ReaddMotes(longTicks, map, preservedLongMotes);

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
            // Rebuilding membership from listerThings was proven to diverge
            // host/client (Desync-342/343). This boundary does not perform a
            // full rebuild; the order normalizer may only append spawned entries
            // proven missing from the reconstructed buckets on the first real tick.
            Patch_TickListOrderNormalizer.NormalizeAllBuckets(tickList, map);
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

        private static List<Mote> CaptureMotes(TickList tickList)
        {
            var result = new List<Mote>();
            if (tickList == null)
                return result;

            var seen = new HashSet<Mote>();
            var buckets = ThingListsRef(tickList);
            if (buckets != null)
            {
                for (int i = 0; i < buckets.Count; i++)
                {
                    List<Thing> bucket = buckets[i];
                    if (bucket == null)
                        continue;
                    for (int j = 0; j < bucket.Count; j++)
                    {
                        if (bucket[j] is Mote mote && seen.Add(mote))
                            result.Add(mote);
                    }
                }
            }

            List<Thing> pending = ThingsToRegisterRef(tickList);
            if (pending != null)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (pending[i] is Mote mote && seen.Add(mote))
                        result.Add(mote);
                }
            }

            return result;
        }

        private static int ReaddMotes(
            TickList tickList,
            Map ownerMap,
            List<Mote> motes)
        {
            if (tickList == null || ownerMap == null || motes == null)
                return 0;

            var buckets = ThingListsRef(tickList);
            if (buckets == null || buckets.Count == 0)
                return 0;

            int added = 0;
            for (int i = 0; i < motes.Count; i++)
            {
                Mote mote = motes[i];
                if (mote == null || mote.Destroyed || !mote.Spawned ||
                    mote.Map != ownerMap)
                {
                    continue;
                }

                AddDirect(tickList, mote);
                added++;
            }

            return added;
        }

        private static void AddDirect(TickList tickList, Thing thing)
        {
            var buckets = ThingListsRef(tickList);
            if (buckets == null || buckets.Count == 0)
                return;

            int hash = StableHash(thing);
            buckets[hash % buckets.Count].Add(thing);
        }

        private static void ProcessTickList(
            TickList tickList,
            Map ownerMap,
            TickListRuntimeState state,
            int currentTick)
        {
            if (tickList == null || ownerMap == null || state == null)
                return;

            if (state.Owner != ownerMap)
            {
                state.Owner = ownerMap;
                state.Initialized = true;
                state.Dirty = true;
            }

            bool firstTick = !state.Initialized;
            bool safetyDue = IsSafetyReconcileDue(state, currentTick);
            if (!firstTick && !state.Dirty && !safetyDue)
                return;

            state.Initialized = true;

            var buckets = ThingListsRef(tickList);
            var pending = ThingsToRegisterRef(tickList);
            if (buckets == null || pending == null || buckets.Count == 0)
                return;

            TickerType firstTickType = TickerType.Never;
            if (firstTick)
            {
                int rebuilt = RebuildSingleTickList(
                    tickList, ownerMap, out uint membershipFingerprint);
                firstTickType = GetTickType(tickList);
                Log.Message(
                    "[MP-MeowOnlineShop] Rebuilt async TickList immediately before " +
                    $"its first execution: map={ownerMap.uniqueID}, " +
                    $"ticker={firstTickType}, members={rebuilt}, " +
                    $"fingerprint={membershipFingerprint:X8}.");
            }

            if (pending.Count > 0)
            {
                MergePendingRegistrations(buckets, pending, ownerMap, state);
                // Preserve the original guarantee that every bucket is in
                // stable-ID order after a registration tick, including buckets
                // that received no registration (a cheap sortedness check; only
                // actually unsorted buckets are re-sorted). Buckets already
                // normalized by the merge are skipped.
                for (int i = 0; i < buckets.Count; i++)
                {
                    if (state.SortedKnownCache != null &&
                        i < state.SortedKnownCache.Length &&
                        state.SortedKnownCache[i])
                    {
                        continue;
                    }

                    List<Thing> bucket = buckets[i];
                    if (bucket != null && bucket.Count > 1 && !IsSorted(bucket))
                        bucket.Sort(CompareStableThings);
                }
                state.Dirty = true;
            }

            if (safetyDue)
            {
                state.LastReconcileTick = currentTick;
                if (OptimizationGate.IsTickListFullReconcileEnabled)
                {
                    ReconcileAllBuckets(
                        buckets,
                        ownerMap,
                        firstTickType != TickerType.Never
                            ? firstTickType
                            : GetTickType(tickList));
                }
            }
            else
            {
                state.Dirty = false;
            }

            // Buckets stay sorted incrementally: pending registrations are
            // inserted at their stable-ID position and ReconcileAllBuckets
            // re-sorts after rebuilding. Only the first-tick authoritative
            // rebuild appends preserved motes out of order, so restore order
            // once there instead of re-sorting every bucket on every
            // registration tick.
            if (firstTick)
            {
                for (int i = 0; i < buckets.Count; i++)
                {
                    List<Thing> bucket = buckets[i];
                    if (bucket != null && bucket.Count > 1)
                        bucket.Sort(CompareStableThings);
                }
            }
        }

        private static void MergePendingRegistrations(
            List<List<Thing>> buckets,
            List<Thing> pending,
            Map ownerMap,
            TickListRuntimeState state)
        {
            int bucketCount = buckets.Count;
            bool[] sorted = state.SortedCache;
            bool[] sortedKnown = state.SortedKnownCache;
            if (sorted == null || sorted.Length < bucketCount)
            {
                sorted = new bool[bucketCount];
                sortedKnown = new bool[bucketCount];
                state.SortedCache = sorted;
                state.SortedKnownCache = sortedKnown;
            }
            Array.Clear(sorted, 0, bucketCount);
            Array.Clear(sortedKnown, 0, bucketCount);

            for (int i = 0; i < pending.Count; i++)
            {
                Thing thing = pending[i];
                if (thing == null || IsInvalidForOwner(thing, ownerMap))
                    continue;

                RemoveAllOccurrences(buckets, thing);

                int index = StableHash(thing) % bucketCount;
                List<Thing> bucket = buckets[index];
                if (!sortedKnown[index])
                {
                    sortedKnown[index] = true;
                    sorted[index] = IsSorted(bucket);
                }

                if (sorted[index])
                {
                    bucket.Insert(LowerBound(bucket, thing), thing);
                }
                else
                {
                    // A bucket that is not in stable-ID order (for example a
                    // stale tail left by an out-of-band append) is re-normalized
                    // with a full sort so the resulting tick order stays a pure
                    // function of membership and never depends on registration
                    // history.
                    bucket.Add(thing);
                    if (bucket.Count > 1)
                        bucket.Sort(CompareStableThings);
                    sorted[index] = true;
                }
            }
            pending.Clear();
        }

        private static bool IsSorted(List<Thing> list)
        {
            int count = list.Count;
            for (int i = 1; i < count; i++)
            {
                Thing left = list[i - 1];
                Thing right = list[i];
                if (left == null || right == null)
                {
                    if (CompareStableThings(left, right) > 0)
                        return false;
                    continue;
                }

                int leftId = left.thingIDNumber;
                int rightId = right.thingIDNumber;
                if (leftId >= 0 && rightId >= 0)
                {
                    if (leftId > rightId)
                        return false;
                    continue;
                }

                if (CompareStableThings(left, right) > 0)
                    return false;
            }
            return true;
        }

        private static int LowerBound(List<Thing> list, Thing thing)
        {
            int lo = 0;
            int hi = list.Count;
            int targetId = thing.thingIDNumber;
            if (targetId >= 0)
            {
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    Thing candidate = list[mid];
                    if (candidate == null || candidate.thingIDNumber < targetId)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                return lo;
            }

            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (CompareStableThings(list[mid], thing) < 0)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        private static bool RequiresVanillaPerMapPipelinesCached()
        {
            int mapsCount = Find.Maps?.Count ?? 0;
            if (mapsCount == _cachedMapsCount)
                return _cachedRequiresPerMapPipelines;

            // Async time is fixed for a session and the per-map pipeline mode
            // only changes when maps are added or removed, so re-resolve through
            // reflection only on those rare transitions instead of every tick.
            _cachedMapsCount = mapsCount;
            MpRuntimeInfo.TryGetAsyncTimeActive(out bool asyncTimeActive);
            _cachedRequiresPerMapPipelines = asyncTimeActive || mapsCount > 1;
            return _cachedRequiresPerMapPipelines;
        }

        private static void TickPrefix(TickList __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            bool requiresPipelines = RequiresVanillaPerMapPipelinesCached();
            if (!_loggedComplexMapCoverage && requiresPipelines)
            {
                _loggedComplexMapCoverage = true;
                string reason = (Find.Maps?.Count ?? 0) > 1
                    ? "multiple maps"
                    : "async-time";
                Log.Message(
                    "[MP-MeowOnlineShop] Deterministic TickList guard covers each Multiplayer " +
                    $"per-map TickList independently ({reason}); pending registrations are merged " +
                    "and stable thing-ID order is restored before the first map tick.");
            }

            if (!_asyncTickPatchActive && !requiresPipelines)
                return;

            TickListRuntimeState state = RuntimeStates.GetOrCreateValue(__instance);
            if (state.Owner == null)
            {
                if (_asyncTickPatchActive)
                    return;

                Map runtimeOwner = ResolveTickListOwner(__instance);
                if (runtimeOwner == null)
                    return;
                state.Owner = runtimeOwner;
                state.Initialized = true;
                state.Dirty = true;
            }

            ProcessTickList(
                __instance,
                state.Owner,
                state,
                Find.TickManager?.TicksGame ?? 0);
        }

        private static void ReconcileAllBuckets(
            List<List<Thing>> buckets,
            Map ownerMap,
            TickerType tickType)
        {
            if (buckets == null || buckets.Count == 0 || ownerMap == null)
                return;

            int bucketCount = buckets.Count;

            // Snapshot current membership (valid motes are preserved separately)
            // and clear the buckets before rebuilding from the authoritative
            // spawned registry. This avoids allocating a per-bucket expected
            // list array and a second full pass over every bucket.
            var oldSet = new HashSet<Thing>();
            List<Thing> preservedMotes = null;
            for (int i = 0; i < bucketCount; i++)
            {
                List<Thing> bucket = buckets[i];
                if (bucket == null)
                    continue;
                for (int j = 0; j < bucket.Count; j++)
                {
                    Thing thing = bucket[j];
                    if (thing == null)
                        continue;
                    if (IsVisualMote(thing))
                    {
                        if (!thing.Destroyed && thing.Spawned && thing.Map == ownerMap)
                        {
                            if (preservedMotes == null)
                                preservedMotes = new List<Thing>();
                            preservedMotes.Add(thing);
                        }
                        continue;
                    }
                    oldSet.Add(thing);
                }
                bucket.Clear();
            }

            var expectedSet = new HashSet<Thing>();
            int added = 0;
            List<string> addedIds = null;
            List<Thing> allThings = ownerMap.listerThings?.AllThings;
            if (allThings != null)
            {
                var seen = new HashSet<Thing>();
                for (int i = 0; i < allThings.Count; i++)
                {
                    Thing thing = allThings[i];
                    if (thing == null ||
                        thing.Destroyed ||
                        thing.def == null ||
                        thing.Map != ownerMap ||
                        !BelongsToTickList(thing, tickType) ||
                        IsVisualMote(thing) ||
                        !seen.Add(thing))
                    {
                        continue;
                    }

                    buckets[StableHash(thing) % bucketCount].Add(thing);
                    expectedSet.Add(thing);

                    if (!oldSet.Contains(thing))
                    {
                        added++;
                        if (addedIds == null)
                            addedIds = new List<string>();
                        if (addedIds.Count < 8)
                            addedIds.Add(thing.ThingID ?? "<null>");
                    }
                }
            }

            if (preservedMotes != null)
            {
                for (int i = 0; i < preservedMotes.Count; i++)
                {
                    Thing mote = preservedMotes[i];
                    buckets[StableHash(mote) % bucketCount].Add(mote);
                }
            }

            int removed = 0;
            List<string> removedIds = null;
            foreach (Thing thing in oldSet)
            {
                if (expectedSet.Contains(thing))
                    continue;
                removed++;
                if (removedIds == null)
                    removedIds = new List<string>();
                if (removedIds.Count < 8)
                    removedIds.Add(thing?.ThingID ?? "<null>");
            }

            for (int i = 0; i < bucketCount; i++)
            {
                if (buckets[i].Count > 1)
                    buckets[i].Sort(CompareStableThings);
            }

            if (removed > 0 && LoggedStaleMemberMaps.Add(ownerMap.uniqueID))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Removed stale, cross-map, duplicate, or wrong-bucket " +
                    "TickList members before " +
                    $"execution: map={ownerMap.uniqueID}, bucket=<all>, " +
                    $"removed={removed}, ids={string.Join(",", removedIds ?? new List<string>())}.");
            }

            if (added > 0 && LoggedMissingNormalThingMaps.Add(ownerMap.uniqueID))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Restored missing spawned TickList " +
                    $"members before execution: map={ownerMap.uniqueID}, " +
                    $"bucket=<all>, added={added}, " +
                    $"ids={string.Join(",", addedIds ?? new List<string>())}.");
            }
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
            var preservedMotes = CaptureMotes(tickList);
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

            count += ReaddMotes(tickList, ownerMap, preservedMotes);

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

            // Manual loop instead of RemoveAll with a capturing lambda so the
            // pending-registration merge does not allocate a closure per thing
            // and can skip empty buckets cheaply.
            for (int i = 0; i < buckets.Count; i++)
            {
                List<Thing> bucket = buckets[i];
                if (bucket == null || bucket.Count == 0)
                    continue;
                for (int j = bucket.Count - 1; j >= 0; j--)
                {
                    if (ReferenceEquals(bucket[j], thing))
                        bucket.RemoveAt(j);
                }
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

            int leftId = left.thingIDNumber;
            int rightId = right.thingIDNumber;
            if (leftId != rightId)
                return leftId > rightId ? 1 : -1;

            if (leftId >= 0)
                return 0;

            return string.CompareOrdinal(left.ThingID, right.ThingID);
        }

    }
}
