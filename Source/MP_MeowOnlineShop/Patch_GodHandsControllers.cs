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

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Synchronizes God Hands and God Wrench controller boundaries, weapon
    /// handling, magnifier scale changes and precision-adjust transforms.
    /// </summary>
    internal static class Patch_GodHandsControllers
    {
        private static readonly Dictionary<int, IntVec3> LastGodHandDragCell = new Dictionary<int, IntVec3>();
        private static readonly Dictionary<int, int> LastGodHandDragTick = new Dictionary<int, int>();
        private static readonly Dictionary<int, IntVec3> LastWrenchDragCell = new Dictionary<int, IntVec3>();
        private static readonly Dictionary<int, int> LastWrenchDragTick = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> LastPrecisionTick = new Dictionary<int, int>();
        private static readonly FieldInfo WrenchTurretHandlerField =
            Patch_GodHands.WrenchControllerType == null ? null : AccessTools.Field(Patch_GodHands.WrenchControllerType, "turretHandler");

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            TryPatch(harmony, Patch_GodHands.ControllerType, "TryStartGrab", nameof(GodHandStartGrabPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.ControllerType, "TryStartGrabItem", nameof(GodHandStartGrabItemPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.ControllerType, "UpdateDragged", nameof(GodHandDragPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.ControllerType, "ReleaseGrab", nameof(GodHandReleasePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.ControllerType, "ForceReleaseGrab", nameof(GodHandForceReleasePrefix), Priority.Normal);

            TryPatch(harmony, Patch_GodHands.PawnHandlerType, "Update", nameof(PawnHandlerUpdatePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WeaponHandlerType, "Update", nameof(WeaponHandlerUpdatePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WeaponHandlerType, "ToggleShootingMode", nameof(ToggleShootingPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WeaponHandlerType, "ToggleMeleeMode", nameof(ToggleMeleePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.ControllerType, "ShootInShootingMode", nameof(ShootPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.ControllerType, "ResetMouseRelease", nameof(ResetMouseReleasePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.ControllerType, "Update", nameof(GodHandUpdatePauseCancelPrefix), Priority.First + 2);

            TryPatch(harmony, Patch_GodHands.WrenchControllerType, "TryStartGrab", nameof(WrenchStartGrabPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WrenchControllerType, "UpdateDraggedThing", nameof(WrenchDragPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WrenchControllerType, "ReleaseGrab", nameof(WrenchReleasePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WrenchControllerType, "Update", nameof(WrenchUpdatePauseCancelPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WrenchControllerType, "StartBulkScoop", nameof(WrenchStartBulkPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WrenchTurretHandlerType, "TrySnatch", nameof(WrenchSnatchPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.WrenchTurretHandlerType, "TryRemoveFromPawn", nameof(WrenchRemoveFromPawnPrefix), Priority.First + 2);

            TryPatch(harmony, Patch_GodHands.MagnifierControllerType, "IncreaseScale", nameof(MagnifierIncreasePrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.MagnifierControllerType, "DecreaseScale", nameof(MagnifierDecreasePrefix), Priority.First + 2);

            TryPatch(harmony, Patch_GodHands.PrecisionDialogType, "ApplyChanges", nameof(PrecisionApplyChangesPrefix), Priority.First + 2);
            TryPatch(harmony, Patch_GodHands.PrecisionDialogType, "SetTurretRotation", nameof(PrecisionTurretRotationPrefix), Priority.First + 2);
        }

        private static void TryPatch(Harmony harmony, Type type, string methodName, string prefixName, int priority)
        {
            try
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
                MethodInfo prefix = AccessTools.Method(typeof(Patch_GodHandsControllers), prefixName);
                if (method == null || prefix == null)
                    return;
                harmony.Patch(method, prefix: new HarmonyMethod(prefix) { priority = priority });
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] God Hands controller patch {type?.FullName}.{methodName} failed: {e.Message}");
            }
        }

        private static bool ShouldInterceptUi()
        {
            return MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;
        }

        private static bool GodHandStartGrabPrefix(object __instance, Map map, Pawn target, IntVec3 startCell)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            bool rangeMode = UnityInputCompat.GetKey(KeyCode.LeftShift) || UnityInputCompat.GetKey(KeyCode.RightShift);
            float radius = Patch_GodHands.GetSettingsValue("godHandGrabRadius", 3f);
            GodHandSync.SyncGodHandStartGrab(
                playerId,
                map.uniqueID,
                target?.thingIDNumber ?? 0,
                0,
                startCell.x,
                startCell.z,
                rangeMode,
                true,
                radius);
            return false;
        }

        private static bool GodHandStartGrabItemPrefix(object __instance, Map map, Thing target, IntVec3 startCell)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            bool rangeMode = UnityInputCompat.GetKey(KeyCode.LeftShift) || UnityInputCompat.GetKey(KeyCode.RightShift);
            float radius = Patch_GodHands.GetSettingsValue("godHandGrabRadius", 3f);
            GodHandSync.SyncGodHandStartGrab(
                playerId,
                map.uniqueID,
                0,
                target?.thingIDNumber ?? 0,
                startCell.x,
                startCell.z,
                rangeMode,
                false,
                radius);
            return false;
        }

        private static bool GodHandDragPrefix(object __instance, Map map, IntVec3 cell, float time)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            int tick = Find.TickManager.TicksGame;
            if (LastGodHandDragCell.TryGetValue(map.uniqueID, out IntVec3 last) &&
                last == cell &&
                LastGodHandDragTick.TryGetValue(map.uniqueID, out int lastTick) &&
                tick - lastTick < 2)
                return false;
            LastGodHandDragCell[map.uniqueID] = cell;
            LastGodHandDragTick[map.uniqueID] = tick;
            GodHandSync.SyncGodHandDrag(playerId, map.uniqueID, cell.x, cell.z);
            return false;
        }

        private static bool GodHandReleasePrefix(object __instance, Map map, IntVec3 cell, float time)
        {
            if (!ShouldInterceptUi() || map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncGodHandRelease(playerId, map.uniqueID, cell.x, cell.z);
            return false;
        }

        private static bool GodHandForceReleasePrefix(object __instance)
        {
            if (!ShouldInterceptUi())
                return true;
            Map map = Find.CurrentMap;
            if (map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncGodHandForceRelease(playerId, map.uniqueID);
            return false;
        }

        private static bool PawnHandlerUpdatePrefix(object __instance, Map map, Vector3 mousePos, float time)
        {
            // Per-frame drag position changes are command-driven in MP.
            return !MP.IsInMultiplayer;
        }

        private static bool WeaponHandlerUpdatePrefix(object __instance, Map map, Vector3 mousePos)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!MP.IsInMultiplayer)
                return true;
            if (ShouldInterceptUi() && __instance != null && Patch_GodHands.WeaponMeleeModeField != null &&
                (bool)(Patch_GodHands.WeaponMeleeModeField.GetValue(__instance) ?? false))
            {
                TryDispatchMeleeHit(__instance, map, mousePos);
            }
            return false;
        }

        private static void TryDispatchMeleeHit(object weaponHandler, Map map, Vector3 mousePos)
        {
            if (map == null || Patch_GodHands.WeaponCoreField == null || Patch_GodHands.WeaponHitListField == null)
                return;
            object core = Patch_GodHands.WeaponCoreField.GetValue(weaponHandler);
            IList things = core == null ? null : Patch_GodHands.ControllerGrabbedThingsField.GetValue(core) as IList;
            Thing weapon = null;
            if (things != null)
            {
                foreach (object item in things)
                {
                    if (item is Thing t && t.def != null && t.def.IsMeleeWeapon)
                    {
                        weapon = t;
                        break;
                    }
                }
            }
            if (weapon == null)
                return;
            Pawn target = Patch_GodHands.InvokeStatic(Patch_GodHands.GrabbingUtilsGetPawnAtMethod, map, IntVec3.FromVector3(mousePos)) as Pawn;
            var hitList = Patch_GodHands.WeaponHitListField.GetValue(weaponHandler) as HashSet<Thing>;
            if (target != null && !target.Dead)
            {
                if (hitList != null && hitList.Contains(target))
                    return;
                hitList?.Add(target);
                int playerId = Patch_GodHands.GetLocalPlayerId();
                if (playerId >= 0)
                    GodHandSync.SyncGodHandMeleeHit(playerId, map.uniqueID, weapon.thingIDNumber, target.thingIDNumber);
            }
            else
            {
                hitList?.Clear();
            }
        }

        private static bool ToggleShootingPrefix(object __instance)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || __instance == null || Find.CurrentMap == null)
                return true;
            object core = Patch_GodHands.WeaponCoreField?.GetValue(__instance);
            IList things = core == null ? null : Patch_GodHands.ControllerGrabbedThingsField.GetValue(core) as IList;
            Thing weapon = null;
            if (things != null)
            {
                foreach (object item in things)
                {
                    if (item is Thing t && t.def != null && t.def.IsRangedWeapon)
                    {
                        weapon = t;
                        break;
                    }
                }
            }
            if (weapon == null)
                return true;
            Vector3 pos = UI.MouseMapPosition();
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncGodHandToggleShooting(playerId, Find.CurrentMap.uniqueID, weapon.thingIDNumber, pos.x, pos.z);
            Patch_GodHands.SetField(__instance, Patch_GodHands.WeaponShootingModeField, true);
            Patch_GodHands.SetField(__instance, Patch_GodHands.WeaponMeleeModeField, false);
            Patch_GodHands.SetField(__instance, Patch_GodHands.WeaponFixedWeaponPosField, pos);
            Patch_GodHands.SetField(__instance, Patch_GodHands.WeaponWaitForReleaseField, true);
            return false;
        }

        private static bool ToggleMeleePrefix(object __instance)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || __instance == null || Find.CurrentMap == null)
                return true;
            object core = Patch_GodHands.WeaponCoreField?.GetValue(__instance);
            IList things = core == null ? null : Patch_GodHands.ControllerGrabbedThingsField.GetValue(core) as IList;
            Thing weapon = null;
            if (things != null)
            {
                foreach (object item in things)
                {
                    if (item is Thing t && t.def != null && t.def.IsMeleeWeapon)
                    {
                        weapon = t;
                        break;
                    }
                }
            }
            if (weapon == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncGodHandToggleMelee(playerId, Find.CurrentMap.uniqueID, weapon.thingIDNumber);
            Patch_GodHands.SetField(__instance, Patch_GodHands.WeaponMeleeModeField, true);
            Patch_GodHands.SetField(__instance, Patch_GodHands.WeaponShootingModeField, false);
            Patch_GodHands.SetField(__instance, Patch_GodHands.WeaponHitListField, new HashSet<Thing>());
            return false;
        }

        private static bool ShootPrefix(object __instance, Map map, IntVec3 target, float time)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncGodHandShoot(playerId, map.uniqueID, 0, target.x, target.z);
            return false;
        }

        private static bool ResetMouseReleasePrefix(object __instance)
        {
            if (!ShouldInterceptUi() || __instance == null || Find.CurrentMap == null)
                return true;
            object weaponHandler = Patch_GodHands.ControllerWeaponHandlerField?.GetValue(__instance);
            if (weaponHandler == null ||
                !(bool)(Patch_GodHands.WeaponWaitForReleaseField?.GetValue(weaponHandler) ?? false))
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncGodHandResetMouseRelease(playerId, Find.CurrentMap.uniqueID);
            Patch_GodHands.SetField(weaponHandler, Patch_GodHands.WeaponWaitForReleaseField, false);
            return false;
        }

        private static bool GodHandUpdatePauseCancelPrefix(object __instance)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
            {
                GodHandSync.CancelPausedLocalSessions();
                return false;
            }
            return true;
        }

        private static bool WrenchUpdatePauseCancelPrefix(object __instance)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
            {
                GodHandSync.CancelPausedLocalSessions();
                return false;
            }
            return true;
        }

        private static bool WrenchStartGrabPrefix(object __instance, Map map, Thing thing, IntVec3 start)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null || thing == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncWrenchStartGrab(playerId, map.uniqueID, thing.thingIDNumber, start.x, start.z);
            return false;
        }

        private static bool WrenchDragPrefix(object __instance, Map map, IntVec3 currentCell)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            int tick = Find.TickManager.TicksGame;
            if (LastWrenchDragCell.TryGetValue(map.uniqueID, out IntVec3 last) &&
                last == currentCell &&
                LastWrenchDragTick.TryGetValue(map.uniqueID, out int lastTick) &&
                tick - lastTick < 2)
                return false;
            LastWrenchDragCell[map.uniqueID] = currentCell;
            LastWrenchDragTick[map.uniqueID] = tick;
            GodHandSync.SyncWrenchDrag(playerId, map.uniqueID, currentCell.x, currentCell.z);
            return false;
        }

        private static bool WrenchReleasePrefix(object __instance, Map map, IntVec3 cell)
        {
            if (!ShouldInterceptUi() || map == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            GodHandSync.SyncWrenchRelease(playerId, map.uniqueID, cell.x, cell.z);
            return false;
        }

        private static bool WrenchStartBulkPrefix(object __instance, IntVec3 start, object type)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || Find.CurrentMap == null)
                return true;
            int playerId = Patch_GodHands.GetLocalPlayerId();
            if (playerId < 0)
                return true;
            int typeInt = type == null ? 0 : Convert.ToInt32(type);
            float radius = Patch_GodHands.GetSettingsValue("godHandGrabRadius", 3f);
            GodHandSync.SyncWrenchStartBulk(playerId, Find.CurrentMap.uniqueID, start.x, start.z, typeInt, radius);
            return false;
        }

        private static bool WrenchSnatchPrefix(object __instance, Map map, Thing t, Pawn user)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null || t == null)
                return true;
            GodHandSync.SyncWrenchSnatchTurret(map.uniqueID, t.thingIDNumber);
            return false;
        }

        private static bool WrenchRemoveFromPawnPrefix(object __instance, Map map, Pawn p, object head)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || map == null || p == null || head == null)
                return true;
            int headId = head is Thing thing ? thing.thingIDNumber : 0;
            if (headId == 0)
                return true;
            GodHandSync.SyncWrenchRemoveFromPawn(map.uniqueID, p.thingIDNumber, headId);
            return false;
        }

        private static bool MagnifierIncreasePrefix(object __instance)
        {
            return MagnifierScalePrefix(__instance, 1);
        }

        private static bool MagnifierDecreasePrefix(object __instance)
        {
            return MagnifierScalePrefix(__instance, 2);
        }

        private static bool MagnifierScalePrefix(object __instance, int action)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || __instance == null || Find.CurrentMap == null)
                return true;
            Pawn pawn = Patch_GodHands.MagnifierTargetPawnField?.GetValue(__instance) as Pawn;
            if (pawn == null)
                return true;
            bool visualMode = (bool)(Patch_GodHands.MagnifierVisualScaleModeField?.GetValue(__instance) ?? true);
            GodHandSync.SyncMagnifier(Find.CurrentMap.uniqueID, pawn.thingIDNumber, action, visualMode);
            return false;
        }

        private static bool PrecisionApplyChangesPrefix(object __instance)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || __instance == null || Find.CurrentMap == null)
                return true;
            Thing thing = Patch_GodHands.PrecisionTargetThingField?.GetValue(__instance) as Thing;
            object component = Patch_GodHands.PrecisionCurrentComponentField?.GetValue(__instance);
            object transform = Patch_GodHands.PrecisionTransformField?.GetValue(__instance);
            if (thing == null || transform == null)
                return true;

            int tick = Find.TickManager.TicksGame;
            if (LastPrecisionTick.TryGetValue(thing.thingIDNumber, out int last) && tick - last < 2)
                return false;
            LastPrecisionTick[thing.thingIDNumber] = tick;

            string key = GetPrecisionComponentKey(component, thing);
            Vector2 offset = (Vector2)(Patch_GodHands.PrecisionOffsetField?.GetValue(__instance) ?? Vector2.zero);
            float rotation = (float)(Patch_GodHands.PrecisionRotationField?.GetValue(__instance) ?? 0f);
            Vector3 scale = (Vector3)(Patch_GodHands.PrecisionScaleField?.GetValue(__instance) ?? Vector3.one);
            int layer = (int)(Patch_GodHands.PrecisionDrawLayerField?.GetValue(__instance) ?? 0);
            object overrideRot = Patch_GodHands.PrecisionOverrideRotField?.GetValue(__instance);
            int rot = overrideRot is int r ? r : -1;
            GodHandSync.SyncPrecisionTransform(
                Find.CurrentMap.uniqueID,
                thing.thingIDNumber,
                key,
                offset.x,
                offset.y,
                rotation,
                scale.x,
                scale.y,
                scale.z,
                layer,
                rot);
            return false;
        }

        private static bool PrecisionTurretRotationPrefix(object __instance, float rotation)
        {
            if (Patch_GodHands.PausedGodHandsBlocked())
                return false;
            if (!ShouldInterceptUi() || __instance == null || Find.CurrentMap == null)
                return true;
            Thing thing = Patch_GodHands.PrecisionTargetThingField?.GetValue(__instance) as Thing;
            if (thing == null)
                return true;
            GodHandSync.SyncPrecisionTurretRotation(Find.CurrentMap.uniqueID, thing.thingIDNumber, rotation);
            return false;
        }

        private static string GetPrecisionComponentKey(object component, Thing thing)
        {
            if (component == null || thing == null)
                return "";
            FieldInfo nameField = AccessTools.Field(component.GetType(), "name");
            string name = nameField?.GetValue(component) as string;
            return name == "turretTop" ? thing.thingIDNumber + "_turretTop" : "";
        }
    }
}
