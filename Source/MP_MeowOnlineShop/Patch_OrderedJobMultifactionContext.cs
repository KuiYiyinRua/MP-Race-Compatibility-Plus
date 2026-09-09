using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Async command replay normally takes its faction from the ScheduledCommand.
    /// A cold-rejoined multifaction peer can retain its own player faction while
    /// replaying an ordered pawn job, which makes vanilla consume a different
    /// random stream before the job is accepted.  Pawn ownership is serialized
    /// with the job target and is the stable authority for this command.
    /// </summary>
    internal static class Patch_OrderedJobMultifactionContext
    {
        private static bool _applied;
        private static FieldInfo _ofPlayerField;
        private static FieldInfo _executingCmdMapField;
        private static FieldInfo _pawnField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(Pawn_JobTracker),
                    nameof(Pawn_JobTracker.TryTakeOrderedJob));
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_OrderedJobMultifactionContext), nameof(Prefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_OrderedJobMultifactionContext), nameof(Finalizer));
                Type asyncTimeType = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");

                _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
                _pawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
                _executingCmdMapField = asyncTimeType == null
                    ? null
                    : AccessTools.Field(asyncTimeType, "executingCmdMap");

                if (target == null || prefix == null || finalizer == null ||
                    _ofPlayerField == null || _pawnField == null || _executingCmdMapField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ordered-job multifaction context " +
                        "target resolution failed.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

                Log.Message(
                    "[MP-MeowOnlineShop] Ordered-job multifaction context active: " +
                    "async command replay uses the ordered pawn's faction.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ordered-job multifaction context patch failed: " +
                    e.Message);
            }
        }

        private static void Prefix(Pawn_JobTracker __instance, ref ContextState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || __instance == null ||
                !MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) || !multifaction)
            {
                return;
            }

            try
            {
                Map executingMap = _executingCmdMapField.GetValue(null) as Map;
                Pawn pawn = _pawnField.GetValue(__instance) as Pawn;
                FactionManager factionManager = Find.FactionManager;
                if (executingMap == null || pawn == null || factionManager == null ||
                    pawn.MapHeld != executingMap)
                {
                    return;
                }

                Faction owner = pawn.Faction;
                if (owner == null || !owner.IsPlayer)
                    return;

                Faction previous = _ofPlayerField.GetValue(factionManager) as Faction;
                if (previous == owner)
                    return;

                _ofPlayerField.SetValue(factionManager, owner);
                __state = new ContextState(factionManager, previous);
            }
            catch
            {
                __state = null;
            }
        }

        private static void Finalizer(ContextState __state)
        {
            if (__state == null)
                return;

            try
            {
                _ofPlayerField.SetValue(__state.FactionManager, __state.Previous);
            }
            catch
            {
                // AsyncTimeComp restores its outer command context after this call.
            }
        }

        private sealed class ContextState
        {
            internal readonly FactionManager FactionManager;
            internal readonly Faction Previous;

            internal ContextState(FactionManager factionManager, Faction previous)
            {
                FactionManager = factionManager;
                Previous = previous;
            }
        }
    }
}
