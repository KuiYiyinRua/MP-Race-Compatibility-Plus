using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    public sealed class WolfeinScheduledActions : MapComponent
    {
        private List<WolfeinScheduledAction> actions = new List<WolfeinScheduledAction>();
        private static readonly MethodInfo AsyncTime = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "AsyncTime", new[] { typeof(Map) });
        private static readonly FieldInfo MapTicks = AsyncTime == null ? null : AccessTools.Field(AsyncTime.ReturnType, "mapTicks");
        public WolfeinScheduledActions(Map map) : base(map) { }
        internal static bool SchedulePrefix(int delayTicks, Action action)
        {
            if (!MP.IsInMultiplayer) return true;
            Map map = FindMap(action?.Target);
            if (map == null) throw new InvalidOperationException("Wolfein delayed action has no map: " + action?.Method);
            map.GetComponent<WolfeinScheduledActions>().actions.Add(new WolfeinScheduledAction
            {
                tick = (int)MapTicks.GetValue(AsyncTime.Invoke(null, new object[] { map })) + delayTicks,
                action = action
            });
            return false;
        }
        private static Map FindMap(object target)
        {
            if (target is Map map) return map;
            if (target is MapParent parent) return parent.Map;
            if (target == null) return null;
            // Only compiler closures and the audited quest parts are traversed, never arbitrary game graphs.
            foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object value = field.GetValue(target);
                if (value is Map found) return found;
                if (value is MapParent mapParent && mapParent.Map != null) return mapParent.Map;
                if (value != null && (value is QuestPart || value.GetType().Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal)))
                {
                    Map nested = FindMap(value);
                    if (nested != null) return nested;
                }
            }
            return null;
        }
        public override void MapComponentTick()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("wolfein")) return;
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var entry = actions[i];
                if (Find.TickManager.TicksGame < entry.tick) continue;
                actions.RemoveAt(i);
                entry.action();
            }
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref actions, "mpWolfeinScheduledActions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && actions == null) actions = new List<WolfeinScheduledAction>();
        }
    }

    public sealed class WolfeinScheduledAction : IExposable
    {
        internal int tick;
        internal Action action;
        private string declaringType, method;
        private WolfeinActionValue target;
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                declaringType = action.Method.DeclaringType.FullName;
                method = action.Method.Name;
                target = new WolfeinActionValue(action.Target);
            }
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref declaringType, "declaringType"); Scribe_Values.Look(ref method, "method");
            Scribe_Deep.Look(ref target, "target");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (declaringType == null || !declaringType.StartsWith("WolfeinAllegiance.", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected Wolfein delayed action type: " + declaringType);
                var callback = AccessTools.DeclaredMethod(AccessTools.TypeByName(declaringType), method, Type.EmptyTypes)
                    ?? throw new MissingMethodException(declaringType, method);
                action = (Action)Delegate.CreateDelegate(typeof(Action), target?.Restore(), callback);
            }
        }
    }

    // Serialize the small, explicitly supported capture graph used by Allegiance's scheduled callbacks.
    // Game objects remain references; only compiler-generated closure containers are deep-copied.
    public sealed class WolfeinActionValue : IExposable
    {
        private string kind, type, text;
        private List<string> names;
        private List<WolfeinActionValue> fields;
        private int integer;
        private bool boolean;
        private float number;
        private IntVec3 cell;
        private LocalTargetInfo target;
        private Thing thing;
        private Map map;
        private Faction faction;
        private QuestPart part;
        private WorldObject worldObject;
        public WolfeinActionValue() { }
        internal WolfeinActionValue(object value)
        {
            if (value == null) { kind = "null"; return; }
            type = value.GetType().FullName;
            if (value is Thing t) { kind = "thing"; thing = t; }
            else if (value is Map m) { kind = "map"; map = m; }
            else if (value is Faction f) { kind = "faction"; faction = f; }
            else if (value is QuestPart p) { kind = "part"; part = p; }
            else if (value is WorldObject w) { kind = "world"; worldObject = w; }
            else if (value is Def d) { kind = "def"; text = d.defName; }
            else if (value is IntVec3 c) { kind = "cell"; cell = c; }
            else if (value is LocalTargetInfo l) { kind = "target"; target = l; }
            else if (value is int i) { kind = "int"; integer = i; }
            else if (value is bool b) { kind = "bool"; boolean = b; }
            else if (value is float n) { kind = "float"; number = n; }
            else if (value is string s) { kind = "string"; text = s; }
            else if (type.StartsWith("WolfeinAllegiance.", StringComparison.Ordinal) && value.GetType().Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
            {
                kind = "closure"; names = new List<string>(); fields = new List<WolfeinActionValue>();
                foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(fld => fld.Name, StringComparer.Ordinal))
                {
                    names.Add(field.Name); fields.Add(new WolfeinActionValue(field.GetValue(value)));
                }
            }
            else throw new NotSupportedException("Unsupported Wolfein scheduled capture: " + type);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref type, "type"); Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref integer, "integer"); Scribe_Values.Look(ref boolean, "boolean"); Scribe_Values.Look(ref number, "number");
            Scribe_Values.Look(ref cell, "cell"); Scribe_TargetInfo.Look(ref target, "target");
            Scribe_References.Look(ref thing, "thing"); Scribe_References.Look(ref map, "map"); Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref part, "part"); Scribe_References.Look(ref worldObject, "worldObject");
            Scribe_Collections.Look(ref names, "names", LookMode.Value); Scribe_Collections.Look(ref fields, "fields", LookMode.Deep);
        }
        internal object Restore()
        {
            switch (kind)
            {
                case "null": return null;
                case "thing": return thing;
                case "map": return map;
                case "faction": return faction;
                case "part": return part;
                case "world": return worldObject;
                case "def": return GenDefDatabase.GetDef(AccessTools.TypeByName(type), text);
                case "cell": return cell;
                case "target": return target;
                case "int": return integer;
                case "bool": return boolean;
                case "float": return number;
                case "string": return text;
                case "closure":
                    if (type == null || !type.StartsWith("WolfeinAllegiance.", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected capture type");
                    object closure = Activator.CreateInstance(AccessTools.TypeByName(type), true);
                    for (int i = 0; i < names.Count; i++) AccessTools.Field(closure.GetType(), names[i]).SetValue(closure, fields[i].Restore());
                    return closure;
                default: throw new NotSupportedException("Unknown Wolfein capture kind: " + kind);
            }
        }
    }
}
