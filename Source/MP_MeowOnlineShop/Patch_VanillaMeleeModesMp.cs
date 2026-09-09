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
    /// Vanilla Melee Modes (`aliza.vanillameleemodes`) exposes an auto-mode
    /// toggle and a mode-cycle Command_Action from VMM_PawnCompMeleeMode. Both
    /// callbacks write saved comp fields (isAutoMode / mode) on the clicking
    /// peer only. CompTick and OnPlayerStartedMeleeJob update the same fields
    /// deterministically on every peer and are left untouched.
    ///
    /// Only the two gizmo callbacks are replaced with replay commands, which
    /// resolve the parent Thing and apply the same field values everywhere.
    /// </summary>
    internal static class Patch_VanillaMeleeModesMp
    {
        private const string PackageId = "aliza.vanillameleemodes";
        private const string CompTypeName = "VMM_VanillaMeleeModes.Comps.VMM_PawnCompMeleeMode";

        private static Type _compType;
        private static FieldInfo _modeField;
        private static FieldInfo _isAutoModeField;
        private static ISyncMethod _syncToggleAuto;
        private static ISyncMethod _syncCycleMode;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            _modeField = _compType == null ? null : AccessTools.Field(_compType, "mode");
            _isAutoModeField = _compType == null ? null : AccessTools.Field(_compType, "isAutoMode");
            if (_compType == null || _modeField == null || _isAutoModeField == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Vanilla Melee Modes target resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncToggleAuto = MP.RegisterSyncMethod(
                        typeof(Patch_VanillaMeleeModesMp),
                        nameof(SyncToggleAutoMode))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _syncCycleMode = MP.RegisterSyncMethod(
                        typeof(Patch_VanillaMeleeModesMp),
                        nameof(SyncCycleMode))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Vanilla Melee Modes sync registration failed: " + e.Message);
                return;
            }

            var gizmoMethod = AccessTools.Method(_compType, "CompGetGizmosExtra", Type.EmptyTypes);
            var postfix = AccessTools.Method(typeof(Patch_VanillaMeleeModesMp), nameof(GizmoPostfix));
            if (gizmoMethod == null || postfix == null)
                return;

            try
            {
                harmony.Patch(gizmoMethod, postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] Vanilla Melee Modes MP patch active: auto toggle and mode cycle sync.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Vanilla Melee Modes gizmo patch failed: " + e.Message);
            }
        }

        private static void GizmoPostfix(object __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || _syncToggleAuto == null || _syncCycleMode == null)
                return;

            var comp = __instance as ThingComp;
            var parent = comp?.parent;
            if (parent == null)
                return;

            var list = __result.ToList();
            foreach (var gizmo in list)
            {
                if (gizmo is Command_Toggle toggle)
                {
                    toggle.toggleAction = () =>
                    {
                        if (!MP.IsInMultiplayer)
                        {
                            SetAutoMode(comp, !GetAutoMode(comp));
                            SetMode(comp, 0);
                            return;
                        }

                        _syncToggleAuto.DoSync(null, parent, !GetAutoMode(comp));
                    };
                }
                else if (gizmo is Command_Action action)
                {
                    action.action = () =>
                    {
                        int next = (GetMode(comp) + 1) % 4;
                        if (!MP.IsInMultiplayer)
                        {
                            SetMode(comp, next);
                            return;
                        }

                        _syncCycleMode.DoSync(null, parent, next);
                    };
                }
            }

            __result = list;
        }

        public static void SyncToggleAutoMode(Thing parent, bool autoMode)
        {
            var comp = ResolveComp(parent);
            if (comp == null)
                return;

            SetAutoMode(comp, autoMode);
            SetMode(comp, 0);
        }

        public static void SyncCycleMode(Thing parent, int nextMode)
        {
            var comp = ResolveComp(parent);
            if (comp == null)
                return;

            SetMode(comp, nextMode);
        }

        private static ThingComp ResolveComp(Thing parent)
        {
            return (parent as ThingWithComps)?.AllComps?.FirstOrDefault(c => _compType.IsInstanceOfType(c));
        }

        private static bool GetAutoMode(ThingComp comp)
        {
            return comp != null && _isAutoModeField != null &&
                   _isAutoModeField.GetValue(comp) is bool value && value;
        }

        private static void SetAutoMode(ThingComp comp, bool value)
        {
            _isAutoModeField?.SetValue(comp, value);
        }

        private static int GetMode(ThingComp comp)
        {
            if (comp == null || _modeField == null)
                return 0;
            var raw = _modeField.GetValue(comp);
            return raw == null ? 0 : Convert.ToInt32(raw);
        }

        private static void SetMode(ThingComp comp, int value)
        {
            if (comp == null || _modeField == null)
                return;
            _modeField.SetValue(comp, Enum.ToObject(_modeField.FieldType, value));
        }
    }
}
