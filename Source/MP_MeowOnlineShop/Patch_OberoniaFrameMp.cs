using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Oberonia Aurea Frame's category trade request and sale request
    /// confirmation callbacks call private Fulfill(Caravan) methods that
    /// mutate caravan inventory, quest signals, and saved request state.
    /// Registering those stable executors as sync methods keeps the outcome
    /// on every peer.
    /// </summary>
    internal static class Patch_OberoniaFrameMp
    {
        private const string PackageId = "oark.oberoniaaurea.framework";

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
                    Log.Message("[MP-MeowOnlineShop] Oberonia Frame sync skipped (target mod not active).");
                    return;
                }

                int registered = 0;
                registered += TryRegister(
                    "OberoniaAurea_Frame.CategoryTradeRequestComp",
                    "Fulfill",
                    new[] { typeof(Caravan) });
                registered += TryRegister(
                    "OberoniaAurea_Frame.SaleRequestComp",
                    "Fulfill",
                    new[] { typeof(Caravan) });
                Type fixedCaravanType = AccessTools.TypeByName("OberoniaAurea_Frame.FixedCaravan");
                if (fixedCaravanType != null)
                {
                    registered += TryRegister(
                        "OberoniaAurea_Frame.FixedCaravan",
                        "PreConvertToCaravanByPlayer",
                        Type.EmptyTypes);
                    registered += TryRegister(
                        "OberoniaAurea_Frame.OAFrame_FixedCaravanUtility",
                        "ConvertToCaravan",
                        new[] { fixedCaravanType });
                }

                if (registered == 0)
                    Log.Warning("[MP-MeowOnlineShop] Oberonia Frame target resolution failed; patch skipped.");
                else
                    Log.Message("[MP-MeowOnlineShop] Oberonia Frame MP patch active: " + registered + " sync methods.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Oberonia Frame MP compat restore failed: " + e.Message);
            }
        }

        private static int TryRegister(string typeName, string methodName, Type[] argTypes)
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
                Log.Warning("[MP-MeowOnlineShop] Oberonia Frame sync register failed on " + methodName + ": " + e.Message);
                return 0;
            }
        }
    }
}
