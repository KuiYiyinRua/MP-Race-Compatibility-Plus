using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Deterministic boundaries for Milian Modification simulation and dialog
/// generation: GameComponent ticks, Mermeister dialog construction, cooperative
/// study caravan outcomes, pawn/corpse random draws, and the research-history ID
/// generator. All targets are resolved by name from the installed mod DLL.
/// </summary>
[StaticConstructorOnStartup]
public static class MilianModification_WorldDeterminism
{
    private const string LogTag = "[MP-MeowOnlineShop][MilianDet]";

    private static readonly Harmony Harmony;

    private static Type _churchType;
    private static Type _gcType;
    private static FieldInfo _researchHistoryRecordField;
    private static FieldInfo _ofPlayerField;
    private static FieldInfo _currentMapIndexField;

    static MilianModification_WorldDeterminism()
    {
        if (MiliraMpCompatGate.ReferenceModActive)
        {
            Log.Message(LogTag + " skipped: usamiseika.fixmod.miliramultiplayer is active.");
            return;
        }

        Harmony = new Harmony("Ancot.MilianModification.WorldDeterminism");
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
            _churchType = Resolve("MilianModification.Milian_ChurchCooperativeStudy");
            _gcType = Resolve("MilianModification.MiliraGameComponent_MilianComponentRecipe");
            _researchHistoryRecordField = _gcType == null
                ? null
                : AccessTools.DeclaredField(_gcType, "researchHistoryRecord");
            _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
            _currentMapIndexField = AccessTools.Field(typeof(Game), "currentMapIndex");

            int patched = 0;
            patched += PatchDetRand(
                "MilianModification.MiliraGameComponent_MilianComponentRecipe",
                "GameComponentTick");
            patched += PatchDetRand(
                "MilianModification.MermeisterDialogUtility",
                "MermeisterDiaOption",
                new[] { typeof(Map), typeof(Faction), typeof(Pawn) });
            patched += PatchDetRand(
                "MilianModification.MermeisterDialogUtility",
                "RequestResearchAssistance",
                new[] { typeof(Map) });
            patched += PatchDetRand(
                "MilianModification.CompMilianComponentData",
                "Initialize",
                new[] { typeof(CompProperties) });
            patched += PatchDetRand(
                "MilianModification.QuestNode_FindMilianComponentForTradeRequest",
                "TryFindRandomRequestedComponent",
                new[]
                {
                    typeof(Map),
                    typeof(float),
                    typeof(ThingDef).MakeByRefType(),
                    typeof(int).MakeByRefType(),
                    typeof(List<ThingDef>)
                });

            if (CompatUtility.WrapDetRandStatic(
                    Harmony,
                    "MilianModification.Milira_MilianPawnGenerator_Patch",
                    "Postfix"))
            {
                patched++;
            }

            patched += PatchChurchCaravanScopes();
            patched += PatchUniqueId();
            patched += PatchButcherProducts();

            if (patched == 0)
            {
                Log.Warning(LogTag + " no deterministic boundary patched.");
                return;
            }

            Log.Message(LogTag + " active: boundaries=" + patched + ".");
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " DoPatch failed: " + e);
        }
    }

    private static int PatchDetRand(string typeName, string methodName)
    {
        return PatchDetRand(typeName, methodName, null);
    }

    private static int PatchDetRand(string typeName, string methodName, Type[] paramTypes)
    {
        Type type = CompatUtility.ResolveTypeSilent(typeName);
        if (type == null)
            return 0;

        MethodInfo method = paramTypes == null
            ? AccessTools.Method(type, methodName)
            : AccessTools.Method(type, methodName, paramTypes);
        if (method == null)
            return 0;

        try
        {
            Harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(CompatUtility), "PushDetRand_Tick", (Type[])null),
                finalizer: new HarmonyMethod(typeof(CompatUtility), "PopDetRandFinalizer", (Type[])null));
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static int PatchChurchCaravanScopes()
    {
        if (_churchType == null)
            return 0;

        MethodInfo prefix = AccessTools.Method(
            typeof(MilianModification_WorldDeterminism),
            nameof(CaravanScopePrefix));
        MethodInfo finalizer = AccessTools.Method(
            typeof(MilianModification_WorldDeterminism),
            nameof(CaravanScopeFinalizer));
        if (prefix == null || finalizer == null)
            return 0;

        string[] methodNames =
        {
            "Notify_CaravanArrived",
            "Outcome_Raid",
            "Outcome_RaidMilira",
            "GenerateRaidSite"
        };

        int patched = 0;
        foreach (string name in methodNames)
        {
            MethodInfo method = AccessTools.DeclaredMethod(_churchType, name);
            if (method == null)
                continue;
            try
            {
                Harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(prefix),
                    finalizer: new HarmonyMethod(finalizer));
                patched++;
            }
            catch
            {
                // Keep patching the remaining methods.
            }
        }
        return patched;
    }

    private sealed class CaravanScopeState
    {
        internal bool RandActive;
        internal bool FactionActive;
        internal Faction SavedFaction;
        internal bool MapActive;
        internal Map SavedMap;
        internal sbyte SavedMapIndex;
    }

    private static void CaravanScopePrefix(
        Caravan caravan,
        ref CaravanScopeState __state)
    {
        __state = null;
        if (!MP.IsInMultiplayer || caravan == null)
            return;

        CaravanScopeState state = new CaravanScopeState();
        BeginFactionScope(caravan.Faction, state);
        BeginStableMapScope(state);
        CompatUtility.PushDetRand_Tick();
        state.RandActive = true;
        __state = state;
    }

    private static Exception CaravanScopeFinalizer(
        Exception __exception,
        CaravanScopeState __state)
    {
        if (__state == null)
            return __exception;

        if (__state.RandActive)
            CompatUtility.PopDetRandFinalizer(__exception);

        try
        {
            if (__state.MapActive && _currentMapIndexField != null &&
                Current.Game != null)
            {
                sbyte restoreIndex = __state.SavedMapIndex;
                if (__state.SavedMap != null && Find.Maps != null)
                {
                    int currentIndex = Find.Maps.IndexOf(__state.SavedMap);
                    if (currentIndex >= 0 && currentIndex <= sbyte.MaxValue)
                        restoreIndex = (sbyte)currentIndex;
                }
                _currentMapIndexField.SetValue(Current.Game, restoreIndex);
            }

            if (__state.FactionActive && _ofPlayerField != null &&
                Find.FactionManager != null)
            {
                _ofPlayerField.SetValue(
                    Find.FactionManager,
                    __state.SavedFaction);
            }
        }
        catch (Exception e)
        {
            Log.Warning(LogTag + " caravan scope restore failed: " + e.Message);
        }

        return __exception;
    }

    private static void BeginFactionScope(Faction faction, CaravanScopeState state)
    {
        if (_ofPlayerField == null || Find.FactionManager == null)
            return;
        if (faction?.def?.isPlayer != true)
            return;

        Faction previous = _ofPlayerField.GetValue(Find.FactionManager) as Faction;
        if (ReferenceEquals(previous, faction))
            return;

        _ofPlayerField.SetValue(Find.FactionManager, faction);
        state.FactionActive = true;
        state.SavedFaction = previous;
    }

    private static void BeginStableMapScope(CaravanScopeState state)
    {
        if (_currentMapIndexField == null || Current.Game == null ||
            Find.Maps == null || Find.Maps.Count == 0)
            return;

        Map map = Find.Maps
            .Where(m => m != null && m.IsPlayerHome)
            .OrderBy(m => m.uniqueID)
            .FirstOrDefault();
        if (map == null)
            map = Find.Maps.Where(m => m != null).OrderBy(m => m.uniqueID).FirstOrDefault();
        if (map == null)
            return;

        int index = Find.Maps.IndexOf(map);
        if (index < 0 || index > sbyte.MaxValue)
            return;

        sbyte previous = (sbyte)_currentMapIndexField.GetValue(Current.Game);
        sbyte next = (sbyte)index;
        if (previous == next)
            return;

        _currentMapIndexField.SetValue(Current.Game, next);
        state.MapActive = true;
        state.SavedMap = map;
        state.SavedMapIndex = previous;
    }

    private static int PatchUniqueId()
    {
        Type recipeType = Resolve("MilianModification.Recipe_MilianComponentResearch");
        MethodInfo target = recipeType == null
            ? null
            : AccessTools.Method(recipeType, "GenerateUniqueID", Type.EmptyTypes);
        if (target == null)
            return 0;
        try
        {
            Harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(MilianModification_WorldDeterminism),
                    nameof(GenerateUniqueIdPrefix))));
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static bool GenerateUniqueIdPrefix(ref string __result)
    {
        if (!MP.IsInMultiplayer)
            return true;

        object gc = GetGameComp();
        int count = 0;
        if (gc != null && _researchHistoryRecordField?.GetValue(gc) is IDictionary dict)
            count = dict.Count;

        __result = "MIL-" + GenTicks.TicksAbs + "-" +
                   Find.TickManager.TicksGame + "-" + count;
        return false;
    }

    private static int PatchButcherProducts()
    {
        MethodInfo target = AccessTools.Method(
            typeof(Corpse),
            "ButcherProducts",
            new[] { typeof(Pawn), typeof(float) });
        if (target == null)
            return 0;
        try
        {
            Harmony.Patch(
                target,
                postfix: new HarmonyMethod(AccessTools.Method(
                    typeof(MilianModification_WorldDeterminism),
                    nameof(ButcherProductsPostfix))));
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<Thing> ButcherProductsPostfix(IEnumerable<Thing> __result)
    {
        if (!MP.IsInMultiplayer || __result == null)
            return __result;
        return WrapButcherRand(__result);
    }

    private static IEnumerable<Thing> WrapButcherRand(IEnumerable<Thing> source)
    {
        Rand.PushState(Find.TickManager.TicksGame);
        try
        {
            foreach (Thing thing in source)
                yield return thing;
        }
        finally
        {
            Rand.PopState();
        }
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

    private static Type Resolve(string fullName)
    {
        return CompatUtility.ResolveTypeSilent(fullName);
    }
}
