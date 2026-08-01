using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-134: "科研蓝图应用" (apply techprint). Both peers created
    /// `JobDriver_ApplyTechprint` at the same tick (1534753), but the first
    /// divergent map-1 trace (1535132) is a TickList member-order difference
    /// (`Turkey151004` vs `Axolotl1118`) with byte-identical preceding Rand
    /// draws, and it occurs during the 600-tick wait before
    /// `ResearchManager.ApplyTechprint` runs. That is the same map
    /// entity/TickList drift class as Desync-130/131/132/133, not a
    /// techprint-specific Rand/ID mutation.
    ///
    /// This bounded diagnostic records the job targets and the map-state
    /// snapshot at job creation and at `ApplyTechprint`, once per session, so
    /// the next bundle proves whether the techprint path contributes to the
    /// drift or is coincidental. It changes no simulation behavior.
    /// </summary>
    internal static class Patch_TechprintMpDiagnostic
    {
        private const string LogTag =
            "[MP-MeowOnlineShop][TechprintDiag]";

        private static bool _applied;
        private static readonly HashSet<string> LoggedKeys =
            new HashSet<string>();

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo apply = AccessTools.Method(
                    typeof(ResearchManager),
                    nameof(ResearchManager.ApplyTechprint),
                    new[] { typeof(ResearchProjectDef), typeof(Pawn) });
                MethodInfo applyPrefix = AccessTools.Method(
                    typeof(Patch_TechprintMpDiagnostic),
                    nameof(ApplyTechprintPrefix));

                Type driverType = AccessTools.TypeByName(
                    "RimWorld.JobDriver_ApplyTechprint");
                ConstructorInfo driverCtor = driverType == null
                    ? null
                    : AccessTools.FirstConstructor(
                        driverType,
                        _ => true);
                MethodInfo ctorPostfix = AccessTools.Method(
                    typeof(Patch_TechprintMpDiagnostic),
                    nameof(JobDriverCtorPostfix));

                if (apply == null || applyPrefix == null ||
                    driverCtor == null || ctorPostfix == null)
                {
                    Log.Warning(
                        LogTag + " target resolution failed: " +
                        $"apply={apply != null} driver={driverType != null} " +
                        $"ctor={driverCtor != null}; diagnostics disabled.");
                    return;
                }

                harmony.Patch(
                    apply,
                    prefix: new HarmonyMethod(applyPrefix)
                    {
                        priority = Priority.First
                    });
                harmony.Patch(
                    driverCtor,
                    postfix: new HarmonyMethod(ctorPostfix)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    LogTag + " active: job creation and ApplyTechprint are " +
                    "recorded once per session (map state included).");
            }
            catch (Exception e)
            {
                Log.Warning(LogTag + " apply failed: " + e);
            }
        }

        private static void JobDriverCtorPostfix(JobDriver __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return;
            if (!LoggedKeys.Add("job-created"))
                return;

            try
            {
                Pawn pawn = __instance.pawn;
                Job job = __instance.job;
                string pawnId = pawn?.thingIDNumber.ToString() ?? "?";
                string techprintId = "?";
                string benchId = "?";
                if (job != null)
                {
                    techprintId =
                        job.GetTarget(TargetIndex.B).Thing?.thingIDNumber
                        .ToString() ?? "?";
                    benchId =
                        job.GetTarget(TargetIndex.A).Thing?.thingIDNumber
                        .ToString() ?? "?";
                }

                Log.Message(
                    LogTag + " ApplyTechprint job created: tick=" +
                    $"{Find.TickManager?.TicksGame ?? -1}, pawn={pawnId}, " +
                    $"techprint={techprintId}, bench={benchId}, " +
                    MapStateSuffix());
            }
            catch (Exception e)
            {
                Log.Warning(LogTag + " job diagnostic failed: " + e.Message);
            }
        }

        private static void ApplyTechprintPrefix(
            ResearchProjectDef proj,
            Pawn applyingPawn)
        {
            if (!MP.IsInMultiplayer)
                return;

            string key = "apply-" + (proj?.defName ?? "null") + "-" +
                         (applyingPawn?.thingIDNumber ?? -1);
            if (!LoggedKeys.Add(key))
                return;

            try
            {
                int techprints = 0;
                if (proj != null && Find.ResearchManager != null)
                    techprints = Find.ResearchManager.GetTechprints(proj);

                Log.Message(
                    LogTag + " ApplyTechprint: tick=" +
                    $"{Find.TickManager?.TicksGame ?? -1}, project=" +
                    $"{proj?.defName ?? "null"}, pawn=" +
                    $"{applyingPawn?.thingIDNumber ?? -1}, techprints={techprints}, " +
                    MapStateSuffix());
            }
            catch (Exception e)
            {
                Log.Warning(LogTag + " apply diagnostic failed: " + e.Message);
            }
        }

        private static string MapStateSuffix()
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null)
                    return "map=null";

                int thingCount = map.listerThings?.AllThings?.Count ?? -1;
                int pawnCount = map.mapPawns?.AllPawnsSpawned?.Count ?? -1;
                return $"map={map.uniqueID}, tick=" +
                       $"{(Find.TickManager?.TicksGame ?? -1)}, " +
                       $"listerThings={thingCount}, spawnedPawns={pawnCount}";
            }
            catch
            {
                return "mapState=?";
            }
        }
    }
}
