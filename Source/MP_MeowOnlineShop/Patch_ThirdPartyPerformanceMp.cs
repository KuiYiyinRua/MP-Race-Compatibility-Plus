using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Removes simulation-changing performance patches at the MP entry point.
    /// Chokey skips Pawn.Tick and injects extra health/skill ticks, while
    /// PerformanceEsmolas schedules work from process-local hashes and caches.
    /// Both are safe to retain in single-player before hosting/joining.
    /// </summary>
    internal static class Patch_ThirdPartyPerformanceMp
    {
        private static readonly string[] UnsafeOwners =
        {
            "com.tpsoptymalizer",
            "Arkymn.PerformanceEsmolas"
        };

        private static bool _prepared;

        internal static void Apply(Harmony harmony)
        {
            var prefix = AccessTools.Method(
                typeof(Patch_ThirdPartyPerformanceMp),
                nameof(PrepareForMultiplayerPrefix));
            int entrypoints = 0;
            foreach (var target in ResolveMultiplayerEntryPoints())
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                entrypoints++;
            }

            if (entrypoints > 0)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Third-party performance MP guard active: " +
                    $"owners={string.Join(",", UnsafeOwners)}, mpEntrypoints={entrypoints}.");
            }
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

        private static void PrepareForMultiplayerPrefix()
        {
            if (_prepared)
                return;
            _prepared = true;

            var removedByOwner = UnsafeOwners.ToDictionary(owner => owner, owner => 0);
            var unpatcher = new Harmony("mp.meowonlineshop.thirdpartyperformance.cleanup");
            foreach (var original in Harmony.GetAllPatchedMethods().ToList())
            {
                var patchInfo = Harmony.GetPatchInfo(original);
                if (patchInfo == null)
                    continue;

                foreach (var patch in EnumeratePatches(patchInfo).ToList())
                {
                    if (!removedByOwner.ContainsKey(patch.owner) || patch.PatchMethod == null)
                        continue;
                    unpatcher.Unpatch(original, patch.PatchMethod);
                    removedByOwner[patch.owner]++;
                }
            }

            foreach (var pair in removedByOwner)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Multiplayer startup disabled unsafe performance patches: " +
                    $"owner={pair.Key}, removedPatches={pair.Value}. " +
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
    }
}
