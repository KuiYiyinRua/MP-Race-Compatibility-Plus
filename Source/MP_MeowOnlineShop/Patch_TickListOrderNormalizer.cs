using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Rejoin-safe per-bucket TickList order normalization.
    ///
    /// Multiplayer snapshots serialize map state but not the per-map TickList
    /// buckets. A long-running host keeps its historical registration order,
    /// while a rejoining client rebuilds buckets from SpawnSetup order. The
    /// same Normal bucket then ticks pawns and buildings in a different order,
    /// so the first map-Rand/UniqueID call inside a tick diverges within tens
    /// of ticks after rejoin (Desync-462..475 pattern).
    ///
    /// This patch preserves existing membership, but replaces vanilla's
    /// bucket assignment with an explicit thingID-based assignment in
    /// multiplayer. After a snapshot load, it only queues spawned tickables that
    /// are missing from the reconstructed buckets; the queue is merged
    /// deterministically before the first real tick. The order inside each bucket
    /// is a pure function of thingID; things without a thingID (visual motes) stay
    /// after all ID'd things so their peer-local object hash cannot reorder
    /// simulation-relevant tickers.
    /// </summary>
    internal static class Patch_TickListOrderNormalizer
    {
        private static readonly AccessTools.FieldRef<TickList, List<List<Thing>>> ThingListsRef =
            TryGetThingListsRef();
        private static readonly AccessTools.FieldRef<TickList, List<Thing>> ThingsToRegisterRef =
            TryGetThingsToRegisterRef();
        private static readonly ConditionalWeakTable<TickList, State> States =
            new ConditionalWeakTable<TickList, State>();
        private static Type _asyncTimeCompType;
        private static AccessTools.FieldRef<object, Map> _asyncMapRef;
        private static AccessTools.FieldRef<object, TickList> _asyncNormalRef;
        private static AccessTools.FieldRef<object, TickList> _asyncRareRef;
        private static AccessTools.FieldRef<object, TickList> _asyncLongRef;

        private sealed class State
        {
            public bool[] Sorted;
            public bool Logged;
            public bool LoggedTickSort;
            public Map Owner;
            public bool MembershipRepairPending;
        }

        // Latched before any TickList mutation. Never switch schedules mid-session.
        internal static bool EnabledAtStartup { get; private set; }
        private static bool _applied;
        private static bool _loggedFailure;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;
            EnabledAtStartup = MpMeowOnlineShopMod.Settings?.enableTickListOrderNormalizer ?? true;
            Log.Message("[MP-MeowOnlineShop] TickList order normalizer startup setting=" + EnabledAtStartup +
                "; independent of TPS presets; changes require all peers to restart.");
            if (!EnabledAtStartup)
                return;

            MethodInfo target = AccessTools.Method(typeof(TickList), nameof(TickList.Tick));
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_TickListOrderNormalizer),
                nameof(TickPrefix));
            if (target == null || prefix == null ||
                ThingListsRef == null || ThingsToRegisterRef == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] TickList order normalizer targets " +
                    "unresolved; skipped.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });

            // Installed Verse.Thing.GetHashCode returns thingIDNumber for assigned
            // IDs; only unassigned objects use object identity. Keep the existing
            // override, without claiming ordinary pawn buckets were process-random.
            PatchStableBucketOf(harmony);

            PatchFinalizeInit(harmony);

            Log.Message(
                "[MP-MeowOnlineShop] TickList order normalizer active: per-bucket " +
                "tick order is a pure function of thingID in multiplayer.");
        }

        private static void PatchStableBucketOf(Harmony harmony)
        {
            MethodInfo bucketOf = AccessTools.Method(
                typeof(TickList),
                "BucketOf",
                new[] { typeof(Thing) });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_TickListOrderNormalizer),
                nameof(BucketOfPrefix));
            if (bucketOf == null || prefix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] TickList stable bucket patch target " +
                    "unresolved; registration may still depend on object hash.");
                return;
            }

            try
            {
                harmony.Patch(
                    bucketOf,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                Log.Message(
                    "[MP-MeowOnlineShop] TickList stable bucket active: " +
                    "thingID-based bucket assignment replaces object hash in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] TickList stable bucket patch failed: " +
                    e.Message);
            }
        }

        private static bool BucketOfPrefix(
            TickList __instance,
            Thing t,
            ref List<Thing> __result)
        {
            if (!EnabledAtStartup || !MP.IsInMultiplayer || __instance == null || t == null ||
                t.thingIDNumber < 0 || ThingListsRef == null)
            {
                return true;
            }

            List<List<Thing>> buckets = ThingListsRef(__instance);
            if (buckets == null || buckets.Count == 0)
                return true;

            int index = t.thingIDNumber % buckets.Count;
            if (index < 0)
                index += buckets.Count;

            __result = buckets[index];
            return false;
        }

        private static void PatchFinalizeInit(Harmony harmony)
        {
            _asyncTimeCompType = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
            MethodInfo finalizeInit = _asyncTimeCompType == null
                ? null
                : AccessTools.Method(_asyncTimeCompType, "FinalizeInit");
            MethodInfo postfix = AccessTools.Method(
                typeof(Patch_TickListOrderNormalizer),
                nameof(FinalizeInitPostfix));
            if (finalizeInit == null || postfix == null)
                return;

            _asyncMapRef = TryGetAsyncMapRef();
            _asyncNormalRef = TryGetAsyncTickListRef("tickListNormal");
            _asyncRareRef = TryGetAsyncTickListRef("tickListRare");
            _asyncLongRef = TryGetAsyncTickListRef("tickListLong");
            if (_asyncMapRef == null ||
                _asyncNormalRef == null || _asyncRareRef == null || _asyncLongRef == null)
                return;

            harmony.Patch(
                finalizeInit,
                postfix: new HarmonyMethod(postfix)
                {
                    priority = Priority.Last
                });
        }

        private static void FinalizeInitPostfix(object __instance)
        {
            if (!EnabledAtStartup || !MP.IsInMultiplayer || __instance == null ||
                _asyncMapRef == null ||
                _asyncNormalRef == null || _asyncRareRef == null || _asyncLongRef == null)
            {
                return;
            }

            try
            {
                Map map = _asyncMapRef(__instance);
                TickList normal = _asyncNormalRef(__instance);
                TickList rare = _asyncRareRef(__instance);
                TickList longList = _asyncLongRef(__instance);
                NormalizeAllBuckets(normal, map);
                NormalizeAllBuckets(rare, map);
                NormalizeAllBuckets(longList, map);

                // FinalizeInit can run before the snapshot loader repopulates
                // the TickList buckets. Do not let that empty-list pass mark
                // the buckets clean, or the first real tick will skip the
                // normalization and preserve peer-local load order.
                InvalidateBucketSorts(normal);
                InvalidateBucketSorts(rare);
                InvalidateBucketSorts(longList);
                MarkMembershipRepairPending(normal);
                MarkMembershipRepairPending(rare);
                MarkMembershipRepairPending(longList);
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] TickList order normalization at " +
                        "FinalizeInit failed: " + e.Message);
                }
            }
        }

        internal static void NormalizeAllBuckets(TickList tickList, Map ownerMap = null)
        {
            if (!EnabledAtStartup || tickList == null || ThingListsRef == null)
                return;

            List<List<Thing>> buckets = ThingListsRef(tickList);
            if (buckets == null || buckets.Count == 0)
                return;

            State state = States.GetOrCreateValue(tickList);
            if (ownerMap != null)
                state.Owner = ownerMap;
            if (state.Sorted == null || state.Sorted.Length != buckets.Count)
                state.Sorted = new bool[buckets.Count];

            int idedTotal = 0;
            int unidedTotal = 0;
            for (int i = 0; i < buckets.Count; i++)
            {
                int idedBefore = CountIded(buckets[i]);
                int unidedBefore = buckets[i] == null ? 0 : buckets[i].Count - idedBefore;
                SortBucket(buckets[i]);
                idedTotal += idedBefore;
                unidedTotal += unidedBefore;
                state.Sorted[i] = true;
            }

            if (!state.Logged)
            {
                state.Logged = true;
                Log.Message(
                    "[MP-MeowOnlineShop] TickList order normalized: " +
                    "buckets=" + buckets.Count + ", ided=" + idedTotal +
                    ", unided=" + unidedTotal + ".");
            }
        }

        private static void InvalidateBucketSorts(TickList tickList)
        {
            if (!EnabledAtStartup || tickList == null || ThingListsRef == null)
                return;

            List<List<Thing>> buckets = ThingListsRef(tickList);
            if (buckets == null || buckets.Count == 0)
                return;

            State state = States.GetOrCreateValue(tickList);
            if (state.Sorted == null || state.Sorted.Length != buckets.Count)
            {
                state.Sorted = new bool[buckets.Count];
                return;
            }

            Array.Clear(state.Sorted, 0, state.Sorted.Length);
        }

        private static int CountIded(List<Thing> bucket)
        {
            if (bucket == null)
                return 0;
            int count = 0;
            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i] != null && bucket[i].thingIDNumber >= 0)
                    count++;
            }
            return count;
        }

        private static int CountMembers(List<Thing> bucket)
        {
            return bucket == null ? 0 : bucket.Count;
        }

        private static uint BucketFingerprint(List<Thing> bucket)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (bucket == null)
                    return hash;

                for (int i = 0; i < bucket.Count; i++)
                {
                    Thing thing = bucket[i];
                    int key = thing != null && thing.thingIDNumber >= 0
                        ? thing.thingIDNumber
                        : 0;
                    hash ^= (uint)key;
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        private static int CountMissingSpawned(
            List<List<Thing>> buckets,
            Map map,
            TickerType tickType)
        {
            if (map == null || buckets == null)
                return -1;

            var present = new HashSet<Thing>();
            for (int i = 0; i < buckets.Count; i++)
            {
                List<Thing> bucket = buckets[i];
                if (bucket == null)
                    continue;
                for (int j = 0; j < bucket.Count; j++)
                {
                    if (bucket[j] != null)
                        present.Add(bucket[j]);
                }
            }

            List<Thing> all = map.listerThings?.AllThings;
            if (all == null)
                return -1;

            int missing = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                if (thing == null || thing.Destroyed || !thing.Spawned ||
                    thing.Map != map || thing.def == null ||
                    thing.thingIDNumber < 0 ||
                    present.Contains(thing) ||
                    !BelongsToTickList(thing, tickType))
                {
                    continue;
                }

                missing++;
            }

            return missing;
        }

        private static TickerType TickTypeFromBucketCount(int bucketCount)
        {
            if (bucketCount == 1)
                return TickerType.Normal;
            if (bucketCount == 250)
                return TickerType.Rare;
            return TickerType.Long;
        }

        private static bool BelongsToTickList(Thing thing, TickerType tickType)
        {
            if (thing is Mote)
                return false;
            if (tickType == TickerType.Normal)
                return thing is IThingHolder ||
                       thing.def.tickerType == TickerType.Normal;
            return thing.def.tickerType == tickType;
        }

        private static bool TickPrefix(TickList __instance)
        {
            if (!EnabledAtStartup || !MP.IsInMultiplayer || __instance == null ||
                ThingListsRef == null || ThingsToRegisterRef == null)
            {
                return true;
            }

            try
            {
                List<List<Thing>> buckets = ThingListsRef(__instance);
                List<Thing> pending = ThingsToRegisterRef(__instance);
                if (buckets == null || pending == null || buckets.Count == 0)
                    return true;

                State state = States.GetOrCreateValue(__instance);
                if (state.Sorted == null || state.Sorted.Length != buckets.Count)
                    state.Sorted = new bool[buckets.Count];

                if (state.MembershipRepairPending && state.Owner != null)
                {
                    QueueMissingSpawned(
                        buckets,
                        pending,
                        state.Owner,
                        TickTypeFromBucketCount(buckets.Count));
                    state.MembershipRepairPending = false;
                }

                if (pending.Count > 0)
                    MergePending(buckets, pending, state);

                int tick = Find.TickManager?.TicksGame ?? 0;
                int bucketIndex = tick % buckets.Count;
                if (bucketIndex < 0)
                    bucketIndex += buckets.Count;
                if (!state.Sorted[bucketIndex])
                {
                    SortBucket(buckets[bucketIndex]);
                    state.Sorted[bucketIndex] = true;
                }

                if (!state.LoggedTickSort)
                {
                    state.LoggedTickSort = true;
                    int missing = state.Owner == null
                        ? -1
                        : CountMissingSpawned(
                            buckets,
                            state.Owner,
                            TickTypeFromBucketCount(buckets.Count));
                    Log.Message(
                        "[MP-MeowOnlineShop] TickList bucket order " +
                        "fingerprint: buckets=" + buckets.Count +
                        ", index=" + bucketIndex +
                        ", members=" + CountMembers(buckets[bucketIndex]) +
                        ", missingSpawned=" + missing +
                        ", fingerprint=" +
                        BucketFingerprint(buckets[bucketIndex]).ToString("X8") + ".");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] TickList order normalization " +
                        "failed: " + e.Message);
                }
            }

            return true;
        }

        private static void MarkMembershipRepairPending(TickList tickList)
        {
            if (tickList == null)
                return;

            State state = States.GetOrCreateValue(tickList);
            state.MembershipRepairPending = true;
        }

        private static void QueueMissingSpawned(
            List<List<Thing>> buckets,
            List<Thing> pending,
            Map map,
            TickerType tickType)
        {
            if (map == null || buckets == null || pending == null)
                return;

            var present = new HashSet<Thing>();
            for (int i = 0; i < buckets.Count; i++)
            {
                List<Thing> bucket = buckets[i];
                if (bucket == null)
                    continue;
                for (int j = 0; j < bucket.Count; j++)
                {
                    if (bucket[j] != null)
                        present.Add(bucket[j]);
                }
            }
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i] != null)
                    present.Add(pending[i]);
            }

            List<Thing> all = map.listerThings?.AllThings;
            if (all == null)
                return;

            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                if (thing == null || thing.Destroyed || !thing.Spawned ||
                    thing.Map != map || thing.def == null ||
                    thing.thingIDNumber < 0 || present.Contains(thing) ||
                    !BelongsToTickList(thing, tickType))
                {
                    continue;
                }

                pending.Add(thing);
                present.Add(thing);
            }
        }

        private static void MergePending(
            List<List<Thing>> buckets,
            List<Thing> pending,
            State state)
        {
            int bucketCount = buckets.Count;
            for (int i = 0; i < pending.Count; i++)
            {
                Thing thing = pending[i];
                if (thing == null)
                    continue;

                int index = VanillaBucketIndex(thing, bucketCount);
                if (index < 0 || index >= bucketCount)
                    continue;

                List<Thing> bucket = buckets[index];
                if (!state.Sorted[index])
                {
                    SortBucket(bucket);
                    state.Sorted[index] = true;
                }

                RemoveOccurrences(bucket, thing);
                InsertSorted(bucket, thing);
            }

            pending.Clear();
        }

        private static int VanillaBucketIndex(Thing thing, int bucketCount)
        {
            if (thing != null && thing.thingIDNumber >= 0)
            {
                int stableIndex = thing.thingIDNumber % bucketCount;
                if (stableIndex < 0)
                    stableIndex += bucketCount;
                return stableIndex;
            }

            int num = thing.GetHashCode();
            if (num < 0)
                num *= -1;
            int index = num % bucketCount;
            if (index < 0)
                index += bucketCount;
            return index;
        }

        private static void SortBucket(List<Thing> bucket)
        {
            if (bucket == null || bucket.Count < 2)
                return;

            List<Thing> ided = null;
            List<Thing> unided = null;
            for (int i = 0; i < bucket.Count; i++)
            {
                Thing thing = bucket[i];
                if (thing == null || thing.thingIDNumber < 0)
                {
                    if (unided == null)
                        unided = new List<Thing>();
                    unided.Add(thing);
                }
                else
                {
                    if (ided == null)
                        ided = new List<Thing>();
                    ided.Add(thing);
                }
            }

            if (ided != null)
                ided.Sort(CompareById);

            bucket.Clear();
            if (ided != null)
                bucket.AddRange(ided);
            if (unided != null)
                bucket.AddRange(unided);
        }

        private static int CompareById(Thing left, Thing right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;
            return left.thingIDNumber.CompareTo(right.thingIDNumber);
        }

        private static void RemoveOccurrences(List<Thing> bucket, Thing thing)
        {
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(bucket[i], thing))
                    bucket.RemoveAt(i);
            }
        }

        private static void InsertSorted(List<Thing> bucket, Thing thing)
        {
            if (thing.thingIDNumber < 0)
            {
                bucket.Add(thing);
                return;
            }

            int lo = 0;
            int hi = bucket.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                Thing candidate = bucket[mid];
                if (candidate == null)
                {
                    lo = mid + 1;
                    continue;
                }

                if (candidate.thingIDNumber < 0)
                {
                    hi = mid;
                    continue;
                }

                if (candidate.thingIDNumber < thing.thingIDNumber)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            bucket.Insert(lo, thing);
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

        private static AccessTools.FieldRef<object, TickList> TryGetAsyncTickListRef(
            string fieldName)
        {
            try
            {
                return AccessTools.FieldRefAccess<TickList>(_asyncTimeCompType, fieldName);
            }
            catch
            {
                return null;
            }
        }

        private static AccessTools.FieldRef<object, Map> TryGetAsyncMapRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<Map>(_asyncTimeCompType, "map");
            }
            catch
            {
                return null;
            }
        }
    }
}
