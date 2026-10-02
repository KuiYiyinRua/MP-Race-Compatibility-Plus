using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

// These generators run inside beacon CompTick, before the pawn has a MapHeld.
// Rand isolation alone cannot equalize wealth/threat from a local viewing map.
[StaticConstructorOnStartup]
internal static class MilianGenerationMapContext
{
    private static FieldInfo tickingMap, executingMap;
    private static readonly MethodInfo CurrentMap = AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap));
    private static readonly MethodInfo HomeMap = AccessTools.PropertyGetter(typeof(Find), nameof(Find.AnyPlayerHomeMap));

    static MilianGenerationMapContext()
    {
        if (!MP.enabled || !CompatibilityPatchCategories.IsEnabled("milira") ||
            !ModsConfig.IsActive("Ancot.MiliraRace") || MiliraMpCompatGate.ReferenceModActive) return;
        LongEventHandler.ExecuteWhenFinished(Apply);
    }

    private static void Apply()
    {
        var harmony = new Harmony("mp.meowonlineshop.milian-generation-map");
        try
        {
            var asyncTime = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
            tickingMap = AccessTools.DeclaredField(asyncTime, "tickingMap");
            executingMap = AccessTools.DeclaredField(asyncTime, "executingCmdMap");
            if (tickingMap?.FieldType != typeof(Map) || executingMap?.FieldType != typeof(Map))
                throw new MissingFieldException("Multiplayer simulation map fields");
            Patch(harmony, "Milira.Milira_MilianPawnGenerator_Patch", 1, 1);
            if (ModsConfig.IsActive("Ancot.MilianModification"))
                Patch(harmony, "MilianModification.Milira_MilianPawnGenerator_Patch", 1, 0);
            if (ModsConfig.IsActive("sleepycot.wingsofdemocracy"))
                Patch(harmony, "PLAMilira.PLAMilira_MilianPawnGenerator_Patch", 2, 2);
            Log.Message("[MP-MeowOnlineShop][Desync342] READY Milian generation wealth/threat uses simulation map; deterministic home fallback.");
        }
        catch (Exception e)
        {
            harmony.UnpatchAll(harmony.Id);
            Log.Error("[MP-MeowOnlineShop][Desync342] REQUIRED_TARGET_FAILED generation map: " + e);
        }
    }

    private static void Patch(Harmony harmony, string typeName, int currentReads, int homeReads)
    {
        var type = AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName);
        var method = AccessTools.DeclaredMethod(type, "Postfix", new[] { typeof(Pawn).MakeByRefType(), typeof(PawnGenerationRequest) })
            ?? throw new MissingMethodException(typeName, "Postfix(ref Pawn, PawnGenerationRequest)");
        var body = PatchProcessor.GetOriginalInstructions(method);
        if (body.Count(i => i.Calls(CurrentMap)) != currentReads || body.Count(i => i.Calls(HomeMap)) != homeReads)
            throw new InvalidOperationException("Generation map read count changed: " + typeName);
        harmony.Patch(method, transpiler: new HarmonyMethod(typeof(MilianGenerationMapContext), nameof(ReplaceMapReads)));
    }

    private static IEnumerable<CodeInstruction> ReplaceMapReads(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(CurrentMap))
                instruction.operand = AccessTools.DeclaredMethod(typeof(MilianGenerationMapContext), nameof(GenerationMap));
            else if (instruction.Calls(HomeMap))
                instruction.operand = AccessTools.DeclaredMethod(typeof(MilianGenerationMapContext), nameof(GenerationHomeMap));
            yield return instruction;
        }
    }

    private static Map GenerationMap()
    {
        if (!MP.IsInMultiplayer) return Find.CurrentMap;
        return (Map)executingMap.GetValue(null) ?? (Map)tickingMap.GetValue(null) ?? StableHomeOrAnyMap();
    }

    private static Map GenerationHomeMap()
    {
        if (!MP.IsInMultiplayer) return Find.AnyPlayerHomeMap;
        return StableHomeOrAnyMap();
    }

    private static Map StableHomeOrAnyMap()
    {
        // World generation has no ticking map. Do not consult UI perspective.
        return Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID).FirstOrDefault()
            ?? Find.Maps.OrderBy(m => m.uniqueID).FirstOrDefault();
    }
}
