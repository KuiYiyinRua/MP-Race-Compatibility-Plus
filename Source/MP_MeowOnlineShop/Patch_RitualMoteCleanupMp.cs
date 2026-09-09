using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Ideology lightball/loudspeaker comps cache their ritual motes in private
    /// fields. During a gravship takeoff the launch ritual ends and the old map
    /// stops being maintained while it is paused/torn down, so those
    /// needsMaintenance motes can already be destroyed by the time the comps run
    /// once more inside the per-map async TickList. Vanilla checks Destroyed
    /// before recreating motes but not before the off-branch Destroy call, so it
    /// logs "Tried to destroy already-destroyed thing" repeatedly.
    ///
    /// This patch normalizes the cached mote fields before vanilla CompTick
    /// executes: a destroyed mote is treated as absent, which is exactly the
    /// state vanilla assumes after a successful Destroy. It changes no
    /// simulation state and does not add a sync command.
    /// </summary>
    internal static class Patch_RitualMoteCleanupMp
    {
        private static AccessTools.FieldRef<CompLoudspeaker, Mote> loudspeakerLightsRef;
        private static AccessTools.FieldRef<CompLightball, Mote> lightballRotationRef;
        private static AccessTools.FieldRef<CompLightball, Mote> lightballLightsRef;
        private static bool applied;

        internal static void Apply(Harmony harmony)
        {
            if (applied || harmony == null || !MP.enabled)
                return;
            applied = true;

            try
            {
                loudspeakerLightsRef = CreateRef<CompLoudspeaker, Mote>("lightsMote");
                lightballRotationRef = CreateRef<CompLightball, Mote>("rotationMote");
                lightballLightsRef = CreateRef<CompLightball, Mote>("lightsMote");

                MethodInfo loudspeakerTick = AccessTools.Method(
                    typeof(CompLoudspeaker),
                    nameof(CompLoudspeaker.CompTick));
                MethodInfo lightballTick = AccessTools.Method(
                    typeof(CompLightball),
                    nameof(CompLightball.CompTick));
                MethodInfo loudspeakerPrefix = AccessTools.Method(
                    typeof(Patch_RitualMoteCleanupMp),
                    nameof(LoudspeakerTickPrefix));
                MethodInfo lightballPrefix = AccessTools.Method(
                    typeof(Patch_RitualMoteCleanupMp),
                    nameof(LightballTickPrefix));

                if (loudspeakerTick == null || lightballTick == null ||
                    loudspeakerPrefix == null || lightballPrefix == null ||
                    loudspeakerLightsRef == null || lightballRotationRef == null ||
                    lightballLightsRef == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ritual mote cleanup skipped: " +
                        $"loudspeakerTick={loudspeakerTick != null} " +
                        $"lightballTick={lightballTick != null} " +
                        $"loudspeakerRef={loudspeakerLightsRef != null} " +
                        $"lightballRotationRef={lightballRotationRef != null} " +
                        $"lightballLightsRef={lightballLightsRef != null}.");
                    return;
                }

                harmony.Patch(
                    loudspeakerTick,
                    prefix: new HarmonyMethod(loudspeakerPrefix)
                    {
                        priority = Priority.First
                    });
                harmony.Patch(
                    lightballTick,
                    prefix: new HarmonyMethod(lightballPrefix)
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Ritual mote cleanup active: stale " +
                    "destroyed lightball/loudspeaker motes are cleared before " +
                    "vanilla CompTick.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ritual mote cleanup install failed: " + e);
            }
        }

        private static void LoudspeakerTickPrefix(CompLoudspeaker __instance)
        {
            if (__instance == null || loudspeakerLightsRef == null)
                return;

            Mote mote = loudspeakerLightsRef(__instance);
            if (mote != null && mote.Destroyed)
                loudspeakerLightsRef(__instance) = null;
        }

        private static void LightballTickPrefix(CompLightball __instance)
        {
            if (__instance == null)
                return;

            if (lightballRotationRef != null)
            {
                Mote mote = lightballRotationRef(__instance);
                if (mote != null && mote.Destroyed)
                    lightballRotationRef(__instance) = null;
            }

            if (lightballLightsRef != null)
            {
                Mote mote = lightballLightsRef(__instance);
                if (mote != null && mote.Destroyed)
                    lightballLightsRef(__instance) = null;
            }
        }

        private static AccessTools.FieldRef<T, F> CreateRef<T, F>(string fieldName)
            where T : class
        {
            try
            {
                return AccessTools.FieldRefAccess<T, F>(fieldName);
            }
            catch
            {
                return null;
            }
        }
    }
}
