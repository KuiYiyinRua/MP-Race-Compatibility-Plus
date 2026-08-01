using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Sound aggregation is presentation-only and may run on just one peer depending on local
    /// camera/audio state. RimWorld's SoundSizeAggregator constructor uses Verse.Rand, so without
    /// an isolated state an off-screen fire can advance the synchronized map RNG on one peer.
    /// </summary>
    internal static class Patch_AudioRandIsolation
    {
        private const string SoundSizeAggregatorTypeName = "Verse.Sound.SoundSizeAggregator";

        public static void Apply(Harmony harmony)
        {
            if (!MP.enabled || harmony == null)
                return;

            try
            {
                var type = AccessTools.TypeByName(SoundSizeAggregatorTypeName);
                var target = AccessTools.Constructor(type, Type.EmptyTypes);
                var prefix = AccessTools.Method(
                    typeof(Patch_AudioRandIsolation),
                    nameof(ConstructorPrefix));
                var finalizer = AccessTools.Method(
                    typeof(Patch_AudioRandIsolation),
                    nameof(ConstructorFinalizer));

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Audio Rand isolation unresolved: " +
                        "SoundSizeAggregator constructor not found.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });
                Log.Message(
                    "[MP-MeowOnlineShop] Audio Rand isolation applied: " +
                    "SoundSizeAggregator no longer consumes synchronized map Rand.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[MP-MeowOnlineShop] Audio Rand isolation failed: {ex}");
            }
        }

        private static void ConstructorPrefix(ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;

            Rand.PushState();
            __state = true;
        }

        private static Exception ConstructorFinalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
