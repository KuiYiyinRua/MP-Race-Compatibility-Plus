using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-424: toggling an Ancot integration weapon system (Milira
    /// concept weaponry such as Cyclops, and the same family as Paris) flips
    /// CompIntegrationWeaponSystem.activate in a gizmo lambda on only the
    /// clicking peer. Once active on one side, CompTurretGun_Custom.CompTick
    /// consumes per-map Rand during target acquisition while the other peer
    /// does not, so the map stream drifts.
    ///
    /// The patch keeps the narrow simulation boundary:
    /// 1. The IWS activation gizmo is routed through a synchronized command
    ///    that writes activate on every peer and mirrors SwitchGizmo, which
    ///    also carries CompPointDefense.switchOn/showGizmo.
    /// 2. The point-defense toggle is routed through its own synchronized
    ///    command so its direct switchOn write cannot diverge either.
    /// 3. CompTurretGun_Custom.CompTick runs inside a per-turret deterministic
    ///    Rand scope, so target acquisition never consumes the shared map
    ///    stream even when a rejoin or tick-bucket mismatch changes which peer
    ///    ticks the weapon.
    /// 4. Gizmo_ApparelReloadable_Custom.GizmoOnGUI writes targetCharges
    ///    directly when the fuel-capacity slider moves. That field is watched
    ///    and buffered through Multiplayer's native SyncField boundary.
    /// </summary>
    internal static class Patch_AncotIntegrationWeaponMp
    {
        private const string IwsTypeName = "AncotLibrary.CompIntegrationWeaponSystem";
        private const string PointDefenseTypeName = "AncotLibrary.CompPointDefense";
        private const string TurretCompTypeName = "AncotLibrary.CompTurretGun_Custom";
        private const string ReloadableCompTypeName = "AncotLibrary.CompApparelReloadable_Custom";
        private const string ReloadableGizmoTypeName = "AncotLibrary.Gizmo_ApparelReloadable_Custom";
        private const string TargetChargesFieldName = "targetCharges";

        private const int TurretSeedSalt = 0x49575354; // "IWST"
        private const int TurretWorldSeedOffset = 0x49575332; // "IWS2"
        private const int TurretMethodSalt = 0x4354434B; // "CTCK"

        [ThreadStatic]
        private static Map _mapForRandPop;

        private static bool _applied;
        private static FieldInfo _activateField;
        private static MethodInfo _switchGizmoMethod;
        private static MethodInfo _iwsGizmoMethod;
        private static PropertyInfo _iwsPawnOwner;
        private static FieldInfo _pointDefenseSwitchOnField;
        private static MethodInfo _pointDefenseWornGizmoMethod;
        private static MethodInfo _pointDefenseGizmoMethod;
        private static MethodInfo _turretCompTickMethod;
        private static PropertyInfo _turretPawnOwner;
        private static Type _reloadableCompType;
        private static FieldInfo _targetChargesField;
        private static FieldInfo _reloadableGizmoCompField;
        private static MethodInfo _reloadableGizmoOnGuiMethod;
        private static ISyncMethod _syncIwsActivate;
        private static ISyncMethod _syncPointDefenseToggle;

        [ThreadStatic]
        private static bool _watchingTargetCharges;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type iwsType = AccessTools.TypeByName(IwsTypeName);
                Type pointDefenseType = AccessTools.TypeByName(PointDefenseTypeName);
                Type turretCompType = AccessTools.TypeByName(TurretCompTypeName);
                _reloadableCompType = AccessTools.TypeByName(ReloadableCompTypeName);
                Type reloadableGizmoType = AccessTools.TypeByName(ReloadableGizmoTypeName);

                int patched = 0;
                int syncMethods = 0;
                int fuelSliderSync = 0;

                if (_reloadableCompType != null && reloadableGizmoType != null)
                {
                    _targetChargesField = AccessTools.Field(
                        _reloadableCompType,
                        TargetChargesFieldName);
                    _reloadableGizmoCompField = AccessTools.Field(
                        reloadableGizmoType,
                        "compReloadable");
                    _reloadableGizmoOnGuiMethod = AccessTools.DeclaredMethod(
                        reloadableGizmoType,
                        nameof(Gizmo.GizmoOnGUI),
                        new[]
                        {
                            typeof(Vector2),
                            typeof(float),
                            typeof(GizmoRenderParms)
                        });

                    if (_targetChargesField != null &&
                        _reloadableGizmoCompField != null &&
                        _reloadableGizmoOnGuiMethod != null)
                    {
                        try
                        {
                            // targetCharges is changed every frame while the
                            // slider is dragged, so use MP's buffered field
                            // path instead of sending each intermediate value.
                            MP.RegisterSyncField(_targetChargesField)
                                .SetBufferChanges();
                            harmony.Patch(
                                _reloadableGizmoOnGuiMethod,
                                prefix: new HarmonyMethod(
                                    AccessTools.Method(
                                        typeof(Patch_AncotIntegrationWeaponMp),
                                        nameof(TargetChargesWatchPrefix)))
                                {
                                    priority = Priority.First
                                },
                                finalizer: new HarmonyMethod(
                                    AccessTools.Method(
                                        typeof(Patch_AncotIntegrationWeaponMp),
                                        nameof(TargetChargesWatchFinalizer)))
                                {
                                    priority = Priority.Last
                                });
                            patched++;
                            fuelSliderSync++;
                        }
                        catch (Exception e)
                        {
                            Log.Warning(
                                "[MP-MeowOnlineShop] Ancot fuel-capacity slider " +
                                "sync registration/patch skipped: " + e.Message);
                        }
                    }
                }

                if (iwsType != null)
                {
                    _activateField = AccessTools.Field(iwsType, "activate");
                    _switchGizmoMethod = AccessTools.Method(
                        iwsType,
                        "SwitchGizmo",
                        new[] { typeof(bool) });
                    _iwsGizmoMethod = AccessTools.Method(
                        iwsType,
                        "CompGetWornGizmosExtra",
                        Type.EmptyTypes);
                    _iwsPawnOwner = AccessTools.Property(iwsType, "PawnOwner");

                    if (_activateField != null && _switchGizmoMethod != null &&
                        _iwsGizmoMethod != null)
                    {
                        try
                        {
                            _syncIwsActivate = MP.RegisterSyncMethod(
                                typeof(Patch_AncotIntegrationWeaponMp),
                                nameof(SyncIwsActivate));
                            if (_syncIwsActivate != null)
                                syncMethods++;
                        }
                        catch (Exception e)
                        {
                            Log.Warning(
                                "[MP-MeowOnlineShop] Ancot IWS activate sync " +
                                "registration failed: " + e.Message);
                        }

                        harmony.Patch(
                            _iwsGizmoMethod,
                            postfix: new HarmonyMethod(
                                AccessTools.Method(
                                    typeof(Patch_AncotIntegrationWeaponMp),
                                    nameof(IwsGizmosPostfix)))
                            {
                                priority = Priority.Last
                            });
                        patched++;
                    }
                }

                if (pointDefenseType != null)
                {
                    _pointDefenseSwitchOnField = AccessTools.Field(
                        pointDefenseType,
                        "switchOn");
                    _pointDefenseWornGizmoMethod = AccessTools.Method(
                        pointDefenseType,
                        "CompGetWornGizmosExtra",
                        Type.EmptyTypes);
                    _pointDefenseGizmoMethod = AccessTools.Method(
                        pointDefenseType,
                        "CompGetGizmosExtra",
                        Type.EmptyTypes);

                    if (_pointDefenseSwitchOnField != null &&
                        _pointDefenseWornGizmoMethod != null)
                    {
                        try
                        {
                            _syncPointDefenseToggle = MP.RegisterSyncMethod(
                                typeof(Patch_AncotIntegrationWeaponMp),
                                nameof(SyncPointDefenseToggle));
                            if (_syncPointDefenseToggle != null)
                                syncMethods++;
                        }
                        catch (Exception e)
                        {
                            Log.Warning(
                                "[MP-MeowOnlineShop] Ancot point-defense sync " +
                                "registration failed: " + e.Message);
                        }

                        MethodInfo postfix = AccessTools.Method(
                            typeof(Patch_AncotIntegrationWeaponMp),
                            nameof(PointDefenseGizmosPostfix));
                        harmony.Patch(
                            _pointDefenseWornGizmoMethod,
                            postfix: new HarmonyMethod(postfix)
                            {
                                priority = Priority.Last
                            });
                        patched++;

                        if (_pointDefenseGizmoMethod != null)
                        {
                            harmony.Patch(
                                _pointDefenseGizmoMethod,
                                postfix: new HarmonyMethod(postfix)
                                {
                                    priority = Priority.Last
                                });
                            patched++;
                        }
                    }
                }

                if (turretCompType != null)
                {
                    _turretPawnOwner = AccessTools.Property(
                        turretCompType,
                        "PawnOwner");
                    _turretCompTickMethod = AccessTools.Method(
                        turretCompType,
                        "CompTick",
                        Type.EmptyTypes);
                    if (_turretCompTickMethod != null)
                    {
                        harmony.Patch(
                            _turretCompTickMethod,
                            prefix: new HarmonyMethod(
                                AccessTools.Method(
                                    typeof(Patch_AncotIntegrationWeaponMp),
                                    nameof(TurretCompTickPrefix)))
                            {
                                priority = Priority.First
                            },
                            finalizer: new HarmonyMethod(
                                AccessTools.Method(
                                    typeof(Patch_AncotIntegrationWeaponMp),
                                    nameof(TurretCompTickFinalizer)))
                            {
                                priority = Priority.Last
                            });
                        patched++;
                    }
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ancot integration-weapon patch " +
                        "skipped: no resolvable targets.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Ancot integration-weapon MP patch " +
                    "active: iwsToggleSync=" + syncMethods +
                    ", fuelSliderSync=" + fuelSliderSync +
                    ", patchedBoundaries=" + patched + ".");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ancot integration-weapon patch apply " +
                    "failed: " + e.Message);
            }
        }

        public static void SyncIwsActivate(ThingComp comp, bool activate)
        {
            if (comp == null || _activateField == null)
                return;

            bool current = (bool)_activateField.GetValue(comp);
            if (current == activate)
                return;

            _activateField.SetValue(comp, activate);
            TryInvokeSwitchGizmo(comp, activate);
            TrySetPawnRenderDirty(comp);
        }

        public static void SyncPointDefenseToggle(ThingComp comp, bool switchOn)
        {
            if (comp == null || _pointDefenseSwitchOnField == null)
                return;

            bool current = (bool)_pointDefenseSwitchOnField.GetValue(comp);
            if (current == switchOn)
                return;

            _pointDefenseSwitchOnField.SetValue(comp, switchOn);
        }

        private static IEnumerable<Gizmo> IwsGizmosPostfix(
            IEnumerable<Gizmo> __result,
            ThingComp __instance)
        {
            if (!MP.IsInMultiplayer || __result == null || __instance == null ||
                _activateField == null || _syncIwsActivate == null)
            {
                return __result;
            }

            List<Gizmo> gizmos = __result.ToList();
            ThingComp comp = __instance;
            for (int i = 0; i < gizmos.Count; i++)
            {
                if (!(gizmos[i] is Command_Toggle toggle) ||
                    toggle.toggleAction == null)
                {
                    continue;
                }

                toggle.toggleAction = () =>
                {
                    bool next = !(bool)_activateField.GetValue(comp);
                    if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand &&
                        _syncIwsActivate != null)
                    {
                        try
                        {
                            _syncIwsActivate.DoSync(null, comp, next);
                        }
                        catch (Exception e)
                        {
                            Log.Warning(
                                "[MP-MeowOnlineShop] Ancot IWS activate sync " +
                                "dispatch failed: " + e.Message);
                        }
                    }
                };
            }

            return gizmos;
        }

        private static IEnumerable<Gizmo> PointDefenseGizmosPostfix(
            IEnumerable<Gizmo> __result,
            ThingComp __instance)
        {
            if (!MP.IsInMultiplayer || __result == null || __instance == null ||
                _pointDefenseSwitchOnField == null || _syncPointDefenseToggle == null)
            {
                return __result;
            }

            List<Gizmo> gizmos = __result.ToList();
            ThingComp comp = __instance;
            for (int i = 0; i < gizmos.Count; i++)
            {
                if (!(gizmos[i] is Command_Toggle toggle) ||
                    toggle.toggleAction == null)
                {
                    continue;
                }

                toggle.toggleAction = () =>
                {
                    bool next = !(bool)_pointDefenseSwitchOnField.GetValue(comp);
                    if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand &&
                        _syncPointDefenseToggle != null)
                    {
                        try
                        {
                            _syncPointDefenseToggle.DoSync(null, comp, next);
                        }
                        catch (Exception e)
                        {
                            Log.Warning(
                                "[MP-MeowOnlineShop] Ancot point-defense sync " +
                                "dispatch failed: " + e.Message);
                        }
                    }
                };
            }

            return gizmos;
        }

        private static void TargetChargesWatchPrefix(Gizmo __instance)
        {
            _watchingTargetCharges = false;
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                __instance == null || _reloadableCompType == null ||
                _targetChargesField == null ||
                _reloadableGizmoCompField == null)
            {
                return;
            }

            object comp;
            try
            {
                comp = _reloadableGizmoCompField.GetValue(__instance);
                if (comp == null || !_reloadableCompType.IsInstanceOfType(comp))
                    return;

                MP.WatchBegin();
                bool watchStarted = true;
                try
                {
                    // Use the declaring type explicitly: the gizmo holds a
                    // CompIntegrationWeaponSystem subclass, while the field
                    // is registered on its CompApparelReloadable_Custom base.
                    MP.Watch(
                        _reloadableCompType,
                        TargetChargesFieldName,
                        comp);
                    _watchingTargetCharges = true;
                }
                catch
                {
                    if (watchStarted)
                        MP.WatchEnd();
                }
            }
            catch
            {
                // A presentation-only failure must not break the fuel gizmo.
            }
        }

        private static Exception TargetChargesWatchFinalizer(Exception __exception)
        {
            if (_watchingTargetCharges)
            {
                _watchingTargetCharges = false;
                try
                {
                    MP.WatchEnd();
                }
                catch
                {
                    // Keep the original GizmoOnGUI exception, if any.
                }
            }

            return __exception;
        }

        private static void TurretCompTickPrefix(
            ThingComp __instance,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Thing anchor = GetTurretAnchor(__instance);
            Map map = anchor?.Map;
            if (anchor == null || map == null)
                return;

            int seed = Gen.HashCombineInt(TurretSeedSalt, map.uniqueID);
            seed = Gen.HashCombineInt(seed, anchor.thingIDNumber);
            seed = Gen.HashCombineInt(seed, TurretMethodSalt);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    TurretWorldSeedOffset,
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

        private static Exception TurretCompTickFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }

        private static Thing GetTurretAnchor(ThingComp comp)
        {
            try
            {
                if (_turretPawnOwner?.GetValue(comp, null) is Pawn pawn &&
                    pawn != null && pawn.Map != null)
                {
                    return pawn;
                }
            }
            catch
            {
                // Reflect-only fallback below is the safe path.
            }

            return comp.parent;
        }

        private static void TryInvokeSwitchGizmo(ThingComp comp, bool activate)
        {
            try
            {
                _switchGizmoMethod?.Invoke(comp, new object[] { activate });
            }
            catch
            {
                // Visual/gizmo state must never break the synchronized toggle.
            }
        }

        private static void TrySetPawnRenderDirty(ThingComp comp)
        {
            try
            {
                if (_iwsPawnOwner?.GetValue(comp, null) is Pawn pawn &&
                    pawn.Drawer != null)
                {
                    pawn.Drawer.renderer?.renderTree?.SetDirty();
                }
            }
            catch
            {
                // Presentation-only update; the field itself is already synced.
            }
        }
    }
}
