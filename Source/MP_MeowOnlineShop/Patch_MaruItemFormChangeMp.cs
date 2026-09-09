using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Maru Item Form Change (`ItemFormChange_Maru.CompFormChange`) adds
    /// equipment/apparel transformation gizmos. Each Command_Transform_Action
    /// invokes TryTransformInto, which destroys the old item and creates/equips
    /// a replacement with shared component state and cooldown. The postfix
    /// replaces the gizmo action with a command carrying the transform-data
    /// index so the whole outcome is replayed on every peer.
    /// </summary>
    internal static class Patch_MaruItemFormChangeMp
    {
        private const string PackageId = "vamv.maruracemod";
        private const string CompTypeName = "ItemFormChange_Maru.CompFormChange";
        private const string CommandTypeName = "ItemFormChange_Maru.Command_Transform_Action";
        private const string TryTransformMethodName = "TryTransformInto";

        private static Type _compType;
        private static Type _propsType;
        private static Type _commandType;
        private static MethodInfo _tryTransformInto;
        private static PropertyInfo _propsProperty;
        private static FieldInfo _transformDataField;
        private static FieldInfo _commandActionField;
        private static FieldInfo _commandCompField;
        private static FieldInfo _commandTransformField;
        private static ISyncMethod _syncTransform;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            _commandType = AccessTools.TypeByName(CommandTypeName);
            if (_compType == null || _commandType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Maru Item Form Change target resolution failed; patch skipped.");
                return;
            }

            _tryTransformInto = AccessTools.Method(_compType, TryTransformMethodName);
            _propsProperty = AccessTools.Property(_compType, "Props");
            _propsType = _propsProperty?.PropertyType;
            _transformDataField = _propsType == null ? null : AccessTools.Field(_propsType, "transformData");
            _commandActionField = AccessTools.Field(_commandType, "action");
            _commandCompField = AccessTools.Field(_commandType, "compFormChange");
            _commandTransformField = AccessTools.Field(_commandType, "transformData");

            if (_tryTransformInto == null || _propsProperty == null || _propsType == null ||
                _transformDataField == null || _commandActionField == null ||
                _commandCompField == null || _commandTransformField == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Maru Item Form Change field/method resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncTransform = MP.RegisterSyncMethod(
                        typeof(Patch_MaruItemFormChangeMp),
                        nameof(SyncTransform))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Maru Item Form Change sync registration failed: " + e.Message);
                return;
            }

            MethodInfo heldGizmos = AccessTools.Method(_compType, "HeldGizmos", new[] { typeof(Pawn) });
            MethodInfo postfix = AccessTools.Method(
                typeof(Patch_MaruItemFormChangeMp),
                nameof(HeldGizmosPostfix));
            if (heldGizmos == null || postfix == null)
                return;

            try
            {
                harmony.Patch(heldGizmos, postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] Maru Item Form Change MP patch active: transforms sync.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Maru Item Form Change gizmo patch failed: " + e.Message);
            }
        }

        private static void HeldGizmosPostfix(Pawn pawn, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || pawn?.Map == null || _syncTransform == null)
                return;

            var list = __result.ToList();
            foreach (Gizmo gizmo in list)
            {
                if (!_commandType.IsInstanceOfType(gizmo))
                    continue;

                object compObj = _commandCompField.GetValue(gizmo);
                object tsd = _commandTransformField.GetValue(gizmo);
                if (compObj == null || tsd == null || !(compObj is ThingComp comp) ||
                    comp.parent == null || pawn?.Map == null)
                {
                    continue;
                }

                int index = GetTransformIndex(comp, tsd);
                if (index < 0)
                    continue;

                Map map = pawn.Map;
                _commandActionField.SetValue(
                    gizmo,
                    (Action)(() => _syncTransform.DoSync(
                        null,
                        map.Index,
                        comp.parent.thingIDNumber,
                        pawn.thingIDNumber,
                        index,
                        false)));
            }

            __result = list;
        }

        private static int GetTransformIndex(ThingComp comp, object tsd)
        {
            object props = _propsProperty.GetValue(comp);
            if (props == null || _transformDataField.GetValue(props) is not IEnumerable list)
                return -1;

            int index = 0;
            foreach (object item in list)
            {
                if (ReferenceEquals(item, tsd))
                    return index;
                index++;
            }

            return -1;
        }

        public static void SyncTransform(
            int mapIndex,
            int parentThingId,
            int pawnId,
            int transformIndex,
            bool isRevert)
        {
            if (_tryTransformInto == null || _propsProperty == null || _transformDataField == null)
                return;

            Pawn pawn = FindPawnById(FindMap(mapIndex), pawnId);
            Thing parent = MP.GetThingById(parentThingId) as Thing;
            if (pawn == null || !(parent is ThingWithComps parentWithComps) || parentWithComps.AllComps == null)
                return;

            object comp = parentWithComps.AllComps.FirstOrDefault(c => c != null && _compType.IsInstanceOfType(c));
            if (comp == null)
                return;

            object tsd = GetTransformData(comp, transformIndex, isRevert);
            if (tsd == null)
                return;

            _tryTransformInto.Invoke(comp, new[] { pawn, tsd });
        }

        private static object GetTransformData(object comp, int index, bool isRevert)
        {
            object props = _propsProperty.GetValue(comp);
            if (props == null || _transformDataField.GetValue(props) is not IList list)
                return null;

            return index >= 0 && index < list.Count ? list[index] : null;
        }

        private static Map FindMap(int mapIndex)
        {
            return Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
        }

        private static Pawn FindPawnById(Map map, int thingId)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
                return null;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn != null && pawn.thingIDNumber == thingId)
                    return pawn;
            }

            return null;
        }
    }
}
