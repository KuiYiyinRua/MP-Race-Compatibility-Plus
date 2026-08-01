using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Culture DLC (Ideology) ceremonies tick ritual visual effects from
    /// `RimWorld.RitualVisualEffect.Tick`, which draws map Rand for mote/fleck
    /// placement (`CompRitualEffect_IntervalSpawn*.SpawnPos`,
    /// `RandomInRange`, `RandomElementByWeight`). During a ceremony the same
    /// visual effect runs on both peers, but any per-peer difference in lord
    /// state, stage timing, or spawned attendees advances the synchronized map
    /// stream on only one side (Desync-95..100, 102).
    ///
    /// Ritual visual effects are cosmetic: their exact mote positions do not
    /// need to be shared. Isolating all of their Rand use in a deterministic
    /// local scope (seeded by the ritual lord's stable load ID and the map)
    /// keeps the visuals alive on both peers while removing the ceremony path
    /// from the synchronized map stream entirely.
    /// </summary>
    internal static class Patch_RitualVisualEffectRandIsolation
    {
        private const int RitualVfxSeedOffset = 0x51BE77;
        private static bool _loggedActive;

        [ThreadStatic]
        private static Map _mapForRandPop;

        internal static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(RitualVisualEffect), "Tick");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_RitualVisualEffectRandIsolation),
                nameof(Prefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_RitualVisualEffectRandIsolation),
                nameof(Finalizer));

            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] RitualVisualEffect.Tick target resolution failed; ceremony Rand isolation skipped.");
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
                "[MP-MeowOnlineShop] Ritual visual-effect Rand isolation active in multiplayer.");
        }

        private static void Prefix(RitualVisualEffect __instance, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Map map = __instance.ritual?.Map;
            if (map == null)
                return;

            // RitualVisualEffect.Tick runs inside the map's async-time tick, so
            // Find.TickManager.TicksGame is the shared synchronized mapTicks here.
            // Including it in the seed keeps per-tick visual variety while staying
            // deterministic across peers.
            int seed = Gen.HashCombineInt(RitualVfxSeedOffset, map.uniqueID);
            seed = Gen.HashCombineInt(seed, RitualLordStableKey(__instance.ritual));
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(map, seed, RitualVfxSeedOffset + 1, ref __state, out var mapForPop))
            {
                _mapForRandPop = mapForPop;
                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Ritual ceremony visual Rand isolated: " +
                        $"seedSource=map+ritualLord, map={map.uniqueID}.");
                }
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception Finalizer(Exception __exception, int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }

        private static int RitualLordStableKey(LordJob_Ritual ritual)
        {
            if (ritual == null)
                return 0;
            try
            {
                if (ritual.lord != null)
                {
                    if (ritual.lord is ILoadReferenceable lr)
                    {
                        var id = lr.GetUniqueLoadID();
                        if (!string.IsNullOrEmpty(id))
                        {
                            int h = 0;
                            foreach (char c in id)
                                h = Gen.HashCombineInt(h, (int)c);
                            return h;
                        }
                    }

                    if (ritual.lord.loadID >= 0)
                        return ritual.lord.loadID;
                }
            }
            catch
            {
                // ignored
            }

            return 0;
        }
    }
}
