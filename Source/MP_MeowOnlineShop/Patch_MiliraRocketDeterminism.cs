using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-552/553 and Desync-590/591: the first divergent stack is in a
    /// Milira heavy turret's actual VerbTracker firing path. The existing
    /// Ancot patch only scopes target acquisition and warmup selection, while
    /// the heavy particle turret's Verb_LaunchProjectile.TryCastShot can
    /// still consume the shared map Rand while creating a projectile. In
    /// 590/591 the host has an extra MiliraBullet_HeavyParticle immediately
    /// before the first wrong-random trace.
    ///
    /// Keep the boundary narrow: isolate only the two observed Milira heavy
    /// turret/projectile defs and their visual helper methods. The simulation
    /// result, projectile IDs, targets, and damage remain vanilla; only random
    /// draws made by this feature are prevented from perturbing the async map
    /// stream differently on the two peers.
    /// </summary>
    internal static class Patch_MiliraRocketDeterminism
    {
        private const string TurretTypeName =
            "AncotLibrary.Building_SpinTurretGun";
        private const string ProjectileTypeName = "Verse.Projectile";
        private const string LaunchProjectileVerbTypeName =
            "Verse.Verb_LaunchProjectile";
        private const string MiliraFleckMakerTypeName =
            "Milira.MiliraFleckMaker";

        private const string HeavyRocketTurretDef =
            "MiliraTurret_HeavyRocketLauncher";
        private const string HeavyParticleTurretDef =
            "MiliraTurret_HeavyParticle";
        private const string HeavyRocketProjectileDef =
            "MiliraProjectile_HeavyRocket";
        private const string HeavyParticleProjectileDef =
            "MiliraBullet_HeavyParticle";

        private const int TurretTickSalt = 0x4D525454; // "MRTT"
        private const int ProjectileTickSalt = 0x4D525450; // "MRTP"
        private const int HeavyParticleFireSalt = 0x4D524250; // "MRBP"
        private const int PlasmaFleckSalt = 0x4D525046; // "MRPF"
        private const int LineFleckSalt = 0x4D524C46; // "MRLF"
        private const int WorldSeedOffset = 0x4D525457; // "MRTW"

        private static bool _applied;

        private sealed class ScopeState
        {
            internal int RandState;
            internal Map MapForPop;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type turretType = AccessTools.TypeByName(TurretTypeName);
                Type projectileType = AccessTools.TypeByName(ProjectileTypeName);
                Type launchProjectileVerbType = AccessTools.TypeByName(
                    LaunchProjectileVerbTypeName) ??
                    AccessTools.TypeByName("Verb_LaunchProjectile");
                Type fleckMakerType = AccessTools.TypeByName(
                    MiliraFleckMakerTypeName);

                MethodInfo turretTick = turretType == null
                    ? null
                    : AccessTools.Method(
                        turretType, "Tick", Type.EmptyTypes);
                MethodInfo projectileTick = projectileType == null
                    ? null
                    : AccessTools.Method(
                        projectileType, "Tick", Type.EmptyTypes);
                MethodInfo launchProjectileTryCastShot =
                    launchProjectileVerbType == null
                        ? null
                        : AccessTools.Method(
                            launchProjectileVerbType,
                            "TryCastShot",
                            Type.EmptyTypes);
                MethodInfo plasmaFleck = fleckMakerType == null
                    ? null
                    : AccessTools.Method(
                        fleckMakerType,
                        "ThrowPlasmaAirPuffUp",
                        new[] { typeof(Vector3), typeof(Map), typeof(Color) });
                MethodInfo lineFleck = fleckMakerType == null
                    ? null
                    : AccessTools.Method(
                        fleckMakerType,
                        "ThrowLineEMP",
                        new[] { typeof(Vector3), typeof(Map) });

                int patched = 0;
                if (turretTick != null)
                {
                    PatchScopeTarget(
                        harmony,
                        turretTick,
                        nameof(TurretTickPrefix));
                    patched++;
                }

                if (projectileTick != null)
                {
                    PatchScopeTarget(
                        harmony,
                        projectileTick,
                        nameof(ProjectileTickPrefix));
                    patched++;
                }

                if (launchProjectileTryCastShot != null)
                {
                    PatchScopeTarget(
                        harmony,
                        launchProjectileTryCastShot,
                        nameof(HeavyParticleLaunchPrefix));
                    patched++;
                }

                if (plasmaFleck != null)
                {
                    PatchScopeTarget(
                        harmony,
                        plasmaFleck,
                        nameof(PlasmaFleckPrefix));
                    patched++;
                }

                if (lineFleck != null)
                {
                    PatchScopeTarget(
                        harmony,
                        lineFleck,
                        nameof(LineFleckPrefix));
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Milira rocket determinism " +
                        "targets were not resolved; skipped.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Milira rocket determinism active: " +
                    "turretTick=" + (turretTick != null) +
                    ", projectileTick=" + (projectileTick != null) +
                    ", heavyParticleFireBoundary=" +
                    (launchProjectileTryCastShot != null) +
                    ", plasmaFleck=" + (plasmaFleck != null) +
                    ", lineFleck=" + (lineFleck != null) + ".");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira rocket determinism apply " +
                    "failed: " + e.Message);
            }
        }

        private static void PatchScopeTarget(
            Harmony harmony,
            MethodInfo target,
            string prefixName)
        {
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MiliraRocketDeterminism),
                prefixName);
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_MiliraRocketDeterminism),
                nameof(ScopeFinalizer));
            if (prefix == null || finalizer == null)
                throw new MissingMethodException(
                    "Milira rocket scope patch method resolution failed.");

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
        }

        private static void TurretTickPrefix(
            object __instance,
            ref ScopeState __state)
        {
            __state = null;
            Thing turret = __instance as Thing;
            if (!MP.IsInMultiplayer || !IsHeavyTurret(turret))
                return;

            BeginThingScope(
                turret,
                TurretTickSalt,
                ref __state);
        }

        private static void ProjectileTickPrefix(
            object __instance,
            ref ScopeState __state)
        {
            __state = null;
            Thing projectile = __instance as Thing;
            if (!MP.IsInMultiplayer || !IsHeavyProjectile(projectile))
                return;

            BeginThingScope(
                projectile,
                ProjectileTickSalt,
                ref __state);
        }

        private static void HeavyParticleLaunchPrefix(
            object __instance,
            ref ScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !(__instance is Verb verb))
                return;

            Thing caster;
            try
            {
                caster = verb.Caster;
            }
            catch
            {
                return;
            }

            // The desync is tied to the Heavy Particle turret's projectile
            // creation boundary. Do not alter ordinary Milira weapons or the
            // heavy rocket launcher, which already has its own tick scope.
            if (!IsHeavyParticleTurret(caster))
                return;

            BeginThingScope(
                caster,
                HeavyParticleFireSalt,
                ref __state);
        }

        private static void PlasmaFleckPrefix(
            Vector3 loc,
            Map map,
            Color color,
            ref ScopeState __state)
        {
            BeginVisualScope(loc, map, PlasmaFleckSalt, ref __state);
        }

        private static void LineFleckPrefix(
            Vector3 loc,
            Map map,
            ref ScopeState __state)
        {
            BeginVisualScope(loc, map, LineFleckSalt, ref __state);
        }

        private static bool IsHeavyTurret(Thing thing)
        {
            string defName = thing?.def?.defName;
            return defName == HeavyRocketTurretDef ||
                defName == HeavyParticleTurretDef;
        }

        private static bool IsHeavyParticleTurret(Thing thing)
        {
            return thing?.def?.defName == HeavyParticleTurretDef;
        }

        private static bool IsHeavyProjectile(Thing thing)
        {
            string defName = thing?.def?.defName;
            return defName == HeavyRocketProjectileDef ||
                defName == HeavyParticleProjectileDef;
        }

        private static void BeginThingScope(
            Thing thing,
            int salt,
            ref ScopeState state)
        {
            if (thing?.Map == null)
                return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            int seed = Gen.HashCombineInt(salt, thing.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, thing.thingIDNumber);
            seed = Gen.HashCombineInt(seed, thing.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(seed, tick);

            int randState = 0;
            if (!DeterministicRandScope.Begin(
                    thing.Map,
                    seed,
                    WorldSeedOffset,
                    ref randState,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                return;
            }

            state = new ScopeState
            {
                RandState = randState,
                MapForPop = mapForPop
            };
        }

        private static void BeginVisualScope(
            Vector3 loc,
            Map map,
            int salt,
            ref ScopeState state)
        {
            state = null;
            if (!MP.IsInMultiplayer || map == null)
                return;

            IntVec3 cell = loc.ToIntVec3();
            int tick = Find.TickManager?.TicksGame ?? 0;
            int seed = Gen.HashCombineInt(salt, map.uniqueID);
            seed = Gen.HashCombineInt(seed, cell.x);
            seed = Gen.HashCombineInt(seed, cell.z);
            seed = Gen.HashCombineInt(seed, tick);

            int randState = 0;
            if (!DeterministicRandScope.Begin(
                    map,
                    seed,
                    WorldSeedOffset,
                    ref randState,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                return;
            }

            state = new ScopeState
            {
                RandState = randState,
                MapForPop = mapForPop
            };
        }

        private static Exception ScopeFinalizer(
            Exception __exception,
            ScopeState __state)
        {
            if (__state == null || __state.RandState == 0)
                return __exception;

            try
            {
                DeterministicRandScope.End(
                    __state.RandState,
                    __state.MapForPop);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira rocket Rand scope restore " +
                    "failed: " + e.Message);
            }

            return __exception;
        }
    }
}
