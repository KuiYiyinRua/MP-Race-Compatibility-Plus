using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Syncs MoeLotl/Axolotl's jump-mode apparel gizmo. The component keeps
    /// Is_Change in a private, non-serialized field, and Verb_ExplosionJump
    /// chooses between the normal jump and the wide-dive explosion from it.
    ///
    /// CompGetWornGizmosExtra lambda 0 is the hover-only preview; lambda 1 is
    /// the Command_Action callback that toggles Is_Change.
    /// </summary>
    internal static class Patch_AxolotlJumpModeSync
    {
        private const string JumpChangeTypeName = "Axolotl.CompJumpChange";
        private const string GizmoMethodName = "CompGetWornGizmosExtra";
        private const int ToggleActionLambdaOrdinal = 1;

        internal static void Apply()
        {
            Type jumpChangeType = AccessTools.TypeByName(JumpChangeTypeName);
            if (jumpChangeType == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Axolotl jump mode sync: target " +
                    "assembly not active; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethodLambda(
                    jumpChangeType,
                    GizmoMethodName,
                    ToggleActionLambdaOrdinal);

                Log.Message(
                    "[MP-MeowOnlineShop] Axolotl jump mode sync active: " +
                    "CompJumpChange.CompGetWornGizmosExtra action lambda=" +
                    ToggleActionLambdaOrdinal + " registered.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl jump mode sync registration " +
                    "failed: " + e.Message);
            }
        }
    }
}
