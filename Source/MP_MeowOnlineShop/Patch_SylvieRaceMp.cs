using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Sylvie Race (`aifeng.sylvierace`) has a worn-apparel nurse-heal gizmo.
    /// TryUseAbility tends injuries, adds a paralysis hediff, and writes the
    /// saved cooldown tick directly on the clicking peer. It is registered as
    /// the stable sync boundary.
    /// </summary>
    internal static class Patch_SylvieRaceMp
    {
        private const string PackageId = "aifeng.sylvierace";
        private const string CompTypeName = "SylvieMod.SylvieRace_CompNurseHeal";
        private const string MethodName = "TryUseAbility";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type compType = AccessTools.TypeByName(CompTypeName);
            MethodInfo method = compType == null ? null : AccessTools.Method(compType, MethodName, Type.EmptyTypes);
            if (compType == null || method == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Sylvie Race target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(method, null).SetContext(SyncContext.CurrentMap);
                Log.Message("[MP-MeowOnlineShop] Sylvie Race MP patch active: nurse heal syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Sylvie Race sync registration failed: " + e.Message);
            }
        }
    }
}
