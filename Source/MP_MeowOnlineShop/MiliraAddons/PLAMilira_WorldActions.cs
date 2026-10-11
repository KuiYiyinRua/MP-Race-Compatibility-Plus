using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_WorldActions
{
    static MethodInfo callCondition, secretSuccess;
    static Type secretWindow;
    static FieldInfo boundMech;
    static bool Interface => MP.IsInMultiplayer && MP.InInterface;
    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(PLAMilira_WorldActions), name);
    static object Get(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
    static Type Type(string name) => AccessTools.TypeByName("PLAMilira." + name) ?? throw new TypeLoadException(name);
    static PLAMilira_WorldActions()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }
    static void Install()
    {
        try
        {
            var h = new Harmony("Meow.PLAMilira.WorldActions");
            foreach (var name in new[] { nameof(CallSupport), nameof(CompleteSecret), nameof(BindMech) })
                MP.RegisterSyncMethod(typeof(PLAMilira_WorldActions), name);
            callCondition = AccessTools.DeclaredMethod(Type("CompNaglfarSupportWorld"), "CallGameCondition", new[] { typeof(GameConditionDef), typeof(int), typeof(Map) })
                ?? throw new MissingMethodException("CallGameCondition");
            h.Patch(callCondition, prefix: Hook(nameof(BeforeCondition)));
            secretWindow = Type("Dialog_Secret_ArrowWindow");
            secretSuccess = AccessTools.DeclaredMethod(secretWindow, "OnComboSuccess", System.Type.EmptyTypes)
                ?? throw new MissingMethodException("OnComboSuccess");
            h.Patch(secretSuccess, prefix: Hook(nameof(BeforeSecret)));
            var argo = Type("HediffComp_Argo");
            boundMech = AccessTools.DeclaredField(argo, "boundMech") ?? throw new MissingFieldException("boundMech");
            int boundCallbacks = 0;
            foreach (var type in new[] { argo }.Concat(argo.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m => m.Name.StartsWith("<") && m.ReturnType == typeof(void) && m.GetParameters().Length == 0))
            {
                if (!PatchProcessor.GetOriginalInstructions(method).Any(i => i.opcode == OpCodes.Stfld && Equals(i.operand, boundMech))) continue;
                h.Patch(method, prefix: Hook(nameof(BeforeBind)));
                boundCallbacks++;
            }
            if (boundCallbacks != 2) throw new InvalidOperationException("Argo bind/unbind callback count=" + boundCallbacks);
            // EventMapContext and ComponentInitialization in the shipped infrastructure
            // module already own the world-event map selection and static pool lifetime.
            Log.Message("[PLAMilira_WorldActions] REQUIRED_TARGETS_OK remote support, secret reward, Argo binding");
        }
        catch (Exception e) { Log.Error("[PLAMilira_WorldActions] REQUIRED_TARGET_FAILED " + e); }
    }
    static bool BeforeCondition(ThingComp __instance, GameConditionDef conditionDef, int conditionDuration, Map map)
    {
        if (!Interface) return true;
        var source = __instance.parent;
        CallSupport(map, source.Map.uniqueID, source.thingIDNumber, source.AllComps.IndexOf(__instance), conditionDef, conditionDuration);
        return false;
    }
    public static void CallSupport(Map map, int sourceMap, int sourceId, int compIndex, GameConditionDef def, int duration)
    {
        var parent = Find.Maps.FirstOrDefault(m => m.uniqueID == sourceMap)?.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == sourceId) as ThingWithComps;
        if (map == null || parent == null || compIndex < 0 || compIndex >= parent.AllComps.Count) return;
        var comp = parent.AllComps[compIndex];
        if (!callCondition.DeclaringType.IsInstanceOfType(comp) || (int)Get(comp, "supportCooldownticks") > 0) return;
        callCondition.Invoke(comp, new object[] { def, duration, map });
    }
    static bool BeforeSecret(object __instance)
    {
        if (!Interface) return true;
        CompleteSecret((Thing)Get(__instance, "thing"));
        ((Window)__instance).Close();
        return false;
    }
    public static void CompleteSecret(Thing console)
    {
        if (console == null || console.Destroyed || console.GetType().FullName != "PLAMilira.PortableSecretConsole") return;
        var quest = Get(console, "quest") as Quest;
        if (quest == null) return;
        // Replay the entire native reward: signal, random letter and item consumption.
        var window = Activator.CreateInstance(secretWindow, new object[] { new KeyCode[0], console, quest });
        secretSuccess.Invoke(window, null);
    }
    static bool BeforeBind(object __instance)
    {
        if (!Interface) return true;
        var comp = __instance as HediffComp ?? (HediffComp)Get(__instance, "<>4__this");
        var target = __instance is HediffComp ? null : (Pawn)Get(__instance, "targetMech");
        BindMech(comp, target?.thingIDNumber ?? -1);
        return false;
    }
    public static void BindMech(HediffComp comp, int targetId)
    {
        if (comp?.Pawn == null || !boundMech.DeclaringType.IsInstanceOfType(comp)) return;
        var target = targetId < 0 ? null : comp.Pawn.mechanitor?.ControlledPawns.FirstOrDefault(p => p.thingIDNumber == targetId);
        if (targetId >= 0 && target == null) return;
        boundMech.SetValue(comp, target);
    }
}
