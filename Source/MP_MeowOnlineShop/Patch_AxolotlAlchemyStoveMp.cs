using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Axolotl's "通天仪" (Building_AlchemyStove_Base) exposes a UI gizmo that
    /// toggles isAutoAddStuff directly and a float menu that changes the target
    /// pill. Those fields drive WorkGiver_FillAlchemyStove, so a client-only
    /// toggle makes only that peer issue haul/fill jobs and the next SplitOff
    /// desyncs (Desync-207). Sync the auto-add gizmo lambda and the pill reset
    /// methods so the whole UI workflow replays on every peer.
    /// </summary>
    internal static class Patch_AxolotlAlchemyStoveMp
    {
        private const string StoveTypeName = "Axolotl.Building_AlchemyStove_Base";

        internal static void Apply()
        {
            Type stoveType = AccessTools.TypeByName(StoveTypeName);
            if (stoveType == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Axolotl alchemy stove MP: target " +
                    "assembly not active; patch skipped.");
                return;
            }

            int registered = 0;
            try
            {
                MP.RegisterSyncMethodLambda(stoveType, "GetGizmos", 0)
                    .SetContext(SyncContext.MapSelected);
                registered++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl alchemy stove MP: auto-add " +
                    "gizmo lambda registration failed: " + e.Message);
            }

            try
            {
                MP.RegisterSyncMethod(stoveType, "Action_ResetTargetPill")
                    .SetContext(SyncContext.MapSelected);
                registered++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl alchemy stove MP: pill reset " +
                    "registration failed: " + e.Message);
            }

            try
            {
                MP.RegisterSyncMethod(stoveType, "Action_ClearRefineStuff")
                    .SetContext(SyncContext.MapSelected);
                registered++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl alchemy stove MP: clear stuff " +
                    "registration failed: " + e.Message);
            }

            Log.Message(
                "[MP-MeowOnlineShop] Axolotl alchemy stove MP active: " +
                "auto-add gizmo and pill workflow sync registered=" +
                registered + "/3.");
        }
    }
}
