using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-513/514/516/519/520/522 (2026-08-20, faction trade and combat):
    /// every first divergence is a pawn's Pawn_JobTracker.EndCurrentJob on ONE
    /// peer that consumes an extra UniqueIDsManager.GetNextJobID while the other
    /// peer ticks an unrelated building MTB at the same tick, shifting the map
    /// Rand stream by one index. The bundle's 2-3 tick trace window never shows
    /// the upstream job-state mutation (the same pawn's job ends ~1 tick apart
    /// on the two peers). This env-gated recorder (MP_JOB_END_DIAG=1, bounded)
    /// logs every EndCurrentJob with the pawn, the job being ended, the
    /// condition, and the world/map ticks on both peers, so the next
    /// reproduction directly shows which pawn's job ends one tick apart, what
    /// job it was, and on which map - making the upstream mutation traceable.
    /// No-op unless enabled; adds no sync commands; fail-open.
    /// </summary>
    internal static class Patch_JobEndDiagnostic
    {
        private const string EnvVarName = "MP_JOB_END_DIAG";
        private const int MaxRecords = 32;

        internal static readonly bool Enabled =
            string.Equals(
                Environment.GetEnvironmentVariable(EnvVarName),
                "1",
                StringComparison.OrdinalIgnoreCase);

        private static int _recordCount;
        private static bool _installed;
        private static readonly System.Reflection.FieldInfo JobTrackerPawnField =
            AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

        internal static void Apply(Harmony harmony)
        {
            if (!Enabled || _installed || harmony == null)
                return;

            _installed = true;
            try
            {
                harmony.Patch(
                    AccessTools.Method(
                        typeof(Pawn_JobTracker),
                        nameof(Pawn_JobTracker.EndCurrentJob),
                        new[]
                        {
                            typeof(JobCondition),
                            typeof(bool),
                            typeof(bool)
                        }),
                    prefix: new HarmonyMethod(
                        typeof(Patch_JobEndDiagnostic),
                        nameof(EndCurrentJobPrefix))
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Job-end diagnostic active " +
                    "(MP_JOB_END_DIAG=1).");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Job-end diagnostic install failed: " +
                    e.Message);
            }
        }

        private static void EndCurrentJobPrefix(
            Pawn_JobTracker __instance,
            JobCondition condition,
            bool startNewJob)
        {
            if (!MP.IsInMultiplayer || _recordCount >= MaxRecords)
                return;

            try
            {
                _recordCount++;
                Pawn pawn = null;
                try { pawn = JobTrackerPawnField?.GetValue(__instance) as Pawn; }
                catch { }
                Job job = __instance?.curJob;
                Map map = pawn?.Map;
                Log.Message(
                    "[MP-JOB-END-DIAG] EndCurrentJob pawn=" +
                    (pawn != null ? pawn.ThingID : "null") +
                    " job=" + (job?.def?.defName ?? "null") +
                    " cond=" + condition +
                    " startNew=" + startNewJob +
                    " worldTick=" + Find.TickManager.TicksGame +
                    " map=" + (map != null ? map.uniqueID.ToString() : "-") +
                    " drafted=" + (pawn != null && pawn.Drafted));
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Job-end diagnostic failed: " +
                    e.Message);
            }
        }
    }
}
