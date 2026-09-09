using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Monolyn Race (`asel.monolynrace`) ships several player-facing
    /// buildings. The clear simulation boundaries are:
    ///
    /// - MonolynConsumer.work: the activation toggle is a saved bool that
    ///   gates deterministic light consumption; register as SyncField.
    /// - PhoneBooth.CreateCorpseStockpile: the gizmo creates and registers a
    ///   stockpile zone around the booth; register as SyncMethod.
    /// - TowerOfLightBroken.OrderForceTarget: the custom ITargetingSource
    ///   callback starts an ordered job and sets the proximity-letter flag;
    ///   register as SyncMethod so both happen on every peer.
    /// </summary>
    internal static class Patch_AselMonolynMp
    {
        private const string PackageId = "asel.monolynrace";

        private const string ConsumerTypeName = "ASEL.MonolynConsumer";
        private const string WorkFieldName = "work";
        private const string PhoneBoothTypeName = "ASEL.PhoneBooth";
        private const string CreateCorpseStockpileMethodName = "CreateCorpseStockpile";
        private const string TowerTypeName = "ASEL.TowerOfLightBroken";
        private const string OrderForceTargetMethodName = "OrderForceTarget";
        private const string ProducerTypeName = "ASEL.Building_MonolynProducer";
        private const string SelectedOptionFieldName = "selectedOption";
        private const string LightConduitTypeName = "ASEL.CompLightConduit";
        private const string ConnectedConduitFieldName = "connectedConduit";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            int registered = 0;
            Type consumerType = AccessTools.TypeByName(ConsumerTypeName);
            FieldInfo workField = consumerType == null ? null : AccessTools.Field(consumerType, WorkFieldName);
            if (workField != null)
            {
                try
                {
                    MP.RegisterSyncField(workField);
                    registered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Monolyn work sync field registration failed: " + e.Message);
                }
            }

            Type phoneBoothType = AccessTools.TypeByName(PhoneBoothTypeName);
            MethodInfo createStockpile = phoneBoothType == null
                ? null
                : AccessTools.Method(phoneBoothType, CreateCorpseStockpileMethodName, Type.EmptyTypes);
            registered += TryRegisterMethod(createStockpile);

            Type towerType = AccessTools.TypeByName(TowerTypeName);
            MethodInfo orderForceTarget = towerType == null
                ? null
                : AccessTools.Method(towerType, OrderForceTargetMethodName, new[] { typeof(LocalTargetInfo) });
            registered += TryRegisterMethod(orderForceTarget);

            Type producerType = AccessTools.TypeByName(ProducerTypeName);
            FieldInfo selectedOption = producerType == null
                ? null
                : AccessTools.Field(producerType, SelectedOptionFieldName);
            if (selectedOption != null)
            {
                try
                {
                    MP.RegisterSyncField(selectedOption);
                    registered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Monolyn selected-option sync field failed: " + e.Message);
                }
            }

            if (producerType != null)
            {
                Type recipeType = AccessTools.TypeByName("ASEL.MonolynRecipeDef");
                registered += TryRegisterMethod(
                    recipeType == null
                        ? null
                        : AccessTools.Method(producerType, "StartWork", new[] { recipeType, typeof(int) }));
                registered += TryRegisterMethod(
                    AccessTools.Method(producerType, "Cancel", Type.EmptyTypes));
            }

            Type conduitType = AccessTools.TypeByName(LightConduitTypeName);
            FieldInfo connectedConduit = conduitType == null
                ? null
                : AccessTools.Field(conduitType, ConnectedConduitFieldName);
            if (connectedConduit != null)
            {
                try
                {
                    MP.RegisterSyncField(connectedConduit);
                    registered++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Monolyn conduit sync field failed: " + e.Message);
                }
            }

            if (registered == 0)
                Log.Warning("[MP-MeowOnlineShop] Monolyn target resolution failed; patch skipped.");
            else
                Log.Message("[MP-MeowOnlineShop] Monolyn MP patch active: " + registered + " sync registrations.");
        }

        private static int TryRegisterMethod(MethodInfo method)
        {
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null).SetContext(SyncContext.CurrentMap);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Monolyn sync registration failed on " +
                    method.Name + ": " + e.Message);
                return 0;
            }
        }
    }
}
