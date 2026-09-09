using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Visual motes are ticked through the per-map async Normal TickList, so
    /// their lifespan/maintenance cleanup normally runs in the same map Rand
    /// context as the rest of the simulation. A few modded Mote subclasses may
    /// draw Rand inside Tick or TimeInterval; those draws are visual-only and
    /// must not advance the synchronized map stream. Scope Mote.DoTick with a
    /// deterministic seed so expired effects still destroy themselves while the
    /// shared Rand state stays untouched.
    /// </summary>
    internal static class Patch_MoteLifecycleMp
    {
        private const int MoteTickSeedSalt = 0x4D4F5443; // "MOTC"
        private const int MoteTickWorldSeedOffset = 0x4D4F5444; // "MOTD"

        private static bool _applied;

        [ThreadStatic]
        private static Map _mapForRandPop;

        [ThreadStatic]
        private static bool _inMoteTickScope;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            MethodInfo target = AccessTools.Method(
                typeof(Thing), nameof(Thing.DoTick));
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MoteLifecycleMp),
                nameof(MoteDoTickPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_MoteLifecycleMp),
                nameof(MoteDoTickFinalizer));
            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Mote tick Rand isolation target " +
                    "resolution failed; skipped.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Mote tick Rand isolation active in " +
                "multiplayer.");
        }

        private static void MoteDoTickPrefix(
            Thing __instance,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || _inMoteTickScope ||
                !(__instance is Mote mote))
            {
                return;
            }

            Map map = mote.Map;
            if (map == null)
                return;

            int seed = Gen.HashCombineInt(
                MoteTickSeedSalt,
                map.uniqueID);
            seed = Gen.HashCombineInt(
                seed,
                mote.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(seed, mote.spawnTick);
            seed = Gen.HashCombineInt(
                seed,
                Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    MoteTickWorldSeedOffset,
                    ref __state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
                _inMoteTickScope = true;
            }
        }

        private static Exception MoteDoTickFinalizer(
            Exception __exception,
            int __state)
        {
            if (_inMoteTickScope)
            {
                _inMoteTickScope = false;
                if (__state != 0)
                    DeterministicRandScope.End(__state, _mapForRandPop);
                _mapForRandPop = null;
            }

            return __exception;
        }
    }
}
