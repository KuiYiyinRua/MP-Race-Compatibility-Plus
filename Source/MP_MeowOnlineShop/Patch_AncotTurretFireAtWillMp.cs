using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// AncotLibrary.Gizmo_TurretGun writes CompTurretGun_Custom.fireAtWill
    /// directly from GizmoOnGUI. That field is consumed by CompTick/CanShoot,
    /// so a local-only click can make one peer create a different projectile
    /// stream. The stock Command_Toggle/holdFire coverage does not cover this
    /// custom gizmo, including Milira's linked rocket turret.
    ///
    /// Snapshot the grouped components around the exact UI method and route
    /// each changed value through a registered sync method. Presentation-only
    /// work in the original gizmo (sound and render-tree dirtiness) remains
    /// local; the simulation state is applied on every peer.
    /// </summary>
    internal static class Patch_AncotTurretFireAtWillMp
    {
        private const string GizmoTypeName = "AncotLibrary.Gizmo_TurretGun";
        private const string CompTypeName = "AncotLibrary.CompTurretGun_Custom";

        private static bool _applied;
        private static Type _compType;
        private static FieldInfo _compsField;
        private static FieldInfo _fireAtWillField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                Type gizmoType = AccessTools.TypeByName(GizmoTypeName);
                _compType = AccessTools.TypeByName(CompTypeName);
                _compsField = gizmoType == null
                    ? null
                    : AccessTools.Field(gizmoType, "comps");
                _fireAtWillField = _compType == null
                    ? null
                    : AccessTools.Field(_compType, "fireAtWill");

                MethodInfo gizmoOnGui = FindGizmoOnGui(gizmoType);
                if (gizmoType == null || _compType == null || _compsField == null ||
                    _fireAtWillField == null || gizmoOnGui == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ancot turret fire-at-will sync " +
                        "skipped: target resolution failed.");
                    return;
                }

                MP.RegisterSyncMethod(
                    typeof(Patch_AncotTurretFireAtWillMp),
                    nameof(SyncFireAtWill));

                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_AncotTurretFireAtWillMp), nameof(GizmoPrefix));
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_AncotTurretFireAtWillMp), nameof(GizmoPostfix));
                harmony.Patch(
                    gizmoOnGui,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    postfix: new HarmonyMethod(postfix) { priority = Priority.Last });

                Log.Message(
                    "[MP-MeowOnlineShop] Ancot/Milira turret fire-at-will sync " +
                    "active: Gizmo_TurretGun=True, fireAtWill=True, syncMethod=True.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ancot turret fire-at-will sync apply " +
                    "failed: " + e.Message);
            }
        }

        public static void SyncFireAtWill(ThingComp comp, bool value)
        {
            if (comp == null || _fireAtWillField == null ||
                !_compType.IsInstanceOfType(comp))
                return;

            bool current = (bool)_fireAtWillField.GetValue(comp);
            if (current != value)
                _fireAtWillField.SetValue(comp, value);
        }

        private static MethodInfo FindGizmoOnGui(Type gizmoType)
        {
            if (gizmoType == null)
                return null;

            MethodInfo[] methods = gizmoType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "GizmoOnGUI" &&
                    method.DeclaringType == gizmoType && parameters.Length == 3)
                {
                    return method;
                }
            }

            return null;
        }

        private static void GizmoPrefix(object __instance, ref ToggleState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || __instance == null || _compsField == null ||
                _fireAtWillField == null)
                return;

            try
            {
                IEnumerable comps = _compsField.GetValue(__instance) as IEnumerable;
                if (comps == null)
                    return;

                ToggleState state = new ToggleState();
                foreach (object candidate in comps)
                {
                    ThingComp comp = candidate as ThingComp;
                    if (comp != null && _compType.IsInstanceOfType(comp))
                    {
                        state.Entries.Add(new ToggleEntry
                        {
                            Comp = comp,
                            OldValue = (bool)_fireAtWillField.GetValue(comp)
                        });
                    }
                }

                if (state.Entries.Count > 0)
                    __state = state;
            }
            catch
            {
                __state = null;
            }
        }

        private static void GizmoPostfix(ToggleState __state)
        {
            if (__state == null || !MP.IsInMultiplayer ||
                MP.IsExecutingSyncCommand)
                return;

            for (int i = 0; i < __state.Entries.Count; i++)
            {
                ToggleEntry entry = __state.Entries[i];
                if (entry == null || entry.Comp == null)
                    continue;

                try
                {
                    bool current = (bool)_fireAtWillField.GetValue(entry.Comp);
                    if (current != entry.OldValue)
                        SyncFireAtWill(entry.Comp, current);
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ancot turret fire-at-will sync " +
                        "dispatch failed: " + e.Message);
                }
            }
        }

        private sealed class ToggleState
        {
            internal readonly List<ToggleEntry> Entries = new List<ToggleEntry>();
        }

        private sealed class ToggleEntry
        {
            internal ThingComp Comp;
            internal bool OldValue;
        }
    }
}
