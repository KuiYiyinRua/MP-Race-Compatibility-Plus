using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

// Keep the native executors: switching a weapon also updates verbs, jobs and stances.
[StaticConstructorOnStartup]
public static class PLAMilira_GizmoCompat
{
    static Type turret, fieldCommand, supportClosure;
    static MethodInfo supportAction;
    static FieldInfo recallCache;
    static readonly string[] turretFields = { "fireAtWill", "currentTarget", "forcedTarget", "isForcetargetDowned", "burstWarmupTicksLeft" };
    static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();
    static bool Interface => MP.IsInMultiplayer && MP.InInterface;

    static PLAMilira_GizmoCompat()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }

    static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
    static MethodInfo RequiredMethod(Type type, string name, params Type[] args) =>
        AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(PLAMilira_GizmoCompat), name);
    static object Get(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);

    // Resolve by the audited mutation as well as the generated callback signature.
    // This rejects changed/ambiguous callbacks instead of guessing a lambda ordinal.
    static void RegisterToggle(Type type, string fieldName)
    {
        var field = AccessTools.DeclaredField(type, fieldName) ?? throw new MissingFieldException(type.FullName, fieldName);
        var method = AccessTools.GetDeclaredMethods(type).Where(m => m.Name.StartsWith("<") &&
            m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
            PatchProcessor.GetOriginalInstructions(m).Any(i => i.opcode == OpCodes.Stfld && Equals(i.operand, field))).Single();
        MP.RegisterSyncMethod(method);
    }

    static void Install()
    {
        try
        {
            var h = new Harmony("PLAMilira.GizmoCompat");
            foreach (var name in new[] { "CompSwitch_New", "CompSwitch_New_New" })
                MP.RegisterSyncMethod(RequiredMethod(RequiredType("PLAMilira." + name), "SwitchVerb"));
            var anti = RequiredType("PLAMilira.CompAntiHarrierLauncher");
            MP.RegisterSyncMethod(RequiredMethod(anti, "TryLaunchInterception"));
            RegisterToggle(anti, "shouldAutoLaunch");
            var ballistic = RequiredType("PLAMilira.CompAntiballistic");
            RegisterToggle(ballistic, "switchOn");
            RegisterToggle(ballistic, "switchOnAntiAir");
            foreach (var name in new[] { "CompNaglfar_Middle_Buff", "CompNaglfar_Right_Buff", "CompNaglfar_Left_Armor", "CompNaglfar_Middle_Armor" })
                RegisterToggle(RequiredType("PLAMilira." + name), "isOn");

            foreach (var name in new[] { nameof(ApplyTurret), nameof(ActivateField), nameof(SelectSupport), nameof(SelectUhlan) })
                MP.RegisterSyncMethod(typeof(PLAMilira_GizmoCompat), name);

            turret = RequiredType("PLAMilira.CompPLAMilira_TurretGun_Building");
            foreach (var name in turretFields)
                fields.Add(name, AccessTools.DeclaredField(turret, name) ?? throw new MissingFieldException(turret.FullName, name));
            var gizmo = RequiredType("PLAMilira.Gizmo_PLAMilira_TurretGunBuilding");
            // The native GUI only writes these five simulation fields (including ResetCurrentTarget).
            // Capture and roll them all back before sending one outcome per component.
            h.Patch(RequiredMethod(gizmo, "GizmoOnGUI", typeof(Vector2), typeof(float), typeof(GizmoRenderParms)),
                prefix: Hook(nameof(BeforeTurret)), finalizer: Hook(nameof(AfterTurret)));
            var target = AccessTools.GetDeclaredMethods(gizmo).Single(m => m.Name.StartsWith("<ProcessTargetingInput>") &&
                m.ReturnType == typeof(void) && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(LocalTargetInfo) }));
            h.Patch(target, prefix: Hook(nameof(BeforeTurret)), finalizer: Hook(nameof(AfterTurret)));

            fieldCommand = RequiredType("PLAMilira.Command_AddHediffs");
            h.Patch(RequiredMethod(fieldCommand, "ProcessInput", typeof(Event)), prefix: Hook(nameof(BeforeField)));
            var support = RequiredType("PLAMilira.Comp_ChangeSupport");
            supportClosure = support.GetNestedType("<>c__DisplayClass32_0", BindingFlags.NonPublic) ?? throw new TypeLoadException("support closure");
            supportAction = RequiredMethod(supportClosure, "<CompGetGizmosExtra>b__1");
            foreach (var name in new[] { "index", "bulletlist", "<>4__this" })
                if (AccessTools.DeclaredField(supportClosure, name) == null) throw new MissingFieldException(supportClosure.FullName, name);
            h.Patch(supportAction, prefix: Hook(nameof(BeforeSupport)));
            recallCache = AccessTools.DeclaredField(support, "RecallTicksCache") ?? throw new MissingFieldException("RecallTicksCache");
            h.Patch(RequiredMethod(support, "PostExposeData"), postfix: Hook(nameof(ExposeSupport)));

            if (ModsConfig.IsActive("ancot.milianmodification"))
            {
                RegisterToggle(RequiredType("PLAMilianModification.HediffComp_Core_Archer"), "canFireMissile");
                var selector = RequiredType("PLAMilianModification.HediffComp_UhlanTurretSelector");
                var closure = selector.GetNestedType("<>c__DisplayClass8_1", BindingFlags.NonPublic) ?? throw new TypeLoadException("Uhlan closure");
                h.Patch(RequiredMethod(closure, "<CompGetGizmos>b__1"), prefix: Hook(nameof(BeforeUhlan)));
            }
            Log.Message("[PLAMilira_GizmoCompat] REQUIRED_TARGETS_OK native toggles/weapons, turret outcomes, field ability, support payload/state, optional Uhlan/Archer");
        }
        catch (Exception e) { Log.Error("[PLAMilira_GizmoCompat] REQUIRED_TARGET_FAILED " + e); }
    }

    public sealed class TurretState
    {
        public ThingComp comp;
        public object[] values;
        public int mask;
    }
    static void BeforeTurret(object __instance, out List<TurretState> __state)
    {
        __state = null;
        if (!Interface) return;
        __state = new List<TurretState>();
        foreach (ThingComp comp in (IEnumerable)Get(__instance, "comps"))
            __state.Add(new TurretState { comp = comp, values = turretFields.Select(n => fields[n].GetValue(comp)).ToArray() });
    }
    static Exception AfterTurret(Exception __exception, List<TurretState> __state)
    {
        if (__state == null) return __exception;
        var changed = new List<TurretState>();
        foreach (var before in __state)
        {
            var after = turretFields.Select(n => fields[n].GetValue(before.comp)).ToArray();
            int mask = 0;
            for (int i = 0; i < turretFields.Length; i++)
            {
                if (!Equals(after[i], before.values[i])) mask |= 1 << i;
                fields[turretFields[i]].SetValue(before.comp, before.values[i]);
            }
            if (mask != 0) changed.Add(new TurretState { comp = before.comp, values = after, mask = mask });
        }
        if (__exception == null)
            foreach (var after in changed)
                ApplyTurret(after.comp, (bool)after.values[0], (LocalTargetInfo)after.values[1],
                    (LocalTargetInfo)after.values[2], (bool)after.values[3], (int)after.values[4], after.mask);
        return __exception;
    }
    public static void ApplyTurret(ThingComp comp, bool fire, LocalTargetInfo current, LocalTargetInfo forced, bool downed, int warmup, int mask)
    {
        if (comp?.parent == null || comp.parent.Destroyed || !turret.IsInstanceOfType(comp)) return;
        object[] values = { fire, current, forced, downed, warmup };
        for (int i = 0; i < turretFields.Length; i++)
            if ((mask & (1 << i)) != 0) fields[turretFields[i]].SetValue(comp, values[i]);
    }
    static bool BeforeField(object __instance)
    {
        if (!Interface) return true;
        ActivateField((ThingComp)Get(__instance, "parentComp"));
        return false;
    }
    public static void ActivateField(ThingComp comp)
    {
        if (comp?.parent == null || !comp.parent.Spawned || (int)Get(comp, "cooldownTicks") > 0) return;
        var command = Activator.CreateInstance(fieldCommand);
        var props = comp.props;
        Set(command, "parentComp", comp);
        Set(command, "targetFaction", comp.parent.Faction);
        Set(command, "targetMap", comp.parent.Map);
        Set(command, "position", comp.parent.Position);
        Set(command, "hediffToAdd", Get(props, "hediffToAdd"));
        RequiredMethod(fieldCommand, "ProcessInput", typeof(Event)).Invoke(command, new object[] { null });
    }
    static bool BeforeSupport(object __instance)
    {
        if (!Interface) return true;
        SelectSupport((ThingComp)Get(__instance, "<>4__this"), (int)Get(__instance, "index"));
        return false;
    }
    public static void SelectSupport(ThingComp comp, int index)
    {
        if (comp?.parent == null || comp.parent.Destroyed) return;
        var options = (IList)AccessTools.Property(comp.GetType(), "SupportLists").GetValue(comp);
        if (index < 0 || index >= options.Count) return;
        var closure = Activator.CreateInstance(supportClosure, true);
        Set(closure, "<>4__this", comp);
        Set(closure, "index", index);
        Set(closure, "bulletlist", options[index]);
        supportAction.Invoke(closure, null);
    }
    static void ExposeSupport(object __instance)
    {
        var cache = (Dictionary<int, int>)recallCache.GetValue(__instance);
        Scribe_Collections.Look(ref cache, "meowSupportRecallTicks", LookMode.Value, LookMode.Value);
        recallCache.SetValue(__instance, cache ?? new Dictionary<int, int>());
    }
    static bool BeforeUhlan(object __instance)
    {
        if (!Interface) return true;
        var outer = Get(__instance, "CS$<>8__locals1");
        var comp = (HediffComp)Get(outer, "<>4__this");
        SelectUhlan(comp.Pawn, comp.parent.loadID, comp.parent.comps.IndexOf(comp), (int)Get(outer, "index"), (ThingDef)Get(__instance, "def"));
        return false;
    }
    public static void SelectUhlan(Pawn pawn, int hediffId, int compIndex, int slot, ThingDef def)
    {
        var hediff = pawn?.health?.hediffSet.hediffs.FirstOrDefault(h => h.loadID == hediffId) as HediffWithComps;
        if (hediff == null || compIndex < 0 || compIndex >= hediff.comps.Count) return;
        var comp = hediff.comps[compIndex];
        if (comp.GetType().FullName != "PLAMilianModification.HediffComp_UhlanTurretSelector") return;
        var selected = (List<ThingDef>)Get(comp, "selectedTurretDef");
        var options = (List<ThingDef>)Get(comp.props, "turretOptions");
        if (slot < 0 || slot >= selected.Count || !options.Contains(def)) return;
        selected[slot] = def;
        RequiredMethod(comp.GetType(), "RecalculateCachedLabel").Invoke(comp, null);
    }
}
