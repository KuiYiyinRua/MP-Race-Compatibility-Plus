using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityRandom = UnityEngine.Random;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 文化DLC / 任务系统联机兜底：在任务 Tick、事件执行与任务相关 ChoiceLetter 上稳定 Rand，
    /// 并为易脱同步的无参/整型选项回调注册 <see cref="ISyncMethod"/>。
    /// </summary>
    internal static class Patch_QuestAndIdeologyMp
    {
        private const string HarmonyId = "mp.meowonlineshop.questideo";
        private static readonly Harmony Harmony = new Harmony(HarmonyId);

        private const int SeedQuestManager = 0x51BE51;
        private const int SeedQuestInstance = 0x51BE52;
        private const int SeedIncidentWorker = 0x51BE53;
        private const int WorldSeedOffset = 0x2A3B;
        private const int UnitySeedOffset = 0x4F17;

        /// <summary>是否已对官方事件补丁打过一次性汇总日志（含 ManhunterPack 检测）。</summary>
        private static bool _loggedIncidentWorkerPatchSummary;

        /// <summary>跳过与 <see cref="Patch_VoiceroidAsAnimal"/> 中 IncidentWorker 补丁重叠的命名空间，避免双重 Rand 包裹。</summary>
        private const string VoiceroidAsAnimalNamespacePrefix = "VoiceroidAsAnimal";

        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;
        private const int StateUnityRand = 8;
        private const int StateNestedIncidentScope = 1 << 30;

        [ThreadStatic]
        private static Map _mapForRandPop;
        [ThreadStatic]
        private static int _incidentRandScopeDepth;
        [ThreadStatic]
        private static UnityEngine.Random.State _unityRandomSavedState;
        [ThreadStatic]
        private static bool _unityRandomStateCaptured;
        [ThreadStatic]
        private static Stack<Tuple<Map, UnityRandom.State, bool>> _randParents;

        private sealed class QuestTickCacheEntry
        {
            public int StableKey;
            public int BaseSeed;
            public bool KeyValid;
        }

        private static readonly ConditionalWeakTable<Quest, QuestTickCacheEntry> QuestTickCache =
            new ConditionalWeakTable<Quest, QuestTickCacheEntry>();
        private static readonly AccessTools.FieldRef<Quest, List<QuestPart>> QuestPartsRef =
            TryGetQuestPartsRef();

        private sealed class QuestMapAccessor
        {
            public Func<object, Map> GetMap;
        }

        private static readonly Dictionary<Type, QuestMapAccessor> QuestMapAccessors =
            new Dictionary<Type, QuestMapAccessor>();
        private static readonly object QuestMapAccessorLock = new object();

        private static bool IsHandledByVaaIncidentPatch(Type t)
        {
            if (t == null)
                return false;
            var ns = t.Namespace ?? "";
            return ns.StartsWith(VoiceroidAsAnimalNamespacePrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static void Apply()
        {
            if (!MP.enabled)
                return;
            try
            {
                TryPatchQuestManagerTick();
                TryPatchQuestTick();
                TryPatchAllIncidentWorkerEntrypoints();
                // Multiplayer's PersistentDialog.Click is already the single
                // synchronization boundary for ordinary DiaOption callbacks.
                // The only known async-time exception is handled narrowly by
                // Patch_AcceptJoinerWorldCommand; do not register guessed
                // ChoiceLetter method names and risk double execution.
                if (ModDebug.EnableQuestIdeologyTrace)
                    Log.Message("[MP-MeowOnlineShop] Quest/Ideology MP patches applied.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Quest/Ideology MP patch Apply failed: {e}");
            }
        }

        private static void TryPatchQuestManagerTick()
        {
            try
            {
                var t = typeof(QuestManager);
                var m = AccessTools.Method(t, "QuestManagerTick");
                if (m == null)
                {
                    if (ModDebug.EnableQuestIdeologyTrace)
                        Log.Message("[MP-MeowOnlineShop] QuestManager.QuestManagerTick not found, skip.");
                    return;
                }

                var prefix = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(QuestManagerTick_Prefix));
                var finalizer = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(QuestManagerTick_Finalizer));
                Harmony.Patch(m, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                if (ModDebug.EnableQuestIdeologyTrace)
                    Log.Message("[MP-MeowOnlineShop] Patched QuestManager.QuestManagerTick (triple Rand).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] QuestManagerTick patch failed: {e.Message}");
            }
        }

        private static void TryPatchQuestTick()
        {
            try
            {
                var t = typeof(Quest);
                var m = AccessTools.Method(t, "QuestTick");
                if (m == null)
                {
                    if (ModDebug.EnableQuestIdeologyTrace)
                        Log.Message("[MP-MeowOnlineShop] Quest.QuestTick not found, skip.");
                    return;
                }

                var prefix = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(QuestTick_Prefix));
                var finalizer = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(QuestTick_Finalizer));
                Harmony.Patch(m, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                if (ModDebug.EnableQuestIdeologyTrace)
                    Log.Message("[MP-MeowOnlineShop] Patched Quest.QuestTick (triple Rand).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] QuestTick patch failed: {e.Message}");
            }
        }

        /// <summary>
        /// 官方/模组事件：Harmony 对基类方法的补丁不会自动套用到子类覆写，
        /// 因此必须对每个具体子类解析出的 <see cref="MethodInfo"/> 按 <see cref="RuntimeMethodHandle"/> 去重后分别打补丁。
        /// 入口层与工作器层都打补丁：若某些模组在 <c>TryExecute</c> 或外层逻辑中消耗随机数，可被同一作用域稳定。
        /// </summary>
        private static void TryPatchAllIncidentWorkerEntrypoints()
        {
            try
            {
                var prefixWorker = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(IncidentTryExecuteWorker_Prefix));
                var finalizerWorker = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(IncidentTryExecuteWorker_Finalizer));
                var prefixEntry = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(IncidentTryExecute_Prefix));
                var finalizerEntry = AccessTools.Method(typeof(Patch_QuestAndIdeologyMp), nameof(IncidentTryExecute_Finalizer));
                if (prefixWorker == null || finalizerWorker == null || prefixEntry == null || finalizerEntry == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] IncidentWorker: Prefix/Finalizer not found, skip.");
                    return;
                }

                var workerHandles = new HashSet<RuntimeMethodHandle>();
                var executeHandles = new HashSet<RuntimeMethodHandle>();
                var workerSuccessCount = 0;
                var executeSuccessCount = 0;
                var manhunterFound = false;
                var pollutionRelatedPatched = new List<string>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null || asm.IsDynamic)
                        continue;

                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types.Where(x => x != null).ToArray();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract || !typeof(IncidentWorker).IsAssignableFrom(t))
                            continue;

                        if (IsHandledByVaaIncidentPatch(t))
                            continue;

                        var mWorker = AccessTools.Method(t, "TryExecuteWorker", new[] { typeof(IncidentParms) });
                        if (mWorker != null && !mWorker.IsAbstract && workerHandles.Add(mWorker.MethodHandle))
                        {
                            try
                            {
                                Harmony.Patch(mWorker, prefix: new HarmonyMethod(prefixWorker), finalizer: new HarmonyMethod(finalizerWorker));
                                workerSuccessCount++;
                                if (IsPollutionRelatedIncidentWorkerType(t))
                                    pollutionRelatedPatched.Add(t.FullName ?? t.Name);
                                if (t.Name.IndexOf("ManhunterPack", StringComparison.OrdinalIgnoreCase) >= 0)
                                    manhunterFound = true;
                                if (ModDebug.EnableQuestIdeologyTrace)
                                    Log.Message($"[MP-MeowOnlineShop] IncidentWorker: patched {t.FullName}.TryExecuteWorker (stable incident Rand scope).");
                            }
                            catch (Exception ex)
                            {
                                workerHandles.Remove(mWorker.MethodHandle);
                                if (ModDebug.EnableQuestIdeologyTrace)
                                    Log.Warning($"[MP-MeowOnlineShop] IncidentWorker: patch {t.FullName}.TryExecuteWorker failed: {ex.Message}");
                            }
                        }

                        var mExecute = AccessTools.Method(t, "TryExecute", new[] { typeof(IncidentParms) });
                        if (mExecute != null && !mExecute.IsAbstract && executeHandles.Add(mExecute.MethodHandle))
                        {
                            try
                            {
                                // MP enters the destination map in its normal-priority prefix.
                                // Unwind our inner scope BEFORE MP saves and restores that map.
                                Harmony.Patch(mExecute,
                                    prefix: new HarmonyMethod(prefixEntry) { priority = Priority.Last },
                                    finalizer: new HarmonyMethod(finalizerEntry) { priority = Priority.First });
                                executeSuccessCount++;
                                if (ModDebug.EnableQuestIdeologyTrace)
                                    Log.Message($"[MP-MeowOnlineShop] IncidentWorker: patched {t.FullName}.TryExecute (stable incident Rand scope).");
                            }
                            catch (Exception ex)
                            {
                                executeHandles.Remove(mExecute.MethodHandle);
                                if (ModDebug.EnableQuestIdeologyTrace)
                                    Log.Warning($"[MP-MeowOnlineShop] IncidentWorker: patch {t.FullName}.TryExecute failed: {ex.Message}");
                            }
                        }
                    }
                }

                LogIncidentWorkerPatchSummary(
                    workerSuccessCount,
                    workerHandles.Count,
                    executeSuccessCount,
                    executeHandles.Count,
                    manhunterFound,
                    pollutionRelatedPatched);
                if (workerSuccessCount == 0 && executeSuccessCount == 0)
                    Log.Warning("[MP-MeowOnlineShop] IncidentWorker: no TryExecute/TryExecuteWorker patches applied; incidents may still desync Rand.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] IncidentWorker patch failed: {e.Message}");
            }
        }

        /// <summary>
        /// 与 <see cref="Patch_PollutionIncidentMp"/> 使用相同启发式，用于首轮摘要日志：污染/毒污/废污舱等相关 <see cref="IncidentWorker"/>。
        /// </summary>
        private static bool IsPollutionRelatedIncidentWorkerType(Type t)
        {
            if (t == null) return false;
            var fn = (t.FullName ?? t.Name ?? "").ToLowerInvariant();
            if (fn.Contains("pollution") || fn.Contains("toxifier") || fn.Contains("wastepack")) return true;
            if (fn.Contains("airpollution")) return true;
            return false;
        }

        private static void LogIncidentWorkerPatchSummary(
            int patchedWorkerCount,
            int workerHandles,
            int patchedEntryCount,
            int entryHandles,
            bool manhunterPackResolved,
            List<string> pollutionRelatedPatched)
        {
            if (_loggedIncidentWorkerPatchSummary)
                return;
            _loggedIncidentWorkerPatchSummary = true;

            var pollutionCount = pollutionRelatedPatched?.Count ?? 0;
            var sample = "";
            if (pollutionRelatedPatched != null && pollutionRelatedPatched.Count > 0)
            {
                const int maxShow = 8;
                var n = Math.Min(maxShow, pollutionRelatedPatched.Count);
                sample = string.Join(", ", pollutionRelatedPatched.Take(n).ToArray());
                if (pollutionRelatedPatched.Count > maxShow)
                    sample += ", ...";
            }

            Log.Message(
                $"[MP-MeowOnlineShop] IncidentWorker Rand stabilization: entry+worker scan, " +
                $"patchedTryExecuteWorker={patchedWorkerCount}, workerMethodHandles={workerHandles}, " +
                $"patchedTryExecute={patchedEntryCount}, entryMethodHandles={entryHandles}, " +
                $"manhunterPackWorkerPatched={(manhunterPackResolved ? "yes" : "no")}, " +
                $"pollutionRelatedWorkersTripleRandPatched={pollutionCount}" +
                (pollutionCount > 0 ? $", pollutionSample=[{sample}]" : "") +
                ".");
        }

        private static void QuestManagerTick_Prefix(ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer)
                return;
            // 不依赖 Find.CurrentMap：视角地图在双端可能不一致，会导致 Map.Rand 种子分叉。
            // QuestManager 为世界级逻辑，仅稳定静态 Rand + World.rand 即可。
            int seed = Gen.HashCombineInt(SeedQuestManager, Find.World?.info?.Seed ?? 0);
            if (!MP.IsExecutingSyncCommand)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick >= 0)
                    seed = Gen.HashCombineInt(seed, tick);
            }

            TryPushTripleRandSafe(null, seed, ref __state, "QuestManagerTick");
        }

        private static Exception QuestManagerTick_Finalizer(Exception __exception, int __state)
        {
            PopTripleRand(__state);
            return __exception;
        }

        private static void QuestTick_Prefix(Quest __instance, ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            if (!__instance.Historical)
            {
                List<QuestPart> parts = QuestPartsRef?.Invoke(__instance);
                if (parts == null || parts.Count == 0)
                    return;
            }

            var map = TryResolveMapForQuestStable(__instance);
            int seed = GetQuestCache(__instance).BaseSeed;
            if (!MP.IsExecutingSyncCommand)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick >= 0)
                    seed = Gen.HashCombineInt(seed, tick);
            }

            TryPushTripleRandSafe(map, seed, ref __state, "QuestTick");
        }

        private static QuestTickCacheEntry GetQuestCache(Quest quest)
        {
            var entry = QuestTickCache.GetOrCreateValue(quest);
            if (!entry.KeyValid)
            {
                entry.StableKey = StableQuestKey(quest);
                entry.BaseSeed = Gen.HashCombineInt(
                    SeedQuestInstance,
                    entry.StableKey);
                entry.KeyValid = true;
            }
            return entry;
        }

        private static AccessTools.FieldRef<Quest, List<QuestPart>> TryGetQuestPartsRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<Quest, List<QuestPart>>("parts");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 尝试从任务实例解析地图（反射），避免使用 Find.CurrentMap；解析失败则仅包静态+世界 Rand。
        /// </summary>
        private static Map TryResolveMapForQuestStable(Quest quest)
        {
            if (quest == null)
                return null;

            var accessor = GetQuestMapAccessor(quest.GetType());
            if (accessor?.GetMap == null)
                return null;
            try
            {
                return accessor.GetMap(quest);
            }
            catch
            {
                return null;
            }
        }

        private static QuestMapAccessor GetQuestMapAccessor(Type questType)
        {
            QuestMapAccessor accessor;
            if (QuestMapAccessors.TryGetValue(questType, out accessor))
                return accessor;

            lock (QuestMapAccessorLock)
            {
                if (QuestMapAccessors.TryGetValue(questType, out accessor))
                    return accessor;

                accessor = BuildQuestMapAccessor(questType);
                OptimizationCacheUtility.EnsureBound(QuestMapAccessors, 256);
                QuestMapAccessors[questType] = accessor;
                return accessor;
            }
        }

        private static QuestMapAccessor BuildQuestMapAccessor(Type questType)
        {
            foreach (var name in new[] { "map", "Map", "involvedMap", "parentMap" })
            {
                var field = AccessTools.Field(questType, name);
                if (field != null && typeof(Map).IsAssignableFrom(field.FieldType))
                {
                    try
                    {
                        var instance = Expression.Parameter(typeof(object), "quest");
                        var body = Expression.Convert(
                            Expression.Field(Expression.Convert(instance, questType), field),
                            typeof(Map));
                        return new QuestMapAccessor
                        {
                            GetMap = Expression.Lambda<Func<object, Map>>(body, instance).Compile()
                        };
                    }
                    catch
                    {
                        // Fall through to the next candidate.
                    }
                }

                var property = AccessTools.Property(questType, name);
                if (property != null &&
                    typeof(Map).IsAssignableFrom(property.PropertyType) &&
                    property.CanRead)
                {
                    var getter = property.GetGetMethod(true);
                    if (getter != null)
                    {
                        try
                        {
                            var instance = Expression.Parameter(typeof(object), "quest");
                            var body = Expression.Convert(
                                Expression.Call(Expression.Convert(instance, questType), getter),
                                typeof(Map));
                            return new QuestMapAccessor
                            {
                                GetMap = Expression.Lambda<Func<object, Map>>(body, instance).Compile()
                            };
                        }
                        catch
                        {
                            // Fall through to the next candidate.
                        }
                    }
                }
            }

            return new QuestMapAccessor();
        }

        /// <summary>Prefix 内安全推 Rand：失败时重置 __state，避免 Finalizer 误 Pop。</summary>
        private static void TryPushTripleRandSafe(Map map, int seed, ref int __state, string label)
        {
            try
            {
                PushTripleRand(map, seed, ref __state);
            }
            catch (Exception ex)
            {
                if (__state != 0) PopTripleRand(__state);
                else RestoreParentRandScope();
                __state = 0;
                if (ModDebug.EnableQuestIdeologyTrace)
                    Log.Warning($"[MP-MeowOnlineShop] {label}: TryPushTripleRandSafe failed: {ex.Message}");
            }
        }

        /// <summary>跨端稳定的任务键：优先存档 ID，其次反射 int 字段/属性。</summary>
        private static int StableQuestKey(Quest quest)
        {
            if (quest == null) return 0;
            try
            {
                if (quest is ILoadReferenceable lr)
                {
                    var lid = lr.GetUniqueLoadID();
                    if (!string.IsNullOrEmpty(lid))
                    {
                        int h = 0;
                        foreach (var c in lid)
                            h = Gen.HashCombineInt(h, c);
                        return h;
                    }
                }
            }
            catch
            {
                // ignored
            }

            foreach (var name in new[] { "id", "Id", "ID" })
            {
                try
                {
                    var f = AccessTools.Field(quest.GetType(), name);
                    if (f != null && f.FieldType == typeof(int))
                        return (int)f.GetValue(quest);
                    var p = AccessTools.Property(quest.GetType(), name);
                    if (p != null && p.PropertyType == typeof(int) && p.CanRead)
                        return (int)p.GetValue(quest, null);
                }
                catch
                {
                    // next
                }
            }

            return DeterministicTypeNameHash(quest.GetType());
        }

        private static int DeterministicTypeNameHash(Type type)
        {
            if (type == null) return 0;
            var name = type.FullName ?? type.Name ?? "";
            var hash = 0;
            foreach (var c in name)
                hash = Gen.HashCombineInt(hash, c);
            return hash;
        }

        private static Exception QuestTick_Finalizer(Exception __exception, int __state)
        {
            PopTripleRand(__state);
            return __exception;
        }

        private static void IncidentTryExecute_Prefix(IncidentParms parms, IncidentWorker __instance, ref int __state)
        {
            IncidentRandScopePrefix(parms, __instance, ref __state, "TryExecute");
        }

        private static Exception IncidentTryExecute_Finalizer(Exception __exception, int __state)
        {
            return IncidentRandScopeFinalizer(__exception, __state, "TryExecute");
        }

        private static void IncidentTryExecuteWorker_Prefix(IncidentParms parms, IncidentWorker __instance, ref int __state)
        {
            IncidentRandScopePrefix(parms, __instance, ref __state, "TryExecuteWorker");
        }

        private static Exception IncidentTryExecuteWorker_Finalizer(Exception __exception, int __state)
        {
            return IncidentRandScopeFinalizer(__exception, __state, "TryExecuteWorker");
        }

        private static void IncidentRandScopePrefix(IncidentParms parms, IncidentWorker worker, ref int __state, string stage)
        {
            __state = 0;
            if (!MP.IsInMultiplayer)
                return;

            if (_incidentRandScopeDepth > 0)
            {
                _incidentRandScopeDepth++;
                __state = StateNestedIncidentScope;
                TraceIncidentSeed(stage, worker, parms, MapFromIncidentTarget(parms?.target), 0, 0, true);
                return;
            }

            var map = MapFromIncidentTarget(parms?.target);
            int seed = IncidentSeed(parms, worker, out var breakdown);
            seed = Gen.HashCombineInt(seed, SeedIncidentWorker);
            _incidentRandScopeDepth = 1;
            TryPushTripleRandSafe(map, seed, ref __state, $"Incident{stage}");
            if (__state == 0)
                _incidentRandScopeDepth = 0;
            TraceIncidentSeed(stage, worker, parms, map, breakdown, seed, false);
        }

        private static Exception IncidentRandScopeFinalizer(Exception __exception, int __state, string stage)
        {
            if (!MP.IsInMultiplayer)
                return __exception;

            try
            {
                if ((__state & StateNestedIncidentScope) != 0)
                {
                    if (_incidentRandScopeDepth > 0)
                        _incidentRandScopeDepth--;
                    return __exception;
                }

                PopTripleRand(__state);
                return __exception;
            }
            finally
            {
                if ((__state & StateNestedIncidentScope) == 0)
                {
                    if (_incidentRandScopeDepth > 0)
                        _incidentRandScopeDepth--;
                    if (_incidentRandScopeDepth < 0)
                        _incidentRandScopeDepth = 0;
                    if (ModDebug.EnableQuestIdeologyTrace)
                        Log.Message($"[MP-MeowOnlineShop] Incident Rand scope leave: stage={stage}, depth={_incidentRandScopeDepth}.");
                }
            }
        }

        private static Map MapFromIncidentTarget(IIncidentTarget target)
        {
            if (target == null) return null;
            if (target is Map m) return m;
            if (target is Thing th) return th.MapHeld;
            return null;
        }

        /// <summary>
        /// 联机稳定种子：仅使用稳定值字段，避免引用型 ID（如 loadID）引入跨端漂移。
        /// </summary>
        private static int IncidentSeed(IncidentParms parms, IncidentWorker worker, out int breakdown)
        {
            int seed = Find.TickManager?.TicksAbs ?? 0;
            breakdown = seed;
            var map = MapFromIncidentTarget(parms?.target);
            if (map != null)
            {
                seed = Gen.HashCombineInt(seed, map.uniqueID);
                breakdown = Gen.HashCombineInt(breakdown, map.uniqueID);
            }
            else if (Find.World?.info != null)
            {
                seed = Gen.HashCombineInt(seed, Find.World.info.Seed);
                breakdown = Gen.HashCombineInt(breakdown, Find.World.info.Seed);
            }

            var targetKey = StableIncidentTargetKey(parms?.target);
            seed = Gen.HashCombineInt(seed, targetKey);
            breakdown = Gen.HashCombineInt(breakdown, targetKey);

            int pointsBits = parms != null ? HashSingle(parms.points) : 0;
            seed = Gen.HashCombineInt(seed, pointsBits);
            breakdown = Gen.HashCombineInt(breakdown, pointsBits);

            int tile = IncidentParmsTileOrDefault(parms);
            seed = Gen.HashCombineInt(seed, tile);
            breakdown = Gen.HashCombineInt(breakdown, tile);

            var raidArrivalDefName = parms?.raidArrivalMode?.defName;
            seed = Gen.HashCombineInt(seed, StableStringHash(raidArrivalDefName));
            breakdown = Gen.HashCombineInt(breakdown, StableStringHash(raidArrivalDefName));

            var faction = parms?.faction;
            if (faction != null)
            {
                seed = Gen.HashCombineInt(seed, StableStringHash(faction.def?.defName));
                seed = Gen.HashCombineInt(seed, StableStringHash(faction.Name));
                seed = Gen.HashCombineInt(seed, faction.IsPlayer ? 1 : 0);
                breakdown = Gen.HashCombineInt(breakdown, StableStringHash(faction.def?.defName));
                breakdown = Gen.HashCombineInt(breakdown, StableStringHash(faction.Name));
            }

            if (worker != null)
            {
                seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(worker.GetType()));
                breakdown = Gen.HashCombineInt(breakdown, DeterministicTypeNameHash(worker.GetType()));
            }
            return seed;
        }

        private static void PushTripleRand(Map map, int seed, ref int __state)
        {
            __state = 0;
            var parent = Tuple.Create(_mapForRandPop, _unityRandomSavedState, _unityRandomStateCaptured);
            if (_randParents == null) _randParents = new Stack<Tuple<Map, UnityRandom.State, bool>>();
            _randParents.Push(parent);
            _mapForRandPop = null;
            _unityRandomStateCaptured = false;

            DeterministicRandScope.Begin(
                map,
                seed,
                WorldSeedOffset,
                ref __state,
                out _mapForRandPop,
                ignoreGate: true);
            if (PushUnityRandIfEnabled(seed + UnitySeedOffset))
                __state |= StateUnityRand;
        }

        private static void PopTripleRand(int __state)
        {
            if (!MP.IsInMultiplayer || __state == 0)
                return;
            try
            {
                if ((__state & StateUnityRand) != 0)
                    PopUnityRandIfExists();
                DeterministicRandScope.End(__state, _mapForRandPop);
            }
            catch
            {
                // ignored
            }
            finally
            {
                RestoreParentRandScope();
            }
        }

        private static void RestoreParentRandScope()
        {
                if (_randParents != null && _randParents.Count > 0)
                {
                    var parent = _randParents.Pop();
                    _mapForRandPop = parent.Item1;
                    _unityRandomSavedState = parent.Item2;
                    _unityRandomStateCaptured = parent.Item3;
                }
                else
                {
                    _mapForRandPop = null;
                    _unityRandomStateCaptured = false;
                }
        }

        private static bool PushUnityRandIfEnabled(int seed)
        {
            if (!ModDebug.EnableIncidentUnityRandomScope)
                return false;
            try
            {
                _unityRandomSavedState = UnityRandom.state;
                _unityRandomStateCaptured = true;
                UnityRandom.InitState(seed);
                return true;
            }
            catch
            {
                _unityRandomStateCaptured = false;
                return false;
            }
        }

        private static void PopUnityRandIfExists()
        {
            if (!_unityRandomStateCaptured)
                return;
            try
            {
                UnityRandom.state = _unityRandomSavedState;
            }
            catch
            {
                // ignored
            }
            finally
            {
                _unityRandomStateCaptured = false;
            }
        }

        private static int StableIncidentTargetKey(IIncidentTarget target)
        {
            if (target == null) return 0;
            if (target is Map map)
                return Gen.HashCombineInt(0x1A11, map.uniqueID);
            if (target is WorldObject wo)
                return Gen.HashCombineInt(0x1A21, wo.ID);
            if (target is Thing thing)
            {
                int key = Gen.HashCombineInt(0x1A31, thing.thingIDNumber);
                if (thing.MapHeld != null)
                    key = Gen.HashCombineInt(key, thing.MapHeld.uniqueID);
                return key;
            }
            return Gen.HashCombineInt(0x1AFF, DeterministicTypeNameHash(target.GetType()));
        }

        private static int StableStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;
            int hash = 0;
            foreach (var c in value)
                hash = Gen.HashCombineInt(hash, c);
            return hash;
        }

        private static int HashSingle(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        private static int IncidentParmsTileOrDefault(IncidentParms parms)
        {
            if (parms == null)
                return -1;
            try
            {
                var t = parms.GetType();
                var f = AccessTools.Field(t, "tile") ?? AccessTools.Field(t, "Tile");
                if (f != null && f.FieldType == typeof(int))
                    return (int)f.GetValue(parms);
                var p = AccessTools.Property(t, "tile") ?? AccessTools.Property(t, "Tile");
                if (p != null && p.PropertyType == typeof(int) && p.CanRead)
                    return (int)p.GetValue(parms, null);
            }
            catch
            {
                // ignored
            }

            return -1;
        }

        private static void TraceIncidentSeed(
            string stage,
            IncidentWorker worker,
            IncidentParms parms,
            Map map,
            int breakdownSeed,
            int finalSeed,
            bool nestedOnly)
        {
            if (!ModDebug.EnableQuestIdeologyTrace)
                return;

            var workerName = worker?.GetType().FullName ?? "null";
            var tile = IncidentParmsTileOrDefault(parms);
            var points = parms?.points ?? 0f;
            var targetKey = StableIncidentTargetKey(parms?.target);
            var targetType = parms?.target?.GetType().Name ?? "null";
            var raidArrival = parms?.raidArrivalMode?.defName ?? "null";
            var syncFlag = MP.IsExecutingSyncCommand ? "sync" : "tick";
            Log.Message(
                "[MP-MeowOnlineShop] Incident Rand trace: " +
                $"stage={stage}, mode={syncFlag}, nested={(nestedOnly ? "yes" : "no")}, worker={workerName}, " +
                $"map={(map != null ? map.uniqueID.ToString() : "null")}, targetType={targetType}, targetKey={targetKey}, " +
                $"tile={tile}, points={points:0.###}, raidArrival={raidArrival}, breakdownSeed={breakdownSeed}, finalSeed={finalSeed}, depth={_incidentRandScopeDepth}.");
        }

        /// <summary>
        /// 核心程序集中与任务/文化/遗物相关的 <see cref="ChoiceLetter"/> 子类全名白名单（按版本存在则注册，缺失则跳过）。
        /// 避免宽泛关键字扫描导致误注册或重复 <see cref="ISyncMethod"/>。
        /// </summary>
        private static readonly string[] ChoiceLetterWhitelistFullNames =
        {
            "RimWorld.ChoiceLetter_ArchonexusChoice",
            "RimWorld.ChoiceLetter_AnimalProductivity",
            "RimWorld.ChoiceLetter_Beggars",
            "RimWorld.ChoiceLetter_BeggarsArrival",
            "RimWorld.ChoiceLetter_CaravanPayment",
            "RimWorld.ChoiceLetter_ChooseMeme",
            "RimWorld.ChoiceLetter_ChooseMeme_Basic",
            "RimWorld.ChoiceLetter_ChooseMeme_Culture",
            "RimWorld.ChoiceLetter_ChooseMeme_Ideology",
            "RimWorld.ChoiceLetter_DefeatAllEnemiesQuest",
            "RimWorld.ChoiceLetter_FoodPoisoning",
            "RimWorld.ChoiceLetter_GrantRoyalFavor",
            "RimWorld.ChoiceLetter_Intercept",
            "RimWorld.ChoiceLetter_Intercept_Choice",
            "RimWorld.ChoiceLetter_Joiner",
            "RimWorld.ChoiceLetter_JoinerApocalypse",
            "RimWorld.ChoiceLetter_JoinerDropIn",
            "RimWorld.ChoiceLetter_JoinerWalking",
            "RimWorld.ChoiceLetter_KeepXsInStock",
            "RimWorld.ChoiceLetter_LoyaltyRaid",
            "RimWorld.ChoiceLetter_MechCluster",
            "RimWorld.ChoiceLetter_PayMechCluster",
            "RimWorld.ChoiceLetter_PrisonerRecruits",
            "RimWorld.ChoiceLetter_RansomDemand",
            "RimWorld.ChoiceLetter_Relic",
            "RimWorld.ChoiceLetter_RoyalFavor",
            "RimWorld.ChoiceLetter_Thrall",
            "RimWorld.ChoiceLetter_Venerated"
        };

        /// <summary>
        /// 仅对白名单中的 <see cref="ChoiceLetter"/> 子类注册典型“确认/接受”回调为同步方法，并输出可追踪日志。
        /// </summary>
        private static void RegisterQuestIdeologyChoiceLetterSyncMethods()
        {
            if (!MP.enabled)
                return;

            var letterBase = typeof(ChoiceLetter);
            var core = typeof(QuestManager).Assembly;

            Type[] types;
            try
            {
                types = core.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(x => x != null).ToArray();
            }

            var typeByFullName = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var t in types)
            {
                if (t?.FullName == null)
                    continue;
                if (!typeByFullName.ContainsKey(t.FullName))
                    typeByFullName[t.FullName] = t;
            }

            int registered = 0;
            int skippedMissing = 0;

            foreach (var fullName in ChoiceLetterWhitelistFullNames)
            {
                if (!typeByFullName.TryGetValue(fullName, out var t))
                {
                    skippedMissing++;
                    if (ModDebug.EnableQuestIdeologyTrace)
                        Log.Message($"[MP-MeowOnlineShop] ChoiceLetter whitelist: type not in Core (skip): {fullName}");
                    continue;
                }

                if (t.IsAbstract || !letterBase.IsAssignableFrom(t))
                    continue;

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName || m.ReturnType != typeof(void))
                        continue;

                    var n = m.Name;
                    if (n.IndexOf("Accept", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Execute", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Chosen", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Choose", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Yes", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Ok", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Click", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var ps = m.GetParameters();
                    if (ps.Length > 1)
                        continue;
                    if (ps.Length == 1 && ps[0].ParameterType != typeof(int))
                        continue;

                    try
                    {
                        MP.RegisterSyncMethod(m, null);
                        registered++;
                        if (ModDebug.EnableQuestIdeologyTrace)
                            Log.Message($"[MP-MeowOnlineShop] ChoiceLetter sync registered: {t.FullName}.{m.Name} (params={ps.Length})");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] RegisterSyncMethod {t.FullName}.{m.Name} failed: {ex.Message}");
                    }
                }
            }

            Log.Message(
                $"[MP-MeowOnlineShop] Quest/Ideology ChoiceLetter: sync methods registered={registered}, whitelist types missing in Core={skippedMissing}.");
        }
    }
}
