using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Projectiles keep a live reference to the Thing that launched them. When
    /// that launcher is destroyed while a projectile is still in flight, the
    /// reference survives in host memory but cannot be resolved from a
    /// Multiplayer snapshot. Rejoin logs show
    /// "Could not resolve reference ... curParent=Bullet_...", after which the
    /// host impact runs with the destroyed launcher while the rejoined client
    /// runs with null, so AncotLibrary.Projectile_Custom dereferences
    /// launcher.Map on only one peer.
    ///
    /// This patch:
    /// 1. Clears destroyed launcher/equipment references once, at save time,
    ///    so future snapshots and live objects agree on null.
    /// 2. Replays the two small Ancot impact methods with a null-safe impact
    ///    effecter, so Milira plasma projectiles (Projectile_Custom /
    ///    Projectile_ExplosiveCustom) cannot diverge on a stale launcher.
    /// </summary>
    internal static class Patch_ProjectileLauncherDeterminism
    {
        private const string ProjectileCustomTypeName =
            "AncotLibrary.Projectile_Custom";
        private const string ProjectileExplosiveCustomTypeName =
            "AncotLibrary.Projectile_ExplosiveCustom";
        private const string ProjectileCustomExtensionTypeName =
            "AncotLibrary.Projectile_Custom_Extension";
        private const string ProjectileExplosiveCustomExtensionTypeName =
            "AncotLibrary.Projectile_ExplosiveCustom_Extension";

        private static FieldInfo _launcherField;
        private static FieldInfo _equipmentField;

        private static Type _projectileCustomType;
        private static Type _projectileExplosiveCustomType;
        private static PropertyInfo _customPropsProperty;
        private static PropertyInfo _explosivePropsProperty;
        private static FieldInfo _customImpactEffecterField;
        private static FieldInfo _explosiveImpactEffecterField;
        private static MethodInfo _bulletImpactMethod;
        private static MethodInfo _projectileExplosiveImpactMethod;

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            _launcherField = AccessTools.Field(typeof(Projectile), "launcher");
            _equipmentField = AccessTools.Field(typeof(Projectile), "equipment");

            int patchedSave = 0;
            int patchedImpacts = 0;

            MethodInfo exposeData = AccessTools.Method(
                typeof(Projectile),
                nameof(Projectile.ExposeData));
            MethodInfo exposeDataPrefix = AccessTools.Method(
                typeof(Patch_ProjectileLauncherDeterminism),
                nameof(ExposeDataPrefix));
            if (exposeData != null &&
                exposeDataPrefix != null &&
                _launcherField != null &&
                _equipmentField != null)
            {
                harmony.Patch(
                    exposeData,
                    prefix: new HarmonyMethod(exposeDataPrefix)
                    {
                        priority = Priority.First
                    });
                patchedSave++;
            }

            _projectileCustomType =
                AccessTools.TypeByName(ProjectileCustomTypeName);
            _projectileExplosiveCustomType =
                AccessTools.TypeByName(ProjectileExplosiveCustomTypeName);

            _customImpactEffecterField =
                AccessTools.Field(
                    AccessTools.TypeByName(ProjectileCustomExtensionTypeName),
                    "impactEffecter");
            _explosiveImpactEffecterField = AccessTools.Field(
                AccessTools.TypeByName(
                    ProjectileExplosiveCustomExtensionTypeName),
                "impactEffecter");

            Type bulletType =
                AccessTools.TypeByName("Verse.Bullet")
                ?? AccessTools.TypeByName("RimWorld.Bullet");
            Type explosiveType =
                AccessTools.TypeByName("Verse.Projectile_Explosive")
                ?? AccessTools.TypeByName("RimWorld.Projectile_Explosive");
            _bulletImpactMethod = bulletType == null
                ? null
                : AccessTools.Method(
                    bulletType,
                    "Impact",
                    new[] { typeof(Thing), typeof(bool) });
            _projectileExplosiveImpactMethod = explosiveType == null
                ? null
                : AccessTools.Method(
                    explosiveType,
                    "Impact",
                    new[] { typeof(Thing), typeof(bool) });

            MethodInfo customImpactPrefix = AccessTools.Method(
                typeof(Patch_ProjectileLauncherDeterminism),
                nameof(CustomImpactPrefix));
            MethodInfo explosiveCustomImpactPrefix = AccessTools.Method(
                typeof(Patch_ProjectileLauncherDeterminism),
                nameof(ExplosiveCustomImpactPrefix));

            bool reversePatchesReady = InstallBaseImpactReversePatches(harmony);

            if (_projectileCustomType != null &&
                _bulletImpactMethod != null &&
                reversePatchesReady &&
                customImpactPrefix != null)
            {
                _customPropsProperty = AccessTools.Property(
                    _projectileCustomType,
                    "Props");
                MethodInfo impact = AccessTools.Method(
                    _projectileCustomType,
                    "Impact",
                    new[] { typeof(Thing), typeof(bool) });
                if (impact != null && _customPropsProperty != null)
                {
                    harmony.Patch(
                        impact,
                        prefix: new HarmonyMethod(customImpactPrefix)
                        {
                            priority = Priority.First
                        });
                    patchedImpacts++;
                }
            }

            if (_projectileExplosiveCustomType != null &&
                _projectileExplosiveImpactMethod != null &&
                reversePatchesReady &&
                explosiveCustomImpactPrefix != null)
            {
                _explosivePropsProperty = AccessTools.Property(
                    _projectileExplosiveCustomType,
                    "Props");
                MethodInfo impact = AccessTools.Method(
                    _projectileExplosiveCustomType,
                    "Impact",
                    new[] { typeof(Thing), typeof(bool) });
                if (impact != null && _explosivePropsProperty != null)
                {
                    harmony.Patch(
                        impact,
                        prefix: new HarmonyMethod(explosiveCustomImpactPrefix)
                        {
                            priority = Priority.First
                        });
                    patchedImpacts++;
                }
            }

            if (patchedSave == 0 && patchedImpacts == 0)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Projectile launcher determinism " +
                    "skipped (no resolvable vanilla/Ancot targets).");
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Projectile launcher determinism active: " +
                $"saveClearing={patchedSave}, ancotImpacts={patchedImpacts}, " +
                $"baseImpactReversePatches={reversePatchesReady}.");
        }

        private static bool InstallBaseImpactReversePatches(Harmony harmony)
        {
            if (_bulletImpactMethod == null || _projectileExplosiveImpactMethod == null)
                return false;

            try
            {
                harmony.CreateReversePatcher(
                    _bulletImpactMethod,
                    new HarmonyMethod(AccessTools.Method(
                        typeof(Patch_ProjectileLauncherDeterminism),
                        nameof(CallOriginalBulletImpact))))
                    .Patch();
                harmony.CreateReversePatcher(
                    _projectileExplosiveImpactMethod,
                    new HarmonyMethod(AccessTools.Method(
                        typeof(Patch_ProjectileLauncherDeterminism),
                        nameof(CallOriginalExplosiveImpact))))
                    .Patch();
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Projectile base Impact reverse patches failed; " +
                    "Ancot impact overrides were not installed: " + e.Message);
                return false;
            }
        }

        private static void CallOriginalBulletImpact(
            RimWorld.Bullet instance,
            Thing hitThing,
            bool blockedByShield)
        {
            throw new NotSupportedException("Harmony reverse patch stub");
        }

        private static void CallOriginalExplosiveImpact(
            Projectile_Explosive instance,
            Thing hitThing,
            bool blockedByShield)
        {
            throw new NotSupportedException("Harmony reverse patch stub");
        }

        private static void ExposeDataPrefix(Projectile __instance)
        {
            if (!MP.IsInMultiplayer ||
                __instance == null ||
                Scribe.mode != LoadSaveMode.Saving)
            {
                return;
            }

            ClearDestroyedReference(_launcherField, __instance);
            ClearDestroyedReference(_equipmentField, __instance);
        }

        private static void ClearDestroyedReference(
            FieldInfo field,
            object instance)
        {
            if (field == null || instance == null)
                return;

            try
            {
                if (field.GetValue(instance) is Thing thing &&
                    thing != null &&
                    thing.Destroyed)
                {
                    field.SetValue(instance, null);
                }
            }
            catch
            {
                // Save must not fail because a mod's projectile type refused
                // to expose its base fields.
            }
        }

        private static bool CustomImpactPrefix(
            Projectile __instance,
            Thing hitThing,
            bool blockedByShield)
        {
            if (!MP.IsInMultiplayer ||
                __instance == null ||
                _projectileCustomType == null ||
                !_projectileCustomType.IsInstanceOfType(__instance) ||
                _bulletImpactMethod == null)
            {
                return true;
            }

            try
            {
                CallOriginalBulletImpact(
                    (RimWorld.Bullet)__instance,
                    hitThing,
                    blockedByShield);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Projectile_Custom impact guard " +
                    "failed: " + e.Message);
            }

            try
            {
                TriggerImpactEffecter(
                    __instance,
                    hitThing,
                    _customPropsProperty,
                    _customImpactEffecterField,
                    originFromPosition: true);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Projectile_Custom impact effecter " +
                    "failed: " + e.Message);
            }

            return false;
        }

        private static bool ExplosiveCustomImpactPrefix(
            Projectile __instance,
            Thing hitThing,
            bool blockedByShield)
        {
            if (!MP.IsInMultiplayer ||
                __instance == null ||
                _projectileExplosiveCustomType == null ||
                !_projectileExplosiveCustomType.IsInstanceOfType(__instance) ||
                _projectileExplosiveImpactMethod == null)
            {
                return true;
            }

            try
            {
                TriggerImpactEffecter(
                    __instance,
                    hitThing,
                    _explosivePropsProperty,
                    _explosiveImpactEffecterField,
                    originFromPosition: false);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Projectile_ExplosiveCustom impact " +
                    "effecter failed: " + e.Message);
            }

            try
            {
                CallOriginalExplosiveImpact(
                    (Projectile_Explosive)__instance,
                    hitThing,
                    blockedByShield);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Projectile_ExplosiveCustom impact " +
                    "guard failed: " + e.Message);
            }

            return false;
        }

        private static void TriggerImpactEffecter(
            Projectile projectile,
            Thing hitThing,
            PropertyInfo propsProperty,
            FieldInfo impactEffecterField,
            bool originFromPosition)
        {
            if (propsProperty == null ||
                impactEffecterField == null ||
                projectile == null)
            {
                return;
            }

            object props;
            try
            {
                props = propsProperty.GetValue(projectile);
            }
            catch
            {
                return;
            }

            if (!(impactEffecterField.GetValue(props) is EffecterDef effecterDef) ||
                effecterDef == null)
            {
                return;
            }

            Map map = projectile.Map ?? projectile.Launcher?.Map;
            TargetInfo origin;
            if (originFromPosition)
            {
                origin = new TargetInfo(
                    projectile.ExactPosition.ToIntVec3(),
                    map,
                    false);
            }
            else
            {
                origin = hitThing != null
                    ? new TargetInfo(hitThing)
                    : TargetInfo.Invalid;
            }

            Thing launcher = projectile.Launcher;
            TargetInfo launcherTarget =
                launcher != null && !launcher.Destroyed
                    ? new TargetInfo(launcher)
                    : TargetInfo.Invalid;

            effecterDef.Spawn().Trigger(origin, launcherTarget, -1);
        }
    }
}
