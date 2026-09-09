using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat
{
    /// <summary>
    /// Milira/Milian "release floating shield unit" (Milian_BroadShieldAssist)
    /// determinism and multiplayer compatibility. The ability cast itself goes
    /// through the synced Verb.TryStartCastOn path; this module makes the two
    /// release-side risks deterministic: the effect body runs under a stable
    /// Rand scope, and the projectile's knock-back validation no longer reads
    /// per-peer fog.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MiliraBroadShieldLaunch_Compat
    {
        private const string PackageId = "ancot.milirarace";
        private const string AbilityEffectTypeName =
            "Milira.CompAbilityEffect_LaunchBroadShieldUnit";
        private const string ProjectileTypeName =
            "Milira.Projectile_BroadShieldUnit";
        private const int SeedSalt = 0x4D53484C;

        private static bool _initialized;
        private static readonly Harmony HarmonyInstance =
            new Harmony("mp.meowonlineshop.milirabroadshieldlaunch");

        static MiliraBroadShieldLaunch_Compat()
        {
            if (MiliraMpCompatGate.ReferenceModActive)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Milira broad-shield launch module skipped: " +
                    "usamiseika.fixmod.miliramultiplayer is active.");
                return;
            }

            if (!MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            try
            {
                Initialize();
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira broad-shield launch patch failed: " + e);
            }
        }

        private static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;

            Type effectType = AccessTools.TypeByName(AbilityEffectTypeName);
            Type projectileType = AccessTools.TypeByName(ProjectileTypeName);

            MethodInfo apply = effectType == null
                ? null
                : AccessTools.Method(
                    effectType,
                    "Apply",
                    new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) });
            MethodInfo applyPrefix = AccessTools.Method(
                typeof(MiliraBroadShieldLaunch_Compat),
                nameof(ApplyPrefix));
            MethodInfo applyFinalizer = AccessTools.Method(
                typeof(MiliraBroadShieldLaunch_Compat),
                nameof(ApplyFinalizer));
            if (apply != null && applyPrefix != null && applyFinalizer != null)
            {
                HarmonyInstance.Patch(
                    apply,
                    prefix: new HarmonyMethod(applyPrefix),
                    finalizer: new HarmonyMethod(applyFinalizer));
            }

            MethodInfo validKnockBack = projectileType == null
                ? null
                : AccessTools.Method(
                    projectileType,
                    "ValidKnockBackTarget",
                    new[] { typeof(Map), typeof(IntVec3) });
            MethodInfo validPrefix = AccessTools.Method(
                typeof(MiliraBroadShieldLaunch_Compat),
                nameof(ValidKnockBackPrefix));
            if (validKnockBack != null && validPrefix != null)
            {
                HarmonyInstance.Patch(
                    validKnockBack,
                    prefix: new HarmonyMethod(validPrefix));
            }

            Log.Message(
                "[MP-MeowOnlineShop] Milira broad-shield launch patch active: " +
                $"effectRandScope={apply != null}, knockbackFogIndependent={validKnockBack != null}.");
        }

        private static void ApplyPrefix(out bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;

            int seed = Gen.HashCombineInt(
                SeedSalt,
                Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            __state = true;
        }

        private static Exception ApplyFinalizer(
            Exception __exception,
            bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        private static bool ValidKnockBackPrefix(
            Map map,
            IntVec3 cell,
            ref bool __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            // GridsUtility.Fogged is player-relative and can differ between
            // peers, which would make the knock-back destination diverge. The
            // rest of the validation is pure terrain/walkability data.
            if (!cell.IsValid || !cell.InBounds(map) ||
                cell.Impassable(map) || !cell.Walkable(map))
            {
                __result = false;
                return false;
            }

            Building edifice = cell.GetEdifice(map);
            Building_Door door = edifice as Building_Door;
            if (door != null && !door.Open)
            {
                __result = false;
                return false;
            }

            __result = true;
            return false;
        }
    }
}
