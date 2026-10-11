using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// ASEL constructor/teleport/transform gizmos target a cell and then run
    /// their real executor in OnTargetSelected callbacks. The callbacks write
    /// saved lastUsedTick and spawn/move/transform buildings, so they are
    /// redirected to sync commands only when Multiplayer.ShouldSync is true
    /// (UI action).
    /// </summary>
    internal static class Patch_AselTargetingMp
    {
        private const string PackageId = "asel.monolynrace";

        private static bool _applied;
        private static MethodInfo _constructorOnTarget;
        private static MethodInfo _teleportOnTarget;
        private static MethodInfo _transformOnTarget;
        private static MethodInfo _spawnLightBuilder;
        private static MethodInfo _moveBuilding;
        private static MethodInfo _transform;
        private static FieldInfo _constructorLastUsed;
        private static FieldInfo _teleportLastUsed;
        private static FieldInfo _transformLastUsed;
        private static Type _multiplayerClientType;
        private static PropertyInfo _shouldSyncProperty;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Asel targeting sync skipped (target mod not active).");
                    return;
                }

                Type constructorType = AccessTools.TypeByName("ASEL.CompConstructor");
                Type teleportType = AccessTools.TypeByName("ASEL.CompTeleport");
                Type transformType = AccessTools.TypeByName("ASEL.CompTransform");
                _constructorOnTarget = constructorType == null
                    ? null
                    : AccessTools.Method(constructorType, "OnTargetSelected", new[] { typeof(LocalTargetInfo) });
                _teleportOnTarget = teleportType == null
                    ? null
                    : AccessTools.Method(teleportType, "OnTargetSelected", new[] { typeof(LocalTargetInfo) });
                _transformOnTarget = transformType == null
                    ? null
                    : AccessTools.Method(transformType, "OnTargetSelected", new[] { typeof(LocalTargetInfo) });
                _spawnLightBuilder = constructorType == null
                    ? null
                    : AccessTools.Method(constructorType, "SpawnLightBuilder", new[] { typeof(IntVec3) });
                _moveBuilding = teleportType == null
                    ? null
                    : AccessTools.Method(teleportType, "MoveBuilding", new[] { typeof(IntVec3) });
                _transform = transformType == null
                    ? null
                    : AccessTools.Method(transformType, "Transform", new[] { typeof(IntVec3) });
                _constructorLastUsed = constructorType == null
                    ? null
                    : AccessTools.Field(constructorType, "lastUsedTick");
                _teleportLastUsed = teleportType == null
                    ? null
                    : AccessTools.Field(teleportType, "lastUsedTick");
                _transformLastUsed = transformType == null
                    ? null
                    : AccessTools.Field(transformType, "lastUsedTick");

                if (_constructorOnTarget == null || _teleportOnTarget == null ||
                    _transformOnTarget == null || _spawnLightBuilder == null ||
                    _moveBuilding == null || _transform == null ||
                    _constructorLastUsed == null || _teleportLastUsed == null ||
                    _transformLastUsed == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Asel targeting targets not resolved; patch skipped.");
                    return;
                }

                MethodInfo syncConstructor = AccessTools.Method(
                    typeof(Patch_AselTargetingMp),
                    nameof(SyncConstructor),
                    new[] { typeof(Map), typeof(int), typeof(int), typeof(int) });
                MethodInfo syncTeleport = AccessTools.Method(
                    typeof(Patch_AselTargetingMp),
                    nameof(SyncTeleport),
                    new[] { typeof(Map), typeof(int), typeof(int), typeof(int) });
                MethodInfo syncTransform = AccessTools.Method(
                    typeof(Patch_AselTargetingMp),
                    nameof(SyncTransform),
                    new[] { typeof(Map), typeof(int), typeof(int), typeof(int) });
                if (syncConstructor == null || syncTeleport == null || syncTransform == null)
                    return;

                try
                {
                    MP.RegisterSyncMethod(syncConstructor, null);
                    MP.RegisterSyncMethod(syncTeleport, null);
                    MP.RegisterSyncMethod(syncTransform, null);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Asel targeting sync registration failed: " + e.Message);
                    return;
                }

                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_AselTargetingMp),
                    nameof(OnTargetSelectedPrefix));
                if (prefix == null)
                    return;

                harmony.Patch(_constructorOnTarget, prefix: new HarmonyMethod(prefix));
                harmony.Patch(_teleportOnTarget, prefix: new HarmonyMethod(prefix));
                harmony.Patch(_transformOnTarget, prefix: new HarmonyMethod(prefix));
                Log.Message("[MP-MeowOnlineShop] Asel targeting MP patch active: 3 sync methods.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Asel targeting MP compat restore failed: " + e.Message);
            }
        }

        private static bool OnTargetSelectedPrefix(object __instance, LocalTargetInfo targetInfo)
        {
            if (!IsUiSyncContext())
                return true;

            ThingComp comp = __instance as ThingComp;
            Thing parent = comp?.parent;
            Map map = parent?.Map;
            if (parent == null || map == null)
                return true;

            IntVec3 cell = targetInfo.Cell;
            // Retain the native local reject/reopen flow. Check again during
            // replay because another command may occupy the destination.
            MethodInfo validator = AccessTools.Method(__instance.GetType(), "IsValidTeleportLocation", new[] { typeof(IntVec3) });
            if (validator == null || !(bool)validator.Invoke(__instance, new object[] { cell }))
                return true;
            if (ReferenceEquals(__instance.GetType(), AccessTools.TypeByName("ASEL.CompConstructor")))
                SyncConstructor(map, parent.thingIDNumber, cell.x, cell.z);
            else if (ReferenceEquals(__instance.GetType(), AccessTools.TypeByName("ASEL.CompTeleport")))
                SyncTeleport(map, parent.thingIDNumber, cell.x, cell.z);
            else if (ReferenceEquals(__instance.GetType(), AccessTools.TypeByName("ASEL.CompTransform")))
                SyncTransform(map, parent.thingIDNumber, cell.x, cell.z);
            else
                return true;

            return false;
        }

        public static void SyncConstructor(Map map, int thingId, int cellX, int cellZ)
        {
            object comp = FindComp(map, thingId, "ASEL.CompConstructor");
            if (comp == null || _spawnLightBuilder == null || _constructorLastUsed == null)
                return;
            if (!CanExecute(comp, new IntVec3(cellX, 0, cellZ), _constructorLastUsed)) return;

            _spawnLightBuilder.Invoke(comp, new object[] { new IntVec3(cellX, 0, cellZ) });
            _constructorLastUsed.SetValue(comp, Find.TickManager.TicksGame);
        }

        public static void SyncTeleport(Map map, int thingId, int cellX, int cellZ)
        {
            object comp = FindComp(map, thingId, "ASEL.CompTeleport");
            if (comp == null || _moveBuilding == null || _teleportLastUsed == null)
                return;
            if (!CanExecute(comp, new IntVec3(cellX, 0, cellZ), _teleportLastUsed)) return;

            _moveBuilding.Invoke(comp, new object[] { new IntVec3(cellX, 0, cellZ) });
            _teleportLastUsed.SetValue(comp, Find.TickManager.TicksGame);
        }

        public static void SyncTransform(Map map, int thingId, int cellX, int cellZ)
        {
            object comp = FindComp(map, thingId, "ASEL.CompTransform");
            if (comp == null || _transform == null || _transformLastUsed == null)
                return;
            if (!CanExecute(comp, new IntVec3(cellX, 0, cellZ), _transformLastUsed)) return;

            _transform.Invoke(comp, new object[] { new IntVec3(cellX, 0, cellZ) });
            _transformLastUsed.SetValue(comp, Find.TickManager.TicksGame);
        }

        private static bool CanExecute(object instance, IntVec3 cell, FieldInfo lastUsed)
        {
            var comp = instance as ThingComp;
            if (comp?.parent == null || comp.parent.Destroyed || comp.parent.Map == null) return false;
            var validator = AccessTools.Method(comp.GetType(), "IsValidTeleportLocation", new[] { typeof(IntVec3) });
            if (validator == null || !(bool)validator.Invoke(comp, new object[] { cell })) return false;
            var cooldown = AccessTools.Field(comp.props.GetType(), "cooldownTicks");
            if (cooldown == null || cooldown.FieldType != typeof(int)) throw new MissingFieldException(comp.props.GetType().FullName, "cooldownTicks");
            return DebugSettings.ShowDevGizmos || Find.TickManager.TicksGame - (int)lastUsed.GetValue(comp) >= (int)cooldown.GetValue(comp.props);
        }

        private static object FindComp(Map map, int thingId, string typeName)
        {
            Type compType = AccessTools.TypeByName(typeName);
            if (map?.listerThings?.AllThings == null || !Find.Maps.Contains(map) || compType == null)
                return null;

            for (int i = 0; i < map.listerThings.AllThings.Count; i++)
            {
                Thing thing = map.listerThings.AllThings[i];
                if (!(thing is ThingWithComps twc) || twc.thingIDNumber != thingId ||
                    twc.AllComps == null)
                {
                    continue;
                }

                for (int c = 0; c < twc.AllComps.Count; c++)
                {
                    if (compType.IsInstanceOfType(twc.AllComps[c]))
                        return twc.AllComps[c];
                }
            }

            return null;
        }

        private static bool IsUiSyncContext()
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return false;

            try
            {
                if (_shouldSyncProperty == null)
                {
                    _multiplayerClientType = _multiplayerClientType ??
                                             AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
                    _shouldSyncProperty = AccessTools.Property(_multiplayerClientType, "ShouldSync");
                }

                return _shouldSyncProperty != null &&
                       _shouldSyncProperty.GetValue(null, null) is bool sync && sync;
            }
            catch
            {
                return false;
            }
        }
    }
}
