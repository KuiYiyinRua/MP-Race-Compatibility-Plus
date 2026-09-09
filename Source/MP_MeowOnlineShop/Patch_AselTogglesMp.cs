using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Monolyn Race ships several player-facing boolean toggles that gizmo
    /// lambdas write directly into saved fields. They are registered as
    /// SyncFields; the grav-cannon mode switch additionally calls
    /// MakeGunR/MakeGunA, which are registered as SyncMethods so the gun
    /// replacement is also replayed.
    /// </summary>
    internal static class Patch_AselTogglesMp
    {
        private const string PackageId = "asel.monolynrace";

        private static readonly string[] SyncFieldTargets =
        {
            "ASEL.AstralBeacon:MeteoriteGuidance",
            "ASEL.Building_TowerOfLight:DeliveranceProtocol",
            "ASEL.CompAbilityEffect_DistributorBeam:autoUse",
            "ASEL.CompForgeArray:disable",
            "ASEL.Comp_MNC:autoCharge",
            "ASEL.HediffComp_AutoUseThermalSlash:autoUse",
            "ASEL.Extractor:unloadingEnabled",
            "ASEL.Building_GravityPillar:deceleration",
            "ASEL.Building_MNGravCannon:Attractor"
        };

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
                    Log.Message("[MP-MeowOnlineShop] Asel toggles sync skipped (target mod not active).");
                    return;
                }

                int registered = 0;
                foreach (string target in SyncFieldTargets)
                {
                    int colon = target.IndexOf(':');
                    if (colon <= 0)
                        continue;

                    string typeName = target.Substring(0, colon);
                    string fieldName = target.Substring(colon + 1);
                    registered += TryRegisterField(typeName, fieldName);
                }

                registered += TryRegisterMethod("ASEL.Building_MNGravCannon", "MakeGunR", Type.EmptyTypes);
                registered += TryRegisterMethod("ASEL.Building_MNGravCannon", "MakeGunA", Type.EmptyTypes);
                registered += TryRegisterMethod("ASEL.CompRefund", "Refund", Type.EmptyTypes);
                registered += TryRegisterMethod(
                    "ASEL.CompRefund",
                    "RefundAndTransfer",
                    new[] { typeof(Thing) });
                Type lightReceiverType = AccessTools.TypeByName("ASEL.CompLightReceiver");
                if (lightReceiverType != null)
                {
                    registered += TryRegisterMethod(
                        "ASEL.CompLightDistributor",
                        "RegisterReceiver",
                        new[] { lightReceiverType });
                    registered += TryRegisterMethod(
                        "ASEL.CompLightDistributor",
                        "UnregisterReceiver",
                        new[] { lightReceiverType });
                }
                registered += TryRegisterMethod("ASEL.CompBandNodeMN", "TuneTo", new[] { typeof(Pawn) });
                registered += TryRegisterMethod("ASEL.CompFormatorOverseer", "TuneTo", new[] { typeof(Pawn) });
                registered += TryRegisterMethod("ASEL.Building_Crucible", "EmergencyHeatVenting", Type.EmptyTypes);
                registered += TryRegisterMethod("ASEL.CompComputationMonitor", "MeteorGuidance", Type.EmptyTypes);
                registered += TryRegisterMethod("ASEL.CompLightBuilder", "Cancel", Type.EmptyTypes);

                if (registered == 0)
                    Log.Warning("[MP-MeowOnlineShop] Asel toggles target resolution failed; patch skipped.");
                else
                    Log.Message("[MP-MeowOnlineShop] Asel toggles MP patch active: " + registered + " sync registrations.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Asel toggles MP compat restore failed: " + e.Message);
            }
        }

        private static int TryRegisterField(string typeName, string fieldName)
        {
            Type type = AccessTools.TypeByName(typeName);
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            if (field == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Asel toggle field not resolved: " + typeName + "." + fieldName);
                return 0;
            }

            try
            {
                MP.RegisterSyncField(field);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Asel toggle sync field failed on " + fieldName + ": " + e.Message);
                return 0;
            }
        }

        private static int TryRegisterMethod(string typeName, string methodName, Type[] argTypes)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName, argTypes);
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Asel toggle sync method failed on " + methodName + ": " + e.Message);
                return 0;
            }
        }
    }
}
