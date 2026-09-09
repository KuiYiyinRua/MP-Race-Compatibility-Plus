using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Axolotl's CompGiveThingMote selects a visual mote with Verse.Rand during
    /// CompTick. The mote reference and lastCount are not serialized, so a
    /// joining peer can enter a different branch and advance the synchronized
    /// map Rand stream (Desync-284). Isolate the visual selection so item motes
    /// stay local to each peer.
    /// </summary>
    internal static class Patch_AxolotlMoteRandIsolation
    {
        private const string CompTypeName = "Axolotl.CompGiveThingMote";
        private const int MoteSeedSalt = unchecked((int)0x41784D54); // "AxMT"
        private const int WorldSeedOffset = unchecked((int)0x41784D55);

        private static bool _applied;

        [ThreadStatic]
        private static Map _mapForRandPop;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            Type compType = AccessTools.TypeByName(CompTypeName);
            MethodInfo target = compType == null
                ? null
                : AccessTools.Method(compType, "CompTick", Type.EmptyTypes);
            if (target == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl item-mote Rand isolation: " +
                    CompTypeName + ".CompTick was not resolved; skipped.");
                return;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(
                        typeof(Patch_AxolotlMoteRandIsolation),
                        nameof(CompTickPrefix))
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(
                        typeof(Patch_AxolotlMoteRandIsolation),
                        nameof(CompTickFinalizer))
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Axolotl item-mote Rand isolation " +
                    "active in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl item-mote Rand isolation " +
                    "install failed: " + e.Message);
            }
        }

        private static void CompTickPrefix(
            ThingComp __instance,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance?.parent == null)
                return;

            Map map = __instance.parent.Map;
            if (map == null)
                return;

            int seed = Gen.HashCombineInt(
                MoteSeedSalt,
                map.uniqueID);
            seed = Gen.HashCombineInt(
                seed,
                __instance.parent.thingIDNumber);
            seed = Gen.HashCombineInt(
                seed,
                __instance.parent.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(
                seed,
                Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    WorldSeedOffset,
                    ref __state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception CompTickFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }
    }
}
