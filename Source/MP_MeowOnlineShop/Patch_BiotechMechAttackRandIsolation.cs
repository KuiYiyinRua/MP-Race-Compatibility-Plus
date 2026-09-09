using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-640: the first host-side divergent random call is
    /// Verb_ShootBeam.BurstingTick -> Rand.Chance while a Mech_Tesseron is
    /// ticking. Gun_BeamGraser uses that verb and its beam fleck checks are
    /// visual-only, but they still read the async map Rand before
    /// Multiplayer's FleckMaker guard can save/restore it.
    ///
    /// The Scorcher is the other suspected Biotech mech. Its official attack
    /// is Verb_SpewFire, not Verb_LaunchProjectile; the fire cone enters
    /// GenExplosion with visual effects disabled. Keep that complete attack
    /// boundary deterministic for Mech_Scorcher without touching ordinary
    /// projectile or explosion calls.
    /// </summary>
    internal static class Patch_BiotechMechAttackRandIsolation
    {
        private const string BeamVerbTypeName = "Verse.Verb_ShootBeam";
        private const string SpewFireVerbTypeName = "Verse.Verb_SpewFire";

        private const string TesseronDefName = "Mech_Tesseron";
        private const string ScorcherDefName = "Mech_Scorcher";

        private const int BeamBurstingTickSalt = 0x42544254; // "BTBT"
        private const int ScorcherFireSalt = 0x42545346; // "BTSF"
        private const int WorldSeedOffset = 0x42545753; // "BTWS"

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
                Type beamVerbType = AccessTools.TypeByName(BeamVerbTypeName) ??
                    AccessTools.TypeByName("Verb_ShootBeam");
                Type spewFireVerbType = AccessTools.TypeByName(SpewFireVerbTypeName) ??
                    AccessTools.TypeByName("Verb_SpewFire");

                MethodInfo beamBurstingTick = beamVerbType == null
                    ? null
                    : AccessTools.Method(
                        beamVerbType, "BurstingTick", Type.EmptyTypes);
                MethodInfo spewFireTryCastShot = spewFireVerbType == null
                    ? null
                    : AccessTools.Method(
                        spewFireVerbType, "TryCastShot", Type.EmptyTypes);

                int patched = 0;
                if (beamBurstingTick != null)
                {
                    PatchScopeTarget(
                        harmony,
                        beamBurstingTick,
                        nameof(BeamBurstingTickPrefix));
                    patched++;
                }

                if (spewFireTryCastShot != null)
                {
                    PatchScopeTarget(
                        harmony,
                        spewFireTryCastShot,
                        nameof(ScorcherFirePrefix));
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Biotech mech attack Rand " +
                        "targets were not resolved; skipped.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Biotech mech attack Rand " +
                    "isolation active: tesseronBeamVfx=" +
                    (beamBurstingTick != null) +
                    ", scorcherFire=" +
                    (spewFireTryCastShot != null) + ".");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Biotech mech attack Rand " +
                    "isolation apply failed: " + e.Message);
            }
        }

        private static void PatchScopeTarget(
            Harmony harmony,
            MethodInfo target,
            string prefixName)
        {
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_BiotechMechAttackRandIsolation),
                prefixName);
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_BiotechMechAttackRandIsolation),
                nameof(ScopeFinalizer));
            if (prefix == null || finalizer == null)
                throw new MissingMethodException(
                    "Biotech mech attack Rand scope patch method resolution failed.");

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

        private static void BeamBurstingTickPrefix(
            object __instance,
            ref ScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !(__instance is Verb verb))
                return;

            Thing caster = GetCaster(verb);
            if (caster?.def?.defName != TesseronDefName)
                return;

            BeginVerbScope(
                verb,
                caster,
                BeamBurstingTickSalt,
                ref __state);
        }

        private static void ScorcherFirePrefix(
            object __instance,
            ref ScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !(__instance is Verb verb))
                return;

            Thing caster = GetCaster(verb);
            if (caster?.def?.defName != ScorcherDefName)
                return;

            BeginVerbScope(
                verb,
                caster,
                ScorcherFireSalt,
                ref __state);
        }

        private static Thing GetCaster(Verb verb)
        {
            try
            {
                return verb.Caster;
            }
            catch
            {
                return null;
            }
        }

        private static void BeginVerbScope(
            Verb verb,
            Thing caster,
            int salt,
            ref ScopeState state)
        {
            if (caster?.Map == null)
                return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            int seed = Gen.HashCombineInt(salt, caster.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, caster.thingIDNumber);
            seed = Gen.HashCombineInt(seed, caster.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(seed, tick);
            seed = Gen.HashCombineInt(
                seed,
                verb.CurrentTarget.Cell.x);
            seed = Gen.HashCombineInt(
                seed,
                verb.CurrentTarget.Cell.z);
            seed = Gen.HashCombineInt(
                seed,
                verb.EquipmentSource?.def?.shortHash ?? 0);

            int randState = 0;
            if (!DeterministicRandScope.Begin(
                    caster.Map,
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
                    "[MP-MeowOnlineShop] Biotech mech attack Rand scope " +
                    "restore failed: " + e.Message);
            }

            return __exception;
        }
    }
}
