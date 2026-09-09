using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Ratkin Knights+ (`rkk.ratknights.core`) has two player-driven
    /// simulation paths that bypass ordered jobs:
    ///
    /// - CompMoonlightDash.DashAct teleports the wearer, starts a cooldown,
    ///   and queues delayed dash damage from the weapon gizmo targeter.
    /// - RKK_Building_Atlar exposes named executors for calling knights to a
    ///   defend point, starting trails, recalling knights, and assigning
    ///   traits. The remaining atlas float-menu/gizmo actions are mostly
    ///   state toggles inside the same building and are left for a focused
    ///   follow-up if runtime evidence shows divergence.
    ///
    /// Registering these methods is safe: Multiplayer only broadcasts when
    /// ShouldSync is true, so deterministic TickRare calls stay local.
    /// </summary>
    internal static class Patch_RatkinKnightsMp
    {
        private const string PackageId = "rkk.ratknights.core";
        private const string DashCompTypeName = "RatkinKnights.CompMoonlightDash";
        private const string AtlarTypeName = "RatkinKnights.RKK_Building_Atlar";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            int registered = 0;
            Type dashType = AccessTools.TypeByName(DashCompTypeName);
            MethodInfo dashAct = dashType == null
                ? null
                : AccessTools.Method(dashType, "DashAct", new[] { typeof(Pawn), typeof(LocalTargetInfo) });
            registered += TryRegister(dashAct, SyncContext.CurrentMap);

            Type atlarType = AccessTools.TypeByName(AtlarTypeName);
            if (atlarType != null)
            {
                registered += TryRegister(
                    AccessTools.Method(atlarType, "CallKnightsToDefend", new[] { typeof(LocalTargetInfo) }),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "BeginTrail_First", Type.EmptyTypes),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "BeginTrail_Second", Type.EmptyTypes),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "BeginTrail_Third", Type.EmptyTypes),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "BeginTrail_Fourth", Type.EmptyTypes),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "BeginTrail_Final", Type.EmptyTypes),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "BeginTrail_Plus", Type.EmptyTypes),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "Callback", new[] { typeof(Pawn) }),
                    SyncContext.CurrentMap);
                registered += TryRegister(
                    AccessTools.Method(atlarType, "CheckAndGiveTraits", new[] { typeof(Pawn) }),
                    SyncContext.CurrentMap);
            }

            if (registered == 0)
                Log.Warning("[MP-MeowOnlineShop] Ratkin Knights target resolution failed; patch skipped.");
            else
                Log.Message("[MP-MeowOnlineShop] Ratkin Knights MP patch active: " + registered + " sync methods.");
        }

        private static int TryRegister(MethodInfo method, SyncContext context)
        {
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null).SetContext(context);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ratkin Knights sync registration failed on " +
                    method.Name + ": " + e.Message);
                return 0;
            }
        }
    }
}
