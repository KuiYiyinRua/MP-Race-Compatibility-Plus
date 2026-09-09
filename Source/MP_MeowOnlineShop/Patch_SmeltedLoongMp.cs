using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Smelted Loong (`ny.smeltedloong`) adds two ability-mode toggles whose
    /// saved fields change the outcome of later ability Apply calls. Both are
    /// only written by gizmo callbacks on the clicking peer, so registering
    /// the fields as SyncFields is the narrowest boundary.
    /// </summary>
    internal static class Patch_SmeltedLoongMp
    {
        private const string PackageId = "ny.smeltedloong";
        private const string GiveHediffTypeName = "SmeltedLoong.AC_GiveHediffDual";
        private const string GeneEditingTypeName = "SmeltedLoong.AC_GeneEditing";
        private static readonly string[] BlackBirdPermitTypeNames =
        {
            "SmeltedLoong.BlackBirdPermitWorker_Offensive",
            "SmeltedLoong.BlackBirdPermitWorker_Defensive",
            "SmeltedLoong.BlackBirdPermitWorker_Reinforce"
        };

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            int registered = 0;
            registered += RegisterField(AccessTools.TypeByName(GiveHediffTypeName), "changeHediff");
            registered += RegisterField(AccessTools.TypeByName(GeneEditingTypeName), "changeMode");
            foreach (string typeName in BlackBirdPermitTypeNames)
            {
                registered += RegisterMethod(
                    AccessTools.TypeByName(typeName),
                    "OrderForceTarget",
                    new[] { typeof(LocalTargetInfo) });
            }

            if (registered == 0)
                Log.Warning("[MP-MeowOnlineShop] Smelted Loong target resolution failed; patch skipped.");
            else
                Log.Message("[MP-MeowOnlineShop] Smelted Loong MP patch active: " + registered + " sync fields.");
        }

        private static int RegisterField(Type type, string fieldName)
        {
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            if (field == null)
                return 0;

            try
            {
                MP.RegisterSyncField(field);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Smelted Loong sync field failed on " + fieldName + ": " + e.Message);
                return 0;
            }
        }

        private static int RegisterMethod(Type type, string methodName, Type[] argTypes)
        {
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
                Log.Warning("[MP-MeowOnlineShop] Smelted Loong sync method failed on " + methodName + ": " + e.Message);
                return 0;
            }
        }
    }
}
