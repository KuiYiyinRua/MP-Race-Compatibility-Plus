using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Ling's Item Cuter settings window writes saved Building_ItemCuter
    /// fields directly. Those fields control deterministic TickRare cutting,
    /// so every interface write is registered as a SyncField. Pawn Cuter's
    /// body-part/hediff selection lists and work mode are also saved and are
    /// registered here; BodyPartRecord and Hediff have Multiplayer sync
    /// workers.
    /// </summary>
    internal static class Patch_LingCuterMp
    {
        private const string PackageId = "LingLuo.ItemCuter";
        private const string PawnCuterPackageId = "LingLuo.PawnCuter";
        private const string BuildingTypeName = "LingItemCuter.Building_ItemCuter";
        private const string BodyCuterTypeName = "LingBodyCuter.Building_BodyCuter";

        private static readonly string[] SyncFieldNames =
        {
            "CutCorpse",
            "DestoryApparel",
            "CutOrgan",
            "CutItem",
            "CutBuilding",
            "ForbidOutPut",
            "Efficiency",
            "WorkTime"
        };

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            int registered = 0;
            if (ModsConfig.IsActive(PackageId))
                registered += ApplyItemCuter();
            if (ModsConfig.IsActive(PawnCuterPackageId))
                registered += ApplyPawnCuter();

            if (registered == 0)
                Log.Warning("[MP-MeowOnlineShop] Ling Cuter target resolution failed; patch skipped.");
            else
                Log.Message("[MP-MeowOnlineShop] Ling Cuter MP patch active: " + registered + " sync fields.");
        }

        private static int ApplyItemCuter()
        {
            Type buildingType = AccessTools.TypeByName(BuildingTypeName);
            if (buildingType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ling Item Cuter target type not resolved; patch skipped.");
                return 0;
            }

            int registered = 0;
            foreach (string fieldName in SyncFieldNames)
            {
                FieldInfo field = AccessTools.Field(buildingType, fieldName);
                if (field == null)
                    continue;

                try
                {
                    MP.RegisterSyncField(field);
                    registered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ling Item Cuter sync field failed on " + fieldName + ": " + e.Message);
                }
            }

            return registered;
        }

        private static int ApplyPawnCuter()
        {
            Type buildingType = AccessTools.TypeByName(BodyCuterTypeName);
            if (buildingType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ling Pawn Cuter target type not resolved; patch skipped.");
                return 0;
            }

            int registered = 0;
            foreach (string fieldName in new[]
                     {
                         "pairsAs",
                         "modeCBodyparts",
                         "AdvHediffs",
                         "cutAdvHediffs",
                         "workMode"
                     })
            {
                FieldInfo field = AccessTools.Field(buildingType, fieldName);
                if (field == null)
                    continue;

                try
                {
                    MP.RegisterSyncField(field);
                    registered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ling Pawn Cuter sync field failed on " + fieldName + ": " + e.Message);
                }
            }

            return registered;
        }
    }
}
