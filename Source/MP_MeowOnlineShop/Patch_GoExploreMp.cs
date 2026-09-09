using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Go Explore! adds `Building_AncientStorageUnitLGE`, which overrides
    /// `Building_Casket.EjectContents`. Multiplayer registers the base
    /// `Building_Casket.EjectContents` as a sync method, but a virtual
    /// override is invoked through the derived type and bypasses the base
    /// registration, so ejecting contents from the mod's ancient storage unit
    /// runs only on the clicking peer.
    ///
    /// Register the exact override so the same command is replayed on all
    /// peers; the base-class registration remains untouched.
    /// </summary>
    internal static class Patch_GoExploreMp
    {
        private const string PackageId = "albion.goexplore";
        private const string StorageTypeName = "LetsGoExplore.Building_AncientStorageUnitLGE";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type storageType = AccessTools.TypeByName(StorageTypeName);
            MethodInfo ejectContents = storageType == null
                ? null
                : AccessTools.Method(storageType, "EjectContents", Type.EmptyTypes);

            if (storageType == null || ejectContents == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Go Explore storage unit target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(ejectContents, null)
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                Log.Message("[MP-MeowOnlineShop] Go Explore MP patch active: AncientStorageUnit eject syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Go Explore eject sync registration failed: " + e.Message);
            }
        }
    }
}
