using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Dragonian Mix (`kalospacer.dragonianmix`) overrides a targeted royal
    /// permit worker. Its OrderForceTarget drops resources on the map and its
    /// caravan gizmo calls CallResourcesToCaravan. Both mutate simulation and
    /// spend favor/permit state only on the clicking peer. Wrappers use caller,
    /// permit def, and faction primitives so the third-party worker itself does
    /// not need a custom sync worker.
    /// </summary>
    internal static class Patch_DragonianMixMp
    {
        private const string PackageId = "kalospacer.dragonianmix";
        private const string WorkerTypeName = "DragonianMix.RoyalTitlePermitWorker_DropResources";

        private static Type _workerType;
        private static FieldInfo _factionField;
        private static FieldInfo _callerField;
        private static FieldInfo _mapField;
        private static FieldInfo _freeField;
        private static MethodInfo _callResources;
        private static MethodInfo _callResourcesToCaravan;
        private static ISyncMethod _syncCallResources;
        private static ISyncMethod _syncCallResourcesToCaravan;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _workerType = AccessTools.TypeByName(WorkerTypeName);
            if (_workerType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Dragonian Mix permit worker not resolved; patch skipped.");
                return;
            }

            Type targetedType = _workerType.BaseType;
            _factionField = AccessTools.Field(_workerType, "faction");
            _callerField = targetedType == null ? null : AccessTools.Field(targetedType, "caller");
            _mapField = targetedType == null ? null : AccessTools.Field(targetedType, "map");
            _freeField = targetedType == null ? null : AccessTools.Field(targetedType, "free");
            _callResources = AccessTools.Method(_workerType, "CallResources", new[] { typeof(IntVec3) });
            _callResourcesToCaravan = AccessTools.Method(
                _workerType,
                "CallResourcesToCaravan",
                new[] { typeof(Pawn), typeof(Faction), typeof(bool) });

            if (_factionField == null || _callerField == null || _mapField == null ||
                _freeField == null || _callResources == null || _callResourcesToCaravan == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Dragonian Mix permit worker resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncCallResources = MP.RegisterSyncMethod(
                        typeof(Patch_DragonianMixMp),
                        nameof(SyncCallResources))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _syncCallResourcesToCaravan = MP.RegisterSyncMethod(
                        typeof(Patch_DragonianMixMp),
                        nameof(SyncCallResourcesToCaravan))
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Dragonian Mix permit sync registration failed: " + e.Message);
                return;
            }

            MethodInfo orderPrefix = AccessTools.Method(
                typeof(Patch_DragonianMixMp),
                nameof(OrderForceTargetPrefix));
            MethodInfo caravanPrefix = AccessTools.Method(
                typeof(Patch_DragonianMixMp),
                nameof(CallResourcesToCaravanPrefix));
            MethodInfo orderTarget = AccessTools.Method(
                _workerType,
                "OrderForceTarget",
                new[] { typeof(LocalTargetInfo) });

            try
            {
                if (orderTarget != null && orderPrefix != null)
                    harmony.Patch(orderTarget, prefix: new HarmonyMethod(orderPrefix));
                if (_callResourcesToCaravan != null && caravanPrefix != null)
                    harmony.Patch(_callResourcesToCaravan, prefix: new HarmonyMethod(caravanPrefix));
                Log.Message("[MP-MeowOnlineShop] Dragonian Mix MP patch active: permit drops sync.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Dragonian Mix permit patch failed: " + e.Message);
            }
        }

        private static bool OrderForceTargetPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncCallResources == null || __instance == null || !target.IsValid)
            {
                return true;
            }

            Pawn caller = _callerField.GetValue(__instance) as Pawn;
            Map map = _mapField.GetValue(__instance) as Map;
            Faction faction = _factionField.GetValue(__instance) as Faction;
            bool free = _freeField.GetValue(__instance) is bool value && value;
            string defName = (__instance as RoyalTitlePermitWorker)?.def?.defName;
            if (caller == null || map == null || faction == null || string.IsNullOrEmpty(defName))
                return true;

            try
            {
                _syncCallResources.DoSync(
                    null,
                    map.Index,
                    caller.thingIDNumber,
                    defName,
                    faction,
                    free,
                    target.Cell.x,
                    target.Cell.z);
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Dragonian Mix permit sync send failed: " + e.Message);
                return true;
            }
        }

        private static bool CallResourcesToCaravanPrefix(
            Pawn caller,
            Faction faction,
            bool free)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncCallResourcesToCaravan == null || caller == null || faction == null)
            {
                return true;
            }

            string defName = null;
            foreach (RoyalTitlePermitDef permitDef in DefDatabase<RoyalTitlePermitDef>.AllDefsListForReading)
            {
                if (_workerType.IsInstanceOfType(permitDef.Worker))
                {
                    defName = permitDef.defName;
                    break;
                }
            }

            if (string.IsNullOrEmpty(defName))
                return true;

            try
            {
                _syncCallResourcesToCaravan.DoSync(
                    null,
                    caller.thingIDNumber,
                    defName,
                    faction,
                    free);
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Dragonian Mix caravan sync send failed: " + e.Message);
                return true;
            }
        }

        public static void SyncCallResources(
            int mapIndex,
            int callerId,
            string permitDefName,
            Faction faction,
            bool free,
            int cellX,
            int cellZ)
        {
            Map map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            Pawn caller = FindPawnById(map, callerId);
            if (map == null || caller == null || faction == null)
                return;

            object worker = ResolveWorker(permitDefName);
            if (worker == null)
                return;

            _callerField.SetValue(worker, caller);
            _mapField.SetValue(worker, map);
            _factionField.SetValue(worker, faction);
            _freeField.SetValue(worker, free);
            _callResources.Invoke(worker, new object[] { new IntVec3(cellX, 0, cellZ) });
        }

        public static void SyncCallResourcesToCaravan(
            int callerId,
            string permitDefName,
            Faction faction,
            bool free)
        {
            Pawn caller = MP.GetThingById(callerId) as Pawn;
            if (caller == null || faction == null)
                return;

            object worker = ResolveWorker(permitDefName);
            if (worker == null)
                return;

            _callResourcesToCaravan.Invoke(worker, new object[] { caller, faction, free });
        }

        private static object ResolveWorker(string permitDefName)
        {
            RoyalTitlePermitDef permitDef = DefDatabase<RoyalTitlePermitDef>.GetNamedSilentFail(permitDefName);
            return permitDef?.Worker;
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
