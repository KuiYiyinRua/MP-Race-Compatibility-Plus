using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-415: AncotLibrary.Building_SpinTurretGun overrides both
    /// OrderAttack and GetGizmos, so Multiplayer's vanilla Building_TurretGun
    /// registrations do not cover its force-target or hold-fire commands. The
    /// first divergent trace in 415 is the turret's TryStartShootSomething
    /// FloatRange.RandomInRange draw, which happens after the hold-fire state
    /// or target selection has already diverged on one peer.
    ///
    /// The patch keeps the narrow simulation boundary:
    /// 1. the hold-fire Command_Toggle action is wrapped and its direct
    ///    holdFire write is routed through a synchronized method. The field is
    ///    also registered for compatibility with any MP watch surface.
    /// 2. ResetForcedTarget and the OrderAttack override are registered as
    ///    sync methods so stop/force-target commands replay on every peer.
    /// 3. TryStartShootSomething draws its warmup delay inside a per-turret
    ///    deterministic Rand scope, so the tick stream is not consumed
    ///    differently on one side.
    /// </summary>
    internal static class Patch_AncotSpinTurretMp
    {
        private const string TurretTypeName =
            "AncotLibrary.Building_SpinTurretGun";
        private const int TurretSeedSalt = 0x54555254; // "TURT"
        private const int WorldSeedOffset = 0x544F5032; // "TOP2"
        private const int ShootMethodSalt = 0x53484F54; // "SHOT"

        [ThreadStatic]
        private static Map _mapForRandPop;

        private static bool _applied;
        private static FieldInfo _holdFireField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type turretType = AccessTools.TypeByName(TurretTypeName);
                if (turretType == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ancot spin turret patch skipped: " +
                        TurretTypeName + " not resolved.");
                    return;
                }

                MethodInfo tryStart = AccessTools.Method(
                    turretType,
                    "TryStartShootSomething",
                    new[] { typeof(bool) });
                MethodInfo resetForced = AccessTools.Method(
                    turretType,
                    "ResetForcedTarget",
                    Type.EmptyTypes);
                MethodInfo orderAttack = AccessTools.Method(
                    turretType,
                    "OrderAttack",
                    new[] { typeof(LocalTargetInfo) });
                FieldInfo holdFire = AccessTools.Field(turretType, "holdFire");
                _holdFireField = holdFire;
                MethodInfo getGizmos = AccessTools.Method(
                    turretType, "GetGizmos", Type.EmptyTypes);

                int patched = 0;
                int syncMethods = 0;
                if (tryStart != null)
                {
                    harmony.Patch(
                        tryStart,
                        prefix: new HarmonyMethod(
                            typeof(Patch_AncotSpinTurretMp),
                            nameof(TryStartPrefix))
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(
                            typeof(Patch_AncotSpinTurretMp),
                            nameof(TryStartFinalizer))
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (holdFire != null)
                {
                    try
                    {
                        MP.RegisterSyncField(holdFire);
                        patched++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Ancot spin turret holdFire " +
                            "SyncField registration failed: " + e.Message);
                    }

                    try
                    {
                        MP.RegisterSyncMethod(
                            typeof(Patch_AncotSpinTurretMp),
                            nameof(SyncHoldFire));
                        syncMethods++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Ancot spin turret holdFire " +
                            "sync method registration failed: " + e.Message);
                    }
                }

                if (getGizmos != null && holdFire != null)
                {
                    try
                    {
                        // The lambda also resets forcedTarget when hold-fire
                        // is enabled; syncing only the field misses that
                        // gameplay side effect.
                        MP.RegisterSyncMethodLambda(
                            turretType, "GetGizmos", 2);
                        syncMethods++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Ancot spin turret hold-fire " +
                            "Toggle SyncMethod registration failed: " + e.Message);
                    }

                    harmony.Patch(
                        getGizmos,
                        postfix: new HarmonyMethod(
                            typeof(Patch_AncotSpinTurretMp),
                            nameof(GetGizmosPostfix))
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (resetForced != null)
                {
                    try
                    {
                        MP.RegisterSyncMethod(resetForced, null);
                        patched++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Ancot spin turret " +
                            "ResetForcedTarget sync registration failed: " +
                            e.Message);
                    }
                }

                if (orderAttack != null)
                {
                    try
                    {
                        MP.RegisterSyncMethod(orderAttack, null);
                        patched++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] Ancot spin turret " +
                            "OrderAttack sync registration failed: " +
                            e.Message);
                    }
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ancot spin turret patch had no " +
                        "resolvable targets.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Ancot spin turret MP patch active: " +
                    "holdFire action/SyncField sync (methods=" + syncMethods + "), " +
                    "force-target sync, and deterministic TryStartShootSomething " +
                    "Rand (targets=" + patched + ").");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ancot spin turret patch apply " +
                    "failed: " + e.Message);
            }
        }

        public static void SyncHoldFire(Thing turret, bool value)
        {
            if (turret == null || _holdFireField == null ||
                !_holdFireField.DeclaringType.IsInstanceOfType(turret))
                return;

            bool current = (bool)_holdFireField.GetValue(turret);
            if (current != value)
                _holdFireField.SetValue(turret, value);
        }

        private static IEnumerable<Gizmo> GetGizmosPostfix(
            IEnumerable<Gizmo> __result,
            object __instance)
        {
            if (!MP.IsInMultiplayer || __result == null ||
                !(__instance is Thing turret) || _holdFireField == null)
                return __result;

            List<Gizmo> gizmos = __result.ToList();
            for (int i = 0; i < gizmos.Count; i++)
            {
                if (!(gizmos[i] is Command_Toggle toggle) ||
                    toggle.toggleAction == null)
                    continue;

                Action original = toggle.toggleAction;
                toggle.toggleAction = () =>
                {
                    bool before = (bool)_holdFireField.GetValue(turret);
                    original();

                    if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand)
                    {
                        bool after = (bool)_holdFireField.GetValue(turret);
                        if (before != after)
                            SyncHoldFire(turret, after);
                    }
                };
            }

            return gizmos;
        }

        private static void TryStartPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || !(__instance is Thing turret) ||
                turret.Map == null)
            {
                return;
            }

            int seed = Gen.HashCombineInt(
                TurretSeedSalt,
                turret.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, turret.thingIDNumber);
            seed = Gen.HashCombineInt(seed, ShootMethodSalt);

            if (DeterministicRandScope.Begin(
                    turret.Map,
                    seed,
                    WorldSeedOffset,
                    ref __state,
                    out var mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception TryStartFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }
    }
}
