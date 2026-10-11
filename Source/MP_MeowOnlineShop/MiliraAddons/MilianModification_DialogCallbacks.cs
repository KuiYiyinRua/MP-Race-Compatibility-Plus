using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class MilianModification_DialogCallbacks
{
    static Type outer, inner;
    static MethodInfo nativeAction, mapComp;
    static FieldInfo parentField, mapField, sceneField, componentField, dialogsField, nodeField;
    static PropertyInfo dialogProperty;

    static MilianModification_DialogCallbacks()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                var type = AccessTools.TypeByName("MilianModification.MermeisterDialogUtility") ?? throw new TypeLoadException("MermeisterDialogUtility");
                outer = type.GetNestedType("<>c__DisplayClass8_0", BindingFlags.NonPublic);
                inner = type.GetNestedType("<>c__DisplayClass8_1", BindingFlags.NonPublic);
                nativeAction = AccessTools.DeclaredMethod(inner, "<RequestResearchAssistance>b__3", Type.EmptyTypes);
                parentField = AccessTools.DeclaredField(inner, "CS$<>8__locals1");
                componentField = AccessTools.DeclaredField(inner, "component");
                mapField = AccessTools.DeclaredField(outer, "map"); sceneField = AccessTools.DeclaredField(outer, "sceneB");
                if (nativeAction == null || parentField?.FieldType != outer || mapField?.FieldType != typeof(Map) ||
                    sceneField?.FieldType != typeof(DiaNode) || componentField?.FieldType.FullName != "MilianModification.MilianComponentDef")
                    throw new MissingMemberException("Mermeister native research callback context");
                mapComp = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "MpComp", new[] { typeof(Map) });
                dialogsField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.MultiplayerMapComp"), "mapDialogs");
                dialogProperty = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.PersistentDialog"), "Dialog");
                nodeField = AccessTools.Field(typeof(Dialog_NodeTree), "curNode");
                if (mapComp == null || dialogsField == null || dialogProperty == null || nodeField == null)
                    throw new MissingMemberException("MP persistent dialogue lookup");
                // Exact installed native methods with directly serializable captures.
                string[,] methods = {
                    { "<>c", "<MermeisterDiaOption>b__5_1" },
                    { "<>c", "<MermeisterDiaOption>b__5_3" },
                    { "<>c", "<RequestResearchAssistance>b__8_2" },
                    { "<>c__DisplayClass5_0", "<MermeisterDiaOption>b__0" },
                    { "<>c__DisplayClass5_1", "<MermeisterDiaOption>b__2" },
                    { "<>c__DisplayClass6_0", "<ContactMermeisterNotAvailable>b__0" },
                    { "<>c__DisplayClass7_0", "<ContactMermeisterLeftChurch>b__0" },
                    { "<>c__DisplayClass7_0", "<ContactMermeisterLeftChurch>b__1" }
                };
                for (int i = 0; i < methods.GetLength(0); i++)
                    MiliraDialogDelegates.Register(AccessTools.DeclaredMethod(type.GetNestedType(methods[i, 0], BindingFlags.NonPublic), methods[i, 1], Type.EmptyTypes));
                MiliraDialogDelegates.Register(AccessTools.DeclaredMethod(typeof(SavedResearch), nameof(SavedResearch.Invoke), Type.EmptyTypes));
                var factory = AccessTools.DeclaredMethod(type, "RequestResearchAssistance", new[] { typeof(Map) }) ?? throw new MissingMethodException("RequestResearchAssistance");
                new Harmony("Meow.MilianModification.DialogCallbacks").Patch(factory,
                    postfix: new HarmonyMethod(typeof(MilianModification_DialogCallbacks), nameof(Wrap)));
                Log.Message("[MilianModification_DialogCallbacks] REQUIRED_TARGETS_OK research callback and8native delegates");
            }
            catch (Exception e) { Log.Error("[MilianModification_DialogCallbacks] REQUIRED_TARGET_FAILED " + e); }
        });
    }

    static void Wrap(DiaOption __result)
    {
        if (!MP.IsInMultiplayer || __result?.link == null) return;
        foreach (var option in __result.link.options)
        {
            var action = option.action;
            if (action?.Method != nativeAction) continue;
            var parent = parentField.GetValue(action.Target);
            var saved = new SavedResearch { map = (Map)mapField.GetValue(parent), component = (ThingDef)componentField.GetValue(action.Target) };
            saved.Capture(action);
            option.action = saved.Invoke;
        }
    }

    public abstract class LiveResearch
    {
        protected Action live;
        internal void Capture(Action action) => live = action;
    }

    public sealed class SavedResearch : LiveResearch, IExposable
    {
        public Map map;
        public ThingDef component;
        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_Defs.Look(ref component, "component");
        }
        public void Invoke()
        {
            if (live == null)
            {
                if (map == null || component == null) throw new InvalidOperationException("Mermeister research context not restored");
                var destination = FindDestination();
                var parent = Activator.CreateInstance(outer);
                mapField.SetValue(parent, map); sceneField.SetValue(parent, destination);
                var target = Activator.CreateInstance(inner);
                parentField.SetValue(target, parent); componentField.SetValue(target, component);
                live = (Action)Delegate.CreateDelegate(typeof(Action), target, nativeAction);
            }
            // Preserve native quest generation, response text, cooldown and availability.
            live();
        }

        DiaNode FindDestination()
        {
            var pending = new Stack<DiaNode>();
            foreach (object dialog in (IEnumerable)dialogsField.GetValue(mapComp.Invoke(null, new object[] { map })))
                pending.Push((DiaNode)nodeField.GetValue(dialogProperty.GetValue(dialog)));
            var seen = new HashSet<DiaNode>();
            DiaNode found = null;
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == null || !seen.Add(node)) continue;
                foreach (var option in node.options)
                {
                    if (ReferenceEquals(option.action?.Target, this))
                    {
                        if (found != null || option.link == null) throw new InvalidOperationException("Ambiguous Mermeister response node");
                        found = option.link;
                    }
                    if (option.link != null) pending.Push(option.link);
                }
            }
            return found ?? throw new InvalidOperationException("Restored Mermeister option not present in persistent dialogue");
        }
    }
}
