using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// SnowGrid.CheckVisualOrPathCostChange consumes Verse.Rand solely to
    /// decide whether to dirty visual map meshes for snow. A pawn entering a
    /// different path cell can therefore advance the shared map Rand stream on
    /// only one peer (Desync-336/337). Keep the mesh-dirty decision local and
    /// deterministic so it cannot shift the synchronized map stream.
    /// </summary>
    internal static class Patch_SnowGridRandIsolation
    {
        private const int SeedOffset = 0x534E4F57; // "SNOW"

        private static bool _applied;
        private static FieldInfo _mapField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(SnowGrid),
                    "CheckVisualOrPathCostChange",
                    new[]
                    {
                        typeof(IntVec3),
                        typeof(float),
                        typeof(float)
                    });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_SnowGridRandIsolation),
                    nameof(CheckVisualOrPathCostChangePrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_SnowGridRandIsolation),
                    nameof(CheckVisualOrPathCostChangeFinalizer));
                _mapField = AccessTools.Field(typeof(SnowGrid), "map");

                if (target == null || prefix == null || finalizer == null ||
                    _mapField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] SnowGrid visual Rand isolation " +
                        "skipped: target method or map field was not resolved.");
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
                    "[MP-MeowOnlineShop] SnowGrid visual Rand isolation active " +
                    "in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] SnowGrid visual Rand isolation " +
                    "install failed: " + e.Message);
            }
        }

        private static void CheckVisualOrPathCostChangePrefix(
            SnowGrid __instance,
            IntVec3 c,
            float oldDepth,
            float newDepth,
            ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            try
            {
                Map map = _mapField.GetValue(__instance) as Map;
                if (map == null)
                    return;

                int seed = Gen.HashCombineInt(SeedOffset, map.uniqueID);
                seed = Gen.HashCombineInt(seed, c.x);
                seed = Gen.HashCombineInt(seed, c.z);
                seed = Gen.HashCombineInt(seed, (int)(oldDepth * 1000f));
                seed = Gen.HashCombineInt(seed, (int)(newDepth * 1000f));
                Rand.PushState(seed);
                __state = 1;
            }
            catch
            {
                __state = 0;
            }
        }

        private static Exception CheckVisualOrPathCostChangeFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
            {
                try
                {
                    Rand.PopState();
                }
                catch
                {
                    // Fail open; the next shared draw would only be one draw
                    // ahead, which is better than breaking map ticking.
                }
            }

            return __exception;
        }
    }
}
