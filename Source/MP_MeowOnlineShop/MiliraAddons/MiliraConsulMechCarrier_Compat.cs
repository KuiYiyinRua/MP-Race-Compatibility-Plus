using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat
{
    /// <summary>
    /// The Milira Consul ("BishopIII") inherits both state-changing gizmos
    /// from AncotLibrary.CompMechCarrier_Custom. Multiplayer registers the
    /// vanilla CompMechCarrier callbacks, but those registrations do not
    /// cover the custom carrier's declaring type. The release callback was
    /// previously registered here; the recover callback must also be
    /// registered because it creates resources, destroys the stored pawns,
    /// clears spawnedPawns, and starts the recovery cooldown.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MiliraConsulMechCarrier_Compat
    {
        private const string PackageId = "ancot.milirarace";
        private const string CarrierTypeName =
            "AncotLibrary.CompMechCarrier_Custom";
        private const string ConsulCarrierTypeName =
            "Milira.CompMechCarrier_Consul";
        private const string TrySpawnPawnsName = "TrySpawnPawns";
        private const string CompGetGizmosExtraName = "CompGetGizmosExtra";
        private const int RecoverLambdaOrdinal = 3;

        private static bool _initialized;

        static MiliraConsulMechCarrier_Compat()
        {
            if (MiliraMpCompatGate.ReferenceModActive)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "skipped: usamiseika.fixmod.miliramultiplayer is active.");
                return;
            }

            if (!MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            try
            {
                Initialize();
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "failed: " + e);
            }
        }

        private static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;

            Type carrierType = AccessTools.TypeByName(CarrierTypeName);
            Type consulType = AccessTools.TypeByName(ConsulCarrierTypeName);
            if (carrierType == null || consulType == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "skipped: carrier/consul types were not resolved " +
                    $"(carrier={carrierType != null}, consul={consulType != null}).");
                return;
            }

            MethodInfo trySpawnPawns = AccessTools.Method(
                carrierType,
                TrySpawnPawnsName,
                Type.EmptyTypes);
            if (trySpawnPawns == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "skipped: TrySpawnPawns was not found.");
                return;
            }

            MethodInfo compGetGizmosExtra = AccessTools.DeclaredMethod(
                carrierType,
                CompGetGizmosExtraName,
                Type.EmptyTypes);
            if (compGetGizmosExtra == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "warning: custom carrier CompGetGizmosExtra was not found; " +
                    "recovery callback cannot be registered.");
            }

            bool releaseRegistered = false;
            bool recoverRegistered = false;
            try
            {
                MP.RegisterSyncMethod(trySpawnPawns, (SyncType[])null);
                releaseRegistered = true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "release registration failed: " + e.Message);
            }

            if (compGetGizmosExtra != null)
            {
                try
                {
                    // In the current Ancot 1.6 assembly, lambda 3 is the
                    // recovery action (<CompGetGizmosExtra>b__31_3). It is
                    // the same callback MP calls "Empty" for vanilla carriers.
                    MP.RegisterSyncMethodLambda(
                        carrierType,
                        CompGetGizmosExtraName,
                        RecoverLambdaOrdinal);
                    recoverRegistered = true;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                        "recovery registration failed: " + e.Message);
                }
            }

            Log.Message(
                "[MP-MeowOnlineShop] Milira Consul mech-carrier sync active: " +
                "release=" + releaseRegistered + ", recovery=" +
                recoverRegistered + " (CompGetGizmosExtra lambda " +
                RecoverLambdaOrdinal + ").");
        }
    }
}
