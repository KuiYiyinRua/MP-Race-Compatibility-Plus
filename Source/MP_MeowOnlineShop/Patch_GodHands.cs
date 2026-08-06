using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Shared resolution and sync command layer for PA's God Hands.
    /// All target types are resolved through reflection so the patch never
    /// needs a compile-time reference to the God Hands assembly.
    /// </summary>
    internal static class Patch_GodHands
    {
        public const string TargetPackageId = "Palpha.godhands";

        public static Type HeadType;
        public static Type SlotType;
        public static Type CommandTargetType;
        public static Type ControllerType;
        public static Type PawnHandlerType;
        public static Type ItemHandlerType;
        public static Type WeaponHandlerType;
        public static Type WrenchControllerType;
        public static Type WrenchBulkHandlerType;
        public static Type WrenchTurretHandlerType;
        public static Type WrenchScoopType;
        public static Type ProtectionControllerType;
        public static Type AssistantControllerType;
        public static Type JudgmentControllerType;
        public static Type CleaningControllerType;
        public static Type PokeControllerType;
        public static Type MagnifierControllerType;
        public static Type AssistantMapComponentType;
        public static Type CraftingTaskType;
        public static Type MedicalPawnTaskType;
        public static Type MedicalBedTaskType;
        public static Type DesignatorGodHandType;
        public static Type DesignatorGodWrenchType;
        public static Type DesignatorProtectionType;
        public static Type DesignatorAssistantType;
        public static Type DesignatorCleaningType;
        public static Type DesignatorJudgmentType;
        public static Type DesignatorPokeType;
        public static Type PrecisionDialogType;
        public static Type GraphicTransformManagerType;
        public static Type TransformDataType;
        public static Type SettingsType;
        public static FieldInfo PawnTweenedPosField;
        public static FieldInfo PawnLastTickSpringPosField;

        private static Type _multiplayerClientType;
        private static FieldInfo _mpSessionField;
        private static FieldInfo _sessionPlayerIdField;

        public static FieldInfo HeadTurretSlotsField;
        public static FieldInfo HeadHoldFireField;
        public static FieldInfo SlotStoredModulesField;
        public static FieldInfo SlotCurrentTargetField;
        public static FieldInfo SlotForcedTargetField;
        public static FieldInfo SlotHoldFireField;
        public static FieldInfo SlotSweepModeField;
        public static FieldInfo SlotLeadTargetingField;
        public static FieldInfo SlotSmartRetargetField;
        public static FieldInfo SlotAirDefenseModeField;
        public static FieldInfo SlotIsAirDefenseTurretField;
        public static FieldInfo SlotFuelSystemEnabledField;
        public static FieldInfo SlotFuelField;
        public static FieldInfo SlotFuelCapacityField;
        public static FieldInfo SlotFuelThingDefField;
        public static FieldInfo SlotShellSystemEnabledField;
        public static FieldInfo SlotShellCapacityField;
        public static FieldInfo SlotLoadedShellDefField;
        public static FieldInfo SlotLoadedShellCountField;
        public static FieldInfo SlotActivationRequiredField;
        public static FieldInfo SlotIsActivatedField;
        public static FieldInfo ControllerIsActiveField;
        public static FieldInfo ControllerGrabbedPawnsField;
        public static FieldInfo ControllerGrabbedThingsField;
        public static FieldInfo ControllerGrabStartCellField;
        public static FieldInfo ControllerGrabOffsetsField;
        public static FieldInfo ControllerPawnHandlerField;
        public static FieldInfo ControllerItemHandlerField;
        public static FieldInfo ControllerWeaponHandlerField;
        public static FieldInfo WeaponShootingModeField;
        public static FieldInfo WeaponMeleeModeField;
        public static FieldInfo WeaponFixedWeaponPosField;
        public static FieldInfo WeaponWaitForReleaseField;
        public static FieldInfo WeaponHitListField;
        public static FieldInfo WeaponCoreField;
        public static FieldInfo WrenchGrabbedThingField;
        public static FieldInfo WrenchGrabStartField;
        public static FieldInfo WrenchOriginalPositionField;
        public static FieldInfo WrenchOriginalTerrainField;
        public static FieldInfo WrenchCurrentMapField;
        public static FieldInfo WrenchIsActiveField;
        public static FieldInfo WrenchBulkHandlerField;
        public static FieldInfo BulkStartCellField;
        public static FieldInfo BulkIsScoopedField;
        public static FieldInfo BulkCurrentTypeField;
        public static FieldInfo BulkScoopedMapField;
        public static FieldInfo BulkScoopedThingsField;
        public static FieldInfo BulkScoopedRoofsField;
        public static FieldInfo BulkScoopedResourcesField;
        public static FieldInfo BulkScoopedResourceCountsField;
        public static FieldInfo BulkEntryThingField;
        public static FieldInfo PokeTargetPawnField;
        public static FieldInfo PokeTargetCorpseField;
        public static FieldInfo PokeStartCellField;
        public static FieldInfo PokeIsInteractingField;
        public static FieldInfo MagnifierTargetPawnField;
        public static FieldInfo MagnifierIsActiveField;
        public static FieldInfo MagnifierVisualScaleModeField;
        public static FieldInfo PrecisionTargetThingField;
        public static FieldInfo PrecisionCurrentComponentField;
        public static FieldInfo PrecisionTransformField;
        public static FieldInfo PrecisionOffsetField;
        public static FieldInfo PrecisionRotationField;
        public static FieldInfo PrecisionScaleField;
        public static FieldInfo PrecisionDrawLayerField;
        public static FieldInfo PrecisionOverrideRotField;
        public static PropertyInfo ControllerIsRangeGrabProperty;
        public static PropertyInfo WrenchIsActiveProperty;
        public static PropertyInfo BulkIsActiveProperty;
        public static PropertyInfo SettingsInstanceProperty;
        public static PropertyInfo CleaningCurrentModeProperty;
        public static PropertyInfo CleaningCleanRadiusProperty;
        public static MethodInfo HeadTryOrderRefuelMethod;
        public static MethodInfo ControllerTryStartGrabRealMethod;
        public static MethodInfo ControllerTryStartGrabItemRealMethod;
        public static MethodInfo ControllerForceReleaseGrabMethod;
        public static MethodInfo WrenchReleaseGrabMethod;
        public static MethodInfo WrenchForceReleaseGrabMethod;
        public static MethodInfo WrenchStartBulkScoopMethod;
        public static MethodInfo WrenchTurretHandlerTrySnatchMethod;
        public static MethodInfo WrenchTurretHandlerTryRemoveFromPawnMethod;
        public static MethodInfo ProtectionExecuteMethod;
        public static MethodInfo ProtectionApplyIndividualMethod;
        public static MethodInfo JudgmentExecuteMethod;
        public static MethodInfo CleaningCleanAreaMethod;
        public static MethodInfo PokeEndInteractionMethod;
        public static MethodInfo MagnifierToggleScaleModeMethod;
        public static MethodInfo MagnifierIncreaseScaleMethod;
        public static MethodInfo MagnifierDecreaseScaleMethod;
        public static MethodInfo TransformManagerSetMethod;
        public static MethodInfo TransformManagerGetOrCreateByKeyMethod;
        public static MethodInfo GrabbingUtilsGetPawnsMethod;
        public static MethodInfo GrabbingUtilsGetItemsMethod;
        public static MethodInfo GrabbingUtilsGetPawnAtMethod;
        public static MethodInfo GrabbingUtilsGetDirectionMethod;

        public static bool Resolved;
        public static bool TargetFound;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(TargetPackageId))
                return;

            Resolve();
            if (!TargetFound)
            {
                Log.Warning("[MP-MeowOnlineShop] God Hands patch skipped: target mod types were not resolved.");
                return;
            }

            GodHandSync.Register();
            Patch_GodHandsTurretCommands.Apply(harmony);
            Patch_GodHandsDesignators.Apply(harmony);
            Patch_GodHandsControllers.Apply(harmony);
            Patch_GodHandsSessionLifecycle.Apply(harmony);

            Log.Message(
                "[MP-MeowOnlineShop] God Hands multiplayer compatibility active: " +
                $"target={TargetPackageId}, syncMethods={GodHandSync.RegisteredCount}, " +
                "turret/designator/controller layers applied.");
        }

        public static void Resolve()
        {
            if (Resolved)
                return;
            Resolved = true;

            HeadType = AccessTools.TypeByName("GodHandMod.GodHandTurretHead");
            SlotType = AccessTools.TypeByName("GodHandMod.TurretSlot");
            CommandTargetType = AccessTools.TypeByName("GodHandMod.Command_TurretTarget");
            ControllerType = AccessTools.TypeByName("GodHandMod.GodHandController");
            PawnHandlerType = AccessTools.TypeByName("GodHandMod.GodHandPawnHandler");
            ItemHandlerType = AccessTools.TypeByName("GodHandMod.GodHandItemHandler");
            WeaponHandlerType = AccessTools.TypeByName("GodHandMod.GodHandWeaponHandler");
            WrenchControllerType = AccessTools.TypeByName("GodHandMod.GodWrenchController");
            WrenchBulkHandlerType = AccessTools.TypeByName("GodHandMod.GodWrenchBulkHandler");
            WrenchTurretHandlerType = AccessTools.TypeByName("GodHandMod.GodWrenchTurretHandler");
            WrenchScoopType = WrenchBulkHandlerType?.GetNestedType("ScoopType", BindingFlags.Public);
            ProtectionControllerType = AccessTools.TypeByName("GodHandMod.ProtectionGodHandController");
            AssistantControllerType = AccessTools.TypeByName("GodHandMod.GodAssistantController");
            JudgmentControllerType = AccessTools.TypeByName("GodHandMod.JudgmentGodHandController");
            CleaningControllerType = AccessTools.TypeByName("GodHandMod.CleaningGodHandController");
            PokeControllerType = AccessTools.TypeByName("GodHandMod.PokeGodHandController");
            MagnifierControllerType = AccessTools.TypeByName("GodHandMod.MagnifierGodHandController");
            AssistantMapComponentType = AccessTools.TypeByName("GodHandMod.MapComponent_GodAssistant");
            CraftingTaskType = AccessTools.TypeByName("GodHandMod.GodHandCraftingTask");
            MedicalPawnTaskType = AccessTools.TypeByName("GodHandMod.GodHandMedicalTask_Pawn");
            MedicalBedTaskType = AccessTools.TypeByName("GodHandMod.GodHandMedicalTask_Bed");
            DesignatorGodHandType = AccessTools.TypeByName("GodHandMod.Designator_GodHand");
            DesignatorGodWrenchType = AccessTools.TypeByName("GodHandMod.Designator_GodWrench");
            DesignatorProtectionType = AccessTools.TypeByName("GodHandMod.Designator_ProtectionGodHand");
            DesignatorAssistantType = AccessTools.TypeByName("GodHandMod.Designator_GodAssistant");
            DesignatorCleaningType = AccessTools.TypeByName("GodHandMod.Designator_CleaningGodHand");
            DesignatorJudgmentType = AccessTools.TypeByName("GodHandMod.Designator_JudgmentGodHand");
            DesignatorPokeType = AccessTools.TypeByName("GodHandMod.Designator_PokeGodHand");
            PrecisionDialogType = AccessTools.TypeByName("GodHandMod.Dialog_PrecisionAdjust");
            GraphicTransformManagerType = AccessTools.TypeByName("GodHandMod.GraphicTransformManager");
            TransformDataType = GraphicTransformManagerType?.GetNestedType("TransformData", BindingFlags.Public);
            SettingsType = AccessTools.TypeByName("GodHandMod.GodHandSettings");
            PawnTweenedPosField = AccessTools.Field(typeof(PawnTweener), "tweenedPos");
            PawnLastTickSpringPosField = AccessTools.Field(typeof(PawnTweener), "lastTickSpringPos");

            TargetFound = HeadType != null && SlotType != null && ControllerType != null &&
                          WrenchControllerType != null && DesignatorGodHandType != null &&
                          DesignatorGodWrenchType != null;
            if (!TargetFound)
                return;

            HeadTurretSlotsField = AccessTools.Field(HeadType, "turretSlots");
            HeadHoldFireField = AccessTools.Field(HeadType, "holdFire");
            SlotStoredModulesField = AccessTools.Field(SlotType, "storedModules");
            SlotCurrentTargetField = AccessTools.Field(SlotType, "currentTarget");
            SlotForcedTargetField = AccessTools.Field(SlotType, "forcedTarget");
            SlotHoldFireField = AccessTools.Field(SlotType, "holdFire");
            SlotSweepModeField = AccessTools.Field(SlotType, "sweepMode");
            SlotLeadTargetingField = AccessTools.Field(SlotType, "leadTargeting");
            SlotSmartRetargetField = AccessTools.Field(SlotType, "smartRetarget");
            SlotAirDefenseModeField = AccessTools.Field(SlotType, "airDefenseMode");
            SlotIsAirDefenseTurretField = AccessTools.Field(SlotType, "isAirDefenseTurret");
            SlotFuelSystemEnabledField = AccessTools.Field(SlotType, "fuelSystemEnabled");
            SlotFuelField = AccessTools.Field(SlotType, "fuel");
            SlotFuelCapacityField = AccessTools.Field(SlotType, "fuelCapacity");
            SlotFuelThingDefField = AccessTools.Field(SlotType, "fuelThingDef");
            SlotShellSystemEnabledField = AccessTools.Field(SlotType, "shellSystemEnabled");
            SlotShellCapacityField = AccessTools.Field(SlotType, "shellCapacity");
            SlotLoadedShellDefField = AccessTools.Field(SlotType, "loadedShellDef");
            SlotLoadedShellCountField = AccessTools.Field(SlotType, "loadedShellCount");
            SlotActivationRequiredField = AccessTools.Field(SlotType, "activationRequired");
            SlotIsActivatedField = AccessTools.Field(SlotType, "isActivated");

            ControllerIsActiveField = AccessTools.Field(ControllerType, "isActive");
            ControllerGrabbedPawnsField = AccessTools.Field(ControllerType, "grabbedPawns");
            ControllerGrabbedThingsField = AccessTools.Field(ControllerType, "grabbedThings");
            ControllerGrabStartCellField = AccessTools.Field(ControllerType, "grabStartCell");
            ControllerGrabOffsetsField = AccessTools.Field(ControllerType, "grabOffsets");
            ControllerPawnHandlerField = AccessTools.Field(ControllerType, "pawnHandler");
            ControllerItemHandlerField = AccessTools.Field(ControllerType, "itemHandler");
            ControllerWeaponHandlerField = AccessTools.Field(ControllerType, "weaponHandler");
            ControllerIsRangeGrabProperty = AccessTools.Property(ControllerType, "IsRangeGrab");

            WeaponShootingModeField = AccessTools.Field(WeaponHandlerType, "shootingMode");
            WeaponMeleeModeField = AccessTools.Field(WeaponHandlerType, "meleeMode");
            WeaponFixedWeaponPosField = AccessTools.Field(WeaponHandlerType, "fixedWeaponPos");
            WeaponWaitForReleaseField = AccessTools.Field(WeaponHandlerType, "waitForRelease");
            WeaponHitListField = AccessTools.Field(WeaponHandlerType, "hitList");
            WeaponCoreField = AccessTools.Field(WeaponHandlerType, "core");

            WrenchGrabbedThingField = AccessTools.Field(WrenchControllerType, "grabbedThing");
            WrenchGrabStartField = AccessTools.Field(WrenchControllerType, "grabStart");
            WrenchOriginalPositionField = AccessTools.Field(WrenchControllerType, "originalPosition");
            WrenchOriginalTerrainField = AccessTools.Field(WrenchControllerType, "originalTerrain");
            WrenchCurrentMapField = AccessTools.Field(WrenchControllerType, "currentMap");
            WrenchIsActiveField = AccessTools.Field(WrenchControllerType, "isActive");
            WrenchBulkHandlerField = AccessTools.Field(WrenchControllerType, "bulkHandler");
            WrenchIsActiveProperty = AccessTools.Property(WrenchControllerType, "IsActive");

            BulkStartCellField = AccessTools.Field(WrenchBulkHandlerType, "startCell");
            BulkIsScoopedField = AccessTools.Field(WrenchBulkHandlerType, "isScooped");
            BulkCurrentTypeField = AccessTools.Field(WrenchBulkHandlerType, "currentType");
            BulkScoopedMapField = AccessTools.Field(WrenchBulkHandlerType, "scoopedMap");
            BulkScoopedThingsField = AccessTools.Field(WrenchBulkHandlerType, "scoopedThings");
            BulkScoopedRoofsField = AccessTools.Field(WrenchBulkHandlerType, "scoopedRoofs");
            BulkScoopedResourcesField = AccessTools.Field(WrenchBulkHandlerType, "scoopedResources");
            BulkScoopedResourceCountsField = AccessTools.Field(WrenchBulkHandlerType, "scoopedResourceCounts");
            BulkIsActiveProperty = AccessTools.Property(WrenchBulkHandlerType, "IsActive");

            Type bulkEntryType = WrenchBulkHandlerType?.GetNestedType("ScoopedThingEntry", BindingFlags.NonPublic);
            BulkEntryThingField = bulkEntryType == null ? null : AccessTools.Field(bulkEntryType, "thing");

            PokeTargetPawnField = AccessTools.Field(PokeControllerType, "targetPawn");
            PokeTargetCorpseField = AccessTools.Field(PokeControllerType, "targetCorpse");
            PokeStartCellField = AccessTools.Field(PokeControllerType, "startCell");
            PokeIsInteractingField = AccessTools.Field(PokeControllerType, "isInteracting");

            MagnifierTargetPawnField = AccessTools.Field(MagnifierControllerType, "targetPawn");
            MagnifierIsActiveField = AccessTools.Field(MagnifierControllerType, "isActive");
            MagnifierVisualScaleModeField = AccessTools.Field(MagnifierControllerType, "visualScaleMode");

            PrecisionTargetThingField = AccessTools.Field(PrecisionDialogType, "targetThing");
            PrecisionCurrentComponentField = AccessTools.Field(PrecisionDialogType, "currentComponent");
            PrecisionTransformField = AccessTools.Field(PrecisionDialogType, "transform");
            PrecisionOffsetField = AccessTools.Field(PrecisionDialogType, "offset");
            PrecisionRotationField = AccessTools.Field(PrecisionDialogType, "rotation");
            PrecisionScaleField = AccessTools.Field(PrecisionDialogType, "scale");
            PrecisionDrawLayerField = AccessTools.Field(PrecisionDialogType, "drawLayer");
            PrecisionOverrideRotField = AccessTools.Field(PrecisionDialogType, "overrideRot");

            Type mainModType = AccessTools.TypeByName("GodHandMod.GodHandModMain");
            SettingsInstanceProperty = mainModType == null ? null : AccessTools.Property(mainModType, "Settings");
            CleaningCurrentModeProperty = AccessTools.Property(CleaningControllerType, "CurrentMode");
            CleaningCleanRadiusProperty = AccessTools.Property(CleaningControllerType, "CleanRadius");

            HeadTryOrderRefuelMethod = AccessTools.Method(HeadType, "TryOrderRefuel");
            ControllerTryStartGrabRealMethod = AccessTools.Method(ControllerType, "TryStartGrabReal");
            ControllerTryStartGrabItemRealMethod = AccessTools.Method(ControllerType, "TryStartGrabItemReal");
            ControllerForceReleaseGrabMethod = AccessTools.Method(ControllerType, "ForceReleaseGrab");
            WrenchReleaseGrabMethod = AccessTools.Method(WrenchControllerType, "ReleaseGrab");
            WrenchForceReleaseGrabMethod = AccessTools.Method(WrenchControllerType, "ForceReleaseGrab");
            WrenchStartBulkScoopMethod = AccessTools.Method(WrenchControllerType, "StartBulkScoop");
            WrenchTurretHandlerTrySnatchMethod = AccessTools.Method(WrenchTurretHandlerType, "TrySnatch");
            WrenchTurretHandlerTryRemoveFromPawnMethod = AccessTools.Method(WrenchTurretHandlerType, "TryRemoveFromPawn");
            ProtectionExecuteMethod = AccessTools.Method(ProtectionControllerType, "Execute");
            ProtectionApplyIndividualMethod = AccessTools.Method(ProtectionControllerType, "ApplyIndividual");
            JudgmentExecuteMethod = AccessTools.Method(JudgmentControllerType, "ExecuteJudgment");
            CleaningCleanAreaMethod = AccessTools.Method(CleaningControllerType, "CleanAreaAtPosition");
            PokeEndInteractionMethod = AccessTools.Method(PokeControllerType, "EndInteraction");
            MagnifierToggleScaleModeMethod = AccessTools.Method(MagnifierControllerType, "ToggleScaleMode");
            MagnifierIncreaseScaleMethod = AccessTools.Method(MagnifierControllerType, "IncreaseScale");
            MagnifierDecreaseScaleMethod = AccessTools.Method(MagnifierControllerType, "DecreaseScale");
            TransformManagerSetMethod = AccessTools.Method(GraphicTransformManagerType, "Set");
            TransformManagerGetOrCreateByKeyMethod = AccessTools.Method(GraphicTransformManagerType, "GetOrCreateByKey");

            Type grabbingUtilsType = AccessTools.TypeByName("GodHandMod.GrabbingUtils");
            GrabbingUtilsGetPawnsMethod = AccessTools.Method(grabbingUtilsType, "GetValidPawnsInRadius");
            GrabbingUtilsGetItemsMethod = AccessTools.Method(grabbingUtilsType, "GetValidItemsInRadius");
            GrabbingUtilsGetPawnAtMethod = AccessTools.Method(grabbingUtilsType, "GetPawnAt");
            GrabbingUtilsGetDirectionMethod = AccessTools.Method(grabbingUtilsType, "GetDirection");
        }

        public static Map FindMap(int mapIndex)
        {
            if (Find.Maps == null)
                return null;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map != null && map.uniqueID == mapIndex)
                    return map;
            }
            return null;
        }

        public static Thing FindThingById(Map map, int thingId)
        {
            if (map?.listerThings?.AllThings == null || thingId == 0)
                return null;
            var all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].thingIDNumber == thingId)
                    return all[i];
            }
            return null;
        }

        public static Pawn FindPawnById(Map map, int pawnId)
        {
            if (map == null || pawnId == 0)
                return null;
            if (map.mapPawns?.AllPawnsSpawned != null)
            {
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p != null && p.thingIDNumber == pawnId)
                        return p;
                }
            }
            return FindThingById(map, pawnId) as Pawn;
        }

        public static object GetHeadSlot(object head, int slotIndex)
        {
            if (head == null || HeadTurretSlotsField == null || slotIndex < 0)
                return null;
            var slots = HeadTurretSlotsField.GetValue(head) as IList;
            return slots != null && slotIndex < slots.Count ? slots[slotIndex] : null;
        }

        public static object GetSettings()
        {
            try
            {
                return SettingsInstanceProperty?.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        public static void SetSettingsValue(string fieldName, object value)
        {
            try
            {
                object settings = GetSettings();
                if (settings == null || SettingsType == null)
                    return;
                FieldInfo field = AccessTools.Field(SettingsType, fieldName);
                field?.SetValue(settings, value);
            }
            catch
            {
            }
        }

        public static T GetSettingsValue<T>(string fieldName, T fallback)
        {
            try
            {
                object settings = GetSettings();
                if (settings == null || SettingsType == null)
                    return fallback;
                FieldInfo field = AccessTools.Field(SettingsType, fieldName);
                return field == null ? fallback : (T)field.GetValue(settings);
            }
            catch
            {
                return fallback;
            }
        }

        public static bool PausedGodHandsBlocked()
        {
            if (!MP.IsInMultiplayer || Find.TickManager == null || !Find.TickManager.Paused)
                return false;
            return MpMeowOnlineShopMod.Settings?.blockGodHandsWhilePaused ?? false;
        }

        public static object GetMapComponent(Map map, Type componentType)
        {
            if (map?.components == null || componentType == null)
                return null;
            foreach (MapComponent component in map.components)
            {
                if (component != null && componentType.IsInstanceOfType(component))
                    return component;
            }
            return null;
        }

        public static object InvokeStatic(MethodInfo method, params object[] args)
        {
            try
            {
                return method?.Invoke(null, args);
            }
            catch
            {
                return null;
            }
        }

        public static object Invoke(object instance, MethodInfo method, params object[] args)
        {
            try
            {
                return method?.Invoke(instance, args);
            }
            catch
            {
                return null;
            }
        }

        public static object GetField(object instance, FieldInfo field)
        {
            try
            {
                return field?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        public static void SetField(object instance, FieldInfo field, object value)
        {
            try
            {
                field?.SetValue(instance, value);
            }
            catch
            {
            }
        }

        public static object GetProperty(object instance, PropertyInfo property)
        {
            try
            {
                return property?.GetValue(instance, null);
            }
            catch
            {
                return null;
            }
        }

        public static void SetProperty(object instance, PropertyInfo property, object value)
        {
            try
            {
                property?.SetValue(instance, value, null);
            }
            catch
            {
            }
        }

        public static bool IsGodHandClosure(Delegate d)
        {
            if (d?.Method == null || d.Method.DeclaringType == null || HeadType == null)
                return false;
            Type declaring = d.Method.DeclaringType;
            while (declaring != null)
            {
                if (declaring == HeadType)
                    return true;
                declaring = declaring.DeclaringType;
            }
            return false;
        }

        public static Rot4 GetExitDirection(Map map, IntVec3 target)
        {
            IntVec3 center = map.Center;
            int dx = target.x - center.x;
            int dz = target.z - center.z;
            return Mathf.Abs(dx) > Mathf.Abs(dz)
                ? (dx > 0 ? Rot4.East : Rot4.West)
                : (dz > 0 ? Rot4.North : Rot4.South);
        }

        public static int GetLocalPlayerId()
        {
            if (!MP.IsInMultiplayer)
                return -1;
            try
            {
                if (_multiplayerClientType == null)
                    _multiplayerClientType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
                if (_mpSessionField == null)
                    _mpSessionField = _multiplayerClientType == null ? null : AccessTools.Field(_multiplayerClientType, "session");
                object session = _mpSessionField?.GetValue(null);
                if (_sessionPlayerIdField == null)
                    _sessionPlayerIdField = session == null ? null : AccessTools.Field(session.GetType(), "playerId");
                return _sessionPlayerIdField?.GetValue(session) is int playerId ? playerId : -1;
            }
            catch
            {
                return -1;
            }
        }
    }

    /// <summary>
    /// Registered sync methods and per-map drag sessions used by God Hands
    /// compatibility. Commands mutate simulation only through these methods.
    /// </summary>
    internal static class GodHandSync
    {
        private sealed class GodHandDragSession
        {
            public int MapIndex;
            public int PlayerId;
            public IntVec3 StartCell;
            public bool RangeMode;
            public bool IsPawnGrab;
            public Dictionary<int, Pawn> Pawns = new Dictionary<int, Pawn>();
            public Dictionary<int, Thing> Things = new Dictionary<int, Thing>();
            public Dictionary<int, Vector3> Offsets = new Dictionary<int, Vector3>();
            public int WeaponThingId;
            public Vector3 FixedWeaponPos;
            public int LastDragTick = -999;
            public IntVec3 LastDragCell;
            public bool WaitForRelease;
            public int BurstLeft;
            public int NextShotTick;
            public int CooldownUntilTick;
            public LocalTargetInfo BurstTarget;
        }

        private sealed class GodWrenchSession
        {
            public int MapIndex;
            public int PlayerId;
            public bool Bulk;
            public object BulkController;
            public int ThingId;
            public IntVec3 StartCell;
            public IntVec3 OriginalPosition;
            public TerrainDef OriginalTerrain;
            public bool RoofDrag;
            public bool DeepDrag;
            public RoofDef RoofDef;
            public ThingDef DeepDef;
            public int DeepCount;
            public IntVec3 DragStartCell;
            public int LastDragTick = -999;
        }

        private readonly struct SessionKey : IEquatable<SessionKey>
        {
            public readonly int MapIndex;
            public readonly int PlayerId;

            public SessionKey(int mapIndex, int playerId)
            {
                MapIndex = mapIndex;
                PlayerId = playerId;
            }

            public bool Equals(SessionKey other) => MapIndex == other.MapIndex && PlayerId == other.PlayerId;

            public override bool Equals(object obj) => obj is SessionKey other && Equals(other);

            public override int GetHashCode() => unchecked((MapIndex * 397) ^ PlayerId);
        }

        // Session keys use map.uniqueID (not Map.Index) so a command issued
        // while one peer iterates Find.Maps in a different order still targets
        // the same map on every peer during async replay.
        private static readonly Dictionary<SessionKey, GodHandDragSession> GodHandSessions = new Dictionary<SessionKey, GodHandDragSession>();
        private static readonly Dictionary<SessionKey, GodWrenchSession> WrenchSessions = new Dictionary<SessionKey, GodWrenchSession>();
        private static bool _registered;
        private static int _registeredCount;

        public static int RegisteredCount => _registeredCount;

        public static void RemovePlayerSessions(int playerId)
        {
            if (playerId < 0)
                return;

            List<SessionKey> godKeys = null;
            foreach (SessionKey key in GodHandSessions.Keys)
            {
                if (key.PlayerId == playerId)
                {
                    (godKeys ??= new List<SessionKey>()).Add(key);
                }
            }
            if (godKeys != null)
            {
                for (int i = 0; i < godKeys.Count; i++)
                    GodHandSessions.Remove(godKeys[i]);
            }

            List<SessionKey> wrenchKeys = null;
            foreach (SessionKey key in WrenchSessions.Keys)
            {
                if (key.PlayerId == playerId)
                {
                    (wrenchKeys ??= new List<SessionKey>()).Add(key);
                }
            }
            if (wrenchKeys != null)
            {
                for (int i = 0; i < wrenchKeys.Count; i++)
                    WrenchSessions.Remove(wrenchKeys[i]);
            }
        }

        public static void ResetAllSessions()
        {
            GodHandSessions.Clear();
            WrenchSessions.Clear();
        }

        public static void Register()
        {
            if (_registered || !MP.enabled)
                return;
            _registered = true;

            RegisterMethod(nameof(SyncTurretHoldFire));
            RegisterMethod(nameof(SyncTurretSweep));
            RegisterMethod(nameof(SyncTurretLead));
            RegisterMethod(nameof(SyncTurretSmartRetarget));
            RegisterMethod(nameof(SyncTurretAirDefense));
            RegisterMethod(nameof(SyncTurretTarget));
            RegisterMethod(nameof(SyncTurretClearForced));
            RegisterMethod(nameof(SyncTurretActivate));
            RegisterMethod(nameof(SyncTurretRefuelOrder));
            RegisterMethod(nameof(SyncTurretLoadShell));
            RegisterMethod(nameof(SyncTurretModuleAdd));
            RegisterMethod(nameof(SyncTurretModuleRemove));
            RegisterMethod(nameof(SyncGodHandStartGrab));
            RegisterMethod(nameof(SyncGodHandDrag));
            RegisterMethod(nameof(SyncGodHandRelease));
            RegisterMethod(nameof(SyncGodHandForceRelease));
            RegisterMethod(nameof(SyncGodHandToggleShooting));
            RegisterMethod(nameof(SyncGodHandToggleMelee));
            RegisterMethod(nameof(SyncGodHandShoot));
            RegisterMethod(nameof(SyncGodHandResetMouseRelease));
            RegisterMethod(nameof(SyncGodHandMeleeHit));
            RegisterMethod(nameof(SyncWrenchStartGrab));
            RegisterMethod(nameof(SyncWrenchDrag));
            RegisterMethod(nameof(SyncWrenchRelease));
            RegisterMethod(nameof(SyncWrenchForceRelease));
            RegisterMethod(nameof(SyncWrenchStartBulk));
            RegisterMethod(nameof(SyncWrenchRoofStart));
            RegisterMethod(nameof(SyncWrenchRoofRelease));
            RegisterMethod(nameof(SyncWrenchDeepStart));
            RegisterMethod(nameof(SyncWrenchDeepRelease));
            RegisterMethod(nameof(SyncWrenchSnatchTurret));
            RegisterMethod(nameof(SyncWrenchRemoveFromPawn));
            RegisterMethod(nameof(SyncProtectionExecute));
            RegisterMethod(nameof(SyncProtectionIndividual));
            RegisterMethod(nameof(SyncAssistantExecute));
            RegisterMethod(nameof(SyncJudgmentExecute));
            RegisterMethod(nameof(SyncCleanArea));
            RegisterMethod(nameof(SyncPokeEnd));
            RegisterMethod(nameof(SyncMagnifier));
            RegisterMethod(nameof(SyncPrecisionTransform));
            RegisterMethod(nameof(SyncPrecisionTurretRotation));
        }

        private static void RegisterMethod(string name)
        {
            try
            {
                MP.RegisterSyncMethod(typeof(GodHandSync), name)
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _registeredCount++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] God Hands sync registration failed for {name}: {e.Message}");
            }
        }

        public static void SyncTurretHoldFire(int mapIndex, int headThingId, bool value)
        {
            object head = Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), headThingId);
            if (head == null)
                return;
            Patch_GodHands.SetField(head, Patch_GodHands.HeadHoldFireField, value);
            var slots = Patch_GodHands.HeadTurretSlotsField.GetValue(head) as IList;
            if (slots == null)
                return;
            for (int i = 0; i < slots.Count; i++)
            {
                object slot = slots[i];
                Patch_GodHands.SetField(slot, Patch_GodHands.SlotHoldFireField, value);
                if (value)
                    Patch_GodHands.SetField(slot, Patch_GodHands.SlotCurrentTargetField, LocalTargetInfo.Invalid);
            }
        }

        public static void SyncTurretSweep(int mapIndex, int headThingId, bool value)
        {
            SetAllSlotsBool(mapIndex, headThingId, Patch_GodHands.SlotSweepModeField, value);
        }

        public static void SyncTurretLead(int mapIndex, int headThingId, bool value)
        {
            SetAllSlotsBool(mapIndex, headThingId, Patch_GodHands.SlotLeadTargetingField, value);
        }

        public static void SyncTurretSmartRetarget(int mapIndex, int headThingId, bool value)
        {
            SetAllSlotsBool(mapIndex, headThingId, Patch_GodHands.SlotSmartRetargetField, value);
        }

        private static void SetAllSlotsBool(int mapIndex, int headThingId, FieldInfo field, bool value)
        {
            object head = Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), headThingId);
            if (head == null)
                return;
            var slots = Patch_GodHands.HeadTurretSlotsField.GetValue(head) as IList;
            if (slots == null)
                return;
            for (int i = 0; i < slots.Count; i++)
                Patch_GodHands.SetField(slots[i], field, value);
        }

        public static void SyncTurretAirDefense(int mapIndex, int headThingId, int slotIndex)
        {
            object slot = Patch_GodHands.GetHeadSlot(
                Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), headThingId), slotIndex);
            if (slot == null)
                return;
            bool current = (bool)(Patch_GodHands.SlotAirDefenseModeField.GetValue(slot) ?? false);
            Patch_GodHands.SetField(slot, Patch_GodHands.SlotAirDefenseModeField, !current);
            Patch_GodHands.SetField(slot, Patch_GodHands.SlotCurrentTargetField, LocalTargetInfo.Invalid);
        }

        public static void SyncTurretTarget(int mapIndex, int headThingId, int slotIndex, int kind, int targetThingId, int cellX, int cellZ)
        {
            object slot = Patch_GodHands.GetHeadSlot(
                Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), headThingId), slotIndex);
            if (slot == null)
                return;
            LocalTargetInfo target = targetThingId != 0
                ? new LocalTargetInfo(Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), targetThingId))
                : new LocalTargetInfo(new IntVec3(cellX, 0, cellZ));
            Patch_GodHands.SetField(slot, Patch_GodHands.SlotForcedTargetField, target);
            if (kind == 0)
                Patch_GodHands.SetField(slot, Patch_GodHands.SlotCurrentTargetField, target);
        }

        public static void SyncTurretClearForced(int mapIndex, int headThingId)
        {
            object head = Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), headThingId);
            if (head == null)
                return;
            var slots = Patch_GodHands.HeadTurretSlotsField.GetValue(head) as IList;
            if (slots == null)
                return;
            for (int i = 0; i < slots.Count; i++)
            {
                Patch_GodHands.SetField(slots[i], Patch_GodHands.SlotForcedTargetField, LocalTargetInfo.Invalid);
                Patch_GodHands.SetField(slots[i], Patch_GodHands.SlotCurrentTargetField, LocalTargetInfo.Invalid);
            }
        }

        public static void SyncTurretActivate(int mapIndex, int headThingId, int slotIndex)
        {
            object slot = Patch_GodHands.GetHeadSlot(
                Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), headThingId), slotIndex);
            if (slot == null)
                return;
            bool required = (bool)(Patch_GodHands.SlotActivationRequiredField.GetValue(slot) ?? false);
            if (required)
                Patch_GodHands.SetField(slot, Patch_GodHands.SlotIsActivatedField, true);
        }

        public static void SyncTurretRefuelOrder(int mapIndex, int headThingId, int slotIndex)
        {
            object head = Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), headThingId);
            if (head == null)
                return;
            Patch_GodHands.Invoke(head, Patch_GodHands.HeadTryOrderRefuelMethod, slotIndex);
        }

        public static void SyncTurretLoadShell(int mapIndex, int headThingId, int slotIndex, int shellThingId, string shellDefName, int count)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            object slot = Patch_GodHands.GetHeadSlot(Patch_GodHands.FindThingById(map, headThingId), slotIndex);
            Thing shellThing = Patch_GodHands.FindThingById(map, shellThingId);
            if (slot == null || shellThing == null || count <= 0)
                return;
            ThingDef shellDef = string.IsNullOrEmpty(shellDefName)
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(shellDefName);
            if (shellDef == null)
                return;
            shellThing.SplitOff(count).Destroy();
            Patch_GodHands.Invoke(slot, AccessTools.Method(Patch_GodHands.SlotType, "LoadShell"), shellDef, count);
        }

        public static void SyncTurretModuleAdd(int mapIndex, int headThingId, int slotIndex, int moduleThingId)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            object slot = Patch_GodHands.GetHeadSlot(Patch_GodHands.FindThingById(map, headThingId), slotIndex);
            Thing module = Patch_GodHands.FindThingById(map, moduleThingId);
            if (slot == null || module == null)
                return;
            bool ok = (bool)(Patch_GodHands.Invoke(slot, AccessTools.Method(Patch_GodHands.SlotType, "AddModule"), module) ?? false);
            if (ok && !module.Destroyed)
                module.DeSpawn();
        }

        public static void SyncTurretModuleRemove(int mapIndex, int headThingId, int slotIndex, int moduleThingId)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            object slot = Patch_GodHands.GetHeadSlot(Patch_GodHands.FindThingById(map, headThingId), slotIndex);
            if (slot == null || moduleThingId == 0)
                return;
            var stored = Patch_GodHands.SlotStoredModulesField.GetValue(slot) as IList;
            Thing module = null;
            if (stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    if (stored[i] is Thing t && t.thingIDNumber == moduleThingId)
                    {
                        module = t;
                        break;
                    }
                }
            }
            if (module == null)
                return;
            bool ok = (bool)(Patch_GodHands.Invoke(
                slot,
                AccessTools.Method(Patch_GodHands.SlotType, "RemoveModule", new[] { typeof(Thing) }),
                module) ?? false);
            if (ok)
            {
                object head = Patch_GodHands.FindThingById(map, headThingId);
                Pawn wearer = head == null ? null : AccessTools.Property(Patch_GodHands.HeadType, "Wearer")?.GetValue(head, null) as Pawn;
                if (wearer?.Map != null)
                    GenSpawn.Spawn(module, wearer.Position, wearer.Map);
            }
        }

        public static void SyncGodHandStartGrab(int playerId, int mapIndex, int pawnId, int thingId, int startX, int startZ, bool rangeMode, bool grabPawn, float radius)
        {
            if (playerId < 0)
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null)
                return;
            SessionKey key = new SessionKey(mapIndex, playerId);
            if (GodHandSessions.ContainsKey(key))
                return;
            IntVec3 start = new IntVec3(startX, 0, startZ);
            var session = new GodHandDragSession
            {
                MapIndex = mapIndex,
                PlayerId = playerId,
                StartCell = start,
                LastDragCell = start,
                RangeMode = rangeMode,
                IsPawnGrab = grabPawn
            };

            if (rangeMode)
            {
                if (grabPawn)
                {
                    var pawns = Patch_GodHands.InvokeStatic(Patch_GodHands.GrabbingUtilsGetPawnsMethod, map, start, radius) as IList;
                    if (pawns != null)
                    {
                        for (int i = 0; i < pawns.Count; i++)
                        {
                            if (pawns[i] is Pawn p)
                                AddGrabbedPawn(session, p, start, mapIndex);
                        }
                    }
                }
                else
                {
                    var things = Patch_GodHands.InvokeStatic(Patch_GodHands.GrabbingUtilsGetItemsMethod, map, start, radius) as IList;
                    if (things != null)
                    {
                        for (int i = 0; i < things.Count; i++)
                        {
                            if (things[i] is Thing t)
                                AddGrabbedThing(session, t, start, mapIndex);
                        }
                    }
                }
            }
            else if (grabPawn)
            {
                Pawn p = Patch_GodHands.FindPawnById(map, pawnId);
                if (p != null)
                    AddGrabbedPawn(session, p, start, mapIndex);
            }
            else
            {
                Thing t = Patch_GodHands.FindThingById(map, thingId);
                if (t != null)
                    AddGrabbedThing(session, t, start, mapIndex);
            }

            if (session.Pawns.Count == 0 && session.Things.Count == 0)
                return;

            GodHandSessions[key] = session;
            TrySyncLocalGodHandController(session);
        }

        private static void AddGrabbedPawn(GodHandDragSession session, Pawn p, IntVec3 start, int mapIndex)
        {
            if (p == null || p.Dead || p.Destroyed || PawnGrabbedByAnotherPlayer(mapIndex, session.PlayerId, p))
                return;
            session.Pawns[p.thingIDNumber] = p;
            session.Offsets[p.thingIDNumber] = p.DrawPos - start.ToVector3Shifted();
            p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            p.pather?.StopDead();
            p.stances?.CancelBusyStanceHard();
            if (p.jobs != null)
                p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 99999), JobCondition.InterruptForced);
        }

        private static void AddGrabbedThing(GodHandDragSession session, Thing t, IntVec3 start, int mapIndex)
        {
            if (t == null || t.Destroyed || ThingGrabbedByAnotherPlayer(mapIndex, session.PlayerId, t))
                return;
            session.Things[t.thingIDNumber] = t;
            session.Offsets[t.thingIDNumber] = t.DrawPos - start.ToVector3Shifted();
            if (t.Spawned)
                t.DeSpawn(DestroyMode.Vanish);
        }

        private static bool PawnGrabbedByAnotherPlayer(int mapIndex, int playerId, Pawn p)
        {
            foreach (KeyValuePair<SessionKey, GodHandDragSession> pair in GodHandSessions)
            {
                if (pair.Key.MapIndex != mapIndex || pair.Key.PlayerId == playerId)
                    continue;
                if (pair.Value.Pawns.ContainsKey(p.thingIDNumber))
                    return true;
            }
            return false;
        }

        private static bool ThingGrabbedByAnotherPlayer(int mapIndex, int playerId, Thing t)
        {
            foreach (KeyValuePair<SessionKey, GodHandDragSession> pair in GodHandSessions)
            {
                if (pair.Key.MapIndex != mapIndex || pair.Key.PlayerId == playerId)
                    continue;
                if (pair.Value.Things.ContainsKey(t.thingIDNumber))
                    return true;
            }
            return false;
        }

        public static void SyncGodHandDrag(int playerId, int mapIndex, int cellX, int cellZ)
        {
            if (playerId < 0 || !GodHandSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodHandDragSession session))
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null)
                return;
            IntVec3 cell = new IntVec3(cellX, 0, cellZ);
            session.LastDragCell = cell;
            Vector3 mousePos = cell.ToVector3Shifted();
            foreach (var pair in session.Pawns)
            {
                Pawn p = pair.Value;
                if (p == null || p.Destroyed || p.Dead)
                    continue;
                if (!session.Offsets.TryGetValue(pair.Key, out Vector3 offset))
                    offset = Vector3.zero;
                Vector3 targetPos = mousePos + offset;
                IntVec3 target = targetPos.ToIntVec3();
                if (target.InBounds(map) && p.Spawned && p.Position != target)
                    p.Position = target;
                UpdatePawnTweener(p, targetPos);
            }
            session.LastDragTick = Find.TickManager.TicksGame;
        }

        private static void UpdatePawnTweener(Pawn p, Vector3 pos)
        {
            if (p.Drawer?.tweener == null || Patch_GodHands.PawnTweenedPosField == null)
                return;
            try
            {
                Patch_GodHands.PawnTweenedPosField.SetValue(p.Drawer.tweener, pos);
                Patch_GodHands.PawnLastTickSpringPosField?.SetValue(p.Drawer.tweener, pos);
            }
            catch
            {
            }
        }

        public static void SyncGodHandRelease(int playerId, int mapIndex, int cellX, int cellZ)
        {
            if (playerId < 0 || !GodHandSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodHandDragSession session))
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null)
                return;
            IntVec3 cell = new IntVec3(cellX, 0, cellZ);

            foreach (var pair in session.Pawns)
            {
                Pawn p = pair.Value;
                if (p == null || p.Destroyed)
                    continue;
                if (!session.Offsets.TryGetValue(pair.Key, out Vector3 offset))
                    offset = Vector3.zero;
                IntVec3 targetCell = (cell.ToVector3Shifted() + offset).ToIntVec3();
                if (!targetCell.InBounds(map))
                {
                    Rot4 dir = Patch_GodHands.GetExitDirection(map, targetCell);
                    if (p.Faction == Faction.OfPlayer || p.IsColonist)
                        CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan(p, dir);
                    else
                        p.ExitMap(false, dir);
                    continue;
                }
                if (p.Spawned)
                {
                    p.Position = targetCell;
                    p.Notify_Teleported(false, true);
                }
                p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }

            foreach (var pair in session.Things)
            {
                Thing t = pair.Value;
                if (t == null || t.Destroyed)
                    continue;
                if (!session.Offsets.TryGetValue(pair.Key, out Vector3 offset))
                    offset = Vector3.zero;
                IntVec3 targetCell = (cell.ToVector3Shifted() + offset).ToIntVec3();
                if (!targetCell.InBounds(map))
                    targetCell = session.StartCell;
                GenPlace.TryPlaceThing(t, targetCell, map, ThingPlaceMode.Near);
                if (t.Spawned && t.def.IsIngestible &&
                    Patch_GodHands.GetSettingsValue("godHandEnableForceIngest", true))
                {
                    Pawn eater = Patch_GodHands.InvokeStatic(Patch_GodHands.GrabbingUtilsGetPawnAtMethod, map, t.Position) as Pawn;
                    if (eater != null && eater.RaceProps.CanEverEat(t.def))
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.Ingest, t);
                        job.count = 1;
                        eater.jobs?.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                }
            }

            GodHandSessions.Remove(new SessionKey(mapIndex, playerId));
            TrySyncLocalGodHandController(null, mapIndex, playerId);
        }

        public static void SyncGodHandForceRelease(int playerId, int mapIndex)
        {
            SessionKey key = new SessionKey(mapIndex, playerId);
            if (playerId >= 0 && GodHandSessions.TryGetValue(key, out GodHandDragSession session))
            {
                Map map = Patch_GodHands.FindMap(mapIndex);
                if (map != null)
                    AbortGodHandDrag(session, map);
                GodHandSessions.Remove(key);
            }
            TrySyncLocalGodHandController(null, mapIndex, playerId);
        }

        public static bool CancelPausedLocalSessions()
        {
            if (!Patch_GodHands.PausedGodHandsBlocked())
                return false;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return false;

            List<SessionKey> godHandKeys = null;
            foreach (KeyValuePair<SessionKey, GodHandDragSession> pair in GodHandSessions)
            {
                if (pair.Key.PlayerId != playerId)
                    continue;
                if (godHandKeys == null)
                    godHandKeys = new List<SessionKey>();
                godHandKeys.Add(pair.Key);
            }
            if (godHandKeys != null)
            {
                for (int i = 0; i < godHandKeys.Count; i++)
                    SyncGodHandForceRelease(playerId, godHandKeys[i].MapIndex);
            }

            List<SessionKey> wrenchKeys = null;
            foreach (KeyValuePair<SessionKey, GodWrenchSession> pair in WrenchSessions)
            {
                if (pair.Key.PlayerId != playerId)
                    continue;
                if (wrenchKeys == null)
                    wrenchKeys = new List<SessionKey>();
                wrenchKeys.Add(pair.Key);
            }
            if (wrenchKeys != null)
            {
                for (int i = 0; i < wrenchKeys.Count; i++)
                    SyncWrenchForceRelease(playerId, wrenchKeys[i].MapIndex);
            }

            return godHandKeys != null || wrenchKeys != null;
        }

        private static void AbortGodHandDrag(GodHandDragSession session, Map map)
        {
            foreach (Pawn p in session.Pawns.Values)
            {
                if (p == null || p.Destroyed || p.Dead)
                    continue;
                p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }

            IntVec3 fallback = session.StartCell.InBounds(map) ? session.StartCell : map.Center;
            foreach (Thing t in session.Things.Values)
            {
                if (t == null || t.Destroyed || t.Spawned)
                    continue;
                IntVec3 targetCell = fallback;
                if (session.LastDragCell.IsValid && session.LastDragCell.InBounds(map))
                {
                    Vector3 offset = session.Offsets.TryGetValue(t.thingIDNumber, out Vector3 stored)
                        ? stored
                        : Vector3.zero;
                    IntVec3 candidate = (session.LastDragCell.ToVector3Shifted() + offset).ToIntVec3();
                    if (candidate.InBounds(map))
                        targetCell = candidate;
                }
                GenPlace.TryPlaceThing(t, targetCell, map, ThingPlaceMode.Near);
            }
        }

        public static void SyncGodHandToggleShooting(int playerId, int mapIndex, int weaponThingId, float fixedX, float fixedZ)
        {
            if (playerId < 0 || !GodHandSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodHandDragSession session))
                return;
            session.WeaponThingId = weaponThingId;
            session.FixedWeaponPos = new Vector3(fixedX, 0, fixedZ);
            session.WaitForRelease = true;
            session.BurstLeft = 0;
            session.NextShotTick = 0;
            session.CooldownUntilTick = 0;
        }

        public static void SyncGodHandToggleMelee(int playerId, int mapIndex, int weaponThingId)
        {
            if (playerId < 0 || !GodHandSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodHandDragSession session))
                return;
            session.WeaponThingId = weaponThingId;
            session.WaitForRelease = false;
            session.BurstLeft = 0;
            session.NextShotTick = 0;
            session.CooldownUntilTick = 0;
        }

        public static void SyncGodHandShoot(int playerId, int mapIndex, int targetThingId, int cellX, int cellZ)
        {
            if (playerId < 0 || !GodHandSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodHandDragSession session))
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null || session.WeaponThingId == 0 ||
                !session.Things.TryGetValue(session.WeaponThingId, out Thing weapon) ||
                weapon == null || weapon.Destroyed)
                return;
            CompEquippable comp = weapon.TryGetComp<CompEquippable>();
            Verb verb = comp?.AllVerbs.FirstOrDefault(v => v != null && !v.verbProps.IsMeleeAttack);
            if (verb?.verbProps?.defaultProjectile == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (session.WaitForRelease || now < session.CooldownUntilTick)
                return;

            // Mirrors GodHandWeaponHandler.TryShoot + ProcessBurst: each accepted
            // command arms one burst, releases at most one projectile per burst
            // interval, then enters the verb cooldown on all peers.
            if (session.BurstLeft <= 0)
            {
                session.BurstTarget = targetThingId != 0
                    ? new LocalTargetInfo(Patch_GodHands.FindThingById(map, targetThingId))
                    : new LocalTargetInfo(new IntVec3(cellX, 0, cellZ));
                session.BurstLeft = Mathf.Max(1, verb.verbProps.burstShotCount);
                session.NextShotTick = now;
            }

            if (session.BurstLeft > 0 && now >= session.NextShotTick)
            {
                Projectile projectile = (Projectile)GenSpawn.Spawn(
                    verb.verbProps.defaultProjectile,
                    session.FixedWeaponPos.ToIntVec3(),
                    map);
                projectile.Launch(weapon, session.FixedWeaponPos, session.BurstTarget, session.BurstTarget, ProjectileHitFlags.All);
                session.BurstLeft--;
                if (session.BurstLeft > 0)
                    session.NextShotTick = now + verb.verbProps.ticksBetweenBurstShots;
                else
                    session.CooldownUntilTick = now + (int)(verb.verbProps.AdjustedCooldown(verb, null) * 60f);
            }
        }

        public static void SyncGodHandResetMouseRelease(int playerId, int mapIndex)
        {
            if (playerId < 0 || !GodHandSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodHandDragSession session))
                return;
            session.WaitForRelease = false;
        }

        public static void SyncGodHandMeleeHit(int playerId, int mapIndex, int weaponThingId, int targetThingId)
        {
            if (playerId < 0 || !GodHandSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodHandDragSession session))
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null || weaponThingId == 0 ||
                !session.Things.TryGetValue(weaponThingId, out Thing weapon) ||
                weapon == null || weapon.Destroyed)
                return;
            Pawn target = Patch_GodHands.FindPawnById(map, targetThingId);
            if (target == null || target.Dead)
                return;
            CompEquippable comp = weapon.TryGetComp<CompEquippable>();
            Verb verb = comp?.AllVerbs.FirstOrDefault(v => v != null && v.verbProps.IsMeleeAttack);
            if (verb == null)
                return;
            target.TakeDamage(new DamageInfo(
                verb.verbProps.meleeDamageDef ?? DamageDefOf.Blunt,
                verb.verbProps.meleeDamageBaseAmount,
                0,
                -1,
                weapon));
        }

        public static void SyncWrenchStartGrab(int playerId, int mapIndex, int thingId, int startX, int startZ)
        {
            if (playerId < 0)
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            Thing thing = Patch_GodHands.FindThingById(map, thingId);
            if (map == null || thing == null)
                return;
            if (WrenchSessions.ContainsKey(new SessionKey(mapIndex, playerId)))
                return;
            var session = GetWrenchSession(mapIndex, playerId);
            session.Bulk = false;
            session.ThingId = thingId;
            session.StartCell = new IntVec3(startX, 0, startZ);
            session.OriginalPosition = thing.Position;
            session.OriginalTerrain = map.terrainGrid.TerrainAt(thing.Position);
            WrenchSessions[new SessionKey(mapIndex, playerId)] = session;
            TrySyncLocalGodWrenchController(session);
        }

        public static void SyncWrenchDrag(int playerId, int mapIndex, int cellX, int cellZ)
        {
            if (playerId < 0 || !WrenchSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodWrenchSession session) || session.Bulk)
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            Thing thing = Patch_GodHands.FindThingById(map, session.ThingId);
            IntVec3 cell = new IntVec3(cellX, 0, cellZ);
            if (map == null || thing == null || thing.Destroyed || !cell.InBounds(map))
                return;
            if (thing.Position == cell)
                return;
            IntVec3 oldPos = thing.Position;
            map.thingGrid.Deregister(thing, false);
            Patch_GodHands.SetField(thing, AccessTools.Field(typeof(Thing), "positionInt"), cell);
            map.thingGrid.Register(thing);
            MarkCellsDirty(map, oldPos, thing.Rotation, thing.def.size);
            MarkCellsDirty(map, cell, thing.Rotation, thing.def.size);
            session.LastDragTick = Find.TickManager.TicksGame;
        }

        private static void MarkCellsDirty(Map map, IntVec3 position, Rot4 rotation, IntVec2 size)
        {
            IntVec2 s = rotation.IsHorizontal ? new IntVec2(size.z, size.x) : size;
            for (int x = 0; x < s.x; x++)
            {
                for (int z = 0; z < s.z; z++)
                {
                    IntVec3 cell = position + new IntVec3(x, 0, z);
                    if (!cell.InBounds(map))
                        continue;
                    map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings | MapMeshFlagDefOf.Terrain);
                    map.glowGrid.DirtyCell(cell);
                }
            }
        }

        public static void SyncWrenchRelease(int playerId, int mapIndex, int cellX, int cellZ)
        {
            if (playerId < 0 || !WrenchSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodWrenchSession session))
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null)
                return;
            IntVec3 cell = new IntVec3(cellX, 0, cellZ);

            if (session.Bulk && session.BulkController != null)
            {
                Patch_GodHands.Invoke(session.BulkController, Patch_GodHands.WrenchReleaseGrabMethod, map, cell);
                WrenchSessions.Remove(new SessionKey(mapIndex, playerId));
                TrySyncLocalGodWrenchController(null, mapIndex, playerId);
                return;
            }

            Thing thing = Patch_GodHands.FindThingById(map, session.ThingId);
            if (thing != null && !thing.Destroyed)
            {
                map.thingGrid.Deregister(thing, false);
                MarkCellsDirty(map, session.OriginalPosition, thing.Rotation, thing.def.size);
                MarkCellsDirty(map, thing.Position, thing.Rotation, thing.def.size);
                Patch_GodHands.SetField(thing, AccessTools.Field(typeof(Thing), "positionInt"), session.OriginalPosition);
                map.thingGrid.Register(thing);
                if (thing is Building building)
                    map.edificeGrid.Register(building);

                thing.DeSpawn(DestroyMode.Vanish);

                if (session.OriginalTerrain != null && session.OriginalPosition.InBounds(map))
                {
                    TerrainDef current = map.terrainGrid.TerrainAt(session.OriginalPosition);
                    if (!IsNaturalRockTerrain(session.OriginalTerrain) && IsNaturalRockTerrain(current))
                        map.terrainGrid.SetTerrain(session.OriginalPosition, session.OriginalTerrain);
                }

                thing.Position = cell;
                thing.SpawnSetup(map, false);
                map.linkGrid.Notify_LinkerCreatedOrDestroyed(thing);
                if (thing is Building placed)
                    CheckAndMarkStackedBuildings(map, cell, placed);
                NotifyPawnsToStopUsing(map, thing);

                if (thing is Apparel apparel && thing.GetType() == Patch_GodHands.HeadType && thing.Spawned)
                {
                    Pawn p = cell.GetFirstPawn(map);
                    if (p != null && p.RaceProps.Humanlike && p.apparel != null)
                    {
                        Apparel existing = p.apparel.WornApparel.FirstOrDefault(a => a.GetType() == Patch_GodHands.HeadType);
                        if (existing == null)
                        {
                            p.apparel.Wear(apparel);
                        }
                        else
                        {
                            var droppedSlots = Patch_GodHands.HeadTurretSlotsField.GetValue(thing) as IList;
                            var targetSlots = Patch_GodHands.HeadTurretSlotsField.GetValue(existing) as IList;
                            if (droppedSlots != null && targetSlots != null)
                            {
                                var slots = new List<object>();
                                for (int i = 0; i < droppedSlots.Count; i++)
                                    slots.Add(droppedSlots[i]);
                                int max = Patch_GodHands.GetSettingsValue("turretHeadMaxTurrets", 5);
                                foreach (object slot in slots)
                                {
                                    if (targetSlots.Count >= max)
                                        break;
                                    Patch_GodHands.Invoke(thing, AccessTools.Method(Patch_GodHands.HeadType, "RemoveTurretSlot"), slot);
                                    Patch_GodHands.Invoke(existing, AccessTools.Method(Patch_GodHands.HeadType, "AddTurretSlot"), slot);
                                }
                                if (droppedSlots.Count == 0 && !thing.Destroyed)
                                    thing.Destroy();
                            }
                        }
                    }
                }
            }

            WrenchSessions.Remove(new SessionKey(mapIndex, playerId));
            TrySyncLocalGodWrenchController(null, mapIndex, playerId);
        }

        private static bool IsNaturalRockTerrain(TerrainDef terrain)
        {
            if (terrain == null)
                return false;
            string name = terrain.defName.ToLowerInvariant();
            return name.Contains("rock") || name.Contains("rough") || name.Contains("stone") || name.Contains("hewn");
        }

        private static void CheckAndMarkStackedBuildings(Map map, IntVec3 cell, Building placed)
        {
            List<Building> buildings = new List<Building>();
            foreach (Thing t in map.thingGrid.ThingsAt(cell))
            {
                if (t is Building b && b != placed)
                    buildings.Add(b);
            }
            if (buildings.Count == 0)
                return;
            Type tracker = AccessTools.TypeByName("GodHandMod.WrenchStackedBuildingTracker");
            MethodInfo mark = tracker == null ? null : AccessTools.Method(tracker, "MarkAsStacked");
            mark?.Invoke(null, new object[] { placed });
            foreach (Building b in buildings)
                mark?.Invoke(null, new object[] { b });
        }

        private static void NotifyPawnsToStopUsing(Map map, Thing thing)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
                return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.jobs?.curJob == null)
                    continue;
                bool targets = pawn.jobs.curJob.targetA.Thing == thing ||
                               pawn.jobs.curJob.targetB.Thing == thing ||
                               pawn.jobs.curJob.targetC.Thing == thing;
                if (targets)
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            }
        }

        public static void SyncWrenchForceRelease(int playerId, int mapIndex)
        {
            SessionKey key = new SessionKey(mapIndex, playerId);
            if (playerId >= 0 && WrenchSessions.TryGetValue(key, out GodWrenchSession session))
            {
                if (session.Bulk && session.BulkController != null)
                    Patch_GodHands.Invoke(session.BulkController, Patch_GodHands.WrenchForceReleaseGrabMethod);
                else
                {
                    Map map = Patch_GodHands.FindMap(mapIndex);
                    if (map != null)
                    {
                        if (session.RoofDrag && session.RoofDef != null &&
                            session.DragStartCell.InBounds(map))
                        {
                            map.roofGrid.SetRoof(session.DragStartCell, session.RoofDef);
                            RefreshRoofCell(map, session.DragStartCell);
                        }
                        else if (session.DeepDrag && session.DeepDef != null &&
                                 session.DragStartCell.InBounds(map))
                        {
                            map.deepResourceGrid.SetAt(session.DragStartCell, session.DeepDef, session.DeepCount);
                            RefreshRoofCell(map, session.DragStartCell);
                        }
                    }
                }
            }
            WrenchSessions.Remove(key);
            TrySyncLocalGodWrenchController(null, mapIndex, playerId);
        }

        public static void SyncWrenchStartBulk(int playerId, int mapIndex, int startX, int startZ, int type, float radius)
        {
            if (playerId < 0)
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null || Patch_GodHands.WrenchControllerType == null)
                return;
            if (WrenchSessions.ContainsKey(new SessionKey(mapIndex, playerId)))
                return;
            object controller = Activator.CreateInstance(Patch_GodHands.WrenchControllerType);
            var session = GetWrenchSession(mapIndex, playerId);
            session.Bulk = true;
            session.BulkController = controller;
            session.MapIndex = mapIndex;
            session.PlayerId = playerId;
            session.StartCell = new IntVec3(startX, 0, startZ);
            WrenchSessions[new SessionKey(mapIndex, playerId)] = session;

            object oldRadius = Patch_GodHands.GetSettingsValue<object>("godHandGrabRadius", 3f);
            Patch_GodHands.SetSettingsValue("godHandGrabRadius", radius);
            try
            {
                object scoopType = Enum.ToObject(Patch_GodHands.WrenchScoopType, type);
                Patch_GodHands.Invoke(controller, Patch_GodHands.WrenchStartBulkScoopMethod, session.StartCell, scoopType);
            }
            finally
            {
                Patch_GodHands.SetSettingsValue("godHandGrabRadius", oldRadius);
            }

            object bulkHandler = Patch_GodHands.GetField(controller, Patch_GodHands.WrenchBulkHandlerField);
            IList entries = Patch_GodHands.GetField(bulkHandler, Patch_GodHands.BulkScoopedThingsField) as IList;
            if (entries != null && Patch_GodHands.BulkEntryThingField != null)
            {
                var sorted = entries.Cast<object>()
                    .OrderBy(e => ((Thing)Patch_GodHands.BulkEntryThingField.GetValue(e)).thingIDNumber)
                    .ToList();
                entries.Clear();
                foreach (object entry in sorted)
                    entries.Add(entry);
            }
            TrySyncLocalGodWrenchBulkController(mapIndex, playerId, controller);
        }

        public static void SyncWrenchRoofStart(int playerId, int mapIndex, int cellX, int cellZ, string roofDefName)
        {
            if (playerId < 0)
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            RoofDef roof = string.IsNullOrEmpty(roofDefName) ? null : DefDatabase<RoofDef>.GetNamedSilentFail(roofDefName);
            IntVec3 cell = new IntVec3(cellX, 0, cellZ);
            if (map == null || roof == null)
                return;
            if (WrenchSessions.ContainsKey(new SessionKey(mapIndex, playerId)))
                return;
            var session = GetWrenchSession(mapIndex, playerId);
            session.RoofDrag = true;
            session.RoofDef = roof;
            session.DragStartCell = cell;
            WrenchSessions[new SessionKey(mapIndex, playerId)] = session;
            map.roofGrid.SetRoof(cell, null);
            RefreshRoofCell(map, cell);
        }

        public static void SyncWrenchRoofRelease(int playerId, int mapIndex, int endX, int endZ, string roofDefName, int startX, int startZ)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            RoofDef roof = string.IsNullOrEmpty(roofDefName) ? null : DefDatabase<RoofDef>.GetNamedSilentFail(roofDefName);
            IntVec3 end = new IntVec3(endX, 0, endZ);
            IntVec3 start = new IntVec3(startX, 0, startZ);
            if (map == null || roof == null)
                return;
            map.roofGrid.SetRoof(end, roof);
            RefreshRoofCell(map, end);
            if (end != start)
                RefreshRoofCell(map, start);
            WrenchSessions.Remove(new SessionKey(mapIndex, playerId));
        }

        public static void SyncWrenchDeepStart(int playerId, int mapIndex, int cellX, int cellZ, string resourceDefName)
        {
            if (playerId < 0)
                return;
            Map map = Patch_GodHands.FindMap(mapIndex);
            ThingDef def = string.IsNullOrEmpty(resourceDefName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(resourceDefName);
            IntVec3 cell = new IntVec3(cellX, 0, cellZ);
            if (map == null || def == null)
                return;
            if (WrenchSessions.ContainsKey(new SessionKey(mapIndex, playerId)))
                return;
            var session = GetWrenchSession(mapIndex, playerId);
            session.DeepDrag = true;
            session.DeepDef = def;
            session.DeepCount = map.deepResourceGrid.CountAt(cell);
            session.DragStartCell = cell;
            WrenchSessions[new SessionKey(mapIndex, playerId)] = session;
            map.deepResourceGrid.SetAt(cell, null, 0);
            RefreshRoofCell(map, cell);
        }

        public static void SyncWrenchDeepRelease(int playerId, int mapIndex, int endX, int endZ, string resourceDefName, int count, int startX, int startZ)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            ThingDef def = string.IsNullOrEmpty(resourceDefName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(resourceDefName);
            IntVec3 end = new IntVec3(endX, 0, endZ);
            IntVec3 start = new IntVec3(startX, 0, startZ);
            if (map == null || def == null)
                return;
            map.deepResourceGrid.SetAt(end, def, count);
            RefreshRoofCell(map, end);
            if (end != start)
                RefreshRoofCell(map, start);
            WrenchSessions.Remove(new SessionKey(mapIndex, playerId));
        }

        private static void RefreshRoofCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
                return;
            map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Roofs | MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings | MapMeshFlagDefOf.Terrain);
            map.glowGrid.DirtyCell(cell);
        }

        public static void SyncWrenchSnatchTurret(int mapIndex, int turretThingId)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            Thing turret = Patch_GodHands.FindThingById(map, turretThingId);
            if (map == null || turret == null || Patch_GodHands.WrenchControllerType == null)
                return;
            object controller = Activator.CreateInstance(Patch_GodHands.WrenchControllerType);
            object handler = Patch_GodHands.GetField(controller, Patch_GodHands.WrenchBulkHandlerField) == null
                ? null
                : GetWrenchTurretHandler(controller);
            Patch_GodHands.Invoke(handler, Patch_GodHands.WrenchTurretHandlerTrySnatchMethod, map, turret, null);
        }

        public static void SyncWrenchRemoveFromPawn(int mapIndex, int pawnId, int headThingId)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            Pawn pawn = Patch_GodHands.FindPawnById(map, pawnId);
            Thing head = Patch_GodHands.FindThingById(map, headThingId);
            if (map == null || pawn == null || head == null || Patch_GodHands.WrenchControllerType == null)
                return;
            object controller = Activator.CreateInstance(Patch_GodHands.WrenchControllerType);
            object handler = GetWrenchTurretHandler(controller);
            Patch_GodHands.Invoke(handler, Patch_GodHands.WrenchTurretHandlerTryRemoveFromPawnMethod, map, pawn, head);
        }

        private static object GetWrenchTurretHandler(object controller)
        {
            FieldInfo field = AccessTools.Field(Patch_GodHands.WrenchControllerType, "turretHandler");
            return Patch_GodHands.GetField(controller, field);
        }

        public static void SyncProtectionExecute(int mapIndex, int cellX, int cellZ, int mode, float radius, bool infinite, int durationTicks)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null)
                return;
            object oldMode = Patch_GodHands.GetSettingsValue<object>("currentProtectionMode", 0);
            object oldRadius = Patch_GodHands.GetSettingsValue<object>("protectionDomeRadius", 10f);
            object oldInfinite = Patch_GodHands.GetSettingsValue<object>("protectionDomeInfiniteDuration", true);
            object oldDuration = Patch_GodHands.GetSettingsValue<object>("protectionDurationTicks", 60000);
            Patch_GodHands.SetSettingsValue("currentProtectionMode", mode);
            Patch_GodHands.SetSettingsValue("protectionDomeRadius", radius);
            Patch_GodHands.SetSettingsValue("protectionDomeInfiniteDuration", infinite);
            Patch_GodHands.SetSettingsValue("protectionDurationTicks", durationTicks);
            try
            {
                Patch_GodHands.InvokeStatic(
                    Patch_GodHands.ProtectionExecuteMethod,
                    new IntVec3(cellX, 0, cellZ),
                    map,
                    mode,
                    default(IntVec3));
            }
            finally
            {
                Patch_GodHands.SetSettingsValue("currentProtectionMode", oldMode);
                Patch_GodHands.SetSettingsValue("protectionDomeRadius", oldRadius);
                Patch_GodHands.SetSettingsValue("protectionDomeInfiniteDuration", oldInfinite);
                Patch_GodHands.SetSettingsValue("protectionDurationTicks", oldDuration);
            }
        }

        public static void SyncProtectionIndividual(int mapIndex, int pawnId)
        {
            Pawn pawn = Patch_GodHands.FindPawnById(Patch_GodHands.FindMap(mapIndex), pawnId);
            if (pawn != null)
                Patch_GodHands.InvokeStatic(Patch_GodHands.ProtectionApplyIndividualMethod, pawn);
        }

        public static void SyncAssistantExecute(int mapIndex, int thingId, int mode, bool shift)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            Thing thing = Patch_GodHands.FindThingById(map, thingId);
            if (map == null || thing == null)
                return;
            object comp = Patch_GodHands.GetMapComponent(map, Patch_GodHands.AssistantMapComponentType);
            if (comp == null)
                return;

            MethodInfo isTableBusy = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "IsTableBusy");
            MethodInfo removeTable = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "RemoveTaskFor", new[] { typeof(Building_WorkTable) });
            MethodInfo getCraftingCount = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "GetCraftingTaskCount");
            MethodInfo addTask = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "AddTask");
            MethodInfo hasMedicalPawn = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "HasMedicalTaskFor", new[] { typeof(Pawn) });
            MethodInfo hasMedicalBed = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "HasMedicalTaskFor", new[] { typeof(Building_Bed) });
            MethodInfo removePawn = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "RemoveTaskFor", new[] { typeof(Pawn) });
            MethodInfo removeBed = AccessTools.Method(Patch_GodHands.AssistantMapComponentType, "RemoveTaskFor", new[] { typeof(Building_Bed) });

            if (mode == 0 && thing is Building_WorkTable table)
            {
                if (shift)
                {
                    bool busy = (bool)(Patch_GodHands.Invoke(comp, isTableBusy, table) ?? false);
                    if (busy)
                        Patch_GodHands.Invoke(comp, removeTable, table);
                    return;
                }
                int active = (int)(Patch_GodHands.Invoke(comp, getCraftingCount) ?? 0);
                int max = Patch_GodHands.GetSettingsValue("maxGodAssistantTasks", 3);
                if (active >= max)
                    return;
                object task = CreateTaskInstance(Patch_GodHands.CraftingTaskType, table);
                Patch_GodHands.Invoke(comp, addTask, task);
                return;
            }

            if (mode == 1)
            {
                if (thing is Pawn patient)
                {
                    if (shift)
                    {
                        Patch_GodHands.Invoke(comp, removePawn, patient);
                        return;
                    }
                    bool exists = (bool)(Patch_GodHands.Invoke(comp, hasMedicalPawn, patient) ?? false);
                    if (!exists)
                        Patch_GodHands.Invoke(comp, addTask, CreateTaskInstance(Patch_GodHands.MedicalPawnTaskType, patient));
                    return;
                }
                if (thing is Building_Bed bed)
                {
                    if (shift)
                    {
                        Patch_GodHands.Invoke(comp, removeBed, bed);
                        return;
                    }
                    bool exists = (bool)(Patch_GodHands.Invoke(comp, hasMedicalBed, bed) ?? false);
                    if (!exists)
                        Patch_GodHands.Invoke(comp, addTask, CreateTaskInstance(Patch_GodHands.MedicalBedTaskType, bed));
                }
            }
        }

        private static object CreateTaskInstance(Type taskType, object arg)
        {
            try
            {
                return Activator.CreateInstance(
                    taskType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { arg },
                    null);
            }
            catch
            {
                return null;
            }
        }

        public static void SyncJudgmentExecute(int mapIndex, int cellX, int cellZ, int mode)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null)
                return;
            object oldMode = Patch_GodHands.GetSettingsValue<object>("currentJudgmentMode", 0);
            Patch_GodHands.SetSettingsValue("currentJudgmentMode", mode);
            try
            {
                Patch_GodHands.InvokeStatic(Patch_GodHands.JudgmentExecuteMethod, new IntVec3(cellX, 0, cellZ), map);
            }
            finally
            {
                Patch_GodHands.SetSettingsValue("currentJudgmentMode", oldMode);
            }
        }

        public static void SyncCleanArea(int mapIndex, int cellX, int cellZ, int mode, int radius)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null || Patch_GodHands.CleaningControllerType == null)
                return;
            object oldMode = Patch_GodHands.GetProperty(null, Patch_GodHands.CleaningCurrentModeProperty);
            object oldRadius = Patch_GodHands.GetProperty(null, Patch_GodHands.CleaningCleanRadiusProperty);
            Patch_GodHands.SetProperty(null, Patch_GodHands.CleaningCurrentModeProperty, Enum.ToObject(
                AccessTools.TypeByName("GodHandMod.CleaningMode"), mode));
            Patch_GodHands.SetProperty(null, Patch_GodHands.CleaningCleanRadiusProperty, radius);
            try
            {
                Patch_GodHands.InvokeStatic(Patch_GodHands.CleaningCleanAreaMethod, map, new IntVec3(cellX, 0, cellZ));
            }
            finally
            {
                Patch_GodHands.SetProperty(null, Patch_GodHands.CleaningCurrentModeProperty, oldMode);
                Patch_GodHands.SetProperty(null, Patch_GodHands.CleaningCleanRadiusProperty, oldRadius);
            }
        }

        public static void SyncPokeEnd(int mapIndex, int pawnId, int corpseId, int startX, int startZ, int endX, int endZ)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            if (map == null || Patch_GodHands.PokeControllerType == null)
                return;
            object controller = Activator.CreateInstance(Patch_GodHands.PokeControllerType);
            IntVec3 start = new IntVec3(startX, 0, startZ);
            IntVec3 end = new IntVec3(endX, 0, endZ);
            Patch_GodHands.SetField(controller, Patch_GodHands.PokeIsInteractingField, true);
            Patch_GodHands.SetField(controller, Patch_GodHands.PokeStartCellField, start);
            if (pawnId != 0)
                Patch_GodHands.SetField(controller, Patch_GodHands.PokeTargetPawnField, Patch_GodHands.FindPawnById(map, pawnId));
            else if (corpseId != 0)
                Patch_GodHands.SetField(controller, Patch_GodHands.PokeTargetCorpseField, Patch_GodHands.FindThingById(map, corpseId) as Corpse);
            int state = 0;
            Map mapForPop = null;
            int seed = Gen.HashCombineInt(map.uniqueID, Gen.HashCombineInt(pawnId ^ corpseId, Gen.HashCombineInt(start.GetHashCode(), end.GetHashCode())));
            DeterministicRandScope.Begin(map, seed, 0x77A1, ref state, out mapForPop, ignoreGate: true);
            try
            {
                Patch_GodHands.Invoke(controller, Patch_GodHands.PokeEndInteractionMethod, map, end);
            }
            finally
            {
                DeterministicRandScope.End(state, mapForPop);
            }
        }

        public static void SyncMagnifier(int mapIndex, int pawnId, int action, bool visualMode)
        {
            Pawn pawn = Patch_GodHands.FindPawnById(Patch_GodHands.FindMap(mapIndex), pawnId);
            if (pawn == null || Patch_GodHands.MagnifierControllerType == null)
                return;
            object controller = Activator.CreateInstance(Patch_GodHands.MagnifierControllerType);
            Patch_GodHands.SetField(controller, Patch_GodHands.MagnifierTargetPawnField, pawn);
            Patch_GodHands.SetField(controller, Patch_GodHands.MagnifierIsActiveField, true);
            Patch_GodHands.SetField(controller, Patch_GodHands.MagnifierVisualScaleModeField, visualMode);
            MethodInfo method = action == 1
                ? Patch_GodHands.MagnifierIncreaseScaleMethod
                : action == 2 ? Patch_GodHands.MagnifierDecreaseScaleMethod : Patch_GodHands.MagnifierToggleScaleModeMethod;
            Patch_GodHands.Invoke(controller, method);
        }

        public static void SyncPrecisionTransform(int mapIndex, int thingId, string componentKey,
            float ox, float oy, float rotation, float sx, float sy, float sz, int layer, int overrideRot)
        {
            Map map = Patch_GodHands.FindMap(mapIndex);
            Thing thing = Patch_GodHands.FindThingById(map, thingId);
            if (map == null || thing == null)
                return;
            Vector2 offset = new Vector2(ox, oy);
            Vector3 scale = new Vector3(sx, sy, sz);
            int? rot = overrideRot < 0 ? (int?)null : overrideRot;
            if (string.IsNullOrEmpty(componentKey))
            {
                Patch_GodHands.InvokeStatic(Patch_GodHands.TransformManagerSetMethod, thing, offset, rotation, scale, layer, rot);
            }
            else if (Patch_GodHands.TransformDataType != null)
            {
                object data = Patch_GodHands.InvokeStatic(Patch_GodHands.TransformManagerGetOrCreateByKeyMethod, componentKey);
                if (data != null)
                {
                    Patch_GodHands.SetField(data, AccessTools.Field(Patch_GodHands.TransformDataType, "offset"), offset);
                    Patch_GodHands.SetField(data, AccessTools.Field(Patch_GodHands.TransformDataType, "rotation"), rotation);
                    Patch_GodHands.SetField(data, AccessTools.Field(Patch_GodHands.TransformDataType, "scale"), scale);
                    Patch_GodHands.SetField(data, AccessTools.Field(Patch_GodHands.TransformDataType, "drawLayer"), layer);
                    Patch_GodHands.SetField(data, AccessTools.Field(Patch_GodHands.TransformDataType, "overrideRot"), rot);
                    map.mapDrawer.MapMeshDirty(thing.Position, MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings);
                }
            }
        }

        public static void SyncPrecisionTurretRotation(int mapIndex, int thingId, float rotation)
        {
            Thing thing = Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), thingId);
            if (thing == null)
                return;
            FieldInfo topField = AccessTools.Field(typeof(Building_Turret), "top");
            object top = topField?.GetValue(thing);
            PropertyInfo curRotation = top == null ? null : AccessTools.Property(top.GetType(), "CurRotation");
            curRotation?.SetValue(top, rotation, null);
        }

        private static GodWrenchSession GetWrenchSession(int mapIndex, int playerId)
        {
            if (!WrenchSessions.TryGetValue(new SessionKey(mapIndex, playerId), out GodWrenchSession session))
                session = new GodWrenchSession { MapIndex = mapIndex, PlayerId = playerId };
            return session;
        }

        private static void TrySyncLocalGodHandController(GodHandDragSession session)
        {
            if (session == null)
                return;
            TrySyncLocalGodHandController(session, session.MapIndex);
        }

        private static void TrySyncLocalGodHandController(GodHandDragSession session, int mapIndex, int? ownerPlayerId = null)
        {
            if (session == null)
            {
                if (ownerPlayerId == null || ownerPlayerId.Value != Patch_GodHands.GetLocalPlayerId())
                    return;
            }
            else if (session.PlayerId != Patch_GodHands.GetLocalPlayerId())
            {
                return;
            }

            Designator designator = FindSelectedDesignator(Patch_GodHands.DesignatorGodHandType);
            if (designator == null)
                return;
            object controller = AccessTools.Field(Patch_GodHands.DesignatorGodHandType, "controller")?.GetValue(designator);
            if (controller == null)
                return;
            if (session == null)
            {
                Patch_GodHands.SetField(controller, Patch_GodHands.ControllerIsActiveField, false);
                Patch_GodHands.SetProperty(controller, Patch_GodHands.ControllerIsRangeGrabProperty, false);
                var pawns = Patch_GodHands.ControllerGrabbedPawnsField.GetValue(controller) as IList;
                var things = Patch_GodHands.ControllerGrabbedThingsField.GetValue(controller) as IList;
                var offsets = Patch_GodHands.ControllerGrabOffsetsField.GetValue(controller) as IDictionary;
                pawns?.Clear();
                things?.Clear();
                offsets?.Clear();
                object localPawnHandler = Patch_GodHands.GetField(controller, Patch_GodHands.ControllerPawnHandlerField);
                object localWeaponHandler = Patch_GodHands.GetField(controller, Patch_GodHands.ControllerWeaponHandlerField);
                Patch_GodHands.Invoke(localPawnHandler, AccessTools.Method(Patch_GodHands.PawnHandlerType, "ForceClear"));
                Patch_GodHands.Invoke(localWeaponHandler, AccessTools.Method(Patch_GodHands.WeaponHandlerType, "ForceClear"));
                return;
            }

            Patch_GodHands.SetField(controller, Patch_GodHands.ControllerIsActiveField, true);
            Patch_GodHands.SetField(controller, Patch_GodHands.ControllerGrabStartCellField, session.StartCell);
            Patch_GodHands.SetProperty(controller, Patch_GodHands.ControllerIsRangeGrabProperty, session.RangeMode);
            var grabbedPawns = Patch_GodHands.ControllerGrabbedPawnsField.GetValue(controller) as IList;
            var grabbedThings = Patch_GodHands.ControllerGrabbedThingsField.GetValue(controller) as IList;
            var grabOffsets = Patch_GodHands.ControllerGrabOffsetsField.GetValue(controller) as IDictionary;
            grabbedPawns?.Clear();
            grabbedThings?.Clear();
            grabOffsets?.Clear();
            foreach (Pawn p in session.Pawns.Values)
            {
                grabbedPawns?.Add(p);
                if (grabOffsets != null && session.Offsets.TryGetValue(p.thingIDNumber, out Vector3 offset))
                    grabOffsets[p] = offset;
            }
            foreach (Thing t in session.Things.Values)
            {
                grabbedThings?.Add(t);
                if (grabOffsets != null && session.Offsets.TryGetValue(t.thingIDNumber, out Vector3 offset))
                    grabOffsets[t] = offset;
            }
            object pawnHandler = Patch_GodHands.GetField(controller, Patch_GodHands.ControllerPawnHandlerField);
            Patch_GodHands.Invoke(pawnHandler, AccessTools.Method(Patch_GodHands.PawnHandlerType, "OnStart"), session.StartCell);
        }

        private static void TrySyncLocalGodWrenchController(GodWrenchSession session)
        {
            if (session == null)
                return;
            TrySyncLocalGodWrenchController(session, session.MapIndex);
        }

        private static void TrySyncLocalGodWrenchController(GodWrenchSession session, int mapIndex, int? ownerPlayerId = null)
        {
            if (session == null)
            {
                if (ownerPlayerId == null || ownerPlayerId.Value != Patch_GodHands.GetLocalPlayerId())
                    return;
            }
            else if (session.PlayerId != Patch_GodHands.GetLocalPlayerId())
            {
                return;
            }

            Designator designator = FindSelectedDesignator(Patch_GodHands.DesignatorGodWrenchType);
            if (designator == null)
                return;
            object controller = AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "controller")?.GetValue(designator);
            if (controller == null)
                return;
            Patch_GodHands.SetField(controller, Patch_GodHands.WrenchIsActiveField, session != null);
            if (session == null)
            {
                Patch_GodHands.SetField(controller, Patch_GodHands.WrenchGrabbedThingField, null);
                Patch_GodHandsDesignators.CancelLocalWrenchDrag();
                return;
            }
            Thing thing = session.ThingId == 0 ? null : Patch_GodHands.FindThingById(Patch_GodHands.FindMap(mapIndex), session.ThingId);
            Patch_GodHands.SetField(controller, Patch_GodHands.WrenchGrabbedThingField, thing);
            Patch_GodHands.SetField(controller, Patch_GodHands.WrenchGrabStartField, session.StartCell);
            Patch_GodHands.SetField(controller, Patch_GodHands.WrenchOriginalPositionField, session.OriginalPosition);
            Patch_GodHands.SetField(controller, Patch_GodHands.WrenchOriginalTerrainField, session.OriginalTerrain);
            Patch_GodHands.SetField(controller, Patch_GodHands.WrenchCurrentMapField, Patch_GodHands.FindMap(mapIndex));
        }

        private static void TrySyncLocalGodWrenchBulkController(int mapIndex, int playerId, object sourceController)
        {
            if (playerId != Patch_GodHands.GetLocalPlayerId())
                return;
            Designator designator = FindSelectedDesignator(Patch_GodHands.DesignatorGodWrenchType);
            if (designator == null || sourceController == null)
                return;
            object localController = AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "controller")?.GetValue(designator);
            object sourceBulk = Patch_GodHands.GetField(sourceController, Patch_GodHands.WrenchBulkHandlerField);
            object localBulk = localController == null ? null : Patch_GodHands.GetField(localController, Patch_GodHands.WrenchBulkHandlerField);
            if (localController == null || sourceBulk == null || localBulk == null)
                return;

            Patch_GodHands.SetField(localController, Patch_GodHands.WrenchIsActiveField, true);
            Patch_GodHands.SetField(localBulk, Patch_GodHands.BulkStartCellField,
                Patch_GodHands.GetField(sourceBulk, Patch_GodHands.BulkStartCellField));
            Patch_GodHands.SetField(localBulk, Patch_GodHands.BulkIsScoopedField,
                Patch_GodHands.GetField(sourceBulk, Patch_GodHands.BulkIsScoopedField));
            Patch_GodHands.SetField(localBulk, Patch_GodHands.BulkCurrentTypeField,
                Patch_GodHands.GetField(sourceBulk, Patch_GodHands.BulkCurrentTypeField));
            Patch_GodHands.SetField(localBulk, Patch_GodHands.BulkScoopedMapField,
                Patch_GodHands.GetField(sourceBulk, Patch_GodHands.BulkScoopedMapField));
            CopyListField(sourceBulk, localBulk, Patch_GodHands.BulkScoopedThingsField);
            CopyListField(sourceBulk, localBulk, Patch_GodHands.BulkScoopedRoofsField);
            CopyListField(sourceBulk, localBulk, Patch_GodHands.BulkScoopedResourcesField);
            CopyListField(sourceBulk, localBulk, Patch_GodHands.BulkScoopedResourceCountsField);
        }

        private static void CopyListField(object source, object target, FieldInfo field)
        {
            if (field == null)
                return;
            IList sourceList = field.GetValue(source) as IList;
            IList targetList = field.GetValue(target) as IList;
            if (sourceList == null || targetList == null)
                return;
            targetList.Clear();
            foreach (object item in sourceList)
                targetList.Add(item);
        }

        private static Designator FindSelectedDesignator(Type type)
        {
            try
            {
                Designator selected = Find.DesignatorManager?.SelectedDesignator;
                return selected != null && type.IsInstanceOfType(selected) ? selected : null;
            }
            catch
            {
            }
            return null;
        }
    }
}
