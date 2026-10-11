using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class MilianModification_Transactions
{
    static Type compType, slotType;
    static FieldInfo install, uninstall;
    static MethodInfo remove;
    static MethodInfo[] setters;
    static readonly string[] names = { "AutoReplenishMilian", "AutoUseModifyAbility", "AutoUseModifyAbilityConsumptive" };
    static bool Intercept => MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;
    static ThingComp Comp(Pawn pawn) => pawn?.AllComps.FirstOrDefault(c => compType.IsInstanceOfType(c));
    static MilianModification_Transactions()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive || !MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }
    static void Install()
    {
        try
        {
            compType = AccessTools.TypeByName("MilianModification.CompModification") ?? throw new TypeLoadException("CompModification");
            slotType = AccessTools.TypeByName("MilianModification.ComponentSlot") ?? throw new TypeLoadException("ComponentSlot");
            install = AccessTools.Field(compType, "componentSlotInRecipe") ?? throw new MissingFieldException("componentSlotInRecipe");
            uninstall = AccessTools.Field(compType, "slotUninstallRecipe") ?? throw new MissingFieldException("slotUninstallRecipe");
            remove = AccessTools.DeclaredMethod(AccessTools.TypeByName("MilianModification.MilianModificationUtility"), "RemoveSlotsInDictionary") ?? throw new MissingMethodException("RemoveSlotsInDictionary");
            if (remove.GetParameters().Length != 2 || !typeof(IDictionary).IsAssignableFrom(remove.GetParameters()[0].ParameterType) ||
                remove.GetParameters()[1].ParameterType != typeof(List<>).MakeGenericType(slotType)) throw new InvalidOperationException("Unknown cancellation signature");
            setters = names.Select(n => AccessTools.DeclaredMethod(AccessTools.TypeByName("MilianModification.PawnColumnWorker_" + n), "SetValue", new[] { typeof(Pawn), typeof(bool), typeof(PawnTable) }) ?? throw new MissingMethodException(n)).ToArray();
            MP.RegisterSyncMethod(typeof(MilianModification_Transactions), nameof(CancelEntries));
            MP.RegisterSyncMethod(typeof(MilianModification_Transactions), nameof(SetOption));
            var h = new Harmony("Meow.MilianModification.Transactions");
            h.Patch(remove, prefix: new HarmonyMethod(typeof(MilianModification_Transactions), nameof(BeforeRemove)));
            foreach (var setter in setters) h.Patch(setter, prefix: new HarmonyMethod(typeof(MilianModification_Transactions), nameof(BeforeSet)));
            Log.Message("[MilianTransactions] REQUIRED_TARGETS_OK recipe cancellation and three native checkbox setters");
        }
        catch (Exception e) { Log.Error("[MilianTransactions] REQUIRED_TARGET_FAILED " + e); }
    }
    static bool BeforeRemove(IDictionary __0, IList __1)
    {
        if (!Intercept) return true;
        // This shared native executor is used by both the inspection tab and
        // pawn-column menus. Identify its durable owner only on a real action.
        foreach (var pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
        {
            var comp = Comp(pawn); if (comp == null) continue;
            bool isUninstall = ReferenceEquals(uninstall.GetValue(comp), __0);
            if (!isUninstall && !ReferenceEquals(install.GetValue(comp), __0)) continue;
            var keys = __1.Cast<object>().Where(__0.Contains).ToArray();
            if (keys.Length > 0) CancelEntries(pawn, isUninstall, keys.Select(Convert.ToInt32).ToArray(), keys.Select(k => (__0[k] as Def)?.defName).ToArray());
            return false;
        }
        Log.Error("[MilianTransactions] Recipe cancellation has no durable pawn owner; local mutation rejected");
        return false;
    }
    public static void CancelEntries(Pawn pawn, bool isUninstall, int[] slots, string[] expectedDefs)
    {
        var comp = Comp(pawn);
        if (comp == null || slots == null || expectedDefs == null || slots.Length != expectedDefs.Length) return;
        var dictionary = (IDictionary)(isUninstall ? uninstall : install).GetValue(comp);
        var keys = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(slotType));
        for (int i = 0; i < slots.Length; i++)
        {
            var key = Enum.ToObject(slotType, slots[i]);
            // Do not cancel another player's replacement queued before delivery.
            if (dictionary.Contains(key) && (dictionary[key] as Def)?.defName == expectedDefs[i]) keys.Add(key);
        }
        remove.Invoke(null, new object[] { dictionary, keys });
    }
    static bool BeforeSet(Pawn pawn, bool value, MethodBase __originalMethod)
    {
        if (!Intercept) return true;
        SetOption(pawn, Array.IndexOf(setters, __originalMethod), value);
        return false;
    }
    public static void SetOption(Pawn pawn, int index, bool value)
    {
        if (index < 0 || index >= setters.Length || Comp(pawn) == null) return;
        // The consumptive native setter also enables the general ability flag.
        // Replaying it preserves both writes in the same synchronized command.
        setters[index].Invoke(Activator.CreateInstance(setters[index].DeclaringType), new object[] { pawn, value, null });
    }
}
