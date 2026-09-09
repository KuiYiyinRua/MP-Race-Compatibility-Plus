using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Achtung! (`brrainz.achtung`) has a large UI surface. This patch covers
    /// the stable executor subset that is currently unsynchronized: direct
    /// draft writes (`Tools.SetDraftStatus` writes `draftedInt` and bypasses
    /// Multiplayer's Drafted property sync) and the ForcedWork removal
    /// methods that mutate the saved forced-job WorldComponent.
    ///
    /// ForceAction's item search and the drag-time `cellRadius`/target
    /// expansion remain a separate residual risk.
    /// </summary>
    internal static class Patch_AchtungMp
    {
        private const string PackageId = "brrainz.achtung";

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Achtung sync skipped (target mod not active).");
                    return;
                }

                Type toolsType = AccessTools.TypeByName("AchtungMod.Tools");
                Type forcedWorkType = AccessTools.TypeByName("AchtungMod.ForcedWork");
                if (toolsType == null || forcedWorkType == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Achtung target types not resolved; patch skipped.");
                    return;
                }

                int registered = 0;
                registered += TryRegister(
                    AccessTools.Method(toolsType, "SetDraftStatus", new[] { typeof(Pawn), typeof(bool) }));
                registered += TryRegister(
                    AccessTools.Method(forcedWorkType, "Remove", new[] { typeof(Pawn) }));
                registered += TryRegister(
                    AccessTools.Method(forcedWorkType, "RemoveForcedJob", new[] { typeof(Pawn) }));

                if (registered == 0)
                    Log.Warning("[MP-MeowOnlineShop] Achtung sync registration failed; patch skipped.");
                else
                    Log.Message("[MP-MeowOnlineShop] Achtung MP patch active: " + registered + " sync methods (stable executor subset).");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Achtung MP compat restore failed: " + e.Message);
            }
        }

        private static int TryRegister(MethodInfo method)
        {
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Achtung sync register failed on " + method.Name + ": " + e.Message);
                return 0;
            }
        }
    }
}
