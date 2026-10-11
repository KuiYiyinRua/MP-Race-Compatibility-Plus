using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_ConsoleActions
{
    public struct TargetPayload
    {
        public string ThingDefName, SuccessMessage, ComboName;
        public int CooldownTicks, NeedSilver;
        public float HighLightRange;
    }
    static void SyncTargetPayload(SyncWorker sync, ref TargetPayload payload)
    {
        sync.Bind(ref payload.ThingDefName); sync.Bind(ref payload.SuccessMessage); sync.Bind(ref payload.ComboName);
        sync.Bind(ref payload.CooldownTicks); sync.Bind(ref payload.NeedSilver); sync.Bind(ref payload.HighLightRange);
    }
    sealed class Origin { public Def Def; }
    sealed class Target { public Type Window, Closure, Combo; public ConstructorInfo ComboCtor; public MethodInfo Callback; }
    static readonly List<Target> targets = new List<Target>();
    static readonly ConditionalWeakTable<object, Origin> origins = new ConditionalWeakTable<object, Origin>();
    static readonly Dictionary<MethodBase, int> targetIndexes = new Dictionary<MethodBase, int>();
    static readonly string[] directNames = { "GetRandomAvailableQuest", "CallOrbitalTrader", "CallTecPointResearchHelp" };
    static MethodInfo[] direct;
    static MethodInfo condition;
    static bool Interface => MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;
    // A console caller may stand on another map from the selected strike target.
    // Serializing Pawn and Map in one sync command makes MP bind two different
    // map contexts and reject the command before it can execute.
    static int CallerId(Pawn caller) => caller?.thingIDNumber ?? -1;
    static Pawn ResolveCaller(int id)
    {
        if (id < 0) return null;
        return Find.Maps.SelectMany(m => m.mapPawns.AllPawns).FirstOrDefault(p => p.thingIDNumber == id)
            ?? Find.WorldPawns?.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == id);
    }
    static object Get(object obj, string field) => obj == null ? null : AccessTools.Field(obj.GetType(), field)?.GetValue(obj);
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field)?.SetValue(obj, value);
    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(PLAMilira_ConsoleActions), name);
    static PLAMilira_ConsoleActions()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive || !MP.enabled ||
            !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }
    static void Install()
    {
        try
        {
            var h = new Harmony("Meow.PLAMilira.ConsoleActions");
            MP.RegisterSyncWorker<TargetPayload>(SyncTargetPayload);
            foreach (var name in new[] { nameof(ExecuteTarget), nameof(ExecuteDirect), nameof(ExecuteCondition) }) MP.RegisterSyncMethod(typeof(PLAMilira_ConsoleActions), name);
            var names = new[] { "Harrier", "Destroyer", "Milian", "Church_Milian", "Supply", "Supply" };
            for (int i = 0; i < names.Length; i++)
            {
                var window = AccessTools.TypeByName("PLAMilira.Dialog_" + names[i] + "_ArrowWindow") ?? throw new TypeLoadException(names[i]);
                string entry = i == 5 ? "CallTecPointSilverHelp" : i == 2 || i == 3 ? "StartTargetingWithCombo" : "StartTargetingWithThing";
                var methods = window.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(AccessTools.GetDeclaredMethods)
                    .Where(m => m.Name == "<" + entry + ">b__0" && m.ReturnType == typeof(void) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(LocalTargetInfo)).ToArray();
                if (methods.Length != 1) throw new MissingMethodException(window.FullName + ":" + entry);
                var target = new Target { Window = window, Closure = methods[0].DeclaringType, Callback = methods[0] };
                string[] expected = i < 2 ? new[] { "map", "needSilver", "thingDefName", "<>4__this", "cooldownTicks", "successMessage", "highLightRange" } :
                    i == 5 ? new[] { "map", "<>4__this", "combo" } : new[] { "map", "needSilver", "combo" };
                if (!target.Closure.GetFields().Select(f => f.Name).OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s))) throw new InvalidOperationException("Unknown console captures: " + window.FullName);
                if (i >= 2)
                {
                    target.Combo = AccessTools.Field(target.Closure, "combo").FieldType;
                    target.ComboCtor = target.Combo.GetConstructors().Single(c => c.GetParameters().Length == 1 && typeof(Def).IsAssignableFrom(c.GetParameters()[0].ParameterType));
                    if (i != 5) h.Patch(target.ComboCtor, postfix: Hook(nameof(CaptureOrigin)));
                }
                targetIndexes.Add(target.Callback, i); targets.Add(target);
                h.Patch(target.Callback, prefix: Hook(nameof(BeforeTarget)));
            }
            direct = directNames.Select(n => AccessTools.DeclaredMethod(targets[4].Window, n, new[] { targets[4].Combo }) ?? throw new MissingMethodException(n)).ToArray();
            foreach (var method in direct) h.Patch(method, prefix: Hook(nameof(BeforeDirect)));
            condition = AccessTools.DeclaredMethod(targets[0].Window, "CallGameCondition", new[] { typeof(GameConditionDef), typeof(int), typeof(int), typeof(string), typeof(int) }) ?? throw new MissingMethodException("CallGameCondition");
            h.Patch(condition, prefix: Hook(nameof(BeforeCondition)));
            Log.Message("[PLAMilira_ConsoleActions] REQUIRED_TARGETS_OK six native target callbacks, three supply executors, Harrier condition");
        }
        catch (Exception e) { Log.Error("[PLAMilira_ConsoleActions] REQUIRED_TARGET_FAILED " + e); }
    }
    static void CaptureOrigin(object __instance, Def __0) => origins.GetValue(__instance, _ => new Origin()).Def = __0;
    static string ComboName(object combo)
    {
        if (combo == null) return null;
        if (!origins.TryGetValue(combo, out var origin) || origin.Def == null) throw new InvalidOperationException("Console combo source Def was not captured");
        return origin.Def.defName;
    }
    static object Combo(Target target, string defName)
    {
        if (target.ComboCtor == null) return null;
        var def = GenDefDatabase.GetDef(target.ComboCtor.GetParameters()[0].ParameterType, defName, false);
        return def == null ? null : target.ComboCtor.Invoke(new object[] { def });
    }
    static Window Window(Type type, Pawn caller, bool local = false)
    {
        if (local && MP.IsExecutingSyncCommandIssuedBySelf)
        {
            var existing = Find.WindowStack.Windows.FirstOrDefault(w => w.GetType() == type && Get(w, "instigator") == caller);
            if (existing != null) return existing;
        }
        return (Window)Activator.CreateInstance(type, caller);
    }
    static bool BeforeTarget(object __instance, LocalTargetInfo __0, MethodBase __originalMethod)
    {
        if (!Interface) return true;
        int index = targetIndexes[__originalMethod];
        ExecuteTarget(index, CallerId((Pawn)Get(Get(__instance, "<>4__this"), "instigator")), (Map)Get(__instance, "map"), __0,
            new TargetPayload {
                ThingDefName = (string)Get(__instance, "thingDefName"), CooldownTicks = (int?)Get(__instance, "cooldownTicks") ?? 0,
                NeedSilver = (int?)Get(__instance, "needSilver") ?? 0, SuccessMessage = (string)Get(__instance, "successMessage"),
                HighLightRange = (float?)Get(__instance, "highLightRange") ?? 0, ComboName = ComboName(Get(__instance, "combo")) });
        return false;
    }
    public static void ExecuteTarget(int index, int callerId, Map map, LocalTargetInfo targetInfo, TargetPayload payload)
    {
        if (index < 0 || index >= targets.Count || map == null || !targetInfo.IsValid) return;
        var caller = ResolveCaller(callerId);
        if (caller == null) return;
        var target = targets[index]; var combo = Combo(target, payload.ComboName);
        if (target.ComboCtor != null && combo == null) return;
        // Silver-for-points checked eligibility before opening its targeting cursor.
        // Recheck the balance at execution to prevent concurrent overspending.
        if (index == 5)
        {
            var type = AccessTools.TypeByName("PLAMilira.PLAMilira_Church_Faction_Handler");
            var instance = AccessTools.Property(type, "Instance").GetValue(null);
            if (instance == null || (int)AccessTools.Property(type, "TECPoint").GetValue(instance) < (int)Get(combo, "TecPointCost")) return;
        }
        var closure = Activator.CreateInstance(target.Closure);
        Set(closure, "map", map); Set(closure, "needSilver", payload.NeedSilver); Set(closure, "combo", combo);
        Set(closure, "thingDefName", payload.ThingDefName); Set(closure, "cooldownTicks", payload.CooldownTicks); Set(closure, "successMessage", payload.SuccessMessage); Set(closure, "highLightRange", payload.HighLightRange);
        if (AccessTools.Field(target.Closure, "<>4__this") != null) Set(closure, "<>4__this", Window(target.Window, caller));
        target.Callback.Invoke(closure, new object[] { targetInfo });
    }
    static bool BeforeDirect(object __instance, object __0, MethodBase __originalMethod)
    {
        if (!Interface) return true;
        ExecuteDirect(Array.IndexOf(direct, __originalMethod), CallerId((Pawn)Get(__instance, "instigator")), Find.CurrentMap, ComboName(__0));
        return false;
    }
    public static void ExecuteDirect(int index, int callerId, Map map, string comboName)
    {
        if (index < 0 || index >= direct.Length || map == null) return;
        var caller = ResolveCaller(callerId);
        if (caller == null) return;
        var combo = Combo(targets[4], comboName); if (combo == null) return;
        var previous = Find.CurrentMap;
        try { Current.Game.CurrentMap = map; direct[index].Invoke(Window(targets[4].Window, caller, true), new[] { combo }); }
        finally { Current.Game.CurrentMap = previous; }
    }
    static bool BeforeCondition(object __instance, GameConditionDef conditionDef, int conditionDuration, int cooldownTicks, string successMessage, int needSilver)
    {
        if (!Interface) return true;
        ExecuteCondition(CallerId((Pawn)Get(__instance, "instigator")), Find.CurrentMap, conditionDef, conditionDuration, cooldownTicks, successMessage, needSilver);
        return false;
    }
    public static void ExecuteCondition(int callerId, Map map, GameConditionDef def, int duration, int cooldown, string message, int silver)
    {
        if (map == null || def == null) return;
        var caller = ResolveCaller(callerId);
        if (caller == null) return;
        var previous = Find.CurrentMap;
        try { Current.Game.CurrentMap = map; condition.Invoke(Window(targets[0].Window, caller, true), new object[] { def, duration, cooldown, message, silver }); }
        finally { Current.Game.CurrentMap = previous; }
    }
}
