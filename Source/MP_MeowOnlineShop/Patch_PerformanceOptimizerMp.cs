using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Performance Optimizer applies gameplay patches through process-local
    /// caches and installs hundreds of transpilers over rendered frames. A host
    /// that has already simulated a local world and a joining client can therefore
    /// execute different Plant.TickLong paths and consume different map Rand.
    /// </summary>
    internal static class Patch_PerformanceOptimizerMp
    {
        private const string PerformanceOptimizerHarmonyId = "PerformanceOptimizer.Main";

        private static bool _mpPreparationRequested;
        private static bool _cleanupLogged;

        internal static void Apply(Harmony harmony)
        {
            var performPatchesType =
                AccessTools.TypeByName("PerformanceOptimizer.PerformPatchesPerFrames");
            var performPatches = AccessTools.Method(performPatchesType, "PerformPatches");
            var suppressPrefix = AccessTools.Method(
                typeof(Patch_PerformanceOptimizerMp),
                nameof(PerformPatchesPrefix));
            var moveNext = performPatchesType?
                .GetNestedTypes(AccessTools.all)
                .Select(type => AccessTools.Method(type, "MoveNext"))
                .FirstOrDefault(method => method != null);
            var moveNextPrefix = AccessTools.Method(
                typeof(Patch_PerformanceOptimizerMp),
                nameof(PerformPatchesMoveNextPrefix));

            int entrypoints = 0;
            if (performPatches != null && suppressPrefix != null)
            {
                harmony.Patch(
                    performPatches,
                    prefix: new HarmonyMethod(suppressPrefix) { priority = Priority.First });
            }
            if (moveNext != null && moveNextPrefix != null)
            {
                harmony.Patch(
                    moveNext,
                    prefix: new HarmonyMethod(moveNextPrefix) { priority = Priority.First });
            }

            var preparePrefix = AccessTools.Method(
                typeof(Patch_PerformanceOptimizerMp),
                nameof(PrepareForMultiplayerPrefix));
            foreach (var target in ResolveMultiplayerEntryPoints())
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(preparePrefix) { priority = Priority.First });
                entrypoints++;
            }

            if (performPatches == null && entrypoints == 0)
                return;

            Log.Message(
                "[MP-MeowOnlineShop] Performance Optimizer MP guard active: " +
                $"factoryGuard={(performPatches != null)}, moveNextGuard={(moveNext != null)}, " +
                $"mpEntrypoints={entrypoints}.");
        }

        private static IEnumerable<MethodBase> ResolveMultiplayerEntryPoints()
        {
            var seen = new HashSet<MethodBase>();
            var candidates = new[]
            {
                AccessTools.Method("Multiplayer.Client.HostUtil:HostServer"),
                AccessTools.Method("Multiplayer.Client.HostWindow:HostProgrammatically"),
                AccessTools.Method("Multiplayer.Client.ClientUtil:TryConnectWithWindow")
            };

            foreach (var candidate in candidates)
            {
                if (candidate != null && seen.Add(candidate))
                    yield return candidate;
            }
        }

        private static bool PerformPatchesPrefix(ref IEnumerator __result)
        {
            if (!_mpPreparationRequested)
                return true;

            __result = EmptyEnumerator();
            return false;
        }

        private static IEnumerator EmptyEnumerator()
        {
            yield break;
        }

        private static bool PerformPatchesMoveNextPrefix(ref bool __result)
        {
            if (!_mpPreparationRequested)
                return true;

            __result = false;
            return false;
        }

        private static void PrepareForMultiplayerPrefix()
        {
            if (_mpPreparationRequested)
                return;

            _mpPreparationRequested = true;
            StopPendingPerFramePatches();

            int removed = 0;
            var unpatcher = new Harmony("mp.meowonlineshop.performanceoptimizer.cleanup");
            foreach (var original in Harmony.GetAllPatchedMethods().ToList())
            {
                var patchInfo = Harmony.GetPatchInfo(original);
                if (patchInfo == null)
                    continue;

                foreach (var patch in EnumeratePatches(patchInfo))
                {
                    if (patch.owner != PerformanceOptimizerHarmonyId)
                        continue;

                    unpatcher.Unpatch(original, patch.PatchMethod);
                    removed++;
                }
            }

            if (!_cleanupLogged)
            {
                _cleanupLogged = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Multiplayer startup disabled all Performance Optimizer " +
                    $"runtime patches and per-frame transpilers; removedPatches={removed}. " +
                    "Single-player behavior before starting/joining multiplayer was left unchanged.");
            }
        }

        private static IEnumerable<Patch> EnumeratePatches(Patches patchInfo)
        {
            foreach (var patch in patchInfo.Prefixes)
                yield return patch;
            foreach (var patch in patchInfo.Postfixes)
                yield return patch;
            foreach (var patch in patchInfo.Transpilers)
                yield return patch;
            foreach (var patch in patchInfo.Finalizers)
                yield return patch;
        }

        private static void StopPendingPerFramePatches()
        {
            try
            {
                var modType = AccessTools.TypeByName(
                    "PerformanceOptimizer.PerformanceOptimizerMod");
                var performerField = AccessTools.Field(modType, "performPatchesPerFrames");
                var performer = performerField?.GetValue(null);
                if (performer == null)
                    return;

                AccessTools.Method(performer.GetType(), "StopAllCoroutines")
                    ?.Invoke(performer, null);

                var optimization = AccessTools.Field(
                    performer.GetType(),
                    "optimization")?.GetValue(performer);
                var pending = AccessTools.Field(
                    optimization?.GetType(),
                    "patchesToPerform")?.GetValue(optimization) as IList;
                pending?.Clear();
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Performance Optimizer pending-patch cleanup failed: " +
                    e.Message);
            }
        }
    }
}
