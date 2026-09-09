using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Show Weapon Tallies bundles MVCF. Its `Command_ToggleVerbUsage`
    /// delegates to `ManagedVerb.Toggle()`, which flips the saved `enabledInt`
    /// field. A SyncWorker keyed by `GetUniqueLoadID()` lets us register
    /// `Toggle()` as a sync method.
    /// </summary>
    internal static class Patch_ShowWeaponTalliesMvcfMp
    {
        private const string ManagedVerbTypeName = "MVCF.ManagedVerb";
        private const string VerbManagerTypeName = "MVCF.VerbManager";
        private const string PawnVerbUtilityTypeName = "MVCF.Utilities.PawnVerbUtility";

        private static bool _applied;
        private static MethodInfo _getLoadIdMethod;
        private static MethodInfo _managerMethod;
        private static PropertyInfo _managedVerbsProperty;
        private static FieldInfo _currentVerbField;
        private static FieldInfo _managedVerbVerbField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type managedVerbType = AccessTools.TypeByName(ManagedVerbTypeName);
                Type verbManagerType = AccessTools.TypeByName(VerbManagerTypeName);
                Type pawnVerbUtilityType = AccessTools.TypeByName(PawnVerbUtilityTypeName);
                _getLoadIdMethod = managedVerbType == null
                    ? null
                    : AccessTools.Method(managedVerbType, "GetUniqueLoadID", Type.EmptyTypes);
                _managerMethod = pawnVerbUtilityType == null
                    ? null
                    : AccessTools.Method(
                        pawnVerbUtilityType,
                        "Manager",
                        new[] { typeof(Pawn), typeof(bool) });
                _managedVerbsProperty = verbManagerType == null
                    ? null
                    : verbManagerType.GetProperty(
                        "ManagedVerbs",
                        BindingFlags.Public | BindingFlags.Instance);
                _currentVerbField = verbManagerType == null
                    ? null
                    : AccessTools.Field(verbManagerType, "CurrentVerb");
                _managedVerbVerbField = managedVerbType == null
                    ? null
                    : AccessTools.Field(managedVerbType, "Verb");

                if (managedVerbType == null || _getLoadIdMethod == null ||
                    _managerMethod == null || _managedVerbsProperty == null ||
                    _currentVerbField == null || _managedVerbVerbField == null)
                {
                    if (managedVerbType == null && verbManagerType == null &&
                        pawnVerbUtilityType == null)
                    {
                        Log.Message(
                            "[MP-MeowOnlineShop] MVCF runtime is not active; " +
                            "Show Weapon Tallies requires no MVCF compatibility for this loadout.");
                    }
                    else
                    {
                        Log.Warning(
                            "[MP-MeowOnlineShop] MVCF API was only partially resolved; " +
                            "Show Weapon Tallies compatibility was not installed.");
                    }
                    return;
                }

                try
                {
                    MP.RegisterSyncWorker<object>(
                        ManagedVerbSyncer,
                        managedVerbType,
                        false,
                        false);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] MVCF SyncWorker registration failed: " + e.Message);
                    return;
                }

                MethodInfo toggle = AccessTools.Method(managedVerbType, "Toggle", Type.EmptyTypes);
                if (toggle == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] MVCF Toggle method not resolved; patch skipped.");
                    return;
                }

                MP.RegisterSyncMethod(toggle, null);

                MethodInfo stopForceAttack = AccessTools.Method(
                    typeof(Patch_ShowWeaponTalliesMvcfMp),
                    nameof(SyncStopForceAttack),
                    new[] { typeof(Pawn) });
                if (stopForceAttack == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] MVCF stop-force-attack sync method not resolved; Toggle still synced.");
                }
                else
                {
                    MP.RegisterSyncMethod(stopForceAttack, null);
                }

                int patchedGizmos = 0;
                MethodInfo pawnGizmos = AccessTools.Method(typeof(Pawn), "GetGizmos", Type.EmptyTypes);
                MethodInfo pawnGizmosPostfix = AccessTools.Method(
                    typeof(Patch_ShowWeaponTalliesMvcfMp),
                    nameof(PawnGizmosPostfix));
                if (pawnGizmos != null && pawnGizmosPostfix != null)
                {
                    try
                    {
                        harmony.Patch(
                            pawnGizmos,
                            postfix: new HarmonyMethod(pawnGizmosPostfix) { priority = Priority.Last });
                        patchedGizmos++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] MVCF Pawn.GetGizmos postfix failed: " + e.Message);
                    }
                }

                MethodInfo attackGizmos = AccessTools.Method(
                    typeof(PawnAttackGizmoUtility),
                    "GetAttackGizmos",
                    new[] { typeof(Pawn) });
                MethodInfo attackGizmosPostfix = AccessTools.Method(
                    typeof(Patch_ShowWeaponTalliesMvcfMp),
                    nameof(AttackGizmosPostfix));
                if (attackGizmos != null && attackGizmosPostfix != null)
                {
                    try
                    {
                        harmony.Patch(
                            attackGizmos,
                            postfix: new HarmonyMethod(attackGizmosPostfix) { priority = Priority.Last });
                        patchedGizmos++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] MVCF PawnAttackGizmoUtility.GetAttackGizmos postfix failed: " + e.Message);
                    }
                }

                Log.Message(
                    "[MP-MeowOnlineShop] MVCF/ShowWeaponTallies MP patch active: " +
                    "1 SyncWorker, Toggle synced, stop-force-attack synced, gizmo postfixes=" +
                    patchedGizmos + "/2.");

                MethodInfo orderForceTarget = AccessTools.Method(
                    typeof(Verb),
                    "OrderForceTarget",
                    new[] { typeof(LocalTargetInfo) });
                MethodInfo orderForceTargetPrefix = AccessTools.Method(
                    typeof(Patch_ShowWeaponTalliesMvcfMp),
                    nameof(OrderForceTargetPrefix));
                if (orderForceTarget != null && orderForceTargetPrefix != null)
                {
                    try
                    {
                        harmony.Patch(
                            orderForceTarget,
                            prefix: new HarmonyMethod(orderForceTargetPrefix) { priority = Priority.First });
                        Log.Message("[MP-MeowOnlineShop] MVCF OrderForceTarget CurrentVerb prefix active.");
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] MVCF OrderForceTarget prefix failed: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MVCF/ShowWeaponTallies MP compat restore failed: " + e.Message);
            }
        }

        private static void OrderForceTargetPrefix(Verb __instance)
        {
            if (__instance == null || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return;

            Pawn pawn = __instance.CasterPawn;
            object manager = GetManager(pawn);
            if (manager == null || _currentVerbField == null ||
                _managedVerbsProperty == null || _managedVerbVerbField == null)
            {
                return;
            }

            object verbs = _managedVerbsProperty.GetValue(manager, null);
            if (!(verbs is IEnumerable enumerable))
                return;

            foreach (object managedVerb in enumerable)
            {
                if (managedVerb == null)
                    continue;

                if (ReferenceEquals(_managedVerbVerbField.GetValue(managedVerb), __instance))
                {
                    _currentVerbField.SetValue(manager, null);
                    break;
                }
            }
        }

        public static void SyncStopForceAttack(Pawn pawn)
        {
            if (pawn == null)
                return;

            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true, true);

            object manager = GetManager(pawn);
            if (manager != null && _currentVerbField != null)
                _currentVerbField.SetValue(manager, null);
        }

        private static IEnumerable<Gizmo> PawnGizmosPostfix(
            IEnumerable<Gizmo> __result,
            Pawn __instance)
        {
            return RewriteStopForceAttack(__result, __instance);
        }

        private static IEnumerable<Gizmo> AttackGizmosPostfix(
            IEnumerable<Gizmo> __result,
            Pawn pawn)
        {
            return RewriteStopForceAttack(__result, pawn);
        }

        private static IEnumerable<Gizmo> RewriteStopForceAttack(
            IEnumerable<Gizmo> __result,
            Pawn pawn)
        {
            if (__result == null || pawn == null)
                return __result;

            string stopLabel = Translator.Translate("CommandStopForceAttack").ToString();
            List<Gizmo> list = __result.ToList();
            Pawn targetPawn = pawn;

            foreach (Gizmo gizmo in list)
            {
                if (gizmo is Command_Action command && command.action != null)
                {
                    string label = command.defaultLabel?.ToString();
                    if (label == stopLabel)
                    {
                        command.action = () =>
                        {
                            SyncStopForceAttack(targetPawn);
                            SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_Low, null);
                        };
                    }
                }
            }

            return list;
        }

        private static object GetManager(Pawn pawn)
        {
            if (pawn == null || _managerMethod == null)
                return null;

            try
            {
                return _managerMethod.Invoke(null, new object[] { pawn, false });
            }
            catch
            {
                return null;
            }
        }

        private static void ManagedVerbSyncer(SyncWorker sync, ref object inst)
        {
            if (sync.isWriting)
            {
                string id = inst == null ? null : (string)_getLoadIdMethod.Invoke(inst, null);
                sync.Bind(ref id);
                return;
            }

            string loadId = null;
            sync.Bind(ref loadId);
            inst = FindManagedVerb(loadId);
        }

        private static object FindManagedVerb(string loadId)
        {
            if (string.IsNullOrEmpty(loadId))
                return null;

            var pawns = new List<Pawn>();
            foreach (Map map in Find.Maps)
                pawns.AddRange(map.mapPawns.AllPawns);
            pawns.AddRange(Find.WorldPawns.AllPawnsAliveOrDead);

            foreach (Pawn pawn in pawns)
            {
                if (pawn == null || pawn.def == null)
                    continue;

                object manager;
                try
                {
                    manager = _managerMethod.Invoke(null, new object[] { pawn, false });
                }
                catch
                {
                    continue;
                }

                if (manager == null)
                    continue;

                object verbs = _managedVerbsProperty.GetValue(manager, null);
                if (verbs is IEnumerable enumerable)
                {
                    foreach (object verb in enumerable)
                    {
                        if (verb == null)
                            continue;
                        object id = _getLoadIdMethod.Invoke(verb, null);
                        if (string.Equals(loadId, id as string, StringComparison.Ordinal))
                            return verb;
                    }
                }
            }

            return null;
        }
    }
}
