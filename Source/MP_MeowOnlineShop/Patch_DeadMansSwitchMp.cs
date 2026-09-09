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
    /// The Dead Man's Switch (`aoba.deadmanswitch.core`) has one player-facing
    /// targeted permit worker, `DMS.RoyalTitlePermitWorker_RewardShuttle`. Its
    /// OrderForceTarget override spawns a transport ship, spends favor, and
    /// marks the permit used. Multiplayer registers ITargetingSource workers
    /// only from Assembly-CSharp, so this third-party override is not covered.
    ///
    /// The other DMS paths (CompQuestWorkable.DoWork inside a JobDriver and
    /// CompUseEffect_SummonRaid.DoEffect inside an item-use job) execute on
    /// every peer through already-synchronized deterministic job replay and
    /// need no extra command.
    /// </summary>
    internal static class Patch_DeadMansSwitchMp
    {
        private const string CorePackageId = "aoba.deadmanswitch.core";
        private const string WorkerTypeName = "DMS.RoyalTitlePermitWorker_RewardShuttle";
        private const string CallShuttleMethodName = "CallShuttle";
        private const string OrderForceTargetMethodName = "OrderForceTarget";

        private static Type _workerType;
        private static FieldInfo _calledFactionField;
        private static FieldInfo _callerField;
        private static FieldInfo _mapField;
        private static FieldInfo _freeField;
        private static MethodInfo _callShuttle;
        private static ISyncMethod _syncCallShuttle;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(CorePackageId))
                return;

            _workerType = AccessTools.TypeByName(WorkerTypeName);
            if (_workerType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS permit worker not resolved; patch skipped.");
                return;
            }

            Type targetedType = _workerType.BaseType;
            _calledFactionField = AccessTools.Field(_workerType, "calledFaction");
            _callerField = targetedType == null ? null : AccessTools.Field(targetedType, "caller");
            _mapField = targetedType == null ? null : AccessTools.Field(targetedType, "map");
            _freeField = targetedType == null ? null : AccessTools.Field(targetedType, "free");
            _callShuttle = AccessTools.Method(_workerType, CallShuttleMethodName, new[] { typeof(IntVec3) });

            if (_calledFactionField == null || _callerField == null || _mapField == null ||
                _freeField == null || _callShuttle == null)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS permit worker field/method resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncCallShuttle = MP.RegisterSyncMethod(
                        typeof(Patch_DeadMansSwitchMp),
                        nameof(SyncCallShuttle))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS permit sync registration failed: " + e.Message);
                return;
            }

            MethodInfo orderForceTarget = AccessTools.Method(
                _workerType,
                OrderForceTargetMethodName,
                new[] { typeof(LocalTargetInfo) });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_DeadMansSwitchMp),
                nameof(OrderForceTargetPrefix));
            if (orderForceTarget == null || prefix == null)
                return;

            try
            {
                harmony.Patch(orderForceTarget, prefix: new HarmonyMethod(prefix));
                Log.Message("[MP-MeowOnlineShop] DMS MP patch active: reward shuttle permit syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS permit targeting patch failed: " + e.Message);
            }
        }

        private static bool OrderForceTargetPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _syncCallShuttle == null || __instance == null || !target.IsValid)
            {
                return true;
            }

            Pawn caller = _callerField.GetValue(__instance) as Pawn;
            Map map = _mapField.GetValue(__instance) as Map;
            Faction faction = _calledFactionField.GetValue(__instance) as Faction;
            bool free = _freeField.GetValue(__instance) is bool value && value;
            if (caller == null || map == null || faction == null)
                return true;

            try
            {
                _syncCallShuttle.DoSync(
                    null,
                    map.Index,
                    caller.thingIDNumber,
                    faction,
                    free,
                    target.Cell.x,
                    target.Cell.z);
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS permit sync send failed: " + e.Message);
                return true;
            }
        }

        public static void SyncCallShuttle(int mapIndex, int callerId, Faction faction, bool free, int cellX, int cellZ)
        {
            if (_callShuttle == null || _callerField == null || _mapField == null ||
                _freeField == null || _calledFactionField == null)
            {
                return;
            }

            Map map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            Pawn caller = FindPawnById(map, callerId);
            if (map == null || caller == null || faction == null)
                return;

            object worker;
            try
            {
                worker = Activator.CreateInstance(_workerType, nonPublic: true);
            }
            catch
            {
                return;
            }

            _callerField.SetValue(worker, caller);
            _mapField.SetValue(worker, map);
            _freeField.SetValue(worker, free);
            _calledFactionField.SetValue(worker, faction);
            _callShuttle.Invoke(worker, new object[] { new IntVec3(cellX, 0, cellZ) });
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
