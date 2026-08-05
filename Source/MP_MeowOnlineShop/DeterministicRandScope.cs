using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class DeterministicRandScope
    {
        public const int StateStaticRand = 1;
        public const int StateMapRand = 2;
        public const int StateWorldRand = 4;

        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();
        private static readonly object RandReflectionCacheLock = new object();
        private static readonly Dictionary<Type, AccessorCache> MapRandAccessorCacheByType = new Dictionary<Type, AccessorCache>();

        private sealed class AccessorCache
        {
            public Func<object, object> getRand;
            public Action<object, int> pushState;
            public Action<object> popState;
        }

        public static bool Begin(
            Map map,
            int seed,
            int worldSeedOffset,
            ref int state,
            out Map mapForPop,
            bool ignoreGate = false)
        {
            state = 0;
            mapForPop = null;
            if (!ignoreGate && !OptimizationGate.IsDeterministicRandScopeEnabled)
                return false;

            Rand.PushState(seed);
            state |= StateStaticRand;

            if (TryPushMapRand(map, seed))
            {
                mapForPop = map;
                state |= StateMapRand;
            }

            if (TryPushWorldRand(seed + worldSeedOffset))
                state |= StateWorldRand;

            return true;
        }

        public static void End(int state, Map mapForPop)
        {
            if ((state & StateWorldRand) != 0)
                TryPopWorldRand();
            if ((state & StateMapRand) != 0)
                TryPopMapRand(mapForPop);
            if ((state & StateStaticRand) != 0)
                Rand.PopState();
        }

        private static Func<object> TryGetWorldRandGetter()
        {
            try
            {
                return () =>
                {
                    var world = Find.World;
                    if (world == null)
                        return null;

                    var worldType = world.GetType();
                    return AccessTools.Property(worldType, "Rand")?.GetValue(world)
                           ?? AccessTools.Property(worldType, "rand")?.GetValue(world)
                           ?? AccessTools.Field(worldType, "Rand")?.GetValue(world)
                           ?? AccessTools.Field(worldType, "rand")?.GetValue(world);
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool TryPushWorldRand(int seed)
        {
            try
            {
                if (WorldRandGetter == null)
                    return false;
                var rand = WorldRandGetter();
                if (rand == null)
                    return false;
                var cache = GetOrBuildAccessorCache(rand.GetType());
                if (cache.pushState == null)
                    return false;
                cache.pushState(rand, seed);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryPopWorldRand()
        {
            try
            {
                if (WorldRandGetter == null)
                    return;
                var rand = WorldRandGetter();
                if (rand == null)
                    return;
                var cache = GetOrBuildAccessorCache(rand.GetType());
                cache.popState?.Invoke(rand);
            }
            catch
            {
            }
        }

        private static bool TryPushMapRand(Map map, int seed)
        {
            if (map == null)
                return false;
            try
            {
                var cache = GetOrBuildAccessorCache(map.GetType());
                var mapRand = cache.getRand?.Invoke(map);
                if (mapRand == null || cache.pushState == null)
                    return false;
                cache.pushState(mapRand, seed);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryPopMapRand(Map map)
        {
            if (map == null)
                return;
            try
            {
                var cache = GetOrBuildAccessorCache(map.GetType());
                var mapRand = cache.getRand?.Invoke(map);
                if (mapRand == null)
                    return;
                cache.popState?.Invoke(mapRand);
            }
            catch
            {
            }
        }

        private static AccessorCache GetOrBuildAccessorCache(Type type)
        {
            lock (RandReflectionCacheLock)
            {
                AccessorCache cache;
                if (MapRandAccessorCacheByType.TryGetValue(type, out cache))
                    return cache;

                cache = BuildAccessorCache(type);
                OptimizationCacheUtility.EnsureBound(MapRandAccessorCacheByType, 128);
                MapRandAccessorCacheByType[type] = cache;
                return cache;
            }
        }

        private static AccessorCache BuildAccessorCache(Type type)
        {
            var cache = new AccessorCache();
            var prop = AccessTools.Property(type, "Rand") ?? AccessTools.Property(type, "rand");
            var field = AccessTools.Field(type, "Rand") ?? AccessTools.Field(type, "rand");

            Type randType = null;
            if (prop != null)
            {
                randType = prop.PropertyType;
                var getter = prop.GetGetMethod(true);
                if (getter != null)
                {
                    try
                    {
                        var instance = Expression.Parameter(typeof(object), "instance");
                        var body = Expression.Convert(
                            Expression.Call(Expression.Convert(instance, type), getter),
                            typeof(object));
                        cache.getRand = Expression.Lambda<Func<object, object>>(body, instance).Compile();
                    }
                    catch
                    {
                        cache.getRand = instance => prop.GetValue(instance);
                    }
                }
                else
                {
                    cache.getRand = instance => prop.GetValue(instance);
                }
            }
            else if (field != null)
            {
                randType = field.FieldType;
                try
                {
                    var instance = Expression.Parameter(typeof(object), "instance");
                    var body = Expression.Convert(
                        Expression.Field(Expression.Convert(instance, type), field),
                        typeof(object));
                    cache.getRand = Expression.Lambda<Func<object, object>>(body, instance).Compile();
                }
                catch
                {
                    cache.getRand = instance => field.GetValue(instance);
                }
            }

            if (randType != null)
            {
                var push = randType.GetMethod("PushState", new[] { typeof(int) });
                var pop = randType.GetMethod("PopState", Type.EmptyTypes);
                if (push != null)
                {
                    try
                    {
                        var instance = Expression.Parameter(typeof(object), "rand");
                        var seed = Expression.Parameter(typeof(int), "seed");
                        var body = Expression.Call(
                            Expression.Convert(instance, randType),
                            push,
                            seed);
                        cache.pushState = Expression.Lambda<Action<object, int>>(body, instance, seed).Compile();
                    }
                    catch
                    {
                        cache.pushState = (instance, seed) => push.Invoke(instance, new object[] { seed });
                    }
                }
                if (pop != null)
                {
                    try
                    {
                        var instance = Expression.Parameter(typeof(object), "rand");
                        var body = Expression.Call(Expression.Convert(instance, randType), pop);
                        cache.popState = Expression.Lambda<Action<object>>(body, instance).Compile();
                    }
                    catch
                    {
                        cache.popState = instance => pop.Invoke(instance, null);
                    }
                }
            }

            return cache;
        }
    }
}
