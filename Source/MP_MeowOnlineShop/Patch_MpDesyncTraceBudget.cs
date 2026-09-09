using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Bounds Multiplayer's optional desync trace diagnostics for very large mod
    /// lists. Random-state opinions remain untouched; only trace hashes and their
    /// retained stack items are capped.
    /// </summary>
    internal static class Patch_MpDesyncTraceBudget
    {
        private const int MaxTraceHashesPerOpinion = 1_000_000;
        private static bool _applied;
        private static bool _loggedBudgetHit;
        private static FieldInfo _currentOpinionField;
        private static FieldInfo _desyncStackTraceHashesField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied)
                return;
            _applied = true;

            try
            {
                var coordinatorType = AccessTools.TypeByName("Multiplayer.Client.SyncCoordinator");
                var opinionType = AccessTools.TypeByName("Multiplayer.Client.ClientSyncOpinion");
                _currentOpinionField = AccessTools.Field(coordinatorType, "currentOpinion");
                _desyncStackTraceHashesField = AccessTools.Field(opinionType, "desyncStackTraceHashes");
                var collectionPrefix = AccessTools.Method(
                    typeof(Patch_MpDesyncTraceBudget),
                    nameof(TraceCollectionPrefix));
                var toNetPrefix = AccessTools.Method(
                    typeof(Patch_MpDesyncTraceBudget),
                    nameof(OpinionToNetPrefix));

                int collectionTargets = 0;
                if (coordinatorType != null && collectionPrefix != null)
                {
                    foreach (var method in AccessTools.GetDeclaredMethods(coordinatorType))
                    {
                        if (method.Name != "TryAddStackTraceForDesyncLogRaw" &&
                            method.Name != "TryAddInfoForDesyncLog")
                            continue;

                        harmony.Patch(
                            method,
                            prefix: new HarmonyMethod(collectionPrefix) { priority = Priority.First });
                        collectionTargets++;
                    }
                }

                var toNet = AccessTools.Method(opinionType, "ToNet");
                if (toNet != null && toNetPrefix != null)
                    harmony.Patch(
                        toNet,
                        prefix: new HarmonyMethod(toNetPrefix) { priority = Priority.First });

                Log.Message(
                    "[MP-MeowOnlineShop] MP desync trace budget guard: " +
                    $"collectionTargets={collectionTargets}, toNet={(toNet != null)}, " +
                    $"maxHashesPerOpinion={MaxTraceHashesPerOpinion}.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MP desync trace budget guard init failed: " + e);
            }
        }

        private static bool TraceCollectionPrefix(object __instance)
        {
            if (__instance == null)
                return true;

            try
            {
                var currentOpinion = _currentOpinionField?.GetValue(__instance);
                if (currentOpinion == null)
                    return true;

                int count = GetTraceHashCount(currentOpinion);
                if (count < MaxTraceHashesPerOpinion)
                    return true;

                LogBudgetHit(count, "collection");
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void OpinionToNetPrefix(object __instance)
        {
            if (__instance == null)
                return;

            try
            {
                var hashes = _desyncStackTraceHashesField?.GetValue(__instance);
                if (!(hashes is IList list) || list.Count <= MaxTraceHashesPerOpinion)
                    return;

                int originalCount = list.Count;
                var removeRange = AccessTools.Method(
                    hashes.GetType(),
                    "RemoveRange",
                    new[] { typeof(int), typeof(int) });
                if (removeRange != null)
                {
                    removeRange.Invoke(
                        hashes,
                        new object[]
                        {
                            MaxTraceHashesPerOpinion,
                            originalCount - MaxTraceHashesPerOpinion
                        });
                }
                else
                {
                    while (list.Count > MaxTraceHashesPerOpinion)
                        list.RemoveAt(list.Count - 1);
                }

                LogBudgetHit(originalCount, "serialization");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MP desync trace serialization guard failed: " + e.Message);
            }
        }

        private static int GetTraceHashCount(object opinion)
        {
            var hashes = _desyncStackTraceHashesField?.GetValue(opinion) as ICollection;
            return hashes?.Count ?? 0;
        }

        private static void LogBudgetHit(int count, string stage)
        {
            if (_loggedBudgetHit)
                return;

            _loggedBudgetHit = true;
            Log.Warning(
                "[MP-MeowOnlineShop] MP desync trace budget reached: " +
                $"stage={stage}, observed={count}, retained={MaxTraceHashesPerOpinion}. " +
                "Random-state synchronization remains fully enabled; disable MP desync traces for normal play.");
        }
    }
}
