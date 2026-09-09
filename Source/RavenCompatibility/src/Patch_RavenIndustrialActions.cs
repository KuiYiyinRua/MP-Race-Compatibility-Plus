using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    // Candidate module: integration is gated until the complete Raven audit and runtime matrix pass.
    internal static class Patch_RavenIndustrialActions
    {
        private const string Liquid = "RavenRace.Features.RavenLiquidPipe.";
        private const string Conveyor = "RavenRace.Features.RavenConveyor.";
        private static bool applied;

        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || !ModsConfig.IsActive("ZuoYao.RavenRace")) return;
            applied = true;
            RavenLiquidNetworkState.Apply(harmony);
            RavenConveyorNetworkState.Apply(harmony);
            RavenConveyorPacketState.Apply(harmony);
            RavenReadOnlyQueries.Apply(harmony);
            RavenIndustrialFilters.Apply(harmony);
            RavenCentralResearch.Apply(harmony);
            RavenCentralAbilityState.Apply(harmony);
            RavenReinforcement.Apply(harmony);
            RavenCentralAbilityActions.Apply(harmony);
            RavenCentralStorageActions.Apply(harmony);
            RavenDroneMath.Apply(harmony);
            RavenTrashSettings.Apply(harmony);
            RavenHouseholdActions.Apply(harmony);
            RavenClocks.Apply(harmony); RavenIndustrialClocks.Apply(harmony);
            RavenHypnosisActions.Apply(harmony);
            RavenHypnosisClocks.Apply(harmony);
            RavenHubQueries.Apply(harmony);
            RavenCaravanActions.Apply(harmony);
            RavenConveyorDesignators.Apply(harmony); RavenConveyorConstruction.Apply(harmony);
            RavenDroneStationActions.Apply(harmony);
            RavenDroneReadOnlyQueries.Apply(harmony);
            RavenDroneOrder.Apply(harmony);
            RavenMountActions.Apply(harmony);
            RavenChezhouFlight.Apply(harmony);
            RavenSpecialPawnActions.Apply(harmony);
            RavenDefenseHubActions.Apply(harmony);
            RavenObeliskActions.Apply(harmony);
            RavenOrderedResources.Apply(harmony); RavenBlueprintActions.Apply(harmony); RavenUtilityActions.Apply(harmony); RavenSessionSettings.Apply(harmony); RavenIndustrialTelemetry.Apply(harmony); RavenLogisticsBoxActions.Apply(harmony); RavenAdditionalPawnActions.Apply(harmony); RavenAegisActions.Apply(harmony); RavenStylingActions.Apply(harmony); RavenStoryActions.Apply(harmony); RavenGiftActions.Apply(harmony); RavenOperatorRewardActions.Apply(harmony); RavenOffspringActions.Apply(harmony);
            var targets = new List<MethodInfo>();
            RavenRequestActions.Apply(harmony); RavenTerrainDesignators.Apply(); RavenConfessionActions.Apply(harmony); RavenFusangResourceActions.Apply(harmony);
            RavenSoulAltarActions.Apply(harmony);
            RavenAiScanPersistence.Apply(harmony);
            RavenRadioJobUi.Apply(harmony);
            RavenHotSpringState.Validate();
            RavenGlobalTuningActions.Apply(harmony);
            RavenBloodlineCleanup.Apply(harmony);
            Add(targets, "RavenRace.Features.RavenIndustrialMarker.CompRavenIndustrialMarker", "SelectThing", "Verse.ThingDef");
            Add(targets, "RavenRace.Features.RavenIndustrialMarker.CompRavenIndustrialMarker", "SelectLiquid", Liquid + "RavenLiquidDef");
            Add(targets, "RavenRace.Features.RavenIndustrialMarker.CompRavenIndustrialMarker", "ClearMarker");
            // MP's ThingComp worker preserves the ordinal of repeated comp types.
            Add(targets, "RavenRace.Compat.MuGirl.CompRavenMilkable", "GatherMilk", "Verse.Pawn");
            Add(targets, Liquid + "CompRavenLiquidPort", "SetLiquidFilter", Liquid + "RavenLiquidDef");
            Add(targets, Liquid + "CompRavenLiquidPipe", "SetConnectionBetween", Liquid + "CompRavenLiquidPipe", "System.Boolean");
            Add(targets, Liquid + "CompRavenLiquidPipe", "ClearManualDirectionBlocks");
            Add(targets, Liquid + "CompRavenLiquidPipe", "EmptyConnectedSegment");
            Add(targets, Liquid + "CompRavenLiquidStorage", "ClearAllLiquids");
            Add(targets, Liquid + "CompRavenFillingMachineRecipeProcessor", "SelectRecipe", "System.Int32");
            Add(targets, Liquid + "CompRavenFillingMachineRecipeProcessor", "CancelProduction", "System.Boolean");
            Add(targets, Liquid + "CompRavenFillingMachineRecipeProcessor", "EjectProducts", "System.Boolean");
            Add(targets, Liquid + "CompRavenFillingMachineRecipeProcessor", "SetProductionTarget", "System.Single");
            Add(targets, Liquid + "CompRavenFillingMachineRecipeProcessor", "ResetProductionProgress");
            Add(targets, Liquid + "CompRavenRecipePowerGenerator", "SelectRecipe", "System.Int32");
            Add(targets, Liquid + "CompRavenPlanter", "SelectPlant", "Verse.ThingDef");
            Add(targets, Conveyor + "CompRavenConveyor", "SetOutputDirection", "Verse.IntVec3");
            Add(targets, Conveyor + "CompRavenConveyor", "SetEnabled", "System.Boolean");
            Add(targets, Conveyor + "CompRavenConveyor", "SetSpeedPercent", "System.Int32", "System.Boolean");
            Add(targets, Conveyor + "CompRavenConveyorPort", "BindConveyor", Conveyor + "CompRavenConveyor");
            Add(targets, Conveyor + "CompRavenCuttingMachine", "SelectRecipe", "System.Int32");
            Add(targets, Conveyor + "CompRavenCuttingMachine", "EjectProducts", "System.Boolean");
            Add(targets, Conveyor + "CompRavenCuttingMachine", "SetProductionTarget", "System.Single");
            Add(targets, Conveyor + "CompRavenCuttingMachine", "ResetProductionProgress");
            Add(targets, Conveyor + "CompRavenConveyorMachineBuffer", "EjectAllBuffers");
            Add(targets, "RavenRace.Buildings.Comps.CompSunRaiserInjectorEffects", "ToggleFullLight");
            Add(targets, "RavenRace.Buildings.ComputerDesk.CompRavenComputerDesk", "ToggleMode");
            Add(targets, "RavenRace.Features.Bionics.RavenFluidAccelerator.HediffComp_FluidAccelerator", "DoPop");
            Add(targets, "RavenRace.Features.BedSharing.HediffComp_WingBed", "EjectContents", "System.String");
            Add(targets, "RavenRace.Features.MiscSmallFeatures.Devour.HediffComp_DevouredPawnHolder", "EjectContents");
            Add(targets, "RavenRace.Features.Servitude.ServitudeManager", "AddRelation", "Verse.Pawn", "Verse.Pawn");
            Add(targets, "RavenRace.Features.Servitude.ServitudeManager", "RemoveRelation", "Verse.Pawn");
            Add(targets, "RavenRace.Features.Servitude.ServitudeManager", "RemoveAllServants", "Verse.Pawn");
            Add(targets, "RavenRace.Features.UniqueEquipment.Sandevistan.CompApparelTrail", "Activate", "Verse.Pawn");
            Add(targets, "RavenRace.CompTrigger", "TryTrigger", "Verse.Pawn");
            Add(targets, "RavenRace.Building_AltarInfuser", "SetTarget", "RavenRace.SoulAltarUpgradeDef");
            Add(targets, "RavenRace.Features.Reproduction.HediffCompSpiritEggHolder", "LaunchEgg", "Verse.LocalTargetInfo");
            Add(targets, "RavenRace.Features.Reproduction.HediffCompSpiritEggHolder", "EjectEgg", "System.Boolean");
            Add(targets, "RavenRace.Buildings.Building_EmberExtractor", "CancelExtraction");
            Add(targets, "RavenRace.CompTrigger", "<CompGetGizmosExtra>b__27_2");
            Add(targets, "RavenRace.CompTrigger", "<CompGetGizmosExtra>b__27_3");
            Add(targets, "RavenRace.Building_AltarInfuser", "<GetGizmos>b__12_0");

            var beads = AccessTools.TypeByName("RavenRace.Features.UniqueWeapons.SpiritBeads.CompSpiritBeads");
            var closure = beads?.GetNestedType("<>c__DisplayClass16_0", BindingFlags.NonPublic);
            if (closure == null || AccessTools.DeclaredField(closure, "<>4__this") == null ||
                AccessTools.DeclaredMethod(closure, "<GetEquippedGizmos>b__1", Type.EmptyTypes) == null)
                throw new MissingMemberException("Required Raven spirit beads auto-insert closure");

            // Resolve the entire manifest before registering anything. A version mismatch must
            // be visible, rather than silently binding a similarly named overload.
            foreach (var method in targets)
                MP.RegisterSyncMethod(method, null);
            MP.RegisterSyncDelegate(beads, "<>c__DisplayClass16_0", "<GetEquippedGizmos>b__1", new[] { "<>4__this" });

            // The wrench batches local edits with notifyTopology=false and dirties the map
            // after checking the bool result. That result is unavailable while queuing a
            // sync command. Force invalidation inside each replayed speed command instead.
            harmony.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName(Conveyor + "CompRavenConveyor"),
                "SetSpeedPercent", new[] { typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Patch_RavenIndustrialActions), nameof(ReplaySpeed)));
            Log.Message("[MP-MeowOnlineShop] Raven action targets: " + targets.Count + "/42 + 1/1 delegate; candidate coverage only.");
        }

        private static void ReplaySpeed(ref bool __1)
        {
            if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand) __1 = true;
        }

        private static void Add(List<MethodInfo> targets, string typeName, string name, params string[] argumentNames)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) throw new MissingMemberException("Required Raven type: " + typeName);
            var arguments = new Type[argumentNames.Length];
            for (int i = 0; i < arguments.Length; i++)
                arguments[i] = AccessTools.TypeByName(argumentNames[i])
                    ?? throw new MissingMemberException("Required Raven argument: " + argumentNames[i]);
            var method = AccessTools.DeclaredMethod(type, name, arguments);
            if (method == null) throw new MissingMethodException(typeName, name);
            targets.Add(method);
        }
    }
}



