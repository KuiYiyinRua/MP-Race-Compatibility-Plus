using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Synchronizes Ancot's aerocraft state-changing gizmo callbacks.
    /// The landing designator itself cannot be reconstructed by Multiplayer's
    /// generic Designator serializer, so its final cell selection is routed through
    /// a stable command carrying only the aerocraft, cell, and rotation.
    /// </summary>
    internal static class Patch_AncotAerocraftMp
    {
        private const string AerocraftTypeName = "AncotLibrary.Building_Aerocraft";
        private const string DesignatorLandTypeName = "AncotLibrary.Designator_Land";
        private const string GetGizmosMethodName = "GetGizmos";

        private static bool _applied;
        private static Type _aerocraftType;
        private static Type _designatorLandType;
        private static FieldInfo _designatorThingField;
        private static FieldInfo _designatorRotationField;
        private static FieldInfo _flightStateField;
        private static FieldInfo _landRotationField;
        private static MethodInfo _setTargetDestinationMethod;
        private static bool _landingSyncRegistered;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            _aerocraftType = AccessTools.TypeByName(AerocraftTypeName);
            _designatorLandType = AccessTools.TypeByName(DesignatorLandTypeName);
            if (_aerocraftType == null || _designatorLandType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot aerocraft MP patch skipped: aerocraft or landing designator type not resolved.");
                return;
            }

            MethodInfo getGizmos = AccessTools.Method(_aerocraftType, GetGizmosMethodName, Type.EmptyTypes);
            if (getGizmos == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot aerocraft MP patch skipped: " + AerocraftTypeName + "." + GetGizmosMethodName + " not resolved.");
                return;
            }

            _designatorThingField = AccessTools.Field(_designatorLandType, "thing");
            _designatorRotationField = AccessTools.Field(_designatorLandType, "placingRot");
            _flightStateField = AccessTools.Field(_aerocraftType, "FlightState");
            _landRotationField = AccessTools.Field(_aerocraftType, "landRotation");
            _setTargetDestinationMethod = AccessTools.Method(_aerocraftType, "SetTargetDestination", new[] { typeof(IntVec3) });
            if (_designatorThingField == null || _designatorRotationField == null ||
                _flightStateField == null || _landRotationField == null ||
                _setTargetDestinationMethod == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot aerocraft MP patch skipped: landing callback fields or SetTargetDestination not resolved.");
                return;
            }

            int destinationAndTakeoffRegistered = 0;
            try
            {
                MP.RegisterSyncMethod(typeof(Patch_AncotAerocraftMp), nameof(SyncLand))
                    .SetContext(SyncContext.CurrentMap);
                _landingSyncRegistered = true;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot aerocraft MP patch: landing command registration failed: " + e.Message);
            }

            for (int ordinal = 1; ordinal <= 2; ordinal++)
            {
                try
                {
                    // Current 1.6 assembly resolves these as <GetGizmos>b__55_1 and _2.
                    // Destination changes and takeoff are direct callbacks on the aerocraft.
                    MP.RegisterSyncMethodLambda(_aerocraftType, GetGizmosMethodName, ordinal);
                    destinationAndTakeoffRegistered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ancot aerocraft MP patch: failed to register GetGizmos lambda " + ordinal + ": " + e.Message);
                }
            }

            MethodInfo designateSingleCell = AccessTools.Method(_designatorLandType, "DesignateSingleCell", new[] { typeof(IntVec3) });
            if (designateSingleCell == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot aerocraft MP patch skipped: Designator_Land.DesignateSingleCell not resolved.");
                return;
            }

            harmony.Patch(
                designateSingleCell,
                prefix: new HarmonyMethod(
                    typeof(Patch_AncotAerocraftMp),
                    nameof(DesignateSingleCellPrefix))
                {
                    priority = Priority.First + 10
                });

            Log.Message("[MP-MeowOnlineShop] Ancot aerocraft MP patch active: landing command=" + _landingSyncRegistered + ", destination/takeoff lambdas=" + destinationAndTakeoffRegistered + "/2, landing designator bypass=" + _landingSyncRegistered + ".");
        }

        private static bool DesignateSingleCellPrefix(object __instance, IntVec3 c)
        {
            if (!_landingSyncRegistered || !MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand ||
                __instance == null || !_designatorLandType.IsInstanceOfType(__instance))
                return true;

            Thing aerocraft = _designatorThingField.GetValue(__instance) as Thing;
            if (aerocraft == null || !_aerocraftType.IsInstanceOfType(aerocraft))
                return true;

            Rot4 rotation = (Rot4)_designatorRotationField.GetValue(__instance);
            SyncLand(aerocraft, c, rotation);
            Find.DesignatorManager.Deselect();
            return false;
        }

        public static void SyncLand(Thing aerocraft, IntVec3 cell, Rot4 rotation)
        {
            if (aerocraft == null || _aerocraftType == null || !_aerocraftType.IsInstanceOfType(aerocraft))
                return;

            try
            {
                object landingState = Enum.Parse(_flightStateField.FieldType, "Landing");
                _flightStateField.SetValue(aerocraft, landingState);
                _landRotationField.SetValue(aerocraft, rotation);
                _setTargetDestinationMethod.Invoke(aerocraft, new object[] { cell });
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot aerocraft landing replay failed: " + e.Message);
            }
        }
    }
}
