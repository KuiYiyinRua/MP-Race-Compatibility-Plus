using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-54 showed that Multiplayer's built-in StorageSettings sync worker can fail while
    /// deserializing the target owner.  Intercept the exact vanilla priority-menu callback and
    /// send only stable primitive identifiers instead of serializing StorageSettings itself.
    /// </summary>
    internal static class Patch_StoragePriorityMp
    {
        private const int OwnerThing = 1;
        private const int OwnerZone = 2;
        private const int OwnerGroup = 3;
        private const int OwnerThingComp = 4;
        private const int MaxWarnings = 8;

        private static FieldInfo _settingsField;
        private static FieldInfo _priorityField;
        private static FieldInfo _settingsClosureField;
        private static int _warningCount;

        public static void Apply(Harmony harmony)
        {
            if (!MP.enabled)
                return;

            MP.RegisterSyncMethod(typeof(Patch_StoragePriorityMp), nameof(SyncSetPriorityByOwnerId));

            MethodInfo callback = FindVanillaPriorityCallback();
            if (callback == null)
            {
                Warn("ITab_Storage priority callback was not found; stable priority sync was not installed.");
                return;
            }

            harmony.Patch(callback,
                prefix: new HarmonyMethod(typeof(Patch_StoragePriorityMp), nameof(PriorityCallbackPrefix))
                {
                    priority = Priority.First
                });

            Log.Message($"[MP-MeowOnlineShop] Storage priority stable-owner sync patched: {callback.DeclaringType?.FullName}.{callback.Name}.");
        }

        private static MethodInfo FindVanillaPriorityCallback()
        {
            Type[] nestedTypes = typeof(ITab_Storage).GetNestedTypes(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            foreach (Type nested in nestedTypes)
            {
                FieldInfo priority = nested.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(field => field.FieldType == typeof(StoragePriority));
                if (priority == null)
                    continue;

                FieldInfo closure = nested.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(field => nestedTypes.Contains(field.FieldType));
                Type settingsOwner = closure?.FieldType ?? nested;
                FieldInfo settings = settingsOwner
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(field => field.FieldType == typeof(StorageSettings));
                if (settings == null || priority == null)
                    continue;

                MethodInfo callback = nested.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method => method.Name.StartsWith("<FillTab>b__", StringComparison.Ordinal)
                                              && method.ReturnType == typeof(void)
                                              && method.GetParameters().Length == 0);
                if (callback == null)
                    continue;

                _settingsField = settings;
                _priorityField = priority;
                _settingsClosureField = closure;
                return callback;
            }

            return null;
        }

        private static bool PriorityCallbackPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || __instance == null)
                return true;

            object settingsOwner = _settingsClosureField == null
                ? __instance
                : _settingsClosureField.GetValue(__instance);
            StorageSettings settings = _settingsField?.GetValue(settingsOwner) as StorageSettings;
            if (settings?.owner == null || _priorityField == null)
            {
                Warn("Priority callback had no resolvable StorageSettings owner; falling back to vanilla behavior.");
                return true;
            }

            StoragePriority priority = (StoragePriority)_priorityField.GetValue(__instance);
            if (!TryDescribeOwner(settings.owner, out int mapIndex, out int ownerKind, out int ownerId, out string compType))
            {
                Warn($"Unsupported storage owner {settings.owner.GetType().FullName}; falling back to vanilla sync.");
                return true;
            }

            SyncSetPriorityByOwnerId(mapIndex, ownerKind, ownerId, compType, (int)priority);
            return false;
        }

        private static bool TryDescribeOwner(IStoreSettingsParent owner, out int mapIndex, out int ownerKind, out int ownerId, out string compType)
        {
            mapIndex = -1;
            ownerKind = 0;
            ownerId = -1;
            compType = null;

            if (owner is Thing thing && thing.MapHeld != null)
            {
                mapIndex = thing.MapHeld.Index;
                ownerKind = OwnerThing;
                ownerId = thing.thingIDNumber;
                return ownerId >= 0;
            }

            if (owner is ThingComp comp && comp.parent?.MapHeld != null)
            {
                mapIndex = comp.parent.MapHeld.Index;
                ownerKind = OwnerThingComp;
                ownerId = comp.parent.thingIDNumber;
                compType = comp.GetType().FullName;
                return ownerId >= 0 && !string.IsNullOrEmpty(compType);
            }

            if (owner is Zone zone && zone.Map != null)
            {
                mapIndex = zone.Map.Index;
                ownerKind = OwnerZone;
                ownerId = zone.ID;
                return ownerId >= 0;
            }

            if (owner is StorageGroup group && group.Map != null)
            {
                mapIndex = group.Map.Index;
                ownerKind = OwnerGroup;
                ownerId = group.loadID;
                return ownerId >= 0;
            }

            return false;
        }

        private static void SyncSetPriorityByOwnerId(int mapIndex, int ownerKind, int ownerId, string compType, int priorityValue)
        {
            Map map = Find.Maps?.FirstOrDefault(candidate => candidate != null && candidate.Index == mapIndex);
            if (map == null)
            {
                Warn($"Storage priority replay could not resolve map {mapIndex}.");
                return;
            }

            IStoreSettingsParent owner = ResolveOwner(map, ownerKind, ownerId, compType);
            StorageSettings settings = owner?.GetStoreSettings();
            if (settings == null)
            {
                Warn($"Storage priority replay could not resolve owner kind={ownerKind} id={ownerId} map={mapIndex} comp={compType ?? "<none>"}.");
                return;
            }

            settings.Priority = (StoragePriority)priorityValue;
        }

        private static IStoreSettingsParent ResolveOwner(Map map, int ownerKind, int ownerId, string compType)
        {
            if (ownerKind == OwnerZone)
                return map.zoneManager?.AllZones?.FirstOrDefault(zone => zone != null && zone.ID == ownerId) as IStoreSettingsParent;

            if (ownerKind == OwnerGroup)
                return map.storageGroups?.StorageGroupsForReading?.FirstOrDefault(group => group != null && group.loadID == ownerId);

            Thing thing = map.listerThings?.AllThings?.FirstOrDefault(candidate => candidate != null && candidate.thingIDNumber == ownerId);
            if (thing == null)
                return null;
            if (ownerKind == OwnerThing)
                return thing as IStoreSettingsParent;
            if (ownerKind == OwnerThingComp && thing is ThingWithComps withComps)
                return withComps.AllComps?.FirstOrDefault(comp => comp != null && comp.GetType().FullName == compType) as IStoreSettingsParent;
            return null;
        }

        private static void Warn(string message)
        {
            if (_warningCount++ < MaxWarnings)
                Log.Warning("[MP-MeowOnlineShop] " + message);
        }
    }

    /// <summary>
    /// MultiplayerAsyncQuest.TickQuests enumerates mutable cache lists. Quest.End removes the
    /// active quest from the same cache, invalidating foreach. Replace the input with a snapshot.
    /// </summary>
    internal static class Patch_MultiplayerAsyncQuestSnapshot
    {
        private const int QuestTickSeedOffset = 0x51554954;

        private static bool _loggedDuplicateQuestCacheEntry;

        private sealed class QuestReferenceComparer : IEqualityComparer<Quest>
        {
            internal static readonly QuestReferenceComparer Instance = new QuestReferenceComparer();

            public bool Equals(Quest left, Quest right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(Quest quest)
            {
                return quest == null ? 0 : RuntimeHelpers.GetHashCode(quest);
            }
        }

        [ThreadStatic]
        private static Map _questMapForPop;

        public static void Apply(Harmony harmony)
        {
            Type asyncQuest = AccessTools.TypeByName("Multiplayer.Client.Comp.MultiplayerAsyncQuest");
            MethodInfo tickQuests = asyncQuest == null
                ? null
                : AccessTools.Method(asyncQuest, "TickQuests", new[] { typeof(IEnumerable<Quest>) });
            if (tickQuests == null)
            {
                Log.Error("[MP-MeowOnlineShop] MultiplayerAsyncQuest.TickQuests(IEnumerable<Quest>) not found; mutable quest-cache guard was NOT installed.");
                return;
            }

            if (!tickQuests.IsStatic || tickQuests.ReturnType != typeof(void))
            {
                Log.Error("[MP-MeowOnlineShop] MultiplayerAsyncQuest.TickQuests signature changed; mutable quest-cache guard was NOT installed: " + tickQuests.FullDescription());
                return;
            }

            harmony.Patch(tickQuests,
                prefix: new HarmonyMethod(typeof(Patch_MultiplayerAsyncQuestSnapshot), nameof(TickQuestsPrefix))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(typeof(Patch_MultiplayerAsyncQuestSnapshot), nameof(TickQuestsFinalizer))
                {
                    priority = Priority.Last
                });
            Log.Message(
                "[MP-MeowOnlineShop] MultiplayerAsyncQuest.TickQuests(IEnumerable<Quest>) " +
                "snapshot/order/dedup/Rand guard patched for world and per-map quest caches.");
        }

        private static void TickQuestsPrefix(
            ref IEnumerable<Quest> __0,
            ref int __state)
        {
            __state = 0;
            _questMapForPop = null;

            Quest[] snapshot = __0 as Quest[];
            if (snapshot == null && __0 != null)
                snapshot = __0.ToArray();

            // Multiplayer's SetContextForAccept calls CacheQuest again.  The
            // upstream world-quest branch uses Add rather than an upsert, so
            // accepting a world quest can leave the same Quest reference in
            // worldQuestsCache twice.  That makes the same QuestTick (and any
            // signal/event it emits) run twice per world tick.  Repair the
            // backing List when possible and always pass a unique snapshot to
            // the foreach below.
            if (snapshot != null && snapshot.Length > 1)
            {
                var seen = new HashSet<Quest>(QuestReferenceComparer.Instance);
                var unique = new List<Quest>(snapshot.Length);
                int duplicateCount = 0;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    Quest quest = snapshot[i];
                    if (seen.Add(quest))
                        unique.Add(quest);
                    else
                        duplicateCount++;
                }

                if (duplicateCount > 0)
                {
                    snapshot = unique.ToArray();
                    var cachedList = __0 as List<Quest>;
                    if (cachedList != null)
                    {
                        cachedList.Clear();
                        cachedList.AddRange(snapshot);
                    }

                    if (!_loggedDuplicateQuestCacheEntry)
                    {
                        _loggedDuplicateQuestCacheEntry = true;
                        Log.Warning(
                            "[MP-MeowOnlineShop] Removed duplicate Quest reference(s) from " +
                            $"MultiplayerAsyncQuest cache before ticking: count={duplicateCount}, " +
                            $"tick={Find.TickManager?.TicksAbs ?? 0}.");
                    }
                }
            }

            // Desync-137: two quests (shuttle completion vs ritual-quest
            // refresh) consumed world Rand/letter IDs in a different order on
            // the two peers during the same world tick. Sort by stable Quest id
            // so every peer ticks the same quests in the same order regardless
            // of cache/list insertion order, then isolate the batch Rand.
            if (snapshot != null && snapshot.Length > 1)
            {
                bool sorted = true;
                for (int i = 1; i < snapshot.Length; i++)
                {
                    if ((snapshot[i - 1]?.id ?? 0) > (snapshot[i]?.id ?? 0))
                    {
                        sorted = false;
                        break;
                    }
                }

                if (!sorted)
                    Array.Sort(snapshot, CompareQuestById);
            }

            __0 = snapshot;

            if (!MP.IsInMultiplayer)
                return;

            try
            {
                int seed = Gen.HashCombineInt(
                    QuestTickSeedOffset,
                    Find.TickManager?.TicksAbs ?? 0);
                int state = 0;
                DeterministicRandScope.Begin(
                    null,
                    seed,
                    QuestTickSeedOffset + 1,
                    ref state,
                    out Map mapForPop,
                    ignoreGate: true);
                _questMapForPop = mapForPop;
                __state = state;
            }
            catch
            {
                __state = 0;
                _questMapForPop = null;
            }
        }

        private static int CompareQuestById(Quest left, Quest right)
        {
            int leftId = left?.id ?? 0;
            int rightId = right?.id ?? 0;
            return leftId.CompareTo(rightId);
        }

        private static Exception TickQuestsFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
            {
                DeterministicRandScope.End(__state, _questMapForPop);
                _questMapForPop = null;
            }

            return __exception;
        }
    }
}
