using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Oberonia Snowstorm (`oark.ratkinfaction.scenarioexpand.snowstorm`)
    /// adds Building_IceCrystalCollector with a saved unloading toggle and an
    /// eject gizmo. The toggle writes `unloadingEnabled` only from UI and is
    /// registered as SyncField; EjectContents is the stable executor for
    /// converting stored crystals into a map thing and is registered as
    /// SyncMethod.
    /// </summary>
    internal static class Patch_OberoniaSnowstormMp
    {
        private const string PackageId = "oark.ratkinfaction.scenarioexpand.snowstorm";
        private const string CollectorTypeName = "OberoniaAureaGene.Snowstorm.Building_IceCrystalCollector";
        private const string UnloadingFieldName = "unloadingEnabled";
        private const string EjectMethodName = "EjectContents";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type collectorType = AccessTools.TypeByName(CollectorTypeName);
            if (collectorType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Oberonia Snowstorm collector type not resolved; patch skipped.");
                return;
            }

            int registered = 0;
            FieldInfo unloadingField = AccessTools.Field(collectorType, UnloadingFieldName);
            if (unloadingField != null)
            {
                try
                {
                    MP.RegisterSyncField(unloadingField);
                    registered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Oberonia Snowstorm unloading sync field failed: " + e.Message);
                }
            }

            MethodInfo eject = AccessTools.Method(collectorType, EjectMethodName, Type.EmptyTypes);
            if (eject != null)
            {
                try
                {
                    MP.RegisterSyncMethod(eject, null).SetContext(SyncContext.CurrentMap);
                    registered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Oberonia Snowstorm eject sync failed: " + e.Message);
                }
            }

            if (registered == 0)
                Log.Warning("[MP-MeowOnlineShop] Oberonia Snowstorm target resolution incomplete; patch skipped.");
            else
                Log.Message("[MP-MeowOnlineShop] Oberonia Snowstorm MP patch active: " + registered + " sync registrations.");
        }
    }
}
