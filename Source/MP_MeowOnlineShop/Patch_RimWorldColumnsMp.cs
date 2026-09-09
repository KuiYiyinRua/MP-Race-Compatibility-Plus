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
    /// RimWorld Columns (`nephlite.orbitaltradecolumn`) renders a custom
    /// Gizmo_ClaymoreSafetySettings window whose checkboxes write directly into
    /// the saved ClaymoreCharge.settings[] array. Those writes decide whether a
    /// claymore charge is active, obeys safety checks, or draws obstruction
    /// overlays, so they must be replayed on every peer. The patch snapshots
    /// the three booleans around DrawOptionFor, rolls back any UI mutation, and
    /// sends one sync command per changed setting.
    /// </summary>
    internal static class Patch_RimWorldColumnsMp
    {
        private const string PackageId = "nephlite.orbitaltradecolumn";
        private const string GizmoTypeName = "RimWorldColumns.Gizmo_ClaymoreSafetySettings";
        private const string ColumnTypeName = "RimWorldColumns.Building_ClaymoreColumn";
        private const string ChargeTypeName = "RimWorldColumns.ClaymoreCharge";

        private static bool _applied;
        private static Type _gizmoType;
        private static FieldInfo _claymoreReferenceField;
        private static FieldInfo _chargesField;
        private static FieldInfo _settingsField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] RimWorld Columns sync skipped (target mod not active).");
                    return;
                }

                _gizmoType = AccessTools.TypeByName(GizmoTypeName);
                Type columnType = AccessTools.TypeByName(ColumnTypeName);
                Type chargeType = AccessTools.TypeByName(ChargeTypeName);
                _claymoreReferenceField = _gizmoType == null
                    ? null
                    : AccessTools.Field(_gizmoType, "claymoreReference");
                _chargesField = columnType == null ? null : AccessTools.Field(columnType, "Charges");
                _settingsField = chargeType == null ? null : AccessTools.Field(chargeType, "settings");

                if (_gizmoType == null || columnType == null || chargeType == null ||
                    _claymoreReferenceField == null || _chargesField == null || _settingsField == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] RimWorld Columns target resolution failed; patch skipped.");
                    return;
                }

                MethodInfo syncMethod = AccessTools.Method(
                    typeof(Patch_RimWorldColumnsMp),
                    nameof(SyncToggleSetting),
                    new[] { typeof(Thing), typeof(int), typeof(int) });
                if (syncMethod == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] RimWorld Columns sync method resolution failed; patch skipped.");
                    return;
                }

                MP.RegisterSyncMethod(syncMethod, null);

                MethodInfo drawOption = AccessTools.Method(_gizmoType, "DrawOptionFor");
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_RimWorldColumnsMp),
                    nameof(DrawOptionForPrefix));
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_RimWorldColumnsMp),
                    nameof(DrawOptionForPostfix));

                if (drawOption == null || prefix == null || postfix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] RimWorld Columns gizmo target not resolved; patch skipped.");
                    return;
                }

                harmony.Patch(drawOption, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] RimWorld Columns MP patch active: 1 sync method, safety checkboxes rewritten.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] RimWorld Columns MP compat restore failed: " + e.Message);
            }
        }

        public static void SyncToggleSetting(Thing column, int directionIndex, int settingIndex)
        {
            if (column == null || directionIndex < 0 || settingIndex < 0)
                return;

            bool[] settings = GetSettingsFor(column, directionIndex);
            if (settings == null || settingIndex >= settings.Length)
                return;

            settings[settingIndex] = !settings[settingIndex];
        }

        private static void DrawOptionForPrefix(object __instance, Rot4 direction, ref string __state)
        {
            if (__instance == null || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return;

            bool[] settings = GetSettings(__instance, direction.AsInt);
            if (settings == null || settings.Length < 3)
                return;

            __state = settings[0] + "," + settings[1] + "," + settings[2];
        }

        private static void DrawOptionForPostfix(object __instance, Rot4 direction, string __state)
        {
            if (__instance == null || string.IsNullOrEmpty(__state) ||
                !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
            {
                return;
            }

            string[] parts = __state.Split(',');
            if (parts.Length != 3)
                return;

            bool[] old = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                if (!bool.TryParse(parts[i], out old[i]))
                    return;
            }

            bool[] current = GetSettings(__instance, direction.AsInt);
            if (current == null || current.Length < 3)
                return;

            var changed = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                if (current[i] != old[i])
                    changed.Add(i);
            }

            if (changed.Count == 0)
                return;

            for (int i = 0; i < changed.Count; i++)
                current[changed[i]] = old[changed[i]];

            object column = _claymoreReferenceField.GetValue(__instance);
            if (!(column is Thing columnThing))
                return;

            for (int i = 0; i < changed.Count; i++)
                SyncToggleSetting(columnThing, direction.AsInt, changed[i]);
        }

        private static bool[] GetSettings(object gizmo, int directionIndex)
        {
            if (gizmo == null || _claymoreReferenceField == null || _chargesField == null || _settingsField == null)
                return null;

            object column = _claymoreReferenceField.GetValue(gizmo);
            if (column == null)
                return null;

            IList charges = _chargesField.GetValue(column) as IList;
            if (charges == null || directionIndex < 0 || directionIndex >= charges.Count)
                return null;

            object charge = charges[directionIndex];
            if (charge == null)
                return null;

            return _settingsField.GetValue(charge) as bool[];
        }

        private static bool[] GetSettingsFor(Thing column, int directionIndex)
        {
            if (column == null || _chargesField == null || _settingsField == null)
                return null;

            IList charges = _chargesField.GetValue(column) as IList;
            if (charges == null || directionIndex < 0 || directionIndex >= charges.Count)
                return null;

            object charge = charges[directionIndex];
            if (charge == null)
                return null;

            return _settingsField.GetValue(charge) as bool[];
        }
    }
}
