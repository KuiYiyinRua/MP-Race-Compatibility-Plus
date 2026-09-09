using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Fulton Extraction (`roltonsmods.fultonextraction`) adds a self-extract
    /// gizmo to crates. The gizmo callback calls CompExtractSelf.
    /// StartSelfExtraction, which despawns the crate, moves stacked items into
    /// a map component, and starts the balloon animation. Only the clicking
    /// peer runs that callback, so the method is registered as the stable sync
    /// boundary. The worn-harness Command_Target path already ends in
    /// TryTakeOrderedJob and is covered by Multiplayer.
    /// </summary>
    internal static class Patch_FultonExtractionMp
    {
        private const string PackageId = "roltonsmods.fultonextraction";
        private const string CompTypeName = "FultonExtraction.CompExtractSelf";
        private const string MethodName = "StartSelfExtraction";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type compType = AccessTools.TypeByName(CompTypeName);
            MethodInfo method = compType == null ? null : AccessTools.Method(compType, MethodName, Type.EmptyTypes);
            if (compType == null || method == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Fulton Extraction target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(method, null).SetContext(SyncContext.CurrentMap);
                Log.Message("[MP-MeowOnlineShop] Fulton Extraction MP patch active: self-extract syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Fulton Extraction sync registration failed: " + e.Message);
            }
        }
    }
}
