using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// A role-change ritual writes its result to a Precept_Role and therefore to
    /// the ideology owned by the ritual's player faction.  The vanilla role
    /// assignment also reads Faction.OfPlayer for leader-role side effects.
    /// In a multifaction MP command replay that field is local-player state, so
    /// different peers can write or clear roles against different factions.
    /// Keep the faction context at the ritual ideology's stable owning faction
    /// for the complete outcome application.  Precept_Role's normal serialized
    /// chosen-pawn state then remains the single durable record of the result.
    /// </summary>
    internal static class Patch_RitualRoleChangeMultifactionPersistence
    {
        private static bool _applied;
        private static FieldInfo _ofPlayerField;
        private static AccessTools.FieldRef<FactionManager, Faction> _ofPlayerRef;
        private static bool _multifactionCacheValid;
        private static bool _cachedMultifaction;
        private static int _multifactionCacheTick = int.MinValue;
        private static Game _persistenceCacheGame;
        private static RitualRoleAssignmentPersistenceComponent _persistenceCacheComponent;
        private static bool _persistenceCacheValid;
        [ThreadStatic] private static int _roleChangeApplyDepth;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(RitualOutcomeEffectWorker_RoleChange),
                    nameof(RitualOutcomeEffectWorker_RoleChange.Apply));
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(ApplyPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(ApplyFinalizer));
                MethodInfo applyPostfix = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(ApplyPostfix));
                MethodInfo unassignPrefix = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(RoleSingleUnassignPrefix));
                MethodInfo restorePostfix = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(RoleLifecycleRestorePostfix));
                MethodInfo validatePrefix = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(RoleValidatePawnPrefix));
                MethodInfo roleSingleUnassign = AccessTools.Method(
                    typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.Unassign));
                MethodInfo roleSingleRecache = AccessTools.Method(
                    typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.RecacheActivity));
                MethodInfo roleSingleExpose = AccessTools.Method(
                    typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.ExposeData));
                MethodInfo roleMultiRecache = AccessTools.Method(
                    typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.RecacheActivity));
                MethodInfo roleMultiExpose = AccessTools.Method(
                    typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.ExposeData));
                MethodInfo roleValidatePawn = AccessTools.Method(
                    typeof(Precept_Role), "ValidatePawn", new[] { typeof(Pawn) });
                _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
                _ofPlayerRef = TryGetOfPlayerRef(_ofPlayerField);

                if (target == null || prefix == null || finalizer == null || applyPostfix == null ||
                    unassignPrefix == null || restorePostfix == null || validatePrefix == null ||
                    roleValidatePawn == null || roleSingleUnassign == null ||
                    roleSingleRecache == null || roleSingleExpose == null || roleMultiRecache == null ||
                    roleMultiExpose == null || _ofPlayerField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Role-change ritual multifaction " +
                        "persistence target resolution failed.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    postfix: new HarmonyMethod(applyPostfix) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });
                harmony.Patch(roleSingleUnassign, prefix: new HarmonyMethod(unassignPrefix) { priority = Priority.First });
                harmony.Patch(roleSingleRecache, postfix: new HarmonyMethod(restorePostfix) { priority = Priority.Last });
                harmony.Patch(roleSingleExpose, postfix: new HarmonyMethod(restorePostfix) { priority = Priority.Last });
                harmony.Patch(roleMultiRecache, postfix: new HarmonyMethod(restorePostfix) { priority = Priority.Last });
                harmony.Patch(roleMultiExpose, postfix: new HarmonyMethod(restorePostfix) { priority = Priority.Last });
                harmony.Patch(roleValidatePawn, prefix: new HarmonyMethod(validatePrefix) { priority = Priority.First });

                Log.Message(
                    "[MP-MeowOnlineShop] Role-change ritual multifaction persistence active: " +
                    "outcomes use the ritual ideology's owning faction.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Role-change ritual multifaction " +
                    "persistence patch failed: " + e.Message);
            }
        }

        private static void ApplyPrefix(LordJob_Ritual jobRitual, ref Faction __state)
        {
            __state = null;
            _roleChangeApplyDepth++;
            if (!MP.IsInMultiplayer || jobRitual?.Ritual?.ideo == null ||
                !IsMultifactionActive())
            {
                return;
            }

            try
            {
                Faction owner = Find.FactionManager?.AllFactionsListForReading?
                    .Where(faction => faction?.ideos != null && faction.ideos.Has(jobRitual.Ritual.ideo))
                    .OrderBy(faction => faction.loadID)
                    .FirstOrDefault();
                FactionManager factionManager = Find.FactionManager;
                if (owner == null || factionManager == null)
                    return;

                if (_ofPlayerRef != null)
                {
                    __state = _ofPlayerRef(factionManager);
                    _ofPlayerRef(factionManager) = owner;
                }
                else
                {
                    __state = _ofPlayerField.GetValue(factionManager) as Faction;
                    _ofPlayerField.SetValue(factionManager, owner);
                }
            }
            catch
            {
                __state = null;
            }
        }

        private static void ApplyFinalizer(Faction __state)
        {
            if (_roleChangeApplyDepth > 0)
                _roleChangeApplyDepth--;

            if (__state == null)
                return;

            try
            {
                FactionManager factionManager = Find.FactionManager;
                if (factionManager != null)
                {
                    if (_ofPlayerRef != null)
                        _ofPlayerRef(factionManager) = __state;
                    else
                        _ofPlayerField.SetValue(factionManager, __state);
                }
            }
            catch
            {
                // Preserve Multiplayer's original faction context if restoration fails.
            }
        }

        private static void ApplyPostfix(LordJob_Ritual jobRitual)
        {
            if (!MP.IsInMultiplayer || jobRitual?.assignments == null ||
                !IsMultifactionActive())
            {
                return;
            }

            Precept_Role role = jobRitual.assignments.RoleChangeSelection;
            Pawn pawn = jobRitual.assignments.FirstAssignedPawn("role_changer");
            if (role == null || pawn == null || !role.IsAssigned(pawn))
                return;

            CurrentPersistenceComponent()?.Record(role, pawn);
        }

        private static bool RoleSingleUnassignPrefix(Precept_RoleSingle __instance, Pawn p)
        {
            if (_roleChangeApplyDepth > 0 || !MP.IsInMultiplayer || __instance == null || p == null ||
                !IsMultifactionActive())
            {
                return true;
            }

            RitualRoleAssignmentPersistenceComponent component = CurrentPersistenceComponent();
            return component == null || !component.ShouldRetain(__instance, p);
        }

        private static bool RoleValidatePawnPrefix(Precept_Role __instance, Pawn p, ref bool __result)
        {
            // Assign and RecacheActivity both use ValidatePawn. A persisted
            // ritual assignment must pass here or the restore postfix and
            // RecacheActivity will fight each other on every tick.
            if (!MP.IsInMultiplayer || __instance == null || p == null ||
                !IsMultifactionActive())
            {
                return true;
            }

            RitualRoleAssignmentPersistenceComponent component = CurrentPersistenceComponent();
            if (component != null && component.ShouldRetain(__instance, p))
            {
                __result = true;
                return false;
            }

            return true;
        }

        private static void RoleLifecycleRestorePostfix(Precept_Role __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null ||
                !IsMultifactionActive())
            {
                return;
            }

            CurrentPersistenceComponent()?.Restore(__instance);
        }

        private static bool IsMultifactionActive()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (!_multifactionCacheValid ||
                tick - _multifactionCacheTick >= 600)
            {
                _multifactionCacheValid =
                    MpRuntimeInfo.TryGetMultifactionActive(out _cachedMultifaction);
                _multifactionCacheTick = tick;
            }

            return _multifactionCacheValid && _cachedMultifaction;
        }

        private static RitualRoleAssignmentPersistenceComponent CurrentPersistenceComponent()
        {
            Game game = Current.Game;
            if (!_persistenceCacheValid ||
                !ReferenceEquals(game, _persistenceCacheGame))
            {
                _persistenceCacheComponent =
                    game?.GetComponent<RitualRoleAssignmentPersistenceComponent>();
                _persistenceCacheGame = game;
                _persistenceCacheValid = true;
            }

            return _persistenceCacheComponent;
        }

        private static AccessTools.FieldRef<FactionManager, Faction> TryGetOfPlayerRef(
            FieldInfo field)
        {
            if (field == null)
                return null;
            try
            {
                return AccessTools.FieldRefAccess<FactionManager, Faction>(field);
            }
            catch
            {
                return null;
            }
        }
    }
}
