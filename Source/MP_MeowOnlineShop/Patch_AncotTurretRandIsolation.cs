using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Ancot/Milira spin turrets and vanilla RimWorld turrets draw Verse.Rand
    /// while acquiring targets or playing their idle rotation animation
    /// (TryFindNewTarget) and while playing their idle rotation animation
    /// (SpinTurretTop.TurretTopTick). Both run inside the per-map async tick.
    /// When a gravship landing or rejoin leaves one peer with a different
    /// normal-tick bucket, those draws are consumed on only one side and the
    /// synchronized map Rand stream drifts (Desync-249/251-256 first divergent
    /// traces land in these two methods).
    ///
    /// Turret rotation is visual-only. Target choice is deterministic for a
    /// given turret and map, so both can use a per-turret deterministic scope
    /// that is restored before the map stream is sampled.
    /// </summary>
    internal static class Patch_AncotTurretRandIsolation
    {
        private const int TurretSeedSalt = 0x54555254; // "TURT"
        private const int WorldSeedOffset = 0x544F5031; // "TOP1"
        private const int VanillaTurretSeedSalt = 0x56545552; // "VTUR"
        private const int VanillaTurretWorldSeedOffset = 0x56544F50; // "VTOP"

        private static bool _applied;
        private static FieldInfo _parentTurretField;
        private static AccessTools.FieldRef<object, Thing> _parentTurretRef;
        private static FieldInfo _vanillaParentTurretField;
        private static AccessTools.FieldRef<object, Thing> _vanillaParentTurretRef;

        [ThreadStatic]
        private static Map _mapForRandPop;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                Type turretType = AccessTools.TypeByName(
                    "AncotLibrary.Building_SpinTurretGun");
                Type topType = AccessTools.TypeByName(
                    "AncotLibrary.SpinTurretTop");

                MethodInfo tryFindNewTarget = turretType == null
                    ? null
                    : AccessTools.Method(turretType, "TryFindNewTarget", Type.EmptyTypes);
                MethodInfo turretTopTick = topType == null
                    ? null
                    : AccessTools.Method(topType, "TurretTopTick", Type.EmptyTypes);
                _parentTurretField = topType == null
                    ? null
                    : AccessTools.Field(topType, "parentTurret");
                _parentTurretRef = TryGetInstanceFieldRef<Thing>(topType, "parentTurret");

                Type vanillaTopType = AccessTools.TypeByName("RimWorld.TurretTop");
                MethodInfo vanillaTurretTopTick = vanillaTopType == null
                    ? null
                    : AccessTools.Method(
                        vanillaTopType,
                        "TurretTopTick",
                        Type.EmptyTypes);
                _vanillaParentTurretField = vanillaTopType == null
                    ? null
                    : AccessTools.Field(vanillaTopType, "parentTurret");
                _vanillaParentTurretRef = TryGetInstanceFieldRef<Thing>(
                    vanillaTopType,
                    "parentTurret");

                int patched = 0;
                if (tryFindNewTarget != null)
                {
                    harmony.Patch(
                        tryFindNewTarget,
                        prefix: new HarmonyMethod(AccessTools.Method(
                            typeof(Patch_AncotTurretRandIsolation),
                            nameof(TurretPrefix)))
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(AccessTools.Method(
                            typeof(Patch_AncotTurretRandIsolation),
                            nameof(TurretFinalizer)))
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (turretTopTick != null && _parentTurretField != null)
                {
                    harmony.Patch(
                        turretTopTick,
                        prefix: new HarmonyMethod(AccessTools.Method(
                            typeof(Patch_AncotTurretRandIsolation),
                            nameof(TurretTopPrefix)))
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(AccessTools.Method(
                            typeof(Patch_AncotTurretRandIsolation),
                            nameof(TurretFinalizer)))
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (vanillaTurretTopTick != null &&
                    _vanillaParentTurretField != null)
                {
                    harmony.Patch(
                        vanillaTurretTopTick,
                        prefix: new HarmonyMethod(AccessTools.Method(
                            typeof(Patch_AncotTurretRandIsolation),
                            nameof(VanillaTurretTopPrefix)))
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(AccessTools.Method(
                            typeof(Patch_AncotTurretRandIsolation),
                            nameof(TurretFinalizer)))
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ancot/Milira turret Rand isolation " +
                        "targets were not resolved; skipped.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Ancot/Milira/vanilla turret Rand isolation " +
                    "active: boundaries=" + patched +
                    "; vanilla TurretTopTick is included when resolved.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ancot/Milira turret Rand isolation " +
                    "install failed: " + e.Message);
            }
        }

        private static void TurretPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || !(__instance is Thing turret))
                return;

            BeginScope(turret, "TryFindNewTarget", ref __state);
        }

        private static void TurretTopPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Thing turret = null;
            try
            {
                turret = _parentTurretRef != null
                    ? _parentTurretRef(__instance)
                    : _parentTurretField?.GetValue(__instance) as Thing;
            }
            catch
            {
                // Reflect-only read must never break the turret tick.
            }

            if (turret == null)
                return;

            BeginScope(turret, "TurretTopTick", ref __state);
        }

        private static void VanillaTurretTopPrefix(
            object __instance,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Thing turret = null;
            try
            {
                turret = _vanillaParentTurretRef != null
                    ? _vanillaParentTurretRef(__instance)
                    : _vanillaParentTurretField?.GetValue(__instance) as Thing;
            }
            catch
            {
                // Reflect-only read must never break vanilla turret ticking.
            }

            if (turret == null)
                return;

            BeginScope(
                turret,
                "VanillaTurretTopTick",
                VanillaTurretSeedSalt,
                VanillaTurretWorldSeedOffset,
                ref __state);
        }

        private static AccessTools.FieldRef<object, T> TryGetInstanceFieldRef<T>(
            Type type,
            string name) where T : class
        {
            if (type == null)
                return null;
            try
            {
                return AccessTools.FieldRefAccess<T>(type, name);
            }
            catch
            {
                return null;
            }
        }

        private static void BeginScope(
            Thing turret,
            string methodName,
            ref int state)
        {
            BeginScope(
                turret,
                methodName,
                TurretSeedSalt,
                WorldSeedOffset,
                ref state);
        }

        private static void BeginScope(
            Thing turret,
            string methodName,
            int seedSalt,
            int worldSeedOffset,
            ref int state)
        {
            if (turret == null || turret.Map == null)
                return;

            int methodHash = DeterministicStringHash(methodName);
            int seed = Gen.HashCombineInt(seedSalt, turret.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, turret.thingIDNumber);
            seed = Gen.HashCombineInt(seed, methodHash);

            if (DeterministicRandScope.Begin(
                    turret.Map,
                    seed,
                    worldSeedOffset,
                    ref state,
                    out var mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
            }
            else
            {
                state = 0;
            }
        }

        private static Exception TurretFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            int hash = 0;
            for (int i = 0; i < value.Length; i++)
                hash = Gen.HashCombineInt(hash, value[i]);
            return hash;
        }
    }
}
