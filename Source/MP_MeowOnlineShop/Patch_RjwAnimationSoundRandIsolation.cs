using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-16/17: Kiiro_Race99652 consumes four/eight extra map RNG draws
    /// on the client while playing RJW animation voices. The preceding traces
    /// match; CheckAndPlaySounds reads the local render tree and audio options.
    /// Scope the whole audio callback, including voice probability, cooldown
    /// selection, root/child nodes and sound patches. Scoping only voiceAtTick
    /// misses ticksBetweenPlays.RandomInRange in the caller.
    /// Verified against Rimworld Animations 2.0 / 2.2.0 shipped source and DLL
    /// SHA256 1D0551790A15BFE9F7B7BF015F626582DDFE516A4F6EF4094B0279179E5FECDC.
    /// </summary>
    internal static class Patch_RjwAnimationSoundRandIsolation
    {
        private static bool applied;

        internal static void Apply(Harmony harmony)
        {
            if (applied || harmony == null || !MP.enabled) return;
            var type = AccessTools.TypeByName("Rimworld_Animations.CompExtendedAnimator");
            if (type == null) return; // Optional mod; independent of RJWPE.

            try
            {
                var target = AccessTools.DeclaredMethod(type, "CheckAndPlaySounds", Type.EmptyTypes);
                if (!typeof(ThingComp).IsAssignableFrom(type) || target == null
                    || target.IsStatic || target.ReturnType != typeof(void))
                    throw new MissingMethodException(type.FullName, "CheckAndPlaySounds()");

                // This source can be shipped as a supplemental assembly before
                // the next core release. Either startup order must install only
                // one scope when a later core also includes the same fix.
                if (Harmony.GetPatchInfo(target)?.Prefixes.Any(p =>
                    p.PatchMethod.DeclaringType?.FullName == typeof(Patch_RjwAnimationSoundRandIsolation).FullName) == true)
                {
                    applied = true;
                    Log.Message("[MP-MeowOnlineShop][RJW-Animation-Audio] Existing isolation patch retained.");
                    return;
                }

                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(Patch_RjwAnimationSoundRandIsolation), nameof(Prefix))
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(typeof(Patch_RjwAnimationSoundRandIsolation), nameof(Finalizer))
                    {
                        priority = Priority.Last
                    });
                applied = true;
                Log.Message("[MP-MeowOnlineShop][RJW-Animation-Audio] CheckAndPlaySounds cosmetic Rand isolation registered.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][RJW-Animation-Audio] REQUIRED_TARGET_FAILURE: " + e);
            }
        }

        private static void Prefix(out bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer) return;
            Rand.PushState();
            __state = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            // Restore even when a missing render node or another sound patch
            // throws. Preserve the exception and the original audio behavior.
            // CompTick's animation progression stays outside this scope.
            if (__state) Rand.PopState();
            return __exception;
        }
    }
}
