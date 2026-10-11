using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynSettings
    {
        internal static FieldInfo[] Fields;
        static Type modType, settingsType;
        static MethodInfo gender, incident;
        static FieldInfo xenotype;
        internal static void Apply(Harmony harmony)
        {
            settingsType = AccessTools.TypeByName("ASEL.MonolynModSettings") ?? throw new TypeLoadException("ASEL.MonolynModSettings");
            modType = AccessTools.TypeByName("ASEL.Monolyn") ?? throw new TypeLoadException("ASEL.Monolyn");
            Fields = new[] { "Abstinence", "stelaeStorage", "lightCollection", "researchPoint", "tunerConsciousness", "tunerBand", "FaithTime", "DoubtTime", "useDogmaGizmo", "ConsumedByDarkness", "autoPreaching", "help", "terminusLimit", "priestJoin", "spawnRustedTower", "settingUpdatedC", "monochromeApparel" }
                .Select(name => AccessTools.Field(settingsType, name) ?? throw new MissingFieldException(settingsType.FullName, name)).ToArray();
            gender = AccessTools.DeclaredMethod(modType, "ApplyGenderPatch", Type.EmptyTypes) ?? throw new MissingMethodException(modType.FullName, "ApplyGenderPatch");
            var defs = AccessTools.TypeByName("ASEL.MNXenotypeDefOf") ?? throw new TypeLoadException("ASEL.MNXenotypeDefOf");
            xenotype = AccessTools.Field(defs, "MonolynXenotype") ?? throw new MissingFieldException(defs.FullName, "MonolynXenotype");
            if (xenotype.FieldType != typeof(XenotypeDef)) throw new InvalidOperationException("Monolyn xenotype field type changed");
            incident = AccessTools.DeclaredMethod(modType, "TriggerSpecificIncident", Type.EmptyTypes) ?? throw new MissingMethodException(modType.FullName, "TriggerSpecificIncident");
            foreach (var field in Fields)
            {
                if (!field.IsStatic || !new[] { typeof(bool), typeof(int), typeof(float) }.Contains(field.FieldType))
                    throw new InvalidOperationException("Unexpected Monolyn setting " + field);
                string name = field.Name;
                MP.RegisterSyncField(field).PostApply((target, value) => {
                    if (name == "Abstinence") gender.Invoke(null, null);
                    RefreshBuffer(name);
                });
            }
            var draw = AccessTools.DeclaredMethod(modType, "DoSettingsWindowContents") ?? throw new MissingMethodException(modType.FullName, "DoSettingsWindowContents");
            harmony.Patch(draw, prefix: new HarmonyMethod(typeof(MonolynSettings), nameof(Begin)), finalizer: new HarmonyMethod(typeof(MonolynSettings), nameof(End)));
            harmony.Patch(gender, prefix: new HarmonyMethod(typeof(MonolynSettings), nameof(Gender)),
                transpiler: new HarmonyMethod(typeof(MonolynSettings), nameof(GenderOrder)),
                postfix: new HarmonyMethod(typeof(MonolynSettings), nameof(GenderDefs)));
            harmony.Patch(incident, prefix: new HarmonyMethod(typeof(MonolynSettings), nameof(Incident)));
            MP.RegisterSyncMethod(typeof(MonolynSettings), nameof(TriggerIncident));
            Log.Message("[MonolynSettings] resolved=17 watched/saved settings, native gender callback, incident map command; missing=0");
        }
        static object Mod => LoadedModManager.GetMod(modType);
        static void RefreshBuffer(string name)
        {
            string buffer = name == "lightCollection" ? "LightValueBuffer" : name == "researchPoint" ? "ResearchValueBuffer" : name == "stelaeStorage" ? "StelaeStorageBuffer" : null;
            if (buffer != null) AccessTools.Field(modType, buffer).SetValue(Mod, AccessTools.Field(settingsType, name).GetValue(null).ToString());
        }
        static void Begin(out bool __state)
        {
            __state = MP.IsInMultiplayer && MP.InInterface;
            if (!__state) return;
            MP.WatchBegin();
            foreach (var field in Fields) MP.Watch(settingsType, field.Name);
        }
        static Exception End(Exception __exception, bool __state) { if (__state) MP.WatchEnd(); return __exception; }
        static bool Gender() => !MP.IsInMultiplayer || !MP.InInterface;
        static void GenderDefs()
        {
            if (!MP.IsInMultiplayer || MP.InInterface) return;
            // Native toggles remove then append a gene. A fresh client loads the
            // XML list again, so membership alone does not restore its order.
            var def = (XenotypeDef)xenotype.GetValue(null);
            if (def?.genes == null) throw new InvalidOperationException("Monolyn xenotype genes unavailable");
            def.genes.Sort((a, b) => StringComparer.Ordinal.Compare(a?.defName, b?.defName));
        }
        static List<Pawn> GenderPawns()
        {
            var pawns = PawnsFinder.All_AliveOrDead;
            return MP.IsInMultiplayer ? pawns.OrderBy(p => p == null ? int.MinValue : p.thingIDNumber).ToList() : pawns;
        }
        static IEnumerable<CodeInstruction> GenderOrder(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(PawnsFinder), nameof(PawnsFinder.All_AliveOrDead));
            if (getter.ReturnType != typeof(List<Pawn>)) throw new InvalidOperationException("Pawn finder return type changed");
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(MonolynSettings), nameof(GenderPawns));
                    count++;
                }
                yield return instruction;
            }
            if (count != 2) throw new InvalidOperationException("Expected two Monolyn settings pawn traversals, got " + count);
        }
        static bool Incident()
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            var map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (map != null) TriggerIncident(map);
            return false;
        }
        static void TriggerIncident(Map map)
        {
            if (map != null && Find.Maps.Contains(map)) incident.Invoke(Mod, null);
        }
    }

    public sealed class MonolynSavedSettings : GameComponent
    {
        List<MonolynSettingValue> values;
        public MonolynSavedSettings(Game game) { }
        public override void ExposeData()
        {
            if (MonolynSettings.Fields == null) return;
            if (Scribe.mode == LoadSaveMode.Saving)
                values = MonolynSettings.Fields.Select(f => new MonolynSettingValue(f)).ToList();
            Scribe_Collections.Look(ref values, "meowMonolynSettings", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && values != null)
                foreach (var value in values) value.Apply();
        }
    }
    public sealed class MonolynSettingValue : IExposable
    {
        string name, value;
        public MonolynSettingValue() { }
        internal MonolynSettingValue(FieldInfo field)
        { name = field.Name; value = Convert.ToString(field.GetValue(null), CultureInfo.InvariantCulture); }
        internal void Apply()
        {
            var field = MonolynSettings.Fields.Single(f => f.Name == name);
            field.SetValue(null, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
        }
        public void ExposeData() { Scribe_Values.Look(ref name, "name"); Scribe_Values.Look(ref value, "value"); }
    }
}
