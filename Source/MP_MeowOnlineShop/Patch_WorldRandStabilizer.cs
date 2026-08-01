using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 联机时稳定“世界层”随机状态，减轻 "Wrong random state for the world"、"Random state from commands doesn't match" 与 "last valid tick -1" 掉线。
    /// 原因：加入/加载时或世界 Tick 时，主机与客户端的 World.rand / 当前 Rand 消耗顺序不一致会导致 desync；
    ///       命令回放时若静态 Rand 被整 tick 替换，会导致 "Random state from commands doesn't match"。
    /// 做法：1) World.Tick 内仅包裹 World.rand（不包裹静态 Rand），避免命令执行路径上的 Rand 与多人校验不一致；
    ///       2) Zone_Growing/Zone_Fishing.GetGizmos 用确定性种子包裹 Rand：map + Zone 存档 ID（ILoadReferenceable）+ 类型全名哈希，避免 TicksGame/UI 回放分叉。
    /// </summary>
    internal static class Patch_WorldRandStabilizer
    {
        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();
        internal const int RandScopeStaticRand = 1;
        internal const int RandScopeMapRand = 2;
        internal const int RandScopeWorldRand = 4;
        private const int WorldSeedOffset = 0x1B3C;
        private const int ZoneWorldSeedOffset = 0x2C4D; // Zone 补丁中 World.rand 种子偏移，与 map 区分
        private static int _randScopePushCount;
        private static int _randScopePopCount;

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

        /// <summary>对指定 Map 的 Rand 执行 PushState(seed)，返回是否执行过 Push。</summary>
        private static bool PushMapRand(Map map, int seed)
        {
            if (map == null) return false;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return false;
                var push = mapRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (push == null) return false;
                push.Invoke(mapRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        /// <summary>对指定 Map 的 Rand 执行 PopState。</summary>
        private static void PopMapRand(Map map)
        {
            if (map == null) return;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return;
                mapRand.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(mapRand, null);
            }
            catch { }
        }

        /// <summary>对类型全名做确定性哈希，保证主机与客户端同类型得到相同值（MetadataToken 会因程序集加载顺序不同而不同）。</summary>
        private static int DeterministicTypeHash(Type type)
        {
            if (type == null) return 0;
            string name = type.FullName ?? type.Name ?? "";
            return DeterministicStringHash(name);
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int hash = 0;
            foreach (char c in value)
                hash = Gen.HashCombineInt(hash, (int)c);
            return hash;
        }

        /// <summary>Zone 的稳定键：优先 ILoadReferenceable 的存档 ID（双端一致），避免用 TicksGame 参与 UI 种子导致回放/落后 tick 分叉。</summary>
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

        private static Func<object> TryGetWorldRandGetter()
        {
            try
            {
                return () =>
                {
                    var w = Find.World;
                    if (w == null) return null;
                    var t = w.GetType();
                    return AccessTools.Property(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Property(t, "rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "rand")?.GetValue(w);
                };
            }
            catch { return null; }
        }

        private static bool PushWorldRand(int seed)
        {
            if (WorldRandGetter == null) return false;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return false;
                var push = worldRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (push == null) return false;
                push.Invoke(worldRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        private static void PopWorldRand()
        {
            if (WorldRandGetter == null) return;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return;
                worldRand.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(worldRand, null);
            }
            catch { }
        }

        internal static bool TryEnterDeterministicRandScope(string scope, int seed, Map map, int worldSeedOffset, ref int state, out Map mapForPop)
        {
            state = 0;
            mapForPop = null;
            try
            {
                Rand.PushState(seed);
                state |= RandScopeStaticRand;
                if (PushMapRand(map, seed))
                {
                    mapForPop = map;
                    state |= RandScopeMapRand;
                }

                if (PushWorldRand(seed + worldSeedOffset))
                    state |= RandScopeWorldRand;

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
                if ((state & RandScopeWorldRand) != 0)
                    PopWorldRand();
                if ((state & RandScopeMapRand) != 0 && mapForPop != null)
                    PopMapRand(mapForPop);
                if ((state & RandScopeStaticRand) != 0)
                    Rand.PopState();
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
            try
            {
                var t = instance.GetType();
                var mapObj = AccessTools.Property(t, "Map")?.GetValue(instance)
                             ?? AccessTools.Property(t, "map")?.GetValue(instance)
                             ?? AccessTools.Field(t, "Map")?.GetValue(instance)
                             ?? AccessTools.Field(t, "map")?.GetValue(instance);
                if (mapObj is Map map)
                    return map;
            }
            catch { }
            return null;
        }

        private static int BuildStableContextKey(object instance)
        {
            if (instance == null)
                return 0;
            try
            {
                if (instance is ILoadReferenceable lr)
                    return DeterministicStringHash(lr.GetUniqueLoadID());
            }
            catch { }
            try
            {
                var t = instance.GetType();
                foreach (var name in new[] { "thingIDNumber", "ID", "Tile", "tile", "uniqueID" })
                {
                    var pi = AccessTools.Property(t, name);
                    if (pi != null && pi.PropertyType == typeof(int))
                        return (int)pi.GetValue(instance);
                    var fi = AccessTools.Field(t, name);
                    if (fi != null && fi.FieldType == typeof(int))
                        return (int)fi.GetValue(instance);
                }
            }
            catch { }
            return DeterministicTypeHash(instance.GetType());
        }

        private static int BuildWorldUiSeed(object instance, MethodBase originalMethod, Map map)
        {
            int seed = Gen.HashCombineInt(DeterministicTypeHash(instance?.GetType()), DeterministicStringHash(originalMethod?.Name ?? ""));
            seed = Gen.HashCombineInt(seed, BuildStableContextKey(instance));
            if (map != null)
                seed = Gen.HashCombineInt(seed, map.Index);
            return seed;
        }

        /// <summary>World.Tick：联机时仅包裹 World.rand（不包裹静态 Rand），避免命令回放时 "Random state from commands doesn't match"。</summary>
        /// <remarks>tick &lt; 0（加入/未同步）时不 Push，避免 "last valid tick -1: Wrong random state for the world" 即连即掉。</remarks>
        public static class Patch_WorldTick
        {
            public static void Prefix(ref int __state)
            {
                __state = 0;
                if (!MP.IsInMultiplayer) return;
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick < 0) return; // 未同步时不动 World.rand，防止加入即 desync
                if (PushWorldRand(tick + WorldSeedOffset))
                {
                    __state = 1;
                    TraceRandScopePush("World.Tick", tick + WorldSeedOffset, __state, null);
                }
            }

            public static void Finalizer(int __state)
            {
                if (__state == 0) return;
                try { PopWorldRand(); } catch { /* 确保不因 Pop 异常导致栈错乱 */ }
                TraceRandScopePop("World.Tick", __state, null);
            }
        }

        /// <summary>Zone GetGizmos：点开种植/钓鱼/堆料区时显式包裹 map.Rand + 静态 Rand + World.rand，避免 "Wrong random state on map X" 掉线。</summary>
        /// <remarks>tick &lt; 0（加入/未同步）时直接返回空 Gizmo、不执行原方法，避免任何 Rand 消耗导致 "Wrong random state for the world" 即连即掉。</remarks>
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
                // 未同步时（tick < 0）不执行任何会消耗 World.rand/map.Rand 的逻辑，直接返回空，防止加入即 desync
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

        /// <summary>Zone_Stockpile.GetInspectTabs：与 GetGizmos 相同的 Rand 包裹；tick &lt; 0 时返回空 Tab 列表，避免即连即掉。</summary>
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
        /// 远行队/世界 UI 入口：统一包裹静态 Rand + map.Rand + World.rand，避免 UI 与世界视图切换导致随机状态单边消耗。
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
                    if (!TryEnterDeterministicRandScope($"{__originalMethod?.DeclaringType?.Name}.{__originalMethod?.Name}", seed, map, CaravanWorldSeedOffset, ref __state, out var mapForPop))
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
                    ExitDeterministicRandScope($"{__originalMethod?.DeclaringType?.Name}.{__originalMethod?.Name}", __state, mapForPop);
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
                    ExitDeterministicRandScope($"{__originalMethod?.DeclaringType?.Name}.{__originalMethod?.Name}", __state, mapForPop);
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
