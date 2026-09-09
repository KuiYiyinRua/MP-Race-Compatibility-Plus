using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Oberonia Aurea's crashed science ship sets the saved
    /// `gravityAdjuestType` field and then issues a gravity-adjustment job from
    /// UI float menus. Registering `MakeGravityAdjustmentJob` keeps both the
    /// field write and the ordered job inside one sync boundary.
    /// </summary>
    internal static class Patch_OberoniaScienceShipMp
    {
        private const string PackageId = "oark.ratkinfaction.oberoniaaurea";
        private const string CompTypeName = "OberoniaAurea.CompCrashedScienceShip";

        private static bool _applied;
        private static Type _compType;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Oberonia science ship sync skipped (target mod not active).");
                    return;
                }

                _compType = AccessTools.TypeByName(CompTypeName);
                MethodInfo makeGravityJob = _compType == null
                    ? null
                    : AccessTools.Method(
                        _compType,
                        "MakeGravityAdjustmentJob",
                        new[] { typeof(Pawn), typeof(int) });

                if (_compType == null || makeGravityJob == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Oberonia science ship targets not resolved; patch skipped.");
                    return;
                }

                MP.RegisterSyncMethod(makeGravityJob, null);
                Log.Message("[MP-MeowOnlineShop] Oberonia science ship MP patch active: MakeGravityAdjustmentJob synced.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Oberonia science ship MP compat restore failed: " + e.Message);
            }
        }
    }
}
