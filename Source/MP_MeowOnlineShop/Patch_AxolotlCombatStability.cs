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
            // Map.Index is a session-local list position. It can differ after
            // a rejoin or map add/remove, while uniqueID is the stable map
            // identity required for a cross-peer Rand scope.
            seed = Gen.HashCombineInt(seed, map.uniqueID);
            seed = Gen.HashCombineInt(seed, instigator.thingIDNumber);
            seed = Gen.HashCombineInt(seed, parent?.thingIDNumber ?? 0);
            seed = Gen.HashCombineInt(seed, targetThing?.thingIDNumber ?? 0);

            DeterministicRandScope.Begin(
                map,
                seed,
                WorldSeedOffset,
                ref __state,
                out _mapForRandPop,
                ignoreGate: true);
        }

        public static void MeleeEnhanceFinalizer(int __state)
        {
            if (__state == 0)
                return;

            try
            {
                DeterministicRandScope.End(__state, _mapForRandPop);
            }
            catch
            {
            }
            finally
            {
                _mapForRandPop = null;
            }
        }
    }
}
