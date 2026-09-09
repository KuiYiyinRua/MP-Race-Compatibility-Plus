using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Anomaly DLC endgame fix for multiplayer.
    ///
    /// The last monolith attune (Waking -> VoidAwakened) is gated by a vanilla
    /// Dialog_MessageBox confirmation opened from inside
    /// JobDriver_ActivateMonolith when
    /// Find.Anomaly.NextLevelDef == MonolithLevelDefOf.VoidAwakened.
    ///
    /// Multiplayer does not synchronize Dialog_MessageBox buttons (only
    /// Dialog_NodeTree is handled by the PersistentDialog system) and forces
    /// WindowStack.WindowsForcePause to false, so in an MP game this modal:
    ///   - does not pause the shared simulation;
    ///   - absorbs input on every peer that still has it open;
    ///   - runs its button actions locally only: "Confirm" just closes the
    ///     clicker's copy, while "GoBack"/ESC runs
    ///     pawn.jobs.EndCurrentJob(JobCondition.Succeeded) on the clicking
    ///     machine alone.
    /// The local-only EndCurrentJob aborts the attune job on one peer, so
    /// Building_VoidMonolith.Activate -> AnomalyManager.IncrementLevel ->
    /// GameComponent_Anomaly.TriggerVoidAwakening (the node that starts the
    /// game-ending VoidAwakening quest) never completes on both peers, and the
    /// finale cannot be triggered consistently in multiplayer.
    ///
    /// Fix (MP only): intercept WindowStack.Add and drop this specific
    /// confirmation MessageBox. The attune job then proceeds deterministically
    /// on every peer and the VoidAwakened level / endgame quest starts on all
    /// of them. The player can still abort the attune through synced means
    /// (drafting or cancelling the job), and the vanilla post-activation
    /// letters still inform both players.
    ///
    /// The patch is narrow: it only matches the VoidAwakening confirmation
    /// (translated text key plus the monolith being at the VoidAwakened
    /// boundary), is active only in multiplayer, and fails open (returns true)
    /// on any resolution error.
    /// </summary>
    internal static class Patch_AnomalyVoidMonolithEndingMp
    {
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                MethodInfo windowStackAdd = AccessTools.Method(
                    typeof(WindowStack),
                    "Add",
                    new[] { typeof(Window) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_AnomalyVoidMonolithEndingMp),
                    nameof(SkipVoidAwakeningConfirmationPrefix));

                if (windowStackAdd != null && prefix != null)
                {
                    harmony.Patch(
                        windowStackAdd,
                        prefix: new HarmonyMethod(prefix)
                        {
                            priority = Priority.First
                        });
                    Log.Message(
                        "[MP-MeowOnlineShop] Anomaly final attune confirmation " +
                        "guard applied (drops unsynced Dialog_MessageBox in MP).");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Anomaly final attune confirmation " +
                    "guard failed: " + e.Message);
            }
        }

        private static bool SkipVoidAwakeningConfirmationPrefix(Window window)
        {
            if (!MP.IsInMultiplayer || window == null || !(window is Dialog_MessageBox msg))
                return true;

            try
            {
                if ((string)msg.text != (string)"VoidAwakeningConfirmationText".Translate())
                    return true;

                // Belt-and-suspenders: only skip when the monolith is at the
                // final attune boundary, the same condition under which the
                // vanilla job driver opens this confirmation.
                if (Find.Anomaly == null ||
                    Find.Anomaly.NextLevelDef != MonolithLevelDefOf.VoidAwakened)
                {
                    return true;
                }

                // Drop the unsynced modal in multiplayer so the attune job
                // proceeds deterministically on every peer.
                return false;
            }
            catch
            {
                // Never block a window add because our guard failed to resolve
                // Anomaly state (e.g. main menu / DLC not active).
                return true;
            }
        }
    }
}
