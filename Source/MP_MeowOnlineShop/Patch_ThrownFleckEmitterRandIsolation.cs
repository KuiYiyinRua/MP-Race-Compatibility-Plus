using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// CompThrownFleckEmitter is a vanilla visual-only component. Its CompTick
    /// calls EmissionOffset/EmissionColor through Rand.Range while the per-map
    /// async Rand context is active, so any peer-local visual difference
    /// advances the synchronized map stream and surfaces as the first desynced
    /// map random state (Desync trace: AncientHermeticCrate + this emitter).
    /// Wrap only the tick with a deterministic scope in multiplayer; the
    /// emitter still emits flecks, but its draws cannot move the shared state.
    /// </summary>
    internal static class Patch_ThrownFleckEmitterRandIsolation
    {
        private const int FleckTickSeedSalt = 0x5448464C; // "THFL"
        private const int FleckTickWorldSeedOffset = 0x5448464D; // "THFM"

        private static bool _applied;

        [ThreadStatic]
        private static Map _mapForRandPop;

        [ThreadStatic]
        private static bool _inFleckTickScope;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;

            Type compType = typeof(CompThrownFleckEmitter);
            MethodInfo target = AccessTools.Method(compType, "CompTick");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_ThrownFleckEmitterRandIsolation),
                nameof(FleckCompTickPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_ThrownFleckEmitterRandIsolation),
                nameof(FleckCompTickFinalizer));

            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] CompThrownFleckEmitter Rand " +
                    "isolation target resolution failed; skipped.");
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

            _applied = true;
            Log.Message(
                "[MP-MeowOnlineShop] CompThrownFleckEmitter visual Rand " +
                "isolation active in multiplayer.");
        }

        private static void FleckCompTickPrefix(
            ThingComp __instance,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;

            if (!MP.IsInMultiplayer || _inFleckTickScope ||
                __instance == null || __instance.parent == null)
            {
                return;
            }

            Map map = __instance.parent.Map;
            if (map == null)
                return;

            int seed = Gen.HashCombineInt(
                FleckTickSeedSalt,
                map.uniqueID);
            seed = Gen.HashCombineInt(
                seed,
                __instance.parent.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(
                seed,
                __instance.parent.thingIDNumber);
            seed = Gen.HashCombineInt(
                seed,
                Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    FleckTickWorldSeedOffset,
                    ref __state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
                _inFleckTickScope = true;
            }
        }

        private static Exception FleckCompTickFinalizer(
            Exception __exception,
            int __state)
        {
            if (_inFleckTickScope)
            {
                _inFleckTickScope = false;
                if (__state != 0)
                    DeterministicRandScope.End(__state, _mapForRandPop);
                _mapForRandPop = null;
            }

            return __exception;
        }
    }
}
