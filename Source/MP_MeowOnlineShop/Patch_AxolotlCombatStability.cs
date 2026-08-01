using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Axolotl 强化攻击链路的局部随机稳定补丁。
    /// 目前聚焦近战螈力强化入口 ApplyEffectToTarget（内部会随机挑选 fleck）。
    /// </summary>
    internal static class Patch_AxolotlCombatStability
    {
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;

        private const int SeedOffsetMeleeEnhance = 0x7E4D;
        private const int WorldSeedOffset = 0x5D1A;

        [ThreadStatic] private static Map _mapForRandPop;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            var compType = AccessTools.TypeByName("Axolotl.Comp_LotiQiCloseCombatWeapon_ProjectileCost");
            var method = AccessTools.Method(compType, "ApplyEffectToTarget", new[] { typeof(Thing), typeof(Pawn), typeof(float), typeof(float) });
            var prefix = AccessTools.Method(typeof(Patch_AxolotlCombatStability), nameof(MeleeEnhancePrefix));
            var finalizer = AccessTools.Method(typeof(Patch_AxolotlCombatStability), nameof(MeleeEnhanceFinalizer));
            if (compType == null || method == null || prefix == null || finalizer == null)
                return;

            harmony.Patch(method, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
            Log.Message("[MP-MeowOnlineShop] Axolotl combat stability patch active: melee enhance rand scope.");
        }

        public static void MeleeEnhancePrefix(object __instance, Thing targetThing, Pawn instigator, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;

            if (!MP.IsInMultiplayer || __instance == null || instigator == null)
                return;

            var parent = (__instance as ThingComp)?.parent;
            var map = instigator.Map ?? parent?.Map;
            if (map == null)
                return;

            int seed = SeedOffsetMeleeEnhance;
            seed = Gen.HashCombineInt(seed, map.Index);
            seed = Gen.HashCombineInt(seed, instigator.thingIDNumber);
            seed = Gen.HashCombineInt(seed, parent?.thingIDNumber ?? 0);
            seed = Gen.HashCombineInt(seed, targetThing?.thingIDNumber ?? 0);

            Rand.PushState(seed);
            __state |= StateStaticRand;

            if (PushMapRand(map, seed))
            {
                _mapForRandPop = map;
                __state |= StateMapRand;
            }

            if (PushWorldRand(seed + WorldSeedOffset))
                __state |= StateWorldRand;
        }

        public static void MeleeEnhanceFinalizer(int __state)
        {
            if (__state == 0)
                return;

            try
            {
                if ((__state & StateWorldRand) != 0)
                    PopWorldRand();

                if ((__state & StateMapRand) != 0 && _mapForRandPop != null)
                {
                    PopMapRand(_mapForRandPop);
                    _mapForRandPop = null;
                }

                if ((__state & StateStaticRand) != 0)
                    Rand.PopState();
            }
            catch
            {
            }
        }

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

        private static void PopMapRand(Map map)
        {
            if (map == null) return;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                mapRand?.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(mapRand, null);
            }
            catch { }
        }

        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();

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

        private static bool PushWorldRand(int seed)
        {
            if (WorldRandGetter == null) return false;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return false;
                worldRand.GetType().GetMethod("PushState", new[] { typeof(int) })?.Invoke(worldRand, new object[] { seed });
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
                worldRand?.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(worldRand, null);
            }
            catch { }
        }
    }
}
