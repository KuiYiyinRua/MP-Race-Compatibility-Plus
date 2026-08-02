using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer samples per-map async Rand state in the order the local
    /// process ticks maps. After a rejoin the map list can be iterated in a
    /// different order on each peer, which makes ClientSyncOpinion report
    /// "Map instances don't match" even when both peers hold the same maps.
    /// The map order in that opinion is not simulation state, so keep the
    /// sample list sorted by map ID on both peers before comparison.
    /// </summary>
    internal static class Patch_MapStateOrderNormalizer
    {
        private static bool _applied;
        private static FieldInfo _currentOpinionField;
        private static FieldInfo _mapStatesField;
        private static FieldInfo _mapIdField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            try
            {
                Type syncCoordinatorType =
                    AccessTools.TypeByName("Multiplayer.Client.SyncCoordinator");
                Type opinionType =
                    AccessTools.TypeByName("Multiplayer.Client.ClientSyncOpinion");
                Type mapStateType =
                    AccessTools.TypeByName("Multiplayer.Client.MapRandomStateData");

                _currentOpinionField = syncCoordinatorType?.GetField(
                    "currentOpinion",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _mapStatesField = opinionType?.GetField(
                    "mapStates",
                    BindingFlags.Instance | BindingFlags.Public);
                _mapIdField = mapStateType?.GetField(
                    "mapId",
                    BindingFlags.Instance | BindingFlags.Public);

                MethodInfo target = syncCoordinatorType?.GetMethod(
                    "TryAddMapRandomState",
                    BindingFlags.Instance | BindingFlags.Public);
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_MapStateOrderNormalizer),
                    nameof(TryAddMapRandomStatePostfix));

                if (target == null || postfix == null ||
                    _currentOpinionField == null ||
                    _mapStatesField == null || _mapIdField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Map-state order normalizer targets " +
                        "unresolved; skipped.");
                    return;
                }

                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(postfix)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Map-state order normalizer active: " +
                    "async map state samples are sorted by map ID for peer " +
                    "comparison.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Map-state order normalizer install " +
                    "failed: " + e);
            }
        }

        private static void TryAddMapRandomStatePostfix(object __instance)
        {
            try
            {
                object opinion = _currentOpinionField.GetValue(__instance);
                if (opinion == null)
                    return;

                object list = _mapStatesField.GetValue(opinion);
                if (!(list is IList items) || items.Count <= 1)
                    return;

                for (int i = 1; i < items.Count; i++)
                {
                    object current = items[i];
                    int currentId = (int)_mapIdField.GetValue(current);
                    int j = i - 1;
                    while (j >= 0 &&
                           (int)_mapIdField.GetValue(items[j]) > currentId)
                    {
                        items[j + 1] = items[j];
                        j--;
                    }
                    items[j + 1] = current;
                }
            }
            catch
            {
                // The normalizer must never break map ticking or sync sampling.
            }
        }
    }
}
