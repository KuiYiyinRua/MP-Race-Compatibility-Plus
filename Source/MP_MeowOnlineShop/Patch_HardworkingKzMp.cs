using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Hardworking Kz (`moo.hardworking.kz`) adds two pawn gizmos through
    /// HardworkingUtility.GetGizmosExtra:
    ///
    /// - Cancel current job, which calls EndCurrentJob directly.
    /// - Melee attack, which calls TryTakeOrderedJob and then writes
    ///   CompHardworking.curMeleeAttackInt locally.
    ///
    /// The postfix replaces those two callbacks with replay commands. The
    /// replay uses the mod's own GetMeleeAttackAction so the job creation and
    /// cooldown write remain identical on every peer.
    /// </summary>
    internal static class Patch_HardworkingKzMp
    {
        private const string PackageId = "moo.hardworking.kz";
        private const string UtilityTypeName = "Kz.HardworkingUtility";
        private const string CompTypeName = "Kz.CompHardworking";

        private static Type _utilityType;
        private static Type _compType;
        private static MethodInfo _isHardWorker;
        private static MethodInfo _getMeleeAttackAction;
        private static FieldInfo _curMeleeAttackInt;
        private static ISyncMethod _syncCancelJob;
        private static ISyncMethod _syncMeleeAttack;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _utilityType = AccessTools.TypeByName(UtilityTypeName);
            _compType = AccessTools.TypeByName(CompTypeName);
            _isHardWorker = _utilityType == null
                ? null
                : AccessTools.Method(_utilityType, "IsHardWorker", new[] { typeof(Pawn) });
            _getMeleeAttackAction = _utilityType == null
                ? null
                : AccessTools.Method(
                    _utilityType,
                    "GetMeleeAttackAction",
                    new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(string).MakeByRefType() });
            _curMeleeAttackInt = _compType == null ? null : AccessTools.Field(_compType, "curMeleeAttackInt");

            MethodInfo getGizmos = _utilityType == null
                ? null
                : AccessTools.Method(_utilityType, "GetGizmosExtra", new[] { typeof(Pawn) });
            if (getGizmos == null || _isHardWorker == null || _getMeleeAttackAction == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Hardworking Kz target resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncCancelJob = MP.RegisterSyncMethod(
                        typeof(Patch_HardworkingKzMp),
                        nameof(SyncCancelJob))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _syncMeleeAttack = MP.RegisterSyncMethod(
                        typeof(Patch_HardworkingKzMp),
                        nameof(SyncMeleeAttack))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Hardworking Kz sync registration failed: " + e.Message);
                return;
            }

            MethodInfo postfix = AccessTools.Method(
                typeof(Patch_HardworkingKzMp),
                nameof(GetGizmosExtraPostfix));
            try
            {
                harmony.Patch(getGizmos, postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] Hardworking Kz MP patch active: gizmo actions sync.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Hardworking Kz gizmo patch failed: " + e.Message);
            }
        }

        private static void GetGizmosExtraPostfix(Pawn pawn, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || pawn?.Map == null ||
                _syncCancelJob == null || _syncMeleeAttack == null)
            {
                return;
            }

            var list = __result.ToList();
            foreach (Gizmo gizmo in list)
            {
                if (gizmo is Command_Action actionGizmo && actionGizmo.groupKey == 6165612)
                {
                    actionGizmo.action = () => _syncCancelJob.DoSync(null, pawn.Map.Index, pawn.thingIDNumber);
                }
                else if (gizmo is Command_Target targetGizmo)
                {
                    targetGizmo.action = target =>
                    {
                        Thing targetThing = target.Thing;
                        if (targetThing == null)
                            return;

                        List<int> ids = new List<int>();
                        foreach (object selected in Find.Selector.SelectedObjects)
                        {
                            if (selected is Pawn selectedPawn && IsHardWorker(selectedPawn))
                                ids.Add(selectedPawn.thingIDNumber);
                        }

                        if (ids.Count > 0)
                            _syncMeleeAttack.DoSync(null, pawn.Map.Index, ids, targetThing.thingIDNumber);
                    };
                }
            }

            __result = list;
        }

        public static void SyncCancelJob(int mapIndex, int pawnId)
        {
            Pawn pawn = FindPawnById(FindMap(mapIndex), pawnId);
            if (pawn?.jobs != null && pawn.jobs.IsCurrentJobPlayerInterruptible())
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
        }

        public static void SyncMeleeAttack(int mapIndex, List<int> pawnIds, int targetThingId)
        {
            Map map = FindMap(mapIndex);
            Thing targetThing = FindThingById(map, targetThingId);
            if (map == null || targetThing == null || pawnIds == null)
                return;

            foreach (int pawnId in pawnIds)
            {
                Pawn pawn = FindPawnById(map, pawnId);
                if (pawn == null || !IsHardWorker(pawn))
                    continue;

                string failStr;
                object action = _getMeleeAttackAction.Invoke(
                    null,
                    new object[] { pawn, new LocalTargetInfo(targetThing), null });
                failStr = GetFailString(_getMeleeAttackAction, pawn, targetThing);
                if (action is Action meleeAction && string.IsNullOrEmpty(failStr))
                    meleeAction();
            }
        }

        private static string GetFailString(MethodInfo method, Pawn pawn, Thing target)
        {
            object[] args = new object[] { pawn, new LocalTargetInfo(target), null };
            try
            {
                method.Invoke(null, args);
            }
            catch (TargetInvocationException)
            {
                // The out parameter may still be written before an exception.
            }
            return args[2] as string;
        }

        private static bool IsHardWorker(Pawn pawn)
        {
            if (_isHardWorker == null || pawn == null)
                return false;

            try
            {
                return _isHardWorker.Invoke(null, new object[] { pawn }) is bool value && value;
            }
            catch
            {
                return false;
            }
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

        private static Thing FindThingById(Map map, int thingId)
        {
            if (map?.listerThings?.AllThings == null)
                return null;

            for (int i = 0; i < map.listerThings.AllThings.Count; i++)
            {
                Thing thing = map.listerThings.AllThings[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;
            }

            return null;
        }
    }
}
