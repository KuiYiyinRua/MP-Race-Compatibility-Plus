using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 矿物发现类任务（Milira_SolarCrystalMining）联机兜底：
    /// 1) 稳定 IncidentWorker 与 GenStep 的随机作用域；
    /// 2) 可选记录 WorldObjectTimeout/NoWorldObject 的竞态信号，辅助排查 OOS。
    /// </summary>
    internal static class Patch_MiningDiscoveryMp
    {
        private const string HarmonyId = "mp.meowonlineshop.miningdiscovery";
        private static readonly Harmony Harmony = new Harmony(HarmonyId);

        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;
        private const int StateNestedScope = 1 << 30;

        private const int WorldSeedOffset = 0x2A3B;
        private const int SeedIncident = 0x6D5101;
        private const int SeedGenStep = 0x6D5102;
        private const string SolarCrystalIncidentWorkerTypeName = "Milira.IncidentWorker_SolarCrystalMining";
        private const string SolarCrystalGenStepTypeName = "Milira.GenStep_SolarCrystalMining";
        private const string SolarCrystalQuestTag = "Milira_SolarCrystalMining";

        [ThreadStatic]
        private static Map _mapForRandPop;
        [ThreadStatic]
        private static int _incidentScopeDepth;

        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();

        public static void Apply()
        {
            if (!MP.enabled)
                return;

            try
            {
                TryPatchSolarCrystalIncidentWorker();
                TryPatchSolarCrystalGenStep();
                TryPatchQuestPartDiagnostics();
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Patch_MiningDiscoveryMp.Apply failed: {e}");
            }
        }

        private static void TryPatchSolarCrystalIncidentWorker()
        {
            var type = AccessTools.TypeByName(SolarCrystalIncidentWorkerTypeName);
            if (type == null)
            {
                if (ModDebug.EnableMiningDiscoveryTrace)
                    Log.Message($"[MP-MeowOnlineShop] MiningDiscovery: type not found: {SolarCrystalIncidentWorkerTypeName}");
                return;
            }

            var prefixEntry = AccessTools.Method(typeof(Patch_MiningDiscoveryMp), nameof(IncidentTryExecute_Prefix));
            var finalizerEntry = AccessTools.Method(typeof(Patch_MiningDiscoveryMp), nameof(IncidentTryExecute_Finalizer));
            var prefixWorker = AccessTools.Method(typeof(Patch_MiningDiscoveryMp), nameof(IncidentTryExecuteWorker_Prefix));
            var finalizerWorker = AccessTools.Method(typeof(Patch_MiningDiscoveryMp), nameof(IncidentTryExecuteWorker_Finalizer));
            if (prefixEntry == null || finalizerEntry == null || prefixWorker == null || finalizerWorker == null)
                return;

            var mExecute = type.GetMethod(
                "TryExecute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new[] { typeof(IncidentParms) },
                null);
            var mWorker = type.GetMethod(
                "TryExecuteWorker",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new[] { typeof(IncidentParms) },
                null);

            if (mExecute != null)
                Harmony.Patch(mExecute, prefix: new HarmonyMethod(prefixEntry), finalizer: new HarmonyMethod(finalizerEntry));
            if (mWorker != null)
                Harmony.Patch(mWorker, prefix: new HarmonyMethod(prefixWorker), finalizer: new HarmonyMethod(finalizerWorker));

            Log.Message(
                "[MP-MeowOnlineShop] MiningDiscovery: Milira incident declared targets " +
                $"TryExecute={(mExecute != null)}, TryExecuteWorker={(mWorker != null)}. " +
                "Inherited IncidentWorker.TryExecute remains owned by the shared incident stabilizer.");
        }

        private static void TryPatchSolarCrystalGenStep()
        {
            var type = AccessTools.TypeByName(SolarCrystalGenStepTypeName);
            if (type == null)
            {
                if (ModDebug.EnableMiningDiscoveryTrace)
                    Log.Message($"[MP-MeowOnlineShop] MiningDiscovery: type not found: {SolarCrystalGenStepTypeName}");
                return;
            }

            var prefix = AccessTools.Method(typeof(Patch_MiningDiscoveryMp), nameof(SolarCrystalGenStepGenerate_Prefix));
            var finalizer = AccessTools.Method(typeof(Patch_MiningDiscoveryMp), nameof(SolarCrystalGenStepGenerate_Finalizer));
            if (prefix == null || finalizer == null)
                return;

            MethodInfo target = null;
            foreach (var m in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!string.Equals(m.Name, "Generate", StringComparison.Ordinal))
                    continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && typeof(Map).IsAssignableFrom(ps[0].ParameterType))
                {
                    target = m;
                    break;
                }
            }

            if (target == null)
            {
                Log.Warning("[MP-MeowOnlineShop] MiningDiscovery: GenStep_SolarCrystalMining.Generate(Map, ...) not found.");
                return;
            }

            Harmony.Patch(target, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
            Log.Message("[MP-MeowOnlineShop] MiningDiscovery: patched GenStep_SolarCrystalMining.Generate.");
        }

        private static void TryPatchQuestPartDiagnostics()
        {
            var prefixSignal = AccessTools.Method(typeof(Patch_MiningDiscoveryMp), nameof(QuestPartSignal_TracePrefix));
            if (prefixSignal == null)
                return;

            TryPatchQuestPartSignal("RimWorld.QuestPart_WorldObjectTimeout", prefixSignal);
            TryPatchQuestPartSignal("RimWorld.QuestPart_NoWorldObject", prefixSignal);
        }

        private static void TryPatchQuestPartSignal(string typeName, MethodInfo prefixSignal)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
                return;

            var notify = type.GetMethod(
                "Notify_QuestSignalReceived",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new[] { typeof(Signal) },
                null);
            if (notify != null)
            {
                Harmony.Patch(notify, prefix: new HarmonyMethod(prefixSignal));
                if (ModDebug.EnableMiningDiscoveryTrace)
                    Log.Message($"[MP-MeowOnlineShop] MiningDiscovery: trace patched {typeName}.Notify_QuestSignalReceived");
            }
        }

        private static void IncidentTryExecute_Prefix(IncidentParms parms, IncidentWorker __instance, ref int __state)
        {
            IncidentScopePrefix(parms, __instance, ref __state, "TryExecute");
        }

        private static Exception IncidentTryExecute_Finalizer(Exception __exception, int __state)
        {
            return IncidentScopeFinalizer(__exception, __state);
        }

        private static void IncidentTryExecuteWorker_Prefix(IncidentParms parms, IncidentWorker __instance, ref int __state)
        {
            IncidentScopePrefix(parms, __instance, ref __state, "TryExecuteWorker");
        }

        private static Exception IncidentTryExecuteWorker_Finalizer(Exception __exception, int __state)
        {
            return IncidentScopeFinalizer(__exception, __state);
        }

        private static void IncidentScopePrefix(IncidentParms parms, IncidentWorker worker, ref int __state, string stage)
        {
            __state = 0;
            if (!MP.IsInMultiplayer)
                return;

            if (_incidentScopeDepth > 0)
            {
                _incidentScopeDepth++;
                __state = StateNestedScope;
                return;
            }

            var map = MapFromIncidentTarget(parms?.target);
            var seed = IncidentSeed(parms, worker, stage);
            _incidentScopeDepth = 1;
            TryPushTripleRandSafe(map, seed, ref __state, $"MiningIncident_{stage}");
            if (__state == 0)
                _incidentScopeDepth = 0;
        }

        private static Exception IncidentScopeFinalizer(Exception __exception, int __state)
        {
            if (!MP.IsInMultiplayer)
                return __exception;

            try
            {
                if ((__state & StateNestedScope) != 0)
                {
                    if (_incidentScopeDepth > 0)
                        _incidentScopeDepth--;
                    return __exception;
                }

                PopTripleRand(__state);
                return __exception;
            }
            finally
            {
                if ((__state & StateNestedScope) == 0)
                {
                    if (_incidentScopeDepth > 0)
                        _incidentScopeDepth--;
                    if (_incidentScopeDepth < 0)
                        _incidentScopeDepth = 0;
                }
            }
        }

        private static void SolarCrystalGenStepGenerate_Prefix(Map map, ref int __state, object __instance)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || map == null)
                return;

            int seed = Gen.HashCombineInt(SeedGenStep, map.uniqueID);
            seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(__instance?.GetType()));
            if (!MP.IsExecutingSyncCommand)
                seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);

            TryPushTripleRandSafe(map, seed, ref __state, "MiningGenStep");
        }

        private static Exception SolarCrystalGenStepGenerate_Finalizer(Exception __exception, int __state)
        {
            PopTripleRand(__state);
            return __exception;
        }

        private static void QuestPartSignal_TracePrefix(object __instance, Signal signal)
        {
            if (!MP.IsInMultiplayer || !ModDebug.EnableMiningDiscoveryTrace)
                return;
            if (__instance == null)
                return;

            if (!TryResolveQuestFromPart(__instance, out var quest) || !IsSolarCrystalQuest(quest))
                return;

            int questId = TryReadInt(quest, "id", "ID", "Id");
            int worldObjectId = TryResolveWorldObjectId(__instance);
            string signalTag = signal.tag ?? "null";
            string partType = __instance.GetType().Name;
            int tick = Find.TickManager?.TicksGame ?? -1;

            Log.Message(
                $"[MP-MeowOnlineShop] MiningDiscovery trace: part={partType}, questId={questId}, worldObjectId={worldObjectId}, tick={tick}, signal={signalTag}.");
        }

        private static bool TryResolveQuestFromPart(object part, out Quest quest)
        {
            quest = null;
            if (part == null)
                return false;

            try
            {
                var partType = part.GetType();
                var field = AccessTools.Field(partType, "quest") ?? AccessTools.Field(typeof(QuestPart), "quest");
                if (field != null)
                {
                    quest = field.GetValue(part) as Quest;
                    if (quest != null)
                        return true;
                }

                var prop = AccessTools.Property(partType, "Quest") ?? AccessTools.Property(typeof(QuestPart), "Quest");
                if (prop != null && prop.CanRead)
                {
                    quest = prop.GetValue(part, null) as Quest;
                    if (quest != null)
                        return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        private static bool IsSolarCrystalQuest(Quest quest)
        {
            if (quest == null)
                return false;

            try
            {
                var tagsField = AccessTools.Field(quest.GetType(), "tags");
                if (tagsField != null)
                {
                    var tags = tagsField.GetValue(quest) as System.Collections.IEnumerable;
                    if (ContainsSolarCrystalTag(tags))
                        return true;
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                var tagsProp = AccessTools.Property(quest.GetType(), "Tags");
                if (tagsProp != null && tagsProp.CanRead)
                {
                    var tags = tagsProp.GetValue(quest, null) as System.Collections.IEnumerable;
                    if (ContainsSolarCrystalTag(tags))
                        return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        private static bool ContainsSolarCrystalTag(System.Collections.IEnumerable tags)
        {
            if (tags == null)
                return false;

            foreach (var tag in tags)
            {
                if (tag == null)
                    continue;
                if (string.Equals(tag.ToString(), SolarCrystalQuestTag, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static int TryResolveWorldObjectId(object questPart)
        {
            if (questPart == null)
                return -1;

            try
            {
                var t = questPart.GetType();
                var field = AccessTools.Field(t, "worldObject");
                var worldObject = field?.GetValue(questPart) as WorldObject;
                if (worldObject != null)
                    return worldObject.ID;
            }
            catch
            {
                // ignored
            }

            return -1;
        }

        private static int TryReadInt(object obj, params string[] names)
        {
            if (obj == null || names == null)
                return -1;

            var t = obj.GetType();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                try
                {
                    var f = AccessTools.Field(t, name);
                    if (f != null && f.FieldType == typeof(int))
                        return (int)f.GetValue(obj);
                    var p = AccessTools.Property(t, name);
                    if (p != null && p.PropertyType == typeof(int) && p.CanRead)
                        return (int)p.GetValue(obj, null);
                }
                catch
                {
                    // ignored
                }
            }

            return -1;
        }

        private static int IncidentSeed(IncidentParms parms, IncidentWorker worker, string stage)
        {
            int seed = Gen.HashCombineInt(SeedIncident, StableStringHash(stage));
            if (!MP.IsExecutingSyncCommand)
                seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);

            var map = MapFromIncidentTarget(parms?.target);
            if (map != null)
                seed = Gen.HashCombineInt(seed, map.uniqueID);
            else
                seed = Gen.HashCombineInt(seed, Find.World?.info?.Seed ?? 0);

            seed = Gen.HashCombineInt(seed, StableIncidentTargetKey(parms?.target));
            seed = Gen.HashCombineInt(seed, HashSingle(parms?.points ?? 0f));
            seed = Gen.HashCombineInt(seed, TryReadIncidentTile(parms));
            seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(worker?.GetType()));
            return seed;
        }

        private static Map MapFromIncidentTarget(IIncidentTarget target)
        {
            if (target == null) return null;
            if (target is Map m) return m;
            if (target is Thing t) return t.MapHeld;
            return null;
        }

        private static int TryReadIncidentTile(IncidentParms parms)
        {
            if (parms == null) return -1;
            try
            {
                var t = parms.GetType();
                var field = AccessTools.Field(t, "tile") ?? AccessTools.Field(t, "Tile");
                if (field != null && field.FieldType == typeof(int))
                    return (int)field.GetValue(parms);
                var prop = AccessTools.Property(t, "tile") ?? AccessTools.Property(t, "Tile");
                if (prop != null && prop.PropertyType == typeof(int) && prop.CanRead)
                    return (int)prop.GetValue(parms, null);
            }
            catch
            {
                // ignored
            }

            return -1;
        }

        private static int StableIncidentTargetKey(IIncidentTarget target)
        {
            if (target == null) return 0;
            if (target is Map map)
                return Gen.HashCombineInt(0x19A1, map.uniqueID);
            if (target is WorldObject wo)
                return Gen.HashCombineInt(0x19A2, wo.ID);
            if (target is Thing thing)
            {
                int key = Gen.HashCombineInt(0x19A3, thing.thingIDNumber);
                if (thing.MapHeld != null)
                    key = Gen.HashCombineInt(key, thing.MapHeld.uniqueID);
                return key;
            }

            return DeterministicTypeNameHash(target.GetType());
        }

        private static int DeterministicTypeNameHash(Type type)
        {
            if (type == null)
                return 0;
            return StableStringHash(type.FullName ?? type.Name ?? "");
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

        private static void TryPushTripleRandSafe(Map map, int seed, ref int __state, string label)
        {
            try
            {
                PushTripleRand(map, seed, ref __state);
            }
            catch (Exception e)
            {
                __state = 0;
                if (ModDebug.EnableMiningDiscoveryTrace)
                    Log.Warning($"[MP-MeowOnlineShop] MiningDiscovery {label}: push rand failed: {e.Message}");
            }
        }

        private static void PushTripleRand(Map map, int seed, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;

            Rand.PushState(seed);
            __state |= StateStaticRand;

            if (PushMapRandIfExists(map, seed))
            {
                _mapForRandPop = map;
                __state |= StateMapRand;
            }

            if (PushWorldRandIfExists(seed + WorldSeedOffset))
                __state |= StateWorldRand;
        }

        private static void PopTripleRand(int __state)
        {
            if (!MP.IsInMultiplayer)
                return;

            try
            {
                if ((__state & StateWorldRand) != 0)
                    PopWorldRandIfExists();
                if ((__state & StateMapRand) != 0)
                    PopMapRandIfExists();
                if ((__state & StateStaticRand) != 0)
                    Rand.PopState();
            }
            catch
            {
                // ignored
            }
        }

        private static bool PushMapRandIfExists(Map map, int seed)
        {
            if (map == null) return false;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Property(t, "rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return false;
                var pushMethod = mapRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null) return false;
                pushMethod.Invoke(mapRand, new object[] { seed });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PopMapRandIfExists()
        {
            var map = _mapForRandPop;
            _mapForRandPop = null;
            if (map == null) return;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Property(t, "rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return;
                var popMethod = mapRand.GetType().GetMethod("PopState", Type.EmptyTypes);
                popMethod?.Invoke(mapRand, null);
            }
            catch
            {
                // ignored
            }
        }

        private static Func<object> TryGetWorldRandGetter()
        {
            try
            {
                return () =>
                {
                    var world = Find.World;
                    if (world == null) return null;
                    var t = world.GetType();
                    return AccessTools.Property(t, "Rand")?.GetValue(world)
                           ?? AccessTools.Property(t, "rand")?.GetValue(world)
                           ?? AccessTools.Field(t, "Rand")?.GetValue(world)
                           ?? AccessTools.Field(t, "rand")?.GetValue(world);
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool PushWorldRandIfExists(int seed)
        {
            if (WorldRandGetter == null) return false;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return false;
                var pushMethod = worldRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null) return false;
                pushMethod.Invoke(worldRand, new object[] { seed });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PopWorldRandIfExists()
        {
            if (WorldRandGetter == null) return;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return;
                var popMethod = worldRand.GetType().GetMethod("PopState", Type.EmptyTypes);
                popMethod?.Invoke(worldRand, null);
            }
            catch
            {
                // ignored
            }
        }
    }
}
