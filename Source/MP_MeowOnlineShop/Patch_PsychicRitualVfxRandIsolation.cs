using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI.Group;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Anomaly's Horax psychic ritual ticks visual effects in
    /// PsychicRitualToil_InvokeHorax.TickVfx. That path draws map Rand for
    /// mote placement and an MTB event, but its exact visual outcomes do not
    /// need to be shared. If a dynamic TickList or lord state differs even
    /// briefly, this cosmetic draw collides with real projectile Rand and
    /// produces the "wrong random state on map" desyncs seen in Desync-223,
    /// 224, and 225. Isolate the visual stream with a deterministic seed.
    /// </summary>
    internal static class Patch_PsychicRitualVfxRandIsolation
    {
        private const int HoraxVfxSeedOffset = 0x484F5241;

        private static bool _applied;
        private static bool _loggedActive;

        [ThreadStatic]
        private static Map _mapForRandPop;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(PsychicRitualToil_InvokeHorax),
                    "TickVfx",
                    new[] { typeof(PsychicRitual) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_PsychicRitualVfxRandIsolation),
                    nameof(Prefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_PsychicRitualVfxRandIsolation),
                    nameof(Finalizer));

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Horax psychic-ritual VFX Rand " +
                        "isolation target resolution failed; the shared map " +
                        "stream can still be advanced by visual effects.");
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
                    "[MP-MeowOnlineShop] Horax psychic-ritual VFX Rand " +
                    "isolation active in multiplayer.");
            }
            catch (Exception e)
            {
                _applied = false;
                Log.Warning(
                    "[MP-MeowOnlineShop] Horax psychic-ritual VFX Rand " +
                    "isolation apply failed: " + e.Message);
            }
        }

        private static void Prefix(
            PsychicRitual psychicRitual,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || psychicRitual == null)
                return;

            Map map = psychicRitual.Map;
            if (map == null)
                return;

            int seed = Gen.HashCombineInt(
                HoraxVfxSeedOffset,
                map.uniqueID);
            seed = Gen.HashCombineInt(
                seed,
                RitualLordStableKey(psychicRitual));
            seed = Gen.HashCombineInt(
                seed,
                Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    HoraxVfxSeedOffset + 1,
                    ref __state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Horax psychic-ritual VFX Rand " +
                        "isolated: seedSource=map+ritualLord, " +
                        $"map={map.uniqueID}.");
                }
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception Finalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }

        private static int RitualLordStableKey(PsychicRitual ritual)
        {
            if (ritual?.lord == null)
                return 0;

            try
            {
                if (ritual.lord is ILoadReferenceable referenceable)
                {
                    string id = referenceable.GetUniqueLoadID();
                    if (!string.IsNullOrEmpty(id))
                    {
                        int hash = 0;
                        foreach (char c in id)
                            hash = Gen.HashCombineInt(hash, (int)c);
                        return hash;
                    }
                }

                if (ritual.lord.loadID >= 0)
                    return ritual.lord.loadID;
            }
            catch
            {
                // Fall through to the stable load-ID hash only.
            }

            return 0;
        }
    }
}
