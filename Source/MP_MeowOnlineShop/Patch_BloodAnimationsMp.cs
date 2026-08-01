using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Blood Animations injects cosmetic motes while synchronized map Rand is
    /// active. Their camera/mote-saturation guards are intentionally local, so
    /// one peer may consume Rand while another skips the visual, and several
    /// combat-time callbacks also create visual-only flecks that consume
    /// synchronized map Rand. Suppress those cosmetic callbacks in MP;
    /// gameplay filth logic and SP visuals remain.
    /// </summary>
    internal static class Patch_BloodAnimationsMp
    {
        private const string HealthTickPatchTypeName =
            "BloodAnimations.Pawn_HealthTracker_HealthTick";
        private const string CasingPatchTypeName =
            "BloodAnimations.Verb_TryCastNextBurstShot";
        private const string DeathSprayPatchTypeName =
            "BloodAnimations.Pawn_Kill";
        private const string DamageFilthPatchTypeName =
            "BloodAnimations.GenLeaving_DropFilthDueToDamage";
        private const string InjurySprayPatchTypeName =
            "BloodAnimations.DamageWorker_AddInjury_ApplyToPawn";
        private const string BulletImpactPatchTypeName =
            "BloodAnimations.Bullet_Impact";

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

            var deathSprayPatched = PatchVisualMethod(
                harmony,
                DeathSprayPatchTypeName,
                "Kill",
                new[]
                {
                    typeof(Pawn).MakeByRefType(),
                    typeof(DamageInfo?),
                    typeof(Hediff)
                },
                "death spray");
            var damageFilthPatched = PatchVisualMethod(
                harmony,
                DamageFilthPatchTypeName,
                "DropFilthDueToDamage",
                new[]
                {
                    typeof(Thing),
                    typeof(float)
                },
                "damage filth");
            var injurySprayPatched = PatchVisualMethod(
                harmony,
                InjurySprayPatchTypeName,
                "ApplyToPawn",
                new[]
                {
                    typeof(DamageInfo),
                    typeof(Pawn),
                    typeof(DamageWorker.DamageResult).MakeByRefType()
                },
                "injury spray");
            var bulletImpactPatched = PatchVisualMethod(
                harmony,
                BulletImpactPatchTypeName,
                "Impact",
                new[]
                {
                    typeof(Bullet).MakeByRefType(),
                    typeof(Thing),
                    typeof(bool)
                },
                "bullet impact");

            if (healthTickType == null && casingType == null &&
                AccessTools.TypeByName(DeathSprayPatchTypeName) == null &&
                AccessTools.TypeByName(DamageFilthPatchTypeName) == null &&
                AccessTools.TypeByName(InjurySprayPatchTypeName) == null &&
                AccessTools.TypeByName(BulletImpactPatchTypeName) == null)
            {
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Blood Animations MP guards: HealthTick=" +
                healthTickPatched + ", pawn casing=" + pawnCasingPatched +
                ", turret casing=" + turretCasingPatched +
                ", death spray=" + deathSprayPatched +
                ", damage filth=" + damageFilthPatched +
                ", injury spray=" + injurySprayPatched +
                ", bullet impact=" + bulletImpactPatched +
                ". Cosmetic Rand callbacks are suppressed only in multiplayer.");
        }

        private static bool PatchVisualMethod(
            Harmony harmony,
            string typeName,
            string methodName,
            Type[] parameterTypes,
            string label)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo target = type == null
                ? null
                : AccessTools.Method(type, methodName, parameterTypes);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_BloodAnimationsMp),
                nameof(BloodVisualPrefix));

            if (target == null || prefix == null)
            {
                if (type != null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Blood Animations " + label +
                        " signature drift; cosmetic Rand guard was not installed.");
                }
                return false;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Blood Animations " + label +
                    " guard failed: " + e.Message);
                return false;
            }
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

        private static bool BloodVisualPrefix()
        {
            return !MP.IsInMultiplayer;
        }
    }
}
