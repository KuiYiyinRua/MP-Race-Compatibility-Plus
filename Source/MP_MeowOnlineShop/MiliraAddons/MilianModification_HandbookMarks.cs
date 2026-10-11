using System;
using System.Collections;
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

/// <summary>
/// The handbook stores its star marks in a saved GameComponent. Its GUI writes
/// that list directly, so the host and each client otherwise keep different
/// saved state after using the handbook.
/// </summary>
[StaticConstructorOnStartup]
public static class MilianModification_HandbookMarks
{
    private const string LogTag = "[MP-MeowOnlineShop][MilianHandbookMarks]";
    private static Type _componentType;
    private static Type _gameComponentType;
    private static FieldInfo _marksField;
    private static MethodInfo _add;
    private static MethodInfo _remove;
    private static MethodInfo _clear;

    private static bool Intercept => MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;

    static MilianModification_HandbookMarks()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }

    private static void Install()
    {
        try
        {
            _componentType = AccessTools.TypeByName("MilianModification.MilianComponentDef") ??
                throw new TypeLoadException("MilianComponentDef");
            _gameComponentType = AccessTools.TypeByName("MilianModification.MiliraGameComponent_MilianComponentRecipe") ??
                throw new TypeLoadException("MiliraGameComponent_MilianComponentRecipe");
            var dialogType = AccessTools.TypeByName("MilianModification.Dialog_MilianComponentIllustratedHandbook") ??
                throw new TypeLoadException("Dialog_MilianComponentIllustratedHandbook");
            _marksField = AccessTools.Field(_gameComponentType, "milianComponentAll_Marked") ??
                throw new MissingFieldException("milianComponentAll_Marked");
            var listType = typeof(List<>).MakeGenericType(_componentType);
            if (_marksField.FieldType != listType) throw new MissingFieldException("Unexpected handbook marks type");
            _add = listType.GetMethod("Add", new[] { _componentType });
            _remove = listType.GetMethod("Remove", new[] { _componentType });
            _clear = listType.GetMethod("Clear", Type.EmptyTypes);
            if (_add == null || _remove == null || _clear == null) throw new MissingMethodException("Handbook list mutation");
            var drawSlot = AccessTools.DeclaredMethod(dialogType, "DrawSlot", new[] { typeof(Rect), _componentType }) ??
                throw new MissingMethodException("Dialog_MilianComponentIllustratedHandbook.DrawSlot");
            var drawWindow = AccessTools.DeclaredMethod(dialogType, "DoWindowContents", new[] { typeof(Rect) }) ??
                throw new MissingMethodException("Dialog_MilianComponentIllustratedHandbook.DoWindowContents");

            MP.RegisterSyncMethod(typeof(MilianModification_HandbookMarks), nameof(SyncedSetMark));
            MP.RegisterSyncMethod(typeof(MilianModification_HandbookMarks), nameof(SyncedClearMarks));
            var harmony = new Harmony("Ancot.MilianModification.HandbookMarks");
            harmony.Patch(drawSlot, transpiler: new HarmonyMethod(typeof(MilianModification_HandbookMarks), nameof(ReplaceMarkCalls)));
            harmony.Patch(drawWindow, transpiler: new HarmonyMethod(typeof(MilianModification_HandbookMarks), nameof(ReplaceClearCall)));
            Log.Message(LogTag + " REQUIRED_TARGETS_OK star add/remove and reset sync");
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " REQUIRED_TARGET_FAILED " + e);
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceMarkCalls(IEnumerable<CodeInstruction> instructions)
    {
        int adds = 0, removes = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.Equals(_add))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(MilianModification_HandbookMarks), nameof(RequestAddMark));
                adds++;
            }
            else if (instruction.operand is MethodInfo method2 && method2.Equals(_remove))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(MilianModification_HandbookMarks), nameof(RequestRemoveMark));
                removes++;
            }
            yield return instruction;
        }
        if (adds != 1 || removes != 1) throw new InvalidOperationException("Handbook mark call sites changed: add=" + adds + " remove=" + removes);
    }

    private static IEnumerable<CodeInstruction> ReplaceClearCall(IEnumerable<CodeInstruction> instructions)
    {
        int clears = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.Equals(_clear))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(MilianModification_HandbookMarks), nameof(RequestClearMarks));
                clears++;
            }
            yield return instruction;
        }
        if (clears != 1) throw new InvalidOperationException("Handbook reset call site changed: clear=" + clears);
    }

    private static IList Marks
    {
        get
        {
            if (Current.Game == null) return null;
            var component = Current.Game.components.FirstOrDefault(c => c != null && _gameComponentType.IsInstanceOfType(c));
            return component == null ? null : _marksField.GetValue(component) as IList;
        }
    }

    private static void RequestAddMark(IList list, Def component)
    {
        if (!Intercept || !ReferenceEquals(list, Marks)) { list.Add(component); return; }
        if (component != null) SyncedSetMark(component.defName, true);
    }

    private static bool RequestRemoveMark(IList list, Def component)
    {
        if (!Intercept || !ReferenceEquals(list, Marks))
        {
            bool present = list.Contains(component);
            if (present) list.Remove(component);
            return present;
        }
        if (component != null) SyncedSetMark(component.defName, false);
        return false;
    }

    private static void RequestClearMarks(IList list)
    {
        if (!Intercept || !ReferenceEquals(list, Marks)) { list.Clear(); return; }
        SyncedClearMarks();
    }

    public static void SyncedSetMark(string defName, bool marked)
    {
        var list = Marks;
        if (string.IsNullOrEmpty(defName)) return;
        var component = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        if (list == null || component == null || !_componentType.IsInstanceOfType(component)) return;
        if (marked)
        {
            if (!list.Contains(component)) list.Add(component);
        }
        else if (list.Contains(component)) list.Remove(component);
    }

    public static void SyncedClearMarks()
    {
        Marks?.Clear();
    }
}
