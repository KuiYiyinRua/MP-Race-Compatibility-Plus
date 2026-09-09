using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// QW Archotech Implants Expanded (`qw.archotechimplantsexpanded`) adds a
    /// scar-healing toggle to Hediff_SuperRegeneration. The toggle directly
    /// writes the saved `scarHealingEnabled` field, which changes which
    /// injuries are healed on the deterministic regeneration tick. Registering
    /// ToggleScarHealing synchronizes only the gizmo action.
    /// </summary>
    internal static class Patch_QwArchotechImplantsMp
    {
        private const string PackageId = "qw.archotechimplantsexpanded";
        private const string HediffTypeName = "QW.ArchotechImplantsExpanded.com.Hediff_SuperRegeneration";
        private const string MethodName = "ToggleScarHealing";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type hediffType = AccessTools.TypeByName(HediffTypeName);
            MethodInfo method = hediffType == null ? null : AccessTools.Method(hediffType, MethodName, Type.EmptyTypes);
            if (hediffType == null || method == null)
            {
                Log.Warning("[MP-MeowOnlineShop] QW Archotech target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(method, null).SetContext(SyncContext.CurrentMap);
                Log.Message("[MP-MeowOnlineShop] QW Archotech Implants MP patch active: scar-healing toggle syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] QW Archotech Implants sync registration failed: " + e.Message);
            }
        }
    }
}
