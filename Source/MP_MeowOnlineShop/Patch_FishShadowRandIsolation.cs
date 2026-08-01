using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Odyssey fish shadows are visual-only, but FindFishFleckLocation chooses
    /// from a HashSet with Verse.Rand during MapComponentTick. A large mod stack
    /// can leave that set in a different enumeration order on a joining peer.
    /// Preserve the visual update while preventing it from advancing the
    /// synchronized map Rand stream.
    /// </summary>
    internal static class Patch_FishShadowRandIsolation
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Verse.FishShadowComponent");
            var target = type == null
                ? null
                : AccessTools.Method(type, "MapComponentTick");

            if (target == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Fish-shadow Rand isolation target was not found.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_FishShadowRandIsolation),
                    nameof(Prefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_FishShadowRandIsolation),
                    nameof(Finalizer)))
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Odyssey fish-shadow visual Rand isolation active in multiplayer.");
        }

        private static void Prefix(MapComponent __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            int seed = Gen.HashCombineInt(
                __instance.map?.uniqueID ?? 0,
                Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            __state = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
