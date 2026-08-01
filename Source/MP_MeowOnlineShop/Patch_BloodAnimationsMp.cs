using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Blood Animations injects cosmetic motes while synchronized map Rand is
    /// active. Their camera/mote-saturation guards are intentionally local, so
    /// one peer may consume Rand while another skips the visual. Suppress those
    /// cosmetic callbacks in MP; gameplay filth logic and SP visuals remain.
    /// </summary>
    internal static class Patch_BloodAnimationsMp
    {
        private const string HealthTickPatchTypeName =
            "BloodAnimations.Pawn_HealthTracker_HealthTick";
        private const string CasingPatchTypeName =
            "BloodAnimations.Verb_TryCastNextBurstShot";

        internal static void Apply(Harmony harmony)
        {
            var healthTickType = AccessTools.TypeByName(HealthTickPatchTypeName);
            var healthTickTarget = healthTickType == null
                ? null
                : AccessTools.Method(healthTickType, "HealthTick");
            var healthTickPrefix = AccessTools.Method(
                typeof(Patch_BloodAnimationsMp),
                nameof(HealthTickVisualPrefix));

            var healthTickPatched = false;
            if (healthTickTarget != null && healthTickTarget.IsStatic &&
                healthTickTarget.ReturnType == typeof(void) &&
                healthTickTarget.GetParameters().Length == 2)
            {
                harmony.Patch(healthTickTarget, prefix: new HarmonyMethod(healthTickPrefix)
                {
                    priority = Priority.First
                });
                healthTickPatched = true;
            }

            var casingType = AccessTools.TypeByName(CasingPatchTypeName);
            var pawnCasingTarget = casingType == null
                ? null
                : AccessTools.Method(casingType, "ThrowCasing", new[]
                {
                    typeof(Pawn), typeof(Map), typeof(int), typeof(ThingDef),
                    typeof(float)
                });
            var turretCasingTarget = casingType == null
                ? null
                : AccessTools.Method(casingType, "ThrowCasingTurret", new[]
                {
                    typeof(Thing), typeof(Map), typeof(int), typeof(ThingDef)
                });
            var casingPrefix = AccessTools.Method(
                typeof(Patch_BloodAnimationsMp), nameof(CasingMotePrefix));

            var pawnCasingPatched = PatchCasingMethod(
                harmony, pawnCasingTarget, casingPrefix, "ThrowCasing");
            var turretCasingPatched = PatchCasingMethod(
                harmony, turretCasingTarget, casingPrefix, "ThrowCasingTurret");

            if (healthTickType == null && casingType == null)
                return;

            Log.Message(
                "[MP-MeowOnlineShop] Blood Animations MP guards: HealthTick=" +
                healthTickPatched + ", pawn casing=" + pawnCasingPatched +
                ", turret casing=" + turretCasingPatched +
                ". Cosmetic Rand callbacks are suppressed only in multiplayer.");
        }

        private static bool PatchCasingMethod(
            Harmony harmony, MethodInfo target, MethodInfo prefix, string methodName)
        {
            if (target == null || !target.IsStatic || target.ReturnType != typeof(Mote))
            {
                if (AccessTools.TypeByName(CasingPatchTypeName) != null)
                    Log.Warning(
                        "[MP-MeowOnlineShop] Blood Animations " + methodName +
                        " signature drift; cosmetic casing Rand guard was not installed.");
                return false;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix)
            {
                priority = Priority.First
            });
            return true;
        }

        private static bool HealthTickVisualPrefix()
        {
            return !MP.IsInMultiplayer;
        }

        private static bool CasingMotePrefix(ref Mote __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            __result = null;
            return false;
        }
    }
}
