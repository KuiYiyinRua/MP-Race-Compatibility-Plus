using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Edit the existing aid component so earlier saves and incident readers keep one authority.
    internal static class NivarianAidSettingsUi
    {
        static FieldInfo settingsField;
        static FieldInfo[] fields;
        static ISyncMethod change;
        static UiState activeUi;
        static NivarianAidSettings Shared => MP.IsInMultiplayer ? Current.Game?.GetComponent<NivarianAidSettings>() : null;
        internal static void Apply(Harmony harmony)
        {
            var mod = AccessTools.TypeByName("Nivarian.NivarianMod") ?? throw new TypeLoadException("Nivarian.NivarianMod");
            settingsField = AccessTools.Field(mod, "Settings") ?? throw new MissingFieldException(mod.FullName, "Settings");
            fields = new[] { "EnableNivarianAid", "RequireAllianceForNivarianAid" }.Select(name =>
                AccessTools.Field(settingsField.FieldType, name) ?? throw new MissingFieldException(settingsField.FieldType.FullName, name)).ToArray();
            if (fields.Any(field => field.FieldType != typeof(bool))) throw new InvalidOperationException("Aid setting field type changed");
            change = MP.RegisterSyncMethod(typeof(NivarianAidSettingsUi), nameof(Change));
            harmony.Patch(AccessTools.DeclaredMethod(mod, "DoSettingsWindowContents") ?? throw new MissingMethodException(mod.FullName, "DoSettingsWindowContents"),
                prefix: new HarmonyMethod(typeof(NivarianAidSettingsUi), nameof(BeginUi)),
                finalizer: new HarmonyMethod(typeof(NivarianAidSettingsUi), nameof(EndUi)));
            harmony.Patch(AccessTools.DeclaredMethod(settingsField.FieldType, "ExposeData") ?? throw new MissingMethodException(settingsField.FieldType.FullName, "ExposeData"),
                prefix: new HarmonyMethod(typeof(NivarianAidSettingsUi), nameof(BeginSettingsSave)),
                finalizer: new HarmonyMethod(typeof(NivarianAidSettingsUi), nameof(EndSettingsSave)));
            Log.Message("[TaleNivarianCompat] Aid settings UI edits existing shared Enabled/RequireAlliance fields.");
        }
        sealed class UiState
        {
            internal object Settings;
            internal bool[] Local, Before;
            internal UiState Parent;
        }
        static void BeginUi(out UiState __state)
        {
            __state = null;
            var shared = Shared;
            if (!MP.InInterface || shared == null) return;
            var settings = settingsField.GetValue(null);
            __state = new UiState { Settings = settings, Local = fields.Select(f => (bool)f.GetValue(settings)).ToArray(), Before = new[] { shared.Enabled, shared.RequireAlliance }, Parent = activeUi };
            activeUi = __state;
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(settings, __state.Before[i]);
        }
        static Exception EndUi(Exception __exception, UiState __state)
        {
            if (__state == null) return __exception;
            activeUi = __state.Parent;
            var indices = new List<int>(); var updates = new List<bool>();
            for (int i = 0; i < fields.Length; i++)
            {
                bool next = (bool)fields[i].GetValue(__state.Settings);
                fields[i].SetValue(__state.Settings, __state.Local[i]);
                if (next != __state.Before[i]) { indices.Add(i); updates.Add(next); }
            }
            if (__exception == null && indices.Count != 0) change.DoSync(null, indices.ToArray(), updates.ToArray());
            return __exception;
        }
        // ResetAll clears read notifications and calls ModSettings.Write while the UI is
        // still displaying shared values. Serialize local preferences, then restore the
        // pending UI delta even if the serializer throws. GameComponent saves are separate.
        static void BeginSettingsSave(object __instance, out bool[] __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving) return;
            UiState local = null;
            for (var frame = activeUi; frame != null; frame = frame.Parent)
                if (ReferenceEquals(frame.Settings, __instance)) local = frame;
            if (local == null) return;
            __state = fields.Select(f => (bool)f.GetValue(__instance)).ToArray();
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__instance, local.Local[i]);
        }
        static Exception EndSettingsSave(object __instance, Exception __exception, bool[] __state)
        {
            if (__state != null)
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__instance, __state[i]);
            return __exception;
        }
        static void Change(int[] indices, bool[] updates)
        {
            var shared = Shared;
            if (shared == null || indices == null || updates == null || indices.Length != updates.Length || indices.Length > fields.Length) return;
            // Validate the whole delta before changing any shared value.
            for (int i = 0; i < indices.Length; i++)
                if (indices[i] < 0 || indices[i] >= fields.Length) return;
            for (int i = 0; i < indices.Length; i++) { if (indices[i] == 0) shared.Enabled = updates[i]; else shared.RequireAlliance = updates[i]; }
        }
    }
}


