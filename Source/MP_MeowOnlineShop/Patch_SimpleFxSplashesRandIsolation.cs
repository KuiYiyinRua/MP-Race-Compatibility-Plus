using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Simple FX: Splashes decides which flecks to draw from local camera state.
    /// Fleck setup consumes Verse.Rand, so a peer looking at a different cell can
    /// otherwise advance the synchronized map stream on only that peer.
    /// Keep the visual effect, but contain all of its Rand use in a local scope.
    /// </summary>
    internal static class Patch_SimpleFxSplashesRandIsolation
    {
        private const string SplashesUtilityTypeName =
            "SimpleFxSplashes.SplashesUtility";

        private static int _traceCount;

        internal static void Apply(Harmony harmony)
        {
            Type type = AccessTools.TypeByName(SplashesUtilityTypeName);
            if (type == null)
                return;

            var target = AccessTools.Method(type, "ProcessSplashes", new[] { typeof(Map) });
            if (target == null || target.ReturnType != typeof(void))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Simple FX: Splashes ProcessSplashes signature drift; " +
                    "visual Rand isolation was not installed.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_SimpleFxSplashesRandIsolation),
                    nameof(Prefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_SimpleFxSplashesRandIsolation),
                    nameof(Finalizer)))
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Simple FX: Splashes camera-local visual Rand isolation active in multiplayer.");
        }

        private static void Prefix(Map __0, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || __0 == null)
                return;

            int seed = Gen.HashCombineInt(0x53504C53, __0.uniqueID);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            __state = true;

            if (++_traceCount <= 4 && IsRjwAddonTestEnabled())
            {
                Log.Message(
                    $"[MP-MeowOnlineShop][test] SPLASH_RAND map={__0.uniqueID} " +
                    $"tick={Find.TickManager?.TicksGame ?? 0} seed={seed} ordinal={_traceCount}");
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        private static bool IsRjwAddonTestEnabled()
        {
            string enabledText;
            return GenCommandLine.TryGetCommandLineArg(
                       "mpautotestrjwaddons",
                       out enabledText) &&
                   bool.TryParse(enabledText, out var enabled) &&
                   enabled;
        }
    }
}
