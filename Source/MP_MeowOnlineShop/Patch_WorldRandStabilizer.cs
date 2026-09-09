using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 鑱旀満鏃剁ǔ瀹氣€滀笘鐣屽眰鈥濋殢鏈虹姸鎬侊紝鍑忚交 "Wrong random state for the world"銆?Random state from commands doesn't match" 涓?"last valid tick -1" 鎺夌嚎銆?
    /// 鍘熷洜锛氬姞鍏?鍔犺浇鏃舵垨涓栫晫 Tick 鏃讹紝涓绘満涓庡鎴风鐨?World.rand / 褰撳墠 Rand 娑堣€楅『搴忎笉涓€鑷翠細瀵艰嚧 desync锛?
    ///       鍛戒护鍥炴斁鏃惰嫢闈欐€?Rand 琚暣 tick 鏇挎崲锛屼細瀵艰嚧 "Random state from commands doesn't match"銆?
    /// 鍋氭硶锛?) World.Tick 鍐呬粎鍖呰９ World.rand锛堜笉鍖呰９闈欐€?Rand锛夛紝閬垮厤鍛戒护鎵ц璺緞涓婄殑 Rand 涓庡浜烘牎楠屼笉涓€鑷达紱
    ///       2) Zone_Growing/Zone_Fishing.GetGizmos 鐢ㄧ‘瀹氭€х瀛愬寘瑁?Rand锛歮ap + Zone 瀛樻。 ID锛圛LoadReferenceable锛? 绫诲瀷鍏ㄥ悕鍝堝笇锛岄伩鍏?TicksGame/UI 鍥炴斁鍒嗗弶銆?
    /// </summary>
    internal static class Patch_WorldRandStabilizer
    {
        internal const int RandScopeStaticRand = 1;
        internal const int RandScopeMapRand = 2;
        internal const int RandScopeWorldRand = 4;
        private const int WorldSeedOffset = 0x1B3C;
        private const int ZoneWorldSeedOffset = 0x2C4D; // Zone 琛ヤ竵涓?World.rand 绉嶅瓙鍋忕Щ锛屼笌 map 鍖哄垎
        private static int _randScopePushCount;
        private static int _randScopePopCount;
        private static readonly Dictionary<Type, Func<object, object>> MapAccessorCache =
            new Dictionary<Type, Func<object, object>>();
        private static readonly Dictionary<Type, Func<object, int>> IntAccessorCache =
            new Dictionary<Type, Func<object, int>>();
        private static readonly Dictionary<Type, int> TypeHashCache =
            new Dictionary<Type, int>();
        private static readonly Dictionary<string, int> StringHashCache =
            new Dictionary<string, int>();
        private static readonly object WorldRandCacheLock = new object();
        private static readonly ConditionalWeakTable<object, BoxedInt> StableContextKeyCache =
            new ConditionalWeakTable<object, BoxedInt>();

        private sealed class BoxedInt
        {
            public int Value;
        }

        private static void TraceRandScopePush(string scope, int seed, int state, Map map)
        {
            if (!ModDebug.EnableWorldRandScopeTrace) return;
            int pushes = System.Threading.Interlocked.Increment(ref _randScopePushCount);
            int pops = System.Threading.Volatile.Read(ref _randScopePopCount);
            int balance = pushes - pops;
            Log.Message($"[MP-MeowOnlineShop] RandScope PUSH scope={scope} seed={seed} map={(map != null ? map.Index.ToString() : "null")} state=0x{state:X} balance={balance}");
        }

        private static void TraceRandScopePop(string scope, int state, Map map)
        {
            if (!ModDebug.EnableWorldRandScopeTrace) return;
            int pops = System.Threading.Interlocked.Increment(ref _randScopePopCount);
            int pushes = System.Threading.Volatile.Read(ref _randScopePushCount);
            int balance = pushes - pops;
            if (balance < 0)
                Log.Warning($"[MP-MeowOnlineShop] RandScope POP imbalance scope={scope} map={(map != null ? map.Index.ToString() : "null")} state=0x{state:X} balance={balance}");
            else
                Log.Message($"[MP-MeowOnlineShop] RandScope POP scope={scope} map={(map != null ? map.Index.ToString() : "null")} state=0x{state:X} balance={balance}");
        }


        /// <summary>瀵圭被鍨嬪叏鍚嶅仛纭畾鎬у搱甯岋紝淇濊瘉涓绘満涓庡鎴风鍚岀被鍨嬪緱鍒扮浉鍚屽€硷紙MetadataToken 浼氬洜绋嬪簭闆嗗姞杞介『搴忎笉鍚岃€屼笉鍚岋級銆?/summary>
        private static int DeterministicTypeHash(Type type)
        {
            if (type == null)
                return 0;

            lock (WorldRandCacheLock)
            {
                int cached;
                if (TypeHashCache.TryGetValue(type, out cached))
                    return cached;
            }

            int hash = ComputeDeterministicStringHash(type.FullName ?? type.Name ?? "");
            lock (WorldRandCacheLock)
            {
                if (TypeHashCache.Count >= 512)
                    TypeHashCache.Clear();
                TypeHashCache[type] = hash;
            }
            return hash;
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            lock (WorldRandCacheLock)
            {
                int cached;
                if (StringHashCache.TryGetValue(value, out cached))
                    return cached;
            }

            int hash = ComputeDeterministicStringHash(value);
            lock (WorldRandCacheLock)
            {
                if (StringHashCache.Count >= 512)
                    StringHashCache.Clear();
                StringHashCache[value] = hash;
            }
            return hash;
        }

        private static int ComputeDeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;
            int hash = 0;
            foreach (char c in value)
                hash = Gen.HashCombineInt(hash, (int)c);
            return hash;
        }

        /// <summary>Zone 鐨勭ǔ瀹氶敭锛氫紭鍏?ILoadReferenceable 鐨勫瓨妗?ID锛堝弻绔竴鑷达級锛岄伩鍏嶇敤 TicksGame 鍙備笌 UI 绉嶅瓙瀵艰嚧鍥炴斁/钀藉悗 tick 鍒嗗弶銆?/summary>
        private static int StableZoneKey(Zone z)
        {
            if (z == null) return 0;
            try
            {
                if (z is ILoadReferenceable lr)
                {
                    var id = lr.GetUniqueLoadID();
                    if (!string.IsNullOrEmpty(id))
                    {
                        int h = 0;
                        foreach (char c in id)
                            h = Gen.HashCombineInt(h, (int)c);
                        return h;
                    }
                }
            }
            catch { }
            try
            {
                var pi = z.GetType().GetProperty("ID", BindingFlags.Public | BindingFlags.Instance);
                if (pi != null && pi.PropertyType == typeof(int))
                    return (int)pi.GetValue(z);
            }
            catch { }
            return 0;
        }


        internal static bool TryEnterDeterministicRandScope(string scope, int seed, Map map, int worldSeedOffset, ref int state, out Map mapForPop)
        {
            state = 0;
            mapForPop = null;
            try
            {
                if (!DeterministicRandScope.Begin(
                        map,
                        seed,
                        worldSeedOffset,
                        ref state,
                        out mapForPop,
                        ignoreGate: true))
                {
                    return false;
                }

                TraceRandScopePush(scope, seed, state, mapForPop);
                return true;
            }
            catch (Exception e)
            {
                ExitDeterministicRandScope(scope, state, mapForPop);
                state = 0;
                mapForPop = null;
                if (ModDebug.EnableWorldRandScopeTrace)
                    Log.Warning($"[MP-MeowOnlineShop] RandScope enter failed scope={scope}: {e.Message}");
                return false;
            }
        }

        internal static void ExitDeterministicRandScope(string scope, int state, Map mapForPop)
        {
            if (state == 0)
                return;

            try
            {
                DeterministicRandScope.End(state, mapForPop);
            }
            catch (Exception e)
            {
                if (ModDebug.EnableWorldRandScopeTrace)
                    Log.Warning($"[MP-MeowOnlineShop] RandScope exit failed scope={scope}: {e.Message}");
            }

            TraceRandScopePop(scope, state, mapForPop);
        }

        private static Map TryResolveMapFromContext(object instance)
        {
            if (instance == null)
                return null;
            if (instance is Map directMap)
                return directMap;
            if (instance is Thing thing)
                return thing.MapHeld ?? thing.Map;

            var accessor = GetMapAccessor(instance.GetType());
            if (accessor == null)
                return null;
            try
            {
                return accessor(instance) as Map;
            }
            catch
            {
                return null;
            }
        }

        private static Func<object, object> GetMapAccessor(Type type)
        {
            lock (WorldRandCacheLock)
            {
                Func<object, object> cached;
                if (MapAccessorCache.TryGetValue(type, out cached))
                    return cached;
            }

            Func<object, object> accessor = BuildMapAccessor(type);
            lock (WorldRandCacheLock)
            {
                if (MapAccessorCache.Count >= 256)
                    MapAccessorCache.Clear();
                MapAccessorCache[type] = accessor;
            }
            return accessor;
        }

        private static Func<object, object> BuildMapAccessor(Type type)
        {
            foreach (var name in new[] { "Map", "map" })
            {
                var property = AccessTools.Property(type, name);
                if (property != null &&
                    typeof(Map).IsAssignableFrom(property.PropertyType) &&
                    property.CanRead)
                {
                    var getter = property.GetGetMethod(true);
                    if (getter != null)
                    {
                        try
                        {
                            var instance = Expression.Parameter(typeof(object), "instance");
                            var body = Expression.Convert(
                                Expression.Call(Expression.Convert(instance, type), getter),
                                typeof(object));
                            return Expression.Lambda<Func<object, object>>(body, instance).Compile();
                        }
                        catch
                        {
                            // Fall through to the next candidate.
                        }
                    }
                }

                var field = AccessTools.Field(type, name);
                if (field != null && typeof(Map).IsAssignableFrom(field.FieldType))
                {
                    try
                    {
                        var instance = Expression.Parameter(typeof(object), "instance");
                        var body = Expression.Convert(
                            Expression.Field(Expression.Convert(instance, type), field),
                            typeof(object));
                        return Expression.Lambda<Func<object, object>>(body, instance).Compile();
                    }
                    catch
                    {
                        // Fall through to the next candidate.
                    }
                }
            }

            return null;
        }

        private static int BuildStableContextKey(object instance)
        {
            if (instance == null)
                return 0;

            BoxedInt cached;
            if (StableContextKeyCache.TryGetValue(instance, out cached))
                return cached.Value;

            int key = ComputeStableContextKey(instance);
            StableContextKeyCache.Remove(instance);
            StableContextKeyCache.Add(instance, new BoxedInt { Value = key });
            return key;
        }

        private static int ComputeStableContextKey(object instance)
        {
            try
            {
                if (instance is ILoadReferenceable lr)
                    return DeterministicStringHash(lr.GetUniqueLoadID());
            }
            catch { }

            var accessor = GetIntAccessor(instance.GetType());
            if (accessor != null)
            {
                try
                {
                    return accessor(instance);
                }
                catch { }
            }

            return DeterministicTypeHash(instance.GetType());
        }

        private static Func<object, int> GetIntAccessor(Type type)
        {
            lock (WorldRandCacheLock)
            {
                Func<object, int> cached;
                if (IntAccessorCache.TryGetValue(type, out cached))
                    return cached;
            }

            Func<object, int> accessor = BuildIntAccessor(type);
            lock (WorldRandCacheLock)
            {
                if (IntAccessorCache.Count >= 256)
                    IntAccessorCache.Clear();
                IntAccessorCache[type] = accessor;
            }
            return accessor;
        }

        private static Func<object, int> BuildIntAccessor(Type type)
        {
            foreach (var name in new[] { "thingIDNumber", "ID", "Tile", "tile", "uniqueID" })
            {
                var property = AccessTools.Property(type, name);
                if (property != null &&
                    property.PropertyType == typeof(int) &&
                    property.CanRead)
                {
                    var getter = property.GetGetMethod(true);
                    if (getter != null)
                    {
                        try
                        {
                            var instance = Expression.Parameter(typeof(object), "instance");
                            var body = Expression.Call(
                                Expression.Convert(instance, type),
                                getter);
                            return Expression.Lambda<Func<object, int>>(body, instance).Compile();
                        }
                        catch
                        {
                            // Fall through to the next candidate.
                        }
                    }
                }

                var field = AccessTools.Field(type, name);
                if (field != null && field.FieldType == typeof(int))
                {
                    try
                    {
                        var instance = Expression.Parameter(typeof(object), "instance");
                        var body = Expression.Field(
                            Expression.Convert(instance, type),
                            field);
                        return Expression.Lambda<Func<object, int>>(body, instance).Compile();
                    }
                    catch
                    {
                        // Fall through to the next candidate.
                    }
                }
            }

            int fallback = DeterministicTypeHash(type);
            return _ => fallback;
        }

        private static int BuildWorldUiSeed(object instance, MethodBase originalMethod, Map map)
        {
            int seed = Gen.HashCombineInt(DeterministicTypeHash(instance?.GetType()), DeterministicStringHash(originalMethod?.Name ?? ""));
            seed = Gen.HashCombineInt(seed, BuildStableContextKey(instance));
            if (map != null)
                seed = Gen.HashCombineInt(seed, map.Index);
            return seed;
        }

        /// <summary>World.Tick锛氳仈鏈烘椂浠呭寘瑁?World.rand锛堜笉鍖呰９闈欐€?Rand锛夛紝閬垮厤鍛戒护鍥炴斁鏃?"Random state from commands doesn't match"銆?/summary>
        /// <remarks>tick &lt; 0锛堝姞鍏?鏈悓姝ワ級鏃朵笉 Push锛岄伩鍏?"last valid tick -1: Wrong random state for the world" 鍗宠繛鍗虫帀銆?/remarks>
        public static class Patch_WorldTick
        {
            public static void Prefix(ref int __state)
            {
                __state = 0;
                if (!MP.IsInMultiplayer) return;
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick < 0) return; // 鏈悓姝ユ椂涓嶅姩 World.rand锛岄槻姝㈠姞鍏ュ嵆 desync
                if (DeterministicRandScope.TryPushWorldRand(tick + WorldSeedOffset))
                {
                    __state = 1;
                    TraceRandScopePush("World.Tick", tick + WorldSeedOffset, __state, null);
                }
            }

            public static void Finalizer(int __state)
            {
                if (__state == 0) return;
                try { DeterministicRandScope.TryPopWorldRand(); } catch { /* 纭繚涓嶅洜 Pop 寮傚父瀵艰嚧鏍堥敊涔?*/ }
                TraceRandScopePop("World.Tick", __state, null);
            }
        }

        /// <summary>Zone GetGizmos锛氱偣寮€绉嶆/閽撻奔/鍫嗘枡鍖烘椂鏄惧紡鍖呰９ map.Rand + 闈欐€?Rand + World.rand锛岄伩鍏?"Wrong random state on map X" 鎺夌嚎銆?/summary>
        /// <remarks>tick &lt; 0锛堝姞鍏?鏈悓姝ワ級鏃剁洿鎺ヨ繑鍥炵┖ Gizmo銆佷笉鎵ц鍘熸柟娉曪紝閬垮厤浠讳綍 Rand 娑堣€楀鑷?"Wrong random state for the world" 鍗宠繛鍗虫帀銆?/remarks>
        public static class Patch_ZoneGetGizmos
        {
            [ThreadStatic] private static Map _zoneMapForRandPop;

            public static bool Prefix(Zone __instance, ref int __state, ref IEnumerable<Gizmo> __result)
            {
                __state = 0;
                _zoneMapForRandPop = null;
                if (!MP.IsInMultiplayer || __instance == null) return true;
                var map = __instance.Map;
                if (map == null) return true;
                int tick = Find.TickManager?.TicksGame ?? 0;
                // 鏈悓姝ユ椂锛坱ick < 0锛変笉鎵ц浠讳綍浼氭秷鑰?World.rand/map.Rand 鐨勯€昏緫锛岀洿鎺ヨ繑鍥炵┖锛岄槻姝㈠姞鍏ュ嵆 desync
                if (tick < 0)
                {
                    __result = Enumerable.Empty<Gizmo>();
                    return false;
                }
                int typeHash = DeterministicTypeHash(__instance.GetType());
                int zoneKey = StableZoneKey(__instance);
                int seed = Gen.HashCombineInt(Gen.HashCombineInt(map.Index, zoneKey), typeHash);
                if (!TryEnterDeterministicRandScope("Zone.GetGizmos", seed, map, ZoneWorldSeedOffset, ref __state, out _zoneMapForRandPop))
                    return true;
                return true;
            }

            public static void Finalizer(int __state)
            {
                if (__state == 0) return;
                var mapForTrace = _zoneMapForRandPop;
                ExitDeterministicRandScope("Zone.GetGizmos", __state, mapForTrace);
                _zoneMapForRandPop = null;
            }
        }

        /// <summary>Zone_Stockpile.GetInspectTabs锛氫笌 GetGizmos 鐩稿悓鐨?Rand 鍖呰９锛泃ick &lt; 0 鏃惰繑鍥炵┖ Tab 鍒楄〃锛岄伩鍏嶅嵆杩炲嵆鎺夈€?/summary>
        public static class Patch_ZoneGetInspectTabs
        {
            [ThreadStatic] private static Map _zoneMapForRandPop;

            public static bool Prefix(Zone __instance, ref int __state, ref IEnumerable<InspectTabBase> __result)
            {
                __state = 0;
                _zoneMapForRandPop = null;
                if (!MP.IsInMultiplayer || __instance == null) return true;
                var map = __instance.Map;
                if (map == null) return true;
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick < 0)
                {
                    __result = Enumerable.Empty<InspectTabBase>();
                    return false;
                }
                int typeHash = DeterministicTypeHash(__instance.GetType());
                int zoneKey = StableZoneKey(__instance);
                int seed = Gen.HashCombineInt(Gen.HashCombineInt(map.Index, zoneKey), typeHash);
                if (!TryEnterDeterministicRandScope("Zone.GetInspectTabs", seed, map, ZoneWorldSeedOffset, ref __state, out _zoneMapForRandPop))
                    return true;
                return true;
            }

            public static void Finalizer(int __state)
            {
                if (__state == 0) return;
                var mapForTrace = _zoneMapForRandPop;
                ExitDeterministicRandScope("Zone.GetInspectTabs", __state, mapForTrace);
                _zoneMapForRandPop = null;
            }
        }

        /// <summary>
        /// 杩滆闃?涓栫晫 UI 鍏ュ彛锛氱粺涓€鍖呰９闈欐€?Rand + map.Rand + World.rand锛岄伩鍏?UI 涓庝笘鐣岃鍥惧垏鎹㈠鑷撮殢鏈虹姸鎬佸崟杈规秷鑰椼€?
        /// </summary>
        public static class Patch_CaravanAndWorldUiRand
        {
            private const int CaravanWorldSeedOffset = 0x39AF;
            [ThreadStatic] private static Stack<Map> _mapStackForPop;

            public static void Prefix(object __instance, MethodBase __originalMethod, ref int __state)
            {
                __state = 0;
                if (!MP.IsInMultiplayer)
                    return;
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick < 0)
                    return;
                try
                {
                    var map = TryResolveMapFromContext(__instance);
                    int seed = BuildWorldUiSeed(__instance, __originalMethod, map);
                    if (!TryEnterDeterministicRandScope(__originalMethod?.Name ?? "?", seed, map, CaravanWorldSeedOffset, ref __state, out var mapForPop))
                        return;
                    if (mapForPop != null)
                    {
                        if (_mapStackForPop == null)
                            _mapStackForPop = new Stack<Map>();
                        _mapStackForPop.Push(mapForPop);
                    }
                    if (ModDebug.EnableCaravanUiRandTrace)
                    {
                        Log.Message($"[MP-MeowOnlineShop] Caravan/UI Rand push: method={__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name} map={(map != null ? map.Index.ToString() : "null")} state=0x{__state:X}");
                    }
                }
                catch (Exception e)
                {
                    var mapForPop = (_mapStackForPop != null && _mapStackForPop.Count > 0) ? _mapStackForPop.Pop() : null;
                    ExitDeterministicRandScope(__originalMethod?.Name ?? "?", __state, mapForPop);
                    __state = 0;
                    if (ModDebug.EnableCaravanUiRandTrace)
                        Log.Warning($"[MP-MeowOnlineShop] Caravan/UI Rand prefix failed in {__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name}: {e.Message}");
                }
            }

            public static void Finalizer(MethodBase __originalMethod, int __state)
            {
                if (__state == 0)
                    return;
                try
                {
                    var mapForPop = (__state & RandScopeMapRand) != 0 && _mapStackForPop != null && _mapStackForPop.Count > 0
                        ? _mapStackForPop.Pop()
                        : null;
                    ExitDeterministicRandScope(__originalMethod?.Name ?? "?", __state, mapForPop);
                    if (ModDebug.EnableCaravanUiRandTrace)
                    {
                        Log.Message($"[MP-MeowOnlineShop] Caravan/UI Rand pop: method={__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name} state=0x{__state:X}");
                    }
                }
                catch (Exception e)
                {
                    if (ModDebug.EnableCaravanUiRandTrace)
                        Log.Warning($"[MP-MeowOnlineShop] Caravan UI Rand finalizer pop failed in {__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name}: {e.Message}");
                }
            }
        }
    }
}
