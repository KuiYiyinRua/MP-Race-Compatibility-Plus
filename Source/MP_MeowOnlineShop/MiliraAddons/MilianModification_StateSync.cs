using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Synchronizes the remaining local-only Milian Modification UI mutations:
/// batch uninstall, research-bench settings/presets/history, the backup-battery
/// gizmo, and the custom royal-aid permit worker. Targets are resolved by name
/// so the compatibility assembly never hard-references the target DLL.
/// </summary>
[StaticConstructorOnStartup]
public static class MilianModification_StateSync
{
    private const string LogTag = "[MP-MeowOnlineShop][MilianState]";

    private static readonly Harmony Harmony;

    private static Type _gcType;
    private static Type _utilType;
    private static Type _slotType;
    private static Type _itabResearchType;
    private static Type _batteryCompType;
    private static Type _permitType;

    private static FieldInfo _tmpResourcesField;
    private static FieldInfo _researchBillSavedField;
    private static FieldInfo _researchHistoryRecordField;
    private static FieldInfo _minResearchSkillField;

    private static MethodInfo _addSlotsToUninstallRecipeSync;
    private static MethodInfo _batteryChargeOnce;
    private static MethodInfo _permitOrderForceTarget;
    private static MethodInfo _permitCallResourcesToCaravan;

    static MilianModification_StateSync()
    {
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
        if (MiliraMpCompatGate.ReferenceModActive)
        {
            Log.Message(LogTag + " skipped: usamiseika.fixmod.miliramultiplayer is active.");
            return;
        }

        Harmony = new Harmony("Ancot.MilianModification.StateSync");
        if (!MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification"))
            return;

        try
        {
            LongEventHandler.ExecuteWhenFinished(DoPatch);
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " init failed: " + e);
        }
    }

