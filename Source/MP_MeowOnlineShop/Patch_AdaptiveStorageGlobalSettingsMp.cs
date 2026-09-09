using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Adaptive Storage Global Settings (`nuanki.adaptivestorageglobalsettings`)
    /// edits a ModSettings object at runtime and immediately rewrites storage
    /// ThingDefs. Those changes affect simulation and architect menus, so they
    /// must be identical on every peer. The patch snapshots the whole settings
    /// object after each mutating dialog/import/reset action and replays it by
    /// Scribe deserialization plus ApplyAllSavedSettings.
    /// </summary>
    internal static class Patch_AdaptiveStorageGlobalSettingsMp
    {
        private const string PackageId = "nuanki.adaptivestorageglobalsettings";

        private static Type _settingsType;
        private static FieldInfo _settingsField;
        private static MethodInfo _applyAllSettings;
        private static MethodInfo _recacheArchitectMenu;
        private static MethodInfo _writeExposable;
        private static MethodInfo _readExposable;
        private static ISyncMethod _syncApplySettings;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type modType = AccessTools.TypeByName("AdaptiveStorageGlobalSettings.ASGSMod");
            _settingsType = AccessTools.TypeByName("AdaptiveStorageGlobalSettings.ASGSSettings");
            _settingsField = modType == null ? null : AccessTools.Field(modType, "settings");
            Type initializerType = AccessTools.TypeByName("AdaptiveStorageGlobalSettings.ASGS_Initializer");
            _applyAllSettings = initializerType == null || _settingsType == null
                ? null
                : AccessTools.Method(initializerType, "ApplyAllSavedSettings", new[] { _settingsType });
            _recacheArchitectMenu = initializerType == null
                ? null
                : AccessTools.Method(initializerType, "RecacheArchitectMenu", Type.EmptyTypes);
            Type scribeUtilType = AccessTools.TypeByName("Multiplayer.Client.ScribeUtil");
            _writeExposable = scribeUtilType == null
                ? null
                : scribeUtilType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "WriteExposable" && m.GetParameters().Length == 4);
            _readExposable = scribeUtilType == null
                ? null
                : scribeUtilType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "ReadExposable" && m.IsGenericMethodDefinition &&
                                         m.GetParameters().Length == 2);

            if (_settingsType == null || _settingsField == null || _applyAllSettings == null ||
                _writeExposable == null || _readExposable == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Adaptive Storage Global Settings target resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncApplySettings = MP.RegisterSyncMethod(
                        typeof(Patch_AdaptiveStorageGlobalSettingsMp),
                        nameof(SyncApplySettings))
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Adaptive Storage Global Settings sync registration failed: " + e.Message);
                return;
            }

            PatchSnapshotPostfix(harmony, "AdaptiveStorageGlobalSettings.Dialog_GlobalBuildingSettings", "ApplyChanges");
            PatchSnapshotPostfix(harmony, "AdaptiveStorageGlobalSettings.Dialog_GlobalBuildingSettings", "ApplyOriginalMultipliedValues");
            PatchSnapshotPostfix(harmony, "AdaptiveStorageGlobalSettings.Dialog_BuildingSettings", "ApplyChanges");
            PatchSnapshotPostfix(harmony, "AdaptiveStorageGlobalSettings.ASGSMod", "ResetAllSettings");

            Type ioType = AccessTools.TypeByName("AdaptiveStorageGlobalSettings.ASGS_IO");
            MethodInfo import = ioType == null
                ? null
                : AccessTools.Method(ioType, "ImportSettingsFromFile");
            MethodInfo importPostfix = AccessTools.Method(
                typeof(Patch_AdaptiveStorageGlobalSettingsMp),
                nameof(ImportSettingsFromFilePostfix));
            if (import != null && importPostfix != null)
            {
                try
                {
                    harmony.Patch(import, postfix: new HarmonyMethod(importPostfix));
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Adaptive Storage import postfix failed: " + e.Message);
                }
            }

            Log.Message("[MP-MeowOnlineShop] Adaptive Storage Global Settings MP patch active: settings snapshots sync.");
        }

        private static void PatchSnapshotPostfix(Harmony harmony, string typeName, string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
            MethodInfo postfix = AccessTools.Method(
                typeof(Patch_AdaptiveStorageGlobalSettingsMp),
                nameof(SnapshotPostfix));
            if (method == null || postfix == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Adaptive Storage snapshot target missing: " + typeName + "." + methodName);
                return;
            }

            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Adaptive Storage snapshot postfix failed on " + methodName + ": " + e.Message);
            }
        }

        private static void SnapshotPostfix()
        {
            TrySendSnapshot();
        }

        private static void ImportSettingsFromFilePostfix(bool __result)
        {
            if (__result)
                TrySendSnapshot();
        }

        private static void TrySendSnapshot()
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncApplySettings == null || _settingsField == null || _settingsType == null)
            {
                return;
            }

            try
            {
                object settings = _settingsField.GetValue(null);
                if (settings == null)
                    return;

                byte[] data = (byte[])_writeExposable.Invoke(
                    null,
                    new[] { settings, "root", false, null });
                string snapshot = Convert.ToBase64String(data);
                _syncApplySettings.DoSync(null, snapshot);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Adaptive Storage settings snapshot send failed: " + e.Message);
            }
        }

        public static void SyncApplySettings(string xmlBase64)
        {
            if (string.IsNullOrEmpty(xmlBase64) || _settingsType == null ||
                _settingsField == null || _applyAllSettings == null || _readExposable == null)
            {
                return;
            }

            try
            {
                byte[] data = Convert.FromBase64String(xmlBase64);
                MethodInfo reader = _readExposable.MakeGenericMethod(_settingsType);
                object settings = reader.Invoke(null, new object[] { data, null });
                if (settings == null)
                    return;

                _settingsField.SetValue(null, settings);
                _applyAllSettings.Invoke(null, new[] { settings });
                _recacheArchitectMenu?.Invoke(null, null);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Adaptive Storage settings replay failed: " + e.Message);
            }
        }
    }
}
