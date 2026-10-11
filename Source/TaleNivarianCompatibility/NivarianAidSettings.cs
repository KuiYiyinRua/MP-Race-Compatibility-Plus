using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using RimWorld;

namespace Meow.TaleNivarianCompatibility
{
    // Per-incident choices belong to the host's persisted session, not local preferences.
    public sealed class NivarianAidSettings : GameComponent
    {
        static FieldInfo settingsField, disabledField, allianceField;
        static MethodInfo readDisabled, readAlliance;
        static bool importing;
        static ISyncMethod change;
        static UiState active;
        public bool Enabled = true, RequireAlliance;
        Dictionary<string, bool> disabled = new Dictionary<string, bool>();
        Dictionary<string, bool> alliance = new Dictionary<string, bool>();
        bool initialized;
        static NivarianAidSettings Shared => MP.IsInMultiplayer ? Current.Game?.GetComponent<NivarianAidSettings>() : null;

        public NivarianAidSettings(Game game) { }
        void EnsureInitialized()
        {
            if (initialized || settingsField == null) return;
            var settings = settingsField.GetValue(null);
            if (settings == null) return;
            importing = true;
            try
            {
                foreach (var def in DefDatabase<IncidentDef>.AllDefsListForReading)
                {
                    disabled[def.defName] = !Enabled || (bool)readDisabled.Invoke(settings, new object[] { def });
                    alliance[def.defName] = RequireAlliance || (bool)readAlliance.Invoke(settings, new object[] { def });
                }
                initialized = true;
            }
            finally { importing = false; }
        }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) EnsureInitialized();
            Scribe_Values.Look(ref Enabled, "meowNivarianAidEnabled", true);
            Scribe_Values.Look(ref RequireAlliance, "meowNivarianAidRequireAlliance", false);
            Scribe_Values.Look(ref initialized, "meowNivarianPerIncidentInitialized", false);
            Scribe_Collections.Look(ref disabled, "meowNivarianAidDisabled", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref alliance, "meowNivarianAidAlliance", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                disabled = disabled ?? new Dictionary<string, bool>();
                alliance = alliance ?? new Dictionary<string, bool>();
            }
        }
        internal static void Install(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.NivarianSettings") ?? throw new TypeLoadException("Nivarian.NivarianSettings");
            var mod = AccessTools.TypeByName("Nivarian.NivarianMod");
            settingsField = AccessTools.Field(mod, "Settings") ?? throw new MissingFieldException(mod.FullName, "Settings");
            disabledField = AccessTools.Field(type, "_aidIncidentDisabled") ?? throw new MissingFieldException(type.FullName, "_aidIncidentDisabled");
            allianceField = AccessTools.Field(type, "_aidIncidentRequireAlliance") ?? throw new MissingFieldException(type.FullName, "_aidIncidentRequireAlliance");
            readDisabled = AccessTools.DeclaredMethod(type, "IsAidIncidentDisabled", new[] { typeof(IncidentDef) }) ?? throw new MissingMethodException(type.FullName, "IsAidIncidentDisabled");
            readAlliance = AccessTools.DeclaredMethod(type, "AidIncidentRequiresAlliance", new[] { typeof(IncidentDef) }) ?? throw new MissingMethodException(type.FullName, "AidIncidentRequiresAlliance");
            harmony.Patch(readDisabled, prefix: new HarmonyMethod(typeof(NivarianAidSettings), nameof(ReadDisabled)));
            harmony.Patch(readAlliance, prefix: new HarmonyMethod(typeof(NivarianAidSettings), nameof(ReadAlliance)));
            change = MP.RegisterSyncMethod(typeof(NivarianAidSettings), nameof(Change));
            harmony.Patch(AccessTools.DeclaredMethod(mod, "DoSettingsWindowContents"), prefix: new HarmonyMethod(typeof(NivarianAidSettings), nameof(BeginUi)), finalizer: new HarmonyMethod(typeof(NivarianAidSettings), nameof(EndUi)));
            harmony.Patch(AccessTools.DeclaredMethod(type, "ExposeData"), prefix: new HarmonyMethod(typeof(NivarianAidSettings), nameof(BeginSave)), finalizer: new HarmonyMethod(typeof(NivarianAidSettings), nameof(EndSave)));
        }
        static bool ReadDisabled(IncidentDef def, ref bool __result) => Read(def, false, ref __result);
        static bool ReadAlliance(IncidentDef def, ref bool __result) => Read(def, true, ref __result);
        static bool Read(IncidentDef def, bool requireAlliance, ref bool result)
        {
            var shared = importing ? null : Shared;
            if (shared == null || def == null || active != null) return true;
            shared.EnsureInitialized();
            return !(requireAlliance ? shared.alliance : shared.disabled).TryGetValue(def.defName, out result);
        }
        sealed class UiState
        {
            internal object Settings;
            internal Dictionary<string, bool> LocalDisabled, LocalAlliance;
            internal UiState Parent;
        }
        static Dictionary<string, bool> Copy(FieldInfo field, object settings) => new Dictionary<string, bool>((Dictionary<string, bool>)field.GetValue(settings) ?? new Dictionary<string, bool>());
        static void BeginUi(out UiState __state)
        {
            __state = null;
            var shared = Shared;
            if (!MP.InInterface || shared == null) return;
            shared.EnsureInitialized();
            var settings = settingsField.GetValue(null);
            __state = new UiState { Settings = settings, LocalDisabled = Copy(disabledField, settings), LocalAlliance = Copy(allianceField, settings), Parent = active };
            active = __state;
            disabledField.SetValue(settings, new Dictionary<string, bool>(shared.disabled));
            allianceField.SetValue(settings, new Dictionary<string, bool>(shared.alliance));
        }
        static Exception EndUi(Exception __exception, UiState __state)
        {
            if (__state == null) return __exception;
            var nextDisabled = Copy(disabledField, __state.Settings);
            var nextAlliance = Copy(allianceField, __state.Settings);
            disabledField.SetValue(__state.Settings, __state.LocalDisabled);
            allianceField.SetValue(__state.Settings, __state.LocalAlliance);
            active = __state.Parent;
            var shared = Shared;
            if (__exception == null && shared != null && (!Equal(nextDisabled, shared.disabled) || !Equal(nextAlliance, shared.alliance)))
            {
                // Reset may remove overrides; materialize native defaults as explicit session values.
                var defs = DefDatabase<IncidentDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal).ToArray();
                importing = true;
                disabledField.SetValue(__state.Settings, nextDisabled); allianceField.SetValue(__state.Settings, nextAlliance);
                try { change.DoSync(null, defs.Select(d => d.defName).ToArray(), defs.Select(d => (bool)readDisabled.Invoke(__state.Settings, new object[] { d })).ToArray(), defs.Select(d => (bool)readAlliance.Invoke(__state.Settings, new object[] { d })).ToArray()); }
                finally { importing = false; disabledField.SetValue(__state.Settings, __state.LocalDisabled); allianceField.SetValue(__state.Settings, __state.LocalAlliance); }
            }
            return __exception;
        }
        static bool Equal(Dictionary<string, bool> a, Dictionary<string, bool> b) => a.Count == b.Count && a.All(k => b.TryGetValue(k.Key, out var value) && value == k.Value);
        static void Change(string[] names, bool[] disabledValues, bool[] allianceValues)
        {
            var shared = Shared;
            if (shared == null || names == null || disabledValues == null || allianceValues == null || names.Length != disabledValues.Length || names.Length != allianceValues.Length) return;
            if (names.Distinct().Count() != names.Length || names.Any(n => DefDatabase<IncidentDef>.GetNamedSilentFail(n) == null)) return;
            shared.disabled = names.Select((n, i) => new { n, v = disabledValues[i] }).ToDictionary(p => p.n, p => p.v);
            shared.alliance = names.Select((n, i) => new { n, v = allianceValues[i] }).ToDictionary(p => p.n, p => p.v);
            shared.initialized = true;
        }
        static void BeginSave(object __instance, out Dictionary<string, bool>[] __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving) return;
            var frame = active;
            while (frame != null && !ReferenceEquals(frame.Settings, __instance)) frame = frame.Parent;
            if (frame == null) return;
            __state = new[] { Copy(disabledField, __instance), Copy(allianceField, __instance) };
            disabledField.SetValue(__instance, frame.LocalDisabled); allianceField.SetValue(__instance, frame.LocalAlliance);
        }
        static Exception EndSave(object __instance, Exception __exception, Dictionary<string, bool>[] __state)
        {
            if (__state != null) { disabledField.SetValue(__instance, __state[0]); allianceField.SetValue(__instance, __state[1]); }
            return __exception;
        }
    }
}


