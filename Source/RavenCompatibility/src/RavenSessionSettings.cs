using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenSessionSettings
 {
  private sealed class State { public object effective; public bool hasSnapshot; public Dictionary<string, string> values = new Dictionary<string, string>(); }
  private static readonly ConditionalWeakTable<Game, State> states = new ConditionalWeakTable<Game, State>();
  private static readonly HashSet<string> localNames = new HashSet<string>(new[] { "enableUpdateNews", "lastViewedVersion", "enableUpdateNewsMoanSound", "contentInstallationId", "contentPromptedKeys", "enableVerboseLogging", "enableMemeSounds", "enableLovinRandomSounds", "centralHubUiFontScale", "showSpecialPawnRoster", "memeSoundToggles", "memeSoundVolumes", "showFeatherCooldown", "drawServitudeLines", "enableRavenLiquidCoveredVisuals", "enableBlackSunCloakVisuals", "enableAltarVisualEffects" }, StringComparer.Ordinal);
  private static FieldInfo localField;
  private static FieldInfo[] fields, shared;
  private static MethodInfo changed;
  private static ISyncMethod set;
  private static object Local => localField.GetValue(null);
  internal static void Apply(Harmony h)
  {
   var mod = AccessTools.TypeByName("RavenRace.RavenRaceMod"); var type = AccessTools.TypeByName("RavenRace.RavenRaceSettings");
   localField = AccessTools.DeclaredField(mod, "<Settings>k__BackingField") ?? throw new MissingFieldException("Raven local settings");
   fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
   shared = fields.Where(f => !localNames.Contains(f.Name)).ToArray(); changed = AccessTools.DeclaredMethod(type, "OnSettingsChanged");
   foreach (var field in fields) Encode(field.FieldType, field.GetValue(Local));
   set = MP.RegisterSyncMethod(typeof(RavenSessionSettings), nameof(Set));
   h.Patch(AccessTools.DeclaredPropertyGetter(mod, "Settings"), prefix: new HarmonyMethod(typeof(RavenSessionSettings), nameof(Get)));
   h.Patch(AccessTools.DeclaredMethod(typeof(Game), "ExposeSmallComponents"), prefix: new HarmonyMethod(typeof(RavenSessionSettings), nameof(Expose)) { priority = Priority.First });
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Settings.Dialog_RavenModSettings"), "DoWindowContents"), prefix: new HarmonyMethod(typeof(RavenSessionSettings), nameof(BeforeDraw)), finalizer: new HarmonyMethod(typeof(RavenSessionSettings), nameof(AfterDraw)));
   h.Patch(AccessTools.Method(typeof(ModSettings), nameof(ModSettings.Write)), prefix: new HarmonyMethod(typeof(RavenSessionSettings), nameof(Write)));
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.Features.Bloodline.CompBloodline"), "UnlockBloodlineParts"), prefix: new HarmonyMethod(typeof(RavenSessionSettings), nameof(AllowUnlock)));
  }
  private static bool AllowUnlock() => !MP.InInterface;
  private static State StateFor(Game game)
  {
   var state = states.GetOrCreateValue(game);
   if (state.effective == null)
   {
    state.effective = AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(Local, null);
    foreach (var field in fields) field.SetValue(state.effective, Decode(field.FieldType, Encode(field.FieldType, field.GetValue(Local))));
   }
   return state;
  }
  private static bool Get(ref object __result)
  {
   if (Current.Game == null) return true;
   if (!MP.IsInMultiplayer && (!states.TryGetValue(Current.Game, out var existing) || !existing.hasSnapshot)) return true;
   __result = StateFor(Current.Game).effective; return false;
  }
  private static Dictionary<string, string> Capture(object settings) => shared.ToDictionary(f => f.Name, f => Encode(f.FieldType, f.GetValue(settings)), StringComparer.Ordinal);
  private static void Import(object settings, Dictionary<string, string> values)
  {
   if (values == null) return;
   foreach (var field in shared) if (values.TryGetValue(field.Name, out var value)) field.SetValue(settings, Decode(field.FieldType, value));
  }
  private static void Expose(Game __instance)
  {
   var state = StateFor(__instance);
   if (Scribe.mode == LoadSaveMode.Saving)
   {
    if (!MP.IsInMultiplayer && !state.hasSnapshot) return;
    state.hasSnapshot = true; state.values = Capture(state.effective);
   }
   Scribe_Collections.Look(ref state.values, "mpRavenSessionSettings", LookMode.Value, LookMode.Value);
   // A former MP save must also restore before its host loads it through the
   // single-player menu. Its gameplay settings remain attached to that save.
   if (Scribe.mode == LoadSaveMode.LoadingVars)
   {
    state.hasSnapshot = state.values != null;
    Import(state.effective, state.values);
    if (!state.hasSnapshot && !MP.IsInMultiplayer) state.effective = null;
   }
  }
  private static void BeforeDraw(object ___settings, out Dictionary<string, string> __state)
  {
   __state = MP.InInterface ? Capture(___settings) : null;
  }
  private static void AfterDraw(object ___settings, Dictionary<string, string> __state)
  {
   if (__state == null) return;
   var values = Capture(___settings);
   var names = new List<string>(); var updates = new List<string>();
   foreach (var field in shared)
   {
    string before = __state[field.Name], after = values[field.Name];
    if (before == after) continue;
    field.SetValue(___settings, Decode(field.FieldType, before));
    names.Add(field.Name); updates.Add(after);
   }
   if (names.Count > 0) set.DoSync(null, names.ToArray(), updates.ToArray());
  }
  private static void Set(string[] names, string[] values)
  {
   if (Current.Game == null || names == null || values == null || names.Length != values.Length || names.Length > shared.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length) return;
   var targets = names.Select(name => shared.FirstOrDefault(f => f.Name == name)).ToArray();
   if (targets.Any(f => f == null)) return;
   var decoded = targets.Select((field, i) => Decode(field.FieldType, values[i])).ToArray();
   var settings = StateFor(Current.Game).effective;
   for (int i = 0; i < targets.Length; i++) targets[i].SetValue(settings, decoded[i]);
   changed.Invoke(settings, null);
  }
  private static bool Write(ModSettings __instance)
  {
   if (Current.Game == null || !states.TryGetValue(Current.Game, out var state) || !ReferenceEquals(state.effective, __instance)) return true;
   // Multiplayer gameplay options belong to the game snapshot. Persist only
   // this computer's presentation preferences when its settings UI closes.
   if (MP.InInterface || !MP.IsInMultiplayer)
   {
    foreach (var field in fields.Where(f => localNames.Contains(f.Name))) field.SetValue(Local, Decode(field.FieldType, Encode(field.FieldType, field.GetValue(__instance))));
    ((ModSettings)Local).Write();
   }
   return false;
  }
  private static string Encode(Type type, object value)
  {
   using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
   {
    if (type == typeof(bool)) writer.Write((bool)value);
    else if (type == typeof(int)) writer.Write((int)value);
    else if (type == typeof(float)) writer.Write((float)value);
    else if (type == typeof(string)) { writer.Write(value != null); if (value != null) writer.Write((string)value); }
    else if (type == typeof(HashSet<string>))
    {
     var values = ((HashSet<string>)value)?.OrderBy(s => s, StringComparer.Ordinal).ToArray(); writer.Write(values?.Length ?? -1); if (values != null) foreach (var item in values) writer.Write(item);
    }
    else if (type == typeof(Dictionary<string, bool>))
    {
     var values = ((Dictionary<string, bool>)value)?.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(); writer.Write(values?.Length ?? -1); if (values != null) foreach (var item in values) { writer.Write(item.Key); writer.Write(item.Value); }
    }
    else if (type == typeof(Dictionary<string, float>))
    {
     var values = ((Dictionary<string, float>)value)?.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(); writer.Write(values?.Length ?? -1); if (values != null) foreach (var item in values) { writer.Write(item.Key); writer.Write(item.Value); }
    }
    else throw new NotSupportedException("Raven settings field type " + type.FullName);
    writer.Flush(); return Convert.ToBase64String(stream.ToArray());
   }
  }
  private static object Decode(Type type, string text)
  {
   using (var stream = new MemoryStream(Convert.FromBase64String(text))) using (var reader = new BinaryReader(stream))
   {
    object value;
    if (type == typeof(bool)) value = reader.ReadBoolean();
    else if (type == typeof(int)) value = reader.ReadInt32();
    else if (type == typeof(float)) value = reader.ReadSingle();
    else if (type == typeof(string)) value = reader.ReadBoolean() ? reader.ReadString() : null;
    else
    {
     int count = reader.ReadInt32(); if (count < -1 || count > 100000) throw new InvalidDataException("Raven settings collection count");
     if (count == -1) value = null;
     else if (type == typeof(HashSet<string>)) { var values = new HashSet<string>(StringComparer.Ordinal); for (int i = 0; i < count; i++) values.Add(reader.ReadString()); value = values; }
     else if (type == typeof(Dictionary<string, bool>)) { var values = new Dictionary<string, bool>(StringComparer.Ordinal); for (int i = 0; i < count; i++) values.Add(reader.ReadString(), reader.ReadBoolean()); value = values; }
     else if (type == typeof(Dictionary<string, float>)) { var values = new Dictionary<string, float>(StringComparer.Ordinal); for (int i = 0; i < count; i++) values.Add(reader.ReadString(), reader.ReadSingle()); value = values; }
     else throw new NotSupportedException(type.FullName);
    }
    if (stream.Position != stream.Length) throw new InvalidDataException("Raven settings trailing bytes");
    return value;
   }
  }
 }
}


