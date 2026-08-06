using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Synchronizes God Hands designator paths that bypass Multiplayer's
    /// generic DesignateSingleCell/DesignateThing handling, plus direct
    /// SelectedUpdate/HandleInput mutations.
    /// </summary>
    internal static class Patch_GodHandsDesignators
    {
        private static readonly FieldInfo WrenchControllerField =
            Patch_GodHands.DesignatorGodWrenchType == null ? null : AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "controller");
        private static readonly FieldInfo WrenchIsDraggingField =
            Patch_GodHands.DesignatorGodWrenchType == null ? null : AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "isDragging");
        private static readonly FieldInfo WrenchDragStartCellField =
            Patch_GodHands.DesignatorGodWrenchType == null ? null : AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "dragStartCell");
        private static readonly FieldInfo WrenchDraggedRoofField =
            Patch_GodHands.DesignatorGodWrenchType == null ? null : AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "draggedRoof");
        private static readonly FieldInfo WrenchDraggedResourceField =
            Patch_GodHands.DesignatorGodWrenchType == null ? null : AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "draggedResource");
        private static readonly FieldInfo WrenchDraggedResourceCountField =
            Patch_GodHands.DesignatorGodWrenchType == null ? null : AccessTools.Field(Patch_GodHands.DesignatorGodWrenchType, "draggedResourceCount");
        private static readonly FieldInfo AssistantModeField =
            Patch_GodHands.DesignatorAssistantType == null ? null : AccessTools.Field(Patch_GodHands.DesignatorAssistantType, "currentAssistantMode");
        private static readonly FieldInfo PokeTargetPawnField =
            Patch_GodHands.PokeControllerType == null ? null : AccessTools.Field(Patch_GodHands.PokeControllerType, "targetPawn");
        private static readonly FieldInfo PokeTargetCorpseField =
            Patch_GodHands.PokeControllerType == null ? null : AccessTools.Field(Patch_GodHands.PokeControllerType, "targetCorpse");
        private static readonly FieldInfo PokeStartCellField =
            Patch_GodHands.PokeControllerType == null ? null : AccessTools.Field(Patch_GodHands.PokeControllerType, "startCell");
        private static readonly FieldInfo PokeIsInteractingField =
            Patch_GodHands.PokeControllerType == null ? null : AccessTools.Field(Patch_GodHands.PokeControllerType, "isInteracting");

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            TryPatch(harmony, Patch_GodHands.DesignatorProtectionType, "DesignateSingleCell",
                nameof(ProtectionDesignateSingleCellPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.DesignatorProtectionType, "DesignateThing",
                nameof(ProtectionDesignateThingPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.DesignatorAssistantType, "DesignateSingleCell",
                nameof(AssistantDesignateSingleCellPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.DesignatorAssistantType, "DesignateThing",
                nameof(AssistantDesignateThingPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.DesignatorCleaningType, "DesignateSingleCell",
                nameof(CleaningDesignateSingleCellPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.DesignatorJudgmentType, "DesignateSingleCell",
                nameof(JudgmentDesignateSingleCellPrefix), Priority.First + 2);

            TryPatch(harmony, Patch_GodHands.ProtectionControllerType, "ApplyIndividual",
                nameof(ProtectionApplyIndividualPrefix), Priority.Normal);
            TryPatch(harmony, Patch_GodHands.CleaningControllerType, "CleanAreaAtPosition",
                nameof(CleanAreaPrefix), Priority.Normal);
            TryPatch(harmony, Patch_GodHands.PokeControllerType, "EndInteraction",
                nameof(PokeEndPrefix), Priority.Normal);

            TryPatch(harmony, Patch_GodHands.DesignatorGodWrenchType, "HandleRoofMode",
                nameof(RoofModePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.DesignatorGodWrenchType, "HandleDeepResourceMode",
                nameof(DeepResourceModePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WrenchControllerType, "ForceReleaseGrab",
                nameof(WrenchForceReleasePrefix), Priority.Normal);
        }

        private static void TryPatch(Harmony harmony, Type type, string methodName, string prefixName, int priority)
        {
            try
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
                MethodInfo prefix = AccessTools.Method(typeof(Patch_GodHandsDesignators), prefixName);
                if (method == null || prefix == null)
                    return;
                harmony.Patch(method, prefix: new HarmonyMethod(prefix) { priority = priority });
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] God Hands designator patch {type?.FullName}.{methodName} failed: {e.Message}");
            }
        }

        private static bool ShouldInterceptUi()
        {
            return MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;
        }

        private static bool ProtectionDesignateSingleCellPrefix(object __instance, IntVec3 c)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || Find.CurrentMap == null)
                return true;
            int mode = Patch_GodHands.GetSettingsValue("currentProtectionMode", 0);
            if (mode == 0 || mode == 2)
            {
                float radius = Patch_GodHands.GetSettingsValue("protectionDomeRadius", 10f);
                bool infinite = Patch_GodHands.GetSettingsValue("protectionDomeInfiniteDuration", true);
                int duration = Patch_GodHands.GetSettingsValue("protectionDurationTicks", 60000);
                GodHandSync.SyncProtectionExecute(Find.CurrentMap.uniqueID, c.x, c.z, mode, radius, infinite, duration);
            }
            return false;
        }

        private static bool ProtectionDesignateThingPrefix(object __instance, Thing t)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || Find.CurrentMap == null || !(t is Pawn pawn))
                return true;
            int mode = Patch_GodHands.GetSettingsValue("currentProtectionMode", 0);
            if (mode == 1)
                GodHandSync.SyncProtectionIndividual(Find.CurrentMap.uniqueID, pawn.thingIDNumber);
            return false;
        }

        private static bool AssistantDesignateSingleCellPrefix(object __instance, IntVec3 c)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || Find.CurrentMap == null)
                return true;
            int mode = AssistantModeField?.GetValue(__instance) is int m ? m : 0;
            bool shift = UnityInputCompat.GetKey(KeyCode.LeftShift) || UnityInputCompat.GetKey(KeyCode.RightShift);
            foreach (Thing t in c.GetThingList(Find.CurrentMap))
            {
                if (mode == 0 && t is Building_WorkTable)
                {
                    GodHandSync.SyncAssistantExecute(Find.CurrentMap.uniqueID, t.thingIDNumber, mode, shift);
                    return false;
                }
                if (mode == 1 &&
                    ((t is Pawn candidatePawn && candidatePawn.Spawned) ||
                     (t is Building_Bed candidateBed && candidateBed.Spawned)))
                {
                    GodHandSync.SyncAssistantExecute(Find.CurrentMap.uniqueID, t.thingIDNumber, mode, shift);
                    return false;
                }
            }
            return false;
        }

        private static bool AssistantDesignateThingPrefix(object __instance, Thing t)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || Find.CurrentMap == null || t == null)
                return true;
            int mode = AssistantModeField?.GetValue(__instance) is int m ? m : 0;
            bool shift = UnityInputCompat.GetKey(KeyCode.LeftShift) || UnityInputCompat.GetKey(KeyCode.RightShift);
            GodHandSync.SyncAssistantExecute(Find.CurrentMap.uniqueID, t.thingIDNumber, mode, shift);
            return false;
        }

        private static bool CleaningDesignateSingleCellPrefix(object __instance, IntVec3 c)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || Find.CurrentMap == null)
                return true;
            int mode = GetCleaningMode();
            int radius = GetCleanRadius();
            GodHandSync.SyncCleanArea(Find.CurrentMap.uniqueID, c.x, c.z, mode, radius);
            return false;
        }

        private static bool JudgmentDesignateSingleCellPrefix(object __instance, IntVec3 c)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || Find.CurrentMap == null)
                return true;
            int mode = Patch_GodHands.GetSettingsValue("currentJudgmentMode", 0);
            GodHandSync.SyncJudgmentExecute(Find.CurrentMap.uniqueID, c.x, c.z, mode);
            return false;
        }

        private static bool ProtectionApplyIndividualPrefix(Pawn pawn)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || pawn == null)
                return true;
            Map map = pawn.Map ?? Find.CurrentMap;
            if (map == null)
                return true;
            GodHandSync.SyncProtectionIndividual(map.uniqueID, pawn.thingIDNumber);
            return false;
        }

        private static bool CleanAreaPrefix(Map map, IntVec3 center)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null)
                return true;
            GodHandSync.SyncCleanArea(map.uniqueID, center.x, center.z, GetCleaningMode(), GetCleanRadius());
            return false;
        }

        private static bool PokeEndPrefix(object __instance, Map map, IntVec3 endCell)
        {
            if (!ShouldInterceptUi() || map == null || __instance == null)
                return true;
            if (PokeIsInteractingField == null || !(bool)(PokeIsInteractingField.GetValue(__instance) ?? false))
                return true;
            Pawn pawn = PokeTargetPawnField?.GetValue(__instance) as Pawn;
            Corpse corpse = PokeTargetCorpseField?.GetValue(__instance) as Corpse;
            IntVec3 start = PokeStartCellField == null ? IntVec3.Invalid : (IntVec3)PokeStartCellField.GetValue(__instance);
            GodHandSync.SyncPokeEnd(
                map.uniqueID,
                pawn?.thingIDNumber ?? 0,
                corpse?.thingIDNumber ?? 0,
                start.x,
                start.z,
                endCell.x,
                endCell.z);
            return false;
        }

        private static bool RoofModePrefix(object __instance, Map map, IntVec3 cell)
        {
            if (!ShouldInterceptUi() || map == null || __instance == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            bool mouseDown = UnityInputCompat.GetMouseButton(0);
            bool isDragging = WrenchIsDraggingField != null && (bool)(WrenchIsDraggingField.GetValue(__instance) ?? false);
            if (Patch_GodHands.PausedGodHandsBlocked() && mouseDown)
                return false;
            object controller = WrenchControllerField?.GetValue(__instance);
            if (controller != null && Patch_GodHands.WrenchIsActiveProperty != null &&
                (bool)(Patch_GodHands.WrenchIsActiveProperty.GetValue(controller, null) ?? false))
            {
                if (mouseDown)
                    GodHandSync.SyncWrenchDrag(playerId, map.uniqueID, cell.x, cell.z);
                else
                    GodHandSync.SyncWrenchRelease(playerId, map.uniqueID, cell.x, cell.z);
            }

            if (mouseDown && !isDragging)
            {
                bool shift = UnityInputCompat.GetKey(KeyCode.LeftShift) || UnityInputCompat.GetKey(KeyCode.RightShift);
                if (shift && Patch_GodHands.GetSettingsValue("godWrenchEnableBulkScoop", true))
                {
                    float radius = Patch_GodHands.GetSettingsValue("godHandGrabRadius", 3f);
                    GodHandSync.SyncWrenchStartBulk(playerId, map.uniqueID, cell.x, cell.z, 2, radius);
                    return false;
                }
                RoofDef roof = map.roofGrid.RoofAt(cell);
                if (roof != null)
                {
                    WrenchIsDraggingField?.SetValue(__instance, true);
                    WrenchDragStartCellField?.SetValue(__instance, cell);
                    WrenchDraggedRoofField?.SetValue(__instance, roof);
                    GodHandSync.SyncWrenchRoofStart(playerId, map.uniqueID, cell.x, cell.z, roof.defName);
                }
                return false;
            }

            if (!mouseDown && isDragging)
            {
                RoofDef dragged = WrenchDraggedRoofField?.GetValue(__instance) as RoofDef;
                IntVec3 start = WrenchDragStartCellField == null ? cell : (IntVec3)WrenchDragStartCellField.GetValue(__instance);
                if (dragged != null)
                    GodHandSync.SyncWrenchRoofRelease(playerId, map.uniqueID, cell.x, cell.z, dragged.defName, start.x, start.z);
                CancelWrenchDrag(__instance);
                return false;
            }

            return false;
        }

        private static bool DeepResourceModePrefix(object __instance, Map map, IntVec3 cell)
        {
            if (!ShouldInterceptUi() || map == null || __instance == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            bool mouseDown = UnityInputCompat.GetMouseButton(0);
            bool isDragging = WrenchIsDraggingField != null && (bool)(WrenchIsDraggingField.GetValue(__instance) ?? false);
            if (Patch_GodHands.PausedGodHandsBlocked() && mouseDown)
                return false;
            object controller = WrenchControllerField?.GetValue(__instance);
            if (controller != null && Patch_GodHands.WrenchIsActiveProperty != null &&
                (bool)(Patch_GodHands.WrenchIsActiveProperty.GetValue(controller, null) ?? false))
            {
                if (mouseDown)
                    GodHandSync.SyncWrenchDrag(playerId, map.uniqueID, cell.x, cell.z);
                else
                    GodHandSync.SyncWrenchRelease(playerId, map.uniqueID, cell.x, cell.z);
            }

            if (mouseDown && !isDragging)
            {
                bool shift = UnityInputCompat.GetKey(KeyCode.LeftShift) || UnityInputCompat.GetKey(KeyCode.RightShift);
                if (shift && Patch_GodHands.GetSettingsValue("godWrenchEnableBulkScoop", true))
                {
                    float radius = Patch_GodHands.GetSettingsValue("godHandGrabRadius", 3f);
                    GodHandSync.SyncWrenchStartBulk(playerId, map.uniqueID, cell.x, cell.z, 3, radius);
                    return false;
                }
                ThingDef resource = map.deepResourceGrid.ThingDefAt(cell);
                int count = map.deepResourceGrid.CountAt(cell);
                if (resource != null && count > 0)
                {
                    WrenchIsDraggingField?.SetValue(__instance, true);
                    WrenchDragStartCellField?.SetValue(__instance, cell);
                    WrenchDraggedResourceField?.SetValue(__instance, resource);
                    WrenchDraggedResourceCountField?.SetValue(__instance, count);
                    GodHandSync.SyncWrenchDeepStart(playerId, map.uniqueID, cell.x, cell.z, resource.defName);
                }
                return false;
            }

            if (!mouseDown && isDragging)
            {
                ThingDef dragged = WrenchDraggedResourceField?.GetValue(__instance) as ThingDef;
                int count = WrenchDraggedResourceCountField?.GetValue(__instance) is int c ? c : 0;
                IntVec3 start = WrenchDragStartCellField == null ? cell : (IntVec3)WrenchDragStartCellField.GetValue(__instance);
                if (dragged != null)
                    GodHandSync.SyncWrenchDeepRelease(playerId, map.uniqueID, cell.x, cell.z, dragged.defName, count, start.x, start.z);
                CancelWrenchDrag(__instance);
                return false;
            }

            return false;
        }

        private static void CancelWrenchDrag(object designator)
        {
            WrenchIsDraggingField?.SetValue(designator, false);
            WrenchDragStartCellField?.SetValue(designator, IntVec3.Invalid);
            WrenchDraggedRoofField?.SetValue(designator, null);
            WrenchDraggedResourceField?.SetValue(designator, null);
            WrenchDraggedResourceCountField?.SetValue(designator, 0);
        }

        public static void CancelLocalWrenchDrag()
        {
            try
            {
                Designator selected = Find.DesignatorManager?.SelectedDesignator;
                if (selected != null && Patch_GodHands.DesignatorGodWrenchType != null &&
                    Patch_GodHands.DesignatorGodWrenchType.IsInstanceOfType(selected))
                {
                    CancelWrenchDrag(selected);
                }
            }
            catch
            {
            }
        }

        private static bool WrenchForceReleasePrefix(object __instance)
        {
            if (!ShouldInterceptUi() || __instance == null)
                return true;
            Map map = Find.CurrentMap;
            if (map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncWrenchForceRelease(playerId, map.uniqueID);
            return false;
        }

        private static int GetCleaningMode()
        {
            object value = Patch_GodHands.GetProperty(null, Patch_GodHands.CleaningCurrentModeProperty);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static int GetCleanRadius()
        {
            object value = Patch_GodHands.GetProperty(null, Patch_GodHands.CleaningCleanRadiusProperty);
            return value == null ? 2 : Convert.ToInt32(value);
        }
    }
}
