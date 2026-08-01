using System;
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
    /// Search And Destroy appends its own `Command_Toggle` gizmos to drafted
    /// pawns. The toggle lambda flips `ExtendedPawnData.SD_enabled` and calls
    /// `jobs.EndCurrentJob(JobCondition.InterruptForced, true)`.
    ///
    /// The official Multiplayer-Compatibility package registers the lambda, but
    /// its replay serializes the captured `ExtendedPawnData` through the mod's
    /// store dictionary; when the other peer has not created that store entry,
    /// the replayed lambda receives null data and the command cannot change
    /// state on every peer. Desync-118/119 both log
    /// `SearchAndDestroy_Gizmo_Ranged toggle` immediately before the world
    /// desync while drafting pawns during combat.
    ///
    /// Replace the toggle action with a primitive-only synchronized command
    /// (pawn thing ID + target bool). The replay resolves the pawn by ID and
    /// reads/creates its `ExtendedPawnData` through the mod's own accessor, so
    /// it never depends on a pre-existing store entry on the other peer.
    /// </summary>
    internal static class Patch_SearchAndDestroyMp
    {
        private const string GizmoPatchTypeName =
            "SearchAndDestroy.Harmony.Pawn_DraftController_GetGizmos";
        private const string StorageTypeName =
            "SearchAndDestroy.Storage.ExtendedDataStorage";
        private const string PawnDataTypeName =
            "SearchAndDestroy.Storage.ExtendedPawnData";
        private const string BaseTypeName =
            "SearchAndDestroy.Base";

        private static bool _applied;
        private static bool _logged;
        private static FieldInfo _sdEnabledField;
        private static MethodInfo _getExtendedDataMethod;
        private static Func<object> _storageGetter;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type gizmoPatchType = AccessTools.TypeByName(GizmoPatchTypeName);
                Type storageType = AccessTools.TypeByName(StorageTypeName);
                Type pawnDataType = AccessTools.TypeByName(PawnDataTypeName);
                Type baseType = AccessTools.TypeByName(BaseTypeName);

                _sdEnabledField = pawnDataType?.GetField(
                    "SD_enabled",
                    BindingFlags.Public | BindingFlags.Instance);
                _getExtendedDataMethod = storageType?.GetMethod(
                    "GetExtendedDataFor",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(Pawn) },
                    null);

                PropertyInfo instanceProperty = baseType?.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static);
                PropertyInfo storageProperty = baseType?.GetProperty(
                    "ExtendedDataStorage",
                    BindingFlags.Public | BindingFlags.Instance);

                MethodInfo getGizmos = AccessTools.Method(
                    typeof(Pawn_DraftController),
                    "GetGizmos");
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_SearchAndDestroyMp),
                    nameof(GetGizmosPostfix));

                if (gizmoPatchType == null || storageType == null ||
                    pawnDataType == null || _sdEnabledField == null ||
                    _getExtendedDataMethod == null || instanceProperty == null ||
                    storageProperty == null || getGizmos == null || postfix == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] SearchAndDestroy draft-gizmo sync skipped " +
                        "(target mod not active or signature changed).");
                    return;
                }

                _storageGetter = () =>
                {
                    object instance = instanceProperty.GetValue(null, null);
                    return instance == null
                        ? null
                        : storageProperty.GetValue(instance, null);
                };

                MP.RegisterSyncMethod(
                    typeof(Patch_SearchAndDestroyMp),
                    nameof(SyncToggleSearchAndDestroy));

                harmony.Patch(
                    getGizmos,
                    postfix: new HarmonyMethod(postfix)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] SearchAndDestroy draft-gizmo sync active: " +
                    "SD_enabled toggles and job interrupts run on every peer " +
                    "(pawn-ID command, no store-worker dependency).");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] SearchAndDestroy draft-gizmo sync failed: " +
                    e.Message);
            }
        }

        private static void GetGizmosPostfix(
            Pawn_DraftController __instance,
            ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || __instance?.pawn == null)
                return;

            Pawn pawn = __instance.pawn;
            List<Gizmo> gizmos = __result.ToList();
            bool replaced = false;

            for (int i = 0; i < gizmos.Count; i++)
            {
                if (!(gizmos[i] is Command_Toggle toggle) ||
                    toggle.toggleAction == null)
                {
                    continue;
                }

                MethodBase actionMethod = toggle.toggleAction.Method;
                string declaringType = actionMethod?.DeclaringType?.FullName;
                if (declaringType == null ||
                    !declaringType.StartsWith(
                        GizmoPatchTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Func<bool> isActive = toggle.isActive;
                toggle.toggleAction = () => ToggleViaSync(pawn, isActive);
                replaced = true;
            }

            if (!replaced)
                return;

            __result = gizmos;
            if (!_logged)
            {
                _logged = true;
                Log.Message(
                    "[MP-MeowOnlineShop] SearchAndDestroy draft gizmo clicks routed " +
                    "through MP sync command.");
            }
        }

        private static void ToggleViaSync(Pawn pawn, Func<bool> isActive)
        {
            if (pawn == null)
                return;

            bool current = isActive?.Invoke() ?? false;
            SyncToggleSearchAndDestroy(pawn.thingIDNumber, !current);
        }

        private static void SyncToggleSearchAndDestroy(int pawnId, bool enabled)
        {
            Pawn pawn = FindPawnById(pawnId);
            if (pawn == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] SearchAndDestroy replay could not resolve " +
                    $"pawn id={pawnId}.");
                return;
            }

            object storage = _storageGetter?.Invoke();
            object data = storage == null
                ? null
                : _getExtendedDataMethod.Invoke(storage, new object[] { pawn });
            if (data == null || _sdEnabledField == null)
                return;

            bool previous = (bool)_sdEnabledField.GetValue(data);
            if (previous == enabled)
                return;

            _sdEnabledField.SetValue(data, enabled);
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
        }

        private static Pawn FindPawnById(int pawnId)
        {
            if (Find.Maps != null)
            {
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    Map map = Find.Maps[m];
                    IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null)
                        continue;

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        if (pawns[i] != null && pawns[i].thingIDNumber == pawnId)
                            return pawns[i];
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                List<Pawn> worldPawns = Find.WorldPawns.AllPawnsAliveOrDead;
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    if (worldPawns[i] != null &&
                        worldPawns[i].thingIDNumber == pawnId)
                    {
                        return worldPawns[i];
                    }
                }
            }

            return null;
        }
    }
}
