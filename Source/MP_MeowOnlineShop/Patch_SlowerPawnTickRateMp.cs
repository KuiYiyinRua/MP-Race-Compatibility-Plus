using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Slower Pawn Tick Rate changes the scheduler interval before a host converts
    /// a running single-player world into a multiplayer snapshot. A joining client
    /// has no matching pre-join scheduler history, so pawn tick order and map Rand
    /// can diverge. Remove the scheduler patches at both MP entry paths; the host's
    /// normal SaveAndReload then rebuilds tick lists with vanilla intervals.
    /// </summary>
    internal static class Patch_SlowerPawnTickRateMp
    {
        private const string SlowerPawnTickRateHarmonyId = "Arkymn.SlowerPawnTickRate";
        private static bool _prepared;

        internal static void Apply(Harmony harmony)
        {
            if (!OptimizationGate.IsThirdPartyPerfCleanupEnabled)
            {
                OptimizationGate.LogOnce(
                    "thirdparty.perf.cleanup.disabled",
                    "[MP-MeowOnlineShop] Runtime cleanup of Slower Pawn Tick Rate patches is " +
                    "disabled; its scheduler patches are left installed.");
                return;
            }

            var prefix = AccessTools.Method(
                typeof(Patch_SlowerPawnTickRateMp),
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
                    "[MP-MeowOnlineShop] Slower Pawn Tick Rate MP guard active: " +
                    $"mpEntrypoints={entrypoints}.");
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
            var removals = new List<KeyValuePair<MethodBase, MethodInfo>>();

            foreach (var original in Harmony.GetAllPatchedMethods().ToList())
            {
                var patchInfo = Harmony.GetPatchInfo(original);
                if (patchInfo == null)
                    continue;

                AddOwned(removals, original, patchInfo.Prefixes);
                AddOwned(removals, original, patchInfo.Postfixes);
                AddOwned(removals, original, patchInfo.Transpilers);
                AddOwned(removals, original, patchInfo.Finalizers);
            }

            var unpatcher = new Harmony("mp.meowonlineshop.slowerpawntickrate.cleanup");
            foreach (var removal in removals)
                unpatcher.Unpatch(removal.Key, removal.Value);

            Log.Warning(
                "[MP-MeowOnlineShop] Multiplayer startup disabled Slower Pawn Tick Rate " +
                $"scheduler patches; removedPatches={removals.Count}. " +
                "Single-player behavior before starting/joining multiplayer was left unchanged.");
        }

        private static void AddOwned(
            ICollection<KeyValuePair<MethodBase, MethodInfo>> removals,
            MethodBase original,
            IEnumerable<Patch> patches)
        {
            foreach (var patch in patches)
            {
                if (patch.owner == SlowerPawnTickRateHarmonyId && patch.PatchMethod != null)
                {
                    removals.Add(
                        new KeyValuePair<MethodBase, MethodInfo>(
                            original,
                            patch.PatchMethod));
                }
            }
        }
    }
}
