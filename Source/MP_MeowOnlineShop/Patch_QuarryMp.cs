using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Quarry (`Ogliss.TheWhiteCrayon.Quarry`) exposes a mining-mode float
    /// menu and an auto-haul toggle on Building_Quarry. Both write saved
    /// fields (`mineModeToggle`, `autoHaul`) that drive the deterministic
    /// quarry job, so every UI executor is registered as a sync method.
    /// </summary>
    internal static class Patch_QuarryMp
    {
        private const string PackageId = "Ogliss.TheWhiteCrayon.Quarry";
        private const string BuildingTypeName = "Quarry.Building_Quarry";

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
                    Log.Message("[MP-MeowOnlineShop] Quarry sync skipped (target mod not active).");
                    return;
                }

                Type buildingType = AccessTools.TypeByName(BuildingTypeName);
                if (buildingType == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Quarry target type not resolved; patch skipped.");
                    return;
                }

                int registered = 0;
                registered += TryRegister(
                    AccessTools.Method(buildingType, "MineModeResources", new[] { buildingType }));
                registered += TryRegister(
                    AccessTools.Method(buildingType, "MineModeBlocks", new[] { buildingType }));
                registered += TryRegister(
                    AccessTools.Method(buildingType, "MineModeChunks", new[] { buildingType }));
                registered += TryRegister(
                    AccessTools.Method(buildingType, "ToggleAutoHaul", Type.EmptyTypes));

                if (registered == 0)
                    Log.Warning("[MP-MeowOnlineShop] Quarry sync registration failed; patch skipped.");
                else
                    Log.Message("[MP-MeowOnlineShop] Quarry MP patch active: " + registered + " sync methods.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Quarry MP compat restore failed: " + e.Message);
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
                Log.Warning("[MP-MeowOnlineShop] Quarry sync register failed on " + method.Name + ": " + e.Message);
                return 0;
            }
        }
    }
}