    private static void DoPatch()
    {
        try
        {
            _gcType = Resolve("MilianModification.MiliraGameComponent_MilianComponentRecipe");
            _utilType = Resolve("MilianModification.MilianModificationUtility");
            _slotType = Resolve("MilianModification.ComponentSlot");
            _itabResearchType = Resolve("MilianModification.ITab_Pawn_MilianComponentResearch");
            _batteryCompType = Resolve("MilianModification.HediffComp_BackupBattery");
            _permitType = Resolve("MilianModification.RoyalTitlePermitWorker_DropMilianComponent");

            if (_gcType == null || _utilType == null || _slotType == null ||
                _itabResearchType == null || _batteryCompType == null || _permitType == null)
            {
                Log.Warning(LogTag + " target types unresolved; state sync skipped.");
                return;
            }

            _tmpResourcesField = AccessTools.DeclaredField(_gcType, "tmpResources");
            _researchBillSavedField = AccessTools.DeclaredField(_gcType, "researchBillSaved");
            _researchHistoryRecordField = AccessTools.DeclaredField(_gcType, "researchHistoryRecord");
            _minResearchSkillField = AccessTools.DeclaredField(_gcType, "minResearchSkill");
            _addSlotsToUninstallRecipeSync = AccessTools.Method(
                _utilType,
                "AddSlotsToUninstallRecipeSync",
                new[] { typeof(Pawn), typeof(List<>).MakeGenericType(_slotType) });
            _batteryChargeOnce = AccessTools.DeclaredMethod(_batteryCompType, "BatteryChargeOnce");
            _permitOrderForceTarget = AccessTools.DeclaredMethod(
                _permitType,
                "OrderForceTarget",
                new[] { typeof(LocalTargetInfo) });
            _permitCallResourcesToCaravan = AccessTools.DeclaredMethod(
                _permitType,
                "CallResourcesToCaravan",
                new[] { typeof(Pawn), typeof(Faction), typeof(bool) });

            RegisterSyncMethods();
            PatchUiBoundaries();
            Log.Message(LogTag + " active: batch uninstall, research UI, battery, royal permit.");
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " DoPatch failed: " + e);
        }
    }

    private static void RegisterSyncMethods()
    {
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedBatchUninstall)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedSetMinResearchSkill)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedSetTmpResources)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedSaveResearchBill)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedRemoveResearchBill)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedRemoveResearchHistory)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedBatteryChargeOnce)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedPermitOrderForceTarget)));
        CompatUtility.TryRegisterSyncMethod(AccessTools.Method(
            typeof(MilianModification_StateSync),
            nameof(SyncedPermitCallResourcesToCaravan)));
    }

    private static void PatchUiBoundaries()
    {
        int patched = 0;

        if (_addSlotsToUninstallRecipeSync != null)
        {
            Harmony.Patch(
                _addSlotsToUninstallRecipeSync,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(MilianModification_StateSync),
                    nameof(BatchUninstallPrefix))));
            patched++;
        }

        patched += PatchMinSkill();
        patched += PatchResearchKeys();
        patched += PatchStoredBillDelete();
        patched += PatchHistoryDelete();
        patched += PatchTmpResources();

        if (_batteryChargeOnce != null)
        {
            Harmony.Patch(
                _batteryChargeOnce,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(MilianModification_StateSync),
                    nameof(BatteryChargePrefix))));
            patched++;
        }

        if (_permitOrderForceTarget != null)
        {
            Harmony.Patch(
                _permitOrderForceTarget,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(MilianModification_StateSync),
                    nameof(PermitOrderForceTargetPrefix))));
            patched++;
        }

        if (_permitCallResourcesToCaravan != null)
        {
            Harmony.Patch(
                _permitCallResourcesToCaravan,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(MilianModification_StateSync),
                    nameof(PermitCallResourcesPrefix))));
            patched++;
        }

        if (patched == 0)
        {
            Log.Warning(LogTag + " no UI boundary patched; state sync incomplete.");
            return;
        }

        Log.Message(LogTag + " UI boundaries patched: " + patched + ".");
    }

    private static int PatchMinSkill()
    {
        MethodInfo target = AccessTools.Method(_itabResearchType, "DrawSkillRequireSetting", new[] { typeof(Rect) });
        if (target == null)
            return 0;
        Harmony.Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(MinSkillPrefix))),
            postfix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(MinSkillPostfix))));
        return 1;
    }

    private static int PatchResearchKeys()
    {
        MethodInfo target = AccessTools.Method(_itabResearchType, "DrawFunctionKeys", new[] { typeof(Rect) });
        if (target == null)
            return 0;
        Harmony.Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(ResearchKeysPrefix))),
            postfix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(ResearchKeysPostfix))));
        return 1;
    }

    private static int PatchStoredBillDelete()
    {
        MethodInfo target = AccessTools.Method(_itabResearchType, "DrawStoredResearchBill", new[] { typeof(Rect), typeof(string) });
        if (target == null)
            return 0;
        Harmony.Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(StoredBillPrefix))),
            postfix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(StoredBillPostfix))));
        return 1;
    }

    private static int PatchHistoryDelete()
    {
        Type recordType = Resolve("MilianModification.ResearchHistoryRecord");
        MethodInfo target = recordType == null
            ? null
            : AccessTools.Method(_itabResearchType, "DrawHistoryRecord", new[] { typeof(Rect), recordType, typeof(string) });
        if (target == null)
            return 0;
        Harmony.Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(HistoryRecordPrefix))),
            postfix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(HistoryRecordPostfix))));
        return 1;
    }

    private static int PatchTmpResources()
    {
        MethodInfo target = AccessTools.Method(_itabResearchType, "GetResearchBill", Type.EmptyTypes);
        if (target == null)
            return 0;
        Harmony.Patch(
            target,
            postfix: new HarmonyMethod(AccessTools.Method(
                typeof(MilianModification_StateSync),
                nameof(GetResearchBillPostfix))));
        return 1;
    }

    private static bool ShouldIntercept()
    {
        return MP.IsInMultiplayer && !MP.IsExecutingSyncCommand;
    }

    private static bool BatchUninstallPrefix(Pawn pawn, object slots)
    {
        if (!ShouldIntercept() || pawn == null || !(slots is IList list))
            return true;

        int[] indexes = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
            indexes[i] = Convert.ToInt32(list[i]);

        SyncedBatchUninstall(pawn, indexes);
        return false;
    }

    public static void SyncedBatchUninstall(Pawn pawn, int[] slotIndexes)
    {
        if (pawn == null || slotIndexes == null || slotIndexes.Length == 0 ||
            _addSlotsToUninstallRecipeSync == null)
            return;

        try
        {
            Type listType = typeof(List<>).MakeGenericType(_slotType);
            IList slots = (IList)Activator.CreateInstance(listType);
            foreach (int index in slotIndexes)
                slots.Add(Enum.ToObject(_slotType, index));
            _addSlotsToUninstallRecipeSync.Invoke(null, new object[] { pawn, slots });
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " SyncedBatchUninstall failed: " + e.Message);
        }
    }

    private static void MinSkillPrefix(ref int __state)
    {
        __state = -1;
        if (!ShouldIntercept() || _minResearchSkillField == null)
            return;
        object gc = GetGameComp();
        if (gc != null)
            __state = (int)_minResearchSkillField.GetValue(gc);
    }

    private static void MinSkillPostfix(int __state)
    {
        if (__state < 0 || !ShouldIntercept() || _minResearchSkillField == null)
            return;
        object gc = GetGameComp();
        if (gc == null)
            return;
        int current = (int)_minResearchSkillField.GetValue(gc);
        if (current != __state)
            SyncedSetMinResearchSkill(current);
    }

    public static void SyncedSetMinResearchSkill(int value)
    {
        if (_minResearchSkillField == null)
            return;
        object gc = GetGameComp();
        if (gc != null)
            _minResearchSkillField.SetValue(gc, value);
    }

    private static void ResearchKeysPrefix(ref HashSet<string> __state)
    {
        __state = null;
        if (!ShouldIntercept())
            return;
        object gc = GetGameComp();
        if (gc == null || !(_researchBillSavedField?.GetValue(gc) is IDictionary dict))
            return;
        __state = new HashSet<string>();
        foreach (object key in dict.Keys)
            __state.Add((string)key);
    }

    private static void ResearchKeysPostfix(HashSet<string> __state)
    {
        if (__state == null || !ShouldIntercept())
            return;
        object gc = GetGameComp();
        if (gc == null || !(_researchBillSavedField?.GetValue(gc) is IDictionary dict))
            return;

        foreach (object keyObject in dict.Keys)
        {
            string key = keyObject as string;
            if (string.IsNullOrEmpty(key) || __state.Contains(key))
                continue;
            if (dict[key] is List<ThingDefCount> bill)
            {
                string[] defNames;
                int[] counts;
                BillToArrays(bill, out defNames, out counts);
                SyncedSaveResearchBill(key, defNames, counts);
            }
        }
    }

    private static void GetResearchBillPostfix()
    {
        if (!ShouldIntercept() || _tmpResourcesField == null)
            return;
        object gc = GetGameComp();
        if (gc == null || !(_tmpResourcesField.GetValue(gc) is IDictionary dict))
            return;

        var defNames = new List<string>();
        var counts = new List<int>();
        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Key is ThingDef def)
            {
                defNames.Add(def.defName);
                counts.Add(Convert.ToInt32(entry.Value));
            }
        }
        SyncedSetTmpResources(defNames.ToArray(), counts.ToArray());
    }

    public static void SyncedSetTmpResources(string[] defNames, int[] counts)
    {
        if (_tmpResourcesField == null || defNames == null || counts == null)
            return;
        object gc = GetGameComp();
        if (gc == null || !(_tmpResourcesField.GetValue(gc) is IDictionary dict))
            return;

        dict.Clear();
        int len = Math.Min(defNames.Length, counts.Length);
        for (int i = 0; i < len; i++)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defNames[i]);
            if (def != null)
                dict[def] = counts[i];
        }
    }

    private static void StoredBillPrefix(string key, ref bool __state)
    {
        __state = false;
        if (!ShouldIntercept() || string.IsNullOrEmpty(key))
            return;
        object gc = GetGameComp();
        if (gc != null && _researchBillSavedField?.GetValue(gc) is IDictionary dict)
            __state = dict.Contains(key);
    }

    private static void StoredBillPostfix(string key, bool __state)
    {
        if (!__state || !ShouldIntercept() || string.IsNullOrEmpty(key))
            return;
        object gc = GetGameComp();
        if (gc == null || !(_researchBillSavedField?.GetValue(gc) is IDictionary dict))
            return;
        if (!dict.Contains(key))
            SyncedRemoveResearchBill(key);
    }

    public static void SyncedRemoveResearchBill(string key)
    {
        RemoveDictionaryKey(_researchBillSavedField, key);
    }

    private static void HistoryRecordPrefix(string key, ref bool __state)
    {
        __state = false;
        if (!ShouldIntercept() || string.IsNullOrEmpty(key))
            return;
        object gc = GetGameComp();
        if (gc != null && _researchHistoryRecordField?.GetValue(gc) is IDictionary dict)
            __state = dict.Contains(key);
    }

    private static void HistoryRecordPostfix(string key, bool __state)
    {
        if (!__state || !ShouldIntercept() || string.IsNullOrEmpty(key))
            return;
        object gc = GetGameComp();
        if (gc == null || !(_researchHistoryRecordField?.GetValue(gc) is IDictionary dict))
            return;
        if (!dict.Contains(key))
            SyncedRemoveResearchHistory(key);
    }

    public static void SyncedRemoveResearchHistory(string key)
    {
        RemoveDictionaryKey(_researchHistoryRecordField, key);
    }

    private static void RemoveDictionaryKey(FieldInfo field, string key)
    {
        if (field == null || string.IsNullOrEmpty(key))
            return;
        object gc = GetGameComp();
        if (gc == null || !(field.GetValue(gc) is IDictionary dict))
            return;
        if (dict.Contains(key))
            dict.Remove(key);
    }

    private static bool BatteryChargePrefix(HediffComp __instance)
    {
        if (!ShouldIntercept() || __instance == null)
            return true;
        Hediff parent = __instance.parent;
        if (parent == null || parent.pawn == null)
            return true;
        SyncedBatteryChargeOnce(parent.pawn, parent.def);
        return false;
    }

    public static void SyncedBatteryChargeOnce(Pawn pawn, HediffDef def)
    {
        if (pawn == null || def == null || _batteryChargeOnce == null)
            return;
        try
        {
            Hediff hediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(def, false);
            HediffComp comp = TryGetHediffComp(hediff);
            if (comp != null)
                _batteryChargeOnce.Invoke(comp, null);
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " SyncedBatteryChargeOnce failed: " + e.Message);
        }
    }

    private static bool PermitOrderForceTargetPrefix(object __instance, LocalTargetInfo target)
    {
        if (!ShouldIntercept() || !_permitType.IsInstanceOfType(__instance))
            return true;
        RoyalTitlePermitWorker worker = __instance as RoyalTitlePermitWorker;
        if (worker?.def == null)
            return true;
        Pawn caller = Traverse.Create(worker).Field("caller").GetValue<Pawn>();
        if (caller == null || caller.MapHeld == null)
            return true;
        bool free = Traverse.Create(worker).Field("free").GetValue<bool>();
        SyncedPermitOrderForceTarget(worker.def, caller, target, free);
        return false;
    }

    public static void SyncedPermitOrderForceTarget(
        RoyalTitlePermitDef def,
        Pawn caller,
        LocalTargetInfo target,
        bool isFree)
    {
        if (def == null || caller == null || caller.MapHeld == null ||
            _permitOrderForceTarget == null)
            return;
        try
        {
            RoyalTitlePermitWorker worker = def.Worker;
            if (worker == null)
                return;
            Faction faction = FindPermitFaction(def, caller);
            SetPermitContext(worker, caller, caller.MapHeld, faction, isFree);
            CompatUtility.PushRand();
            try
            {
                _permitOrderForceTarget.Invoke(worker, new object[] { target });
            }
            finally
            {
                CompatUtility.PopRand();
            }
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " SyncedPermitOrderForceTarget failed: " + e.Message);
        }
    }

    private static bool PermitCallResourcesPrefix(
        object __instance,
        Pawn caller,
        Faction faction,
        bool free)
    {
        if (!ShouldIntercept() || !_permitType.IsInstanceOfType(__instance))
            return true;
        RoyalTitlePermitWorker worker = __instance as RoyalTitlePermitWorker;
        if (worker?.def == null || caller == null || faction == null)
            return true;
        SyncedPermitCallResourcesToCaravan(worker.def, caller, faction, free);
        return false;
    }

    public static void SyncedPermitCallResourcesToCaravan(
        RoyalTitlePermitDef def,
        Pawn caller,
        Faction faction,
        bool isFree)
    {
        if (def == null || caller == null || faction == null ||
            _permitCallResourcesToCaravan == null)
            return;
        try
        {
            RoyalTitlePermitWorker worker = def.Worker;
            if (worker == null)
                return;
            SetPermitContext(worker, caller, caller.MapHeld, faction, isFree);
            CompatUtility.PushRand();
            try
            {
                _permitCallResourcesToCaravan.Invoke(
                    worker,
                    new object[] { caller, faction, isFree });
            }
            finally
            {
                CompatUtility.PopRand();
            }
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " SyncedPermitCallResourcesToCaravan failed: " + e.Message);
        }
    }

    private static Faction FindPermitFaction(RoyalTitlePermitDef def, Pawn caller)
    {
        if (caller.royalty == null || Find.FactionManager == null)
            return null;
        foreach (Faction faction in Find.FactionManager.AllFactions)
        {
            try
            {
                if (caller.royalty.HasPermit(def, faction))
                    return faction;
            }
            catch
            {
                // A faction that cannot grant this permit is simply skipped.
            }
        }
        return null;
    }

    private static void SetPermitContext(
        RoyalTitlePermitWorker worker,
        Pawn caller,
        Map map,
        Faction faction,
        bool free)
    {
        TrySetField(worker, "caller", caller);
        TrySetField(worker, "map", map);
        TrySetField(worker, "instigator", caller);
        TrySetField(worker, "faction", faction);
        TrySetField(worker, "_faction", faction);
        TrySetField(worker, "calledFaction", faction);
        TrySetField(worker, "free", free);
    }

    private static void TrySetField(object target, string name, object value)
    {
        try
        {
            Traverse.Create(target).Field(name).SetValue(value);
        }
        catch
        {
            // Missing optional fields are ignored.
        }
    }

    private static void BillToArrays(
        List<ThingDefCount> bill,
        out string[] defNames,
        out int[] counts)
    {
        defNames = new string[bill.Count];
        counts = new int[bill.Count];
        for (int i = 0; i < bill.Count; i++)
        {
            defNames[i] = bill[i].ThingDef?.defName ?? string.Empty;
            counts[i] = bill[i].Count;
        }
    }

    public static void SyncedSaveResearchBill(string key, string[] defNames, int[] counts)
    {
        if (string.IsNullOrEmpty(key) || defNames == null || counts == null)
            return;
        object gc = GetGameComp();
        if (gc == null || !(_researchBillSavedField?.GetValue(gc) is IDictionary dict))
            return;

        var bill = new List<ThingDefCount>();
        int len = Math.Min(defNames.Length, counts.Length);
        for (int i = 0; i < len; i++)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defNames[i]);
            if (def != null)
                bill.Add(new ThingDefCount(def, counts[i]));
        }
        dict[key] = bill;
    }

    private static object GetGameComp()
    {
        if (_gcType == null || Current.Game == null)
            return null;
        foreach (GameComponent comp in Current.Game.components)
        {
            if (comp != null && _gcType.IsInstanceOfType(comp))
                return comp;
        }
        return null;
    }

    private static HediffComp TryGetHediffComp(Hediff hediff)
    {
        if (hediff == null)
            return null;
        try
        {
            MethodInfo generic = typeof(HediffUtility)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.Name == "TryGetComp" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 1);
            if (generic == null)
                return null;
            return generic.MakeGenericMethod(_batteryCompType)
                .Invoke(null, new object[] { hediff }) as HediffComp;
        }
        catch
        {
            return null;
        }
    }

    private static Type Resolve(string fullName)
    {
        return CompatUtility.ResolveTypeSilent(fullName);
    }
}
