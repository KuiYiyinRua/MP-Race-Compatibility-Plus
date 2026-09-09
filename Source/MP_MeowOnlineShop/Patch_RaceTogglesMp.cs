using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Several race mods expose saved boolean toggles through gizmo lambdas
    /// that write the fields directly. These fields steer deterministic Tick
    /// behavior, so each is registered as a SyncField; interface writes are
    /// broadcast while simulation writes stay local.
    /// </summary>
    internal static class Patch_RaceTogglesMp
    {
        private const string WolfeinPackageId = "melondove.wolfeinrace";
        private const string OberoniaYhPackageId = "oark.ratkinfaction.oberoniaaurea";
        private const string SmeltedLoongPackageId = "ny.smeltedloong";

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            int registered = 0;
            // Wolfein's complete callbacks are synchronized by Patch_WolfeinToolsMp.

            if (ModsConfig.IsActive(OberoniaYhPackageId))
            {
                registered += TryRegisterField("OberoniaAurea.GravDataBeacon", "isActive");
                registered += TryRegisterField("OberoniaAurea.CompCircuitRegulator", "repairmentEnabled");
            }

            if (ModsConfig.IsActive(SmeltedLoongPackageId))
                registered += TryRegisterField("SmeltedLoong.HC_TurretGun", "fireAtWill");

            if (registered == 0)
                Log.Message("[MP-MeowOnlineShop] Race toggles sync skipped (no target fields resolved).");
            else
                Log.Message("[MP-MeowOnlineShop] Race toggles MP patch active: " + registered + " sync fields.");
        }

        private static int TryRegisterField(string typeName, string fieldName)
        {
            Type type = AccessTools.TypeByName(typeName);
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            if (field == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Race toggle field not resolved: " + typeName + "." + fieldName);
                return 0;
            }

            try
            {
                MP.RegisterSyncField(field);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Race toggle sync field failed on " + fieldName + ": " + e.Message);
                return 0;
            }
        }
    }
}
