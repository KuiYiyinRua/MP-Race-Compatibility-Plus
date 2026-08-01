using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Milira's fixed day-two Fallen Angel incident creates a quest-owned pawn/drop pod on the
    /// host only in Multiplayer. The unspawned pawn is registered in the host TickList and starts
    /// ticking before the client has a matching object, which immediately desynchronizes Thing IDs
    /// and all later pawn ticks.
    ///
    /// Until that third-party storyteller component is made deterministic, consume only this exact
    /// incident in Multiplayer. Returning success is intentional: StorytellerComp_SingleOnceFixed
    /// must mark the one-shot incident as fired instead of retrying it every interval. Singleplayer
    /// and every other Milira quest remain untouched.
    /// </summary>
    internal static class Patch_MiliraFallenAngelQuest
    {
        private const string WorkerTypeName = "Milira.IncidentWorker_GiveQuestExceptMiliraScenario";
        private const string IncidentDefName = "Milira_FallenAngel_Drop";
        private static bool loggedSuppression;

        public static void Apply(Harmony harmony)
        {
            if (!MP.enabled || harmony == null)
                return;

            try
            {
                var workerType = AccessTools.TypeByName(WorkerTypeName);
                var target = workerType == null
                    ? null
                    : workerType.GetMethod(
                        "TryExecuteWorker",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly,
                        null,
                        new[] { typeof(IncidentParms) },
                        null);
                var prefix = AccessTools.Method(
                    typeof(Patch_MiliraFallenAngelQuest),
                    nameof(TryExecuteWorkerPrefix));

                if (target == null || prefix == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Milira Fallen Angel guard not applied " +
                        "(compatible worker method not present).");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                Log.Message(
                    "[MP-MeowOnlineShop] Milira Fallen Angel MP guard applied " +
                    "(day-two host-only quest suppressed; singleplayer unchanged).");
            }
            catch (Exception ex)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira Fallen Angel MP guard failed: {ex}");
            }
        }

        private static bool TryExecuteWorkerPrefix(IncidentWorker __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer ||
                __instance?.def == null ||
                !string.Equals(__instance.def.defName, IncidentDefName, StringComparison.Ordinal))
            {
                return true;
            }

            __result = true;
            if (!loggedSuppression)
            {
                loggedSuppression = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Suppressed Milira_FallenAngel_Drop in Multiplayer: " +
                    "its quest-owned hidden pawn/drop pod was generated on the host only.");
            }
            return false;
        }
    }
}
