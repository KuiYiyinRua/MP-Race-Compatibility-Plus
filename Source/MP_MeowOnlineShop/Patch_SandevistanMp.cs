using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Sandevistan (`jamlik.sandevistan`) exposes a gizmo on the controller
    /// hediff. ToggleActive flips the saved `active`/`activeSecondsRemaining`
    /// fields, adds/removes the active and overheat hediffs, drains needs, and
    /// registers a global slow-time effect. The mod already switches to
    /// deterministic tick math when Multiplayer is active, but the gizmo
    /// callback still runs on the clicking peer only. Registering the private
    /// ToggleActive method is the narrowest sync boundary; automatic activation
    /// and downed-pawn deactivation inside CompPostTick stay deterministic.
    /// </summary>
    internal static class Patch_SandevistanMp
    {
        private const string PackageId = "jamlik.sandevistan";
        private const string CompTypeName = "Sandevistan.HediffComp_SandevistanController";
        private const string MethodName = "ToggleActive";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type compType = AccessTools.TypeByName(CompTypeName);
            MethodInfo method = compType == null ? null : AccessTools.Method(compType, MethodName, Type.EmptyTypes);
            if (compType == null || method == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Sandevistan target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(method, null).SetContext(SyncContext.CurrentMap);
                Log.Message("[MP-MeowOnlineShop] Sandevistan MP patch active: controller toggle syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Sandevistan sync registration failed: " + e.Message);
            }
        }
    }
}
