using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Cinders of the Embergarden's HediffComp_Regen.CompPostTick iterates
    /// the pawn's hediffs with GenCollection.InRandomOrder on every check
    /// interval. Under async time the static Rand stream is redirected to the
    /// per-map stream, so this single consumer advances synchronized map Rand
    /// and, once any peer reaches a different draw count, it becomes the first
    /// divergent method (Desync-296 trace:
    /// Embergarden.HediffComp_Regen.CompPostTick -> InRandomOrder).
    ///
    /// Scope the whole comp tick with a deterministic seed derived from the
    /// pawn, map, def, and shared tick. The heal ordering stays deterministic
    /// on both peers and no synchronized Rand state is consumed.
    /// </summary>
    internal static class Patch_EmbergardenRegenMp
    {
        private const int RegenSeedSalt = 0x454D4247; // "EMBG"
        private const int RegenWorldSeedOffset = 0x454D4248; // "EMBH"

        [ThreadStatic]
        private static Map _mapForRandPop;

        [ThreadStatic]
        private static bool _inRegenTickScope;

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            Type compType = AccessTools.TypeByName("Embergarden.HediffComp_Regen");
            MethodInfo target = compType == null
                ? null
                : AccessTools.Method(
                    compType,
                    "CompPostTick",
                    new[] { typeof(float).MakeByRefType() });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_EmbergardenRegenMp),
                nameof(RegenCompTickPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_EmbergardenRegenMp),
                nameof(RegenCompTickFinalizer));

            if (target == null || prefix == null || finalizer == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Embergarden regen Rand isolation " +
                    "skipped (target mod not active or signature changed).");
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
                "[MP-MeowOnlineShop] Embergarden regen Rand isolation active: " +
                "HediffComp_Regen.CompPostTick uses a deterministic scope.");
        }

        private static void RegenCompTickPrefix(
            HediffComp __instance,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;

            if (!MP.IsInMultiplayer ||
                _inRegenTickScope ||
                __instance == null ||
                __instance.Pawn == null)
            {
                return;
            }

            Pawn pawn = __instance.Pawn;
            Map map = pawn.Map;

            int seed = Gen.HashCombineInt(RegenSeedSalt, map?.uniqueID ?? 0);
            seed = Gen.HashCombineInt(seed, pawn.thingIDNumber);
            seed = Gen.HashCombineInt(seed, pawn.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(
                seed,
                Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    RegenWorldSeedOffset,
                    ref __state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
                _inRegenTickScope = true;
            }
        }

        private static Exception RegenCompTickFinalizer(
            Exception __exception,
            int __state)
        {
            if (_inRegenTickScope)
            {
                _inRegenTickScope = false;
                if (__state != 0)
                    DeterministicRandScope.End(__state, _mapForRandPop);
                _mapForRandPop = null;
            }

            return __exception;
        }
    }
}
