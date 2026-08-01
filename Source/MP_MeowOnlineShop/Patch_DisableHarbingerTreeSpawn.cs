using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// User request: disable the "厄兆噬树萌芽" (Harbinger tree sprout) event
    /// entirely. It is one of the recurring desync triggers and the player
    /// accepts losing that event content.
    ///
    /// The event has two vanilla entry points:
    /// - `GameComponent_Anomaly.TrySpawnHarbingerTrees` queues
    ///   `IncidentDefOf.HarbingerTreeSpawn` on a timer;
    /// - `IncidentWorker_HarbingerTreeSpawn.TryExecuteWorker` performs the
    ///   actual spawn (and `CanFireNowSub` is the eligibility probe).
    ///
    /// In multiplayer all three are skipped so the event can never be queued,
    /// selected, or executed, and its Rand/eligibility probing cannot advance
    /// the synchronized stream. Singleplayer is also disabled because the
    /// request is unconditional for this event.
    /// </summary>
    internal static class Patch_DisableHarbingerTreeSpawn
    {
        private static bool _applied;
        private static bool _loggedActive;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            try
            {
                int patched = 0;

                patched += TryPatchSkip(
                    harmony,
                    "RimWorld.GameComponent_Anomaly",
                    "TrySpawnHarbingerTrees",
                    Type.EmptyTypes);

                Type workerType = AccessTools.TypeByName(
                    "RimWorld.IncidentWorker_HarbingerTreeSpawn");
                if (workerType != null)
                {
                    patched += TryPatchBoolResult(
                        harmony,
                        workerType,
                        "CanFireNowSub",
                        new[] { typeof(IncidentParms) });
                    patched += TryPatchBoolResult(
                        harmony,
                        workerType,
                        "TryExecuteWorker",
                        new[] { typeof(IncidentParms) });
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Harbinger tree spawn disable NOT active: " +
                        "no vanilla entry points resolved.");
                    return;
                }

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Harbinger tree sprout disabled per user " +
                        $"request (entry points patched={patched}).");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Harbinger tree spawn disable apply failed: " +
                    e.Message);
            }
        }

        private static int TryPatchSkip(
            Harmony harmony,
            string typeName,
            string methodName,
            Type[] args)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                MethodInfo target = type == null
                    ? null
                    : AccessTools.Method(type, methodName, args);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_DisableHarbingerTreeSpawn),
                    nameof(SkipPrefix));
                if (target == null || prefix == null)
                    return 0;

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private static int TryPatchBoolResult(
            Harmony harmony,
            Type type,
            string methodName,
            Type[] args)
        {
            try
            {
                MethodInfo target = type == null
                    ? null
                    : AccessTools.Method(type, methodName, args);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_DisableHarbingerTreeSpawn),
                    nameof(BoolResultPrefix));
                if (target == null || prefix == null)
                    return 0;

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private static bool SkipPrefix()
        {
            return false;
        }

        private static bool BoolResultPrefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }
}
