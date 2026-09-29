using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    // The console is a local Window. Only its selected transactions may enter the MP command queue.
    internal static class ImperiumConsoleActions
    {
        private static MethodInfo getMap, getPawn, getFaction;
        private static MethodInfo exchange, recruit, proceed, notifyFailure;
        private static MethodInfo[] supportActions;
        private static PropertyInfo gameInstance, support;
        private static Type exchangeKind;

        internal static void Apply(Harmony harmony)
        {
            var dialog = Bootstrap.Type("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window");
            var service = Bootstrap.Type("MiliraImperium.MiliraImperiumTransactionService");
            var manager = Bootstrap.Type("MiliraImperium.MiliraImperiumSupportManager");
            var game = Bootstrap.Type("MiliraImperium.MiliraImperium_GameComponent");
            exchangeKind = Bootstrap.Type(service.FullName + "+ExchangeKind");
            getMap = Bootstrap.Method(dialog, "GetReferenceMap");
            getPawn = Bootstrap.Method(dialog, "GetNegotiator");
            getFaction = Bootstrap.Method(dialog, "GetMiliraFaction");
            exchange = Bootstrap.Method(service, "TryExchange", typeof(Map), typeof(Pawn), typeof(Faction), exchangeKind);
            recruit = Bootstrap.Method(service, "TryRecruit", typeof(Map), typeof(Pawn), typeof(Faction));
            proceed = Bootstrap.Method(service, "TryProceedToImperium", typeof(Map), typeof(Faction));
            notifyFailure = Bootstrap.Method(service, "NotifyFailure", exchange.ReturnType);
            gameInstance = AccessTools.Property(game, "Instance") ?? throw new MissingMemberException(game.FullName, "Instance");
            support = AccessTools.Property(game, "Support") ?? throw new MissingMemberException(game.FullName, "Support");
            if (support.PropertyType != manager) throw new MissingMemberException(game.FullName, "Support type");

            supportActions = new[]
            {
                Bootstrap.Method(manager, "TryUpgradeNetworkLevel"),
                Bootstrap.Method(manager, "TryStartImperiumGiftEvent", typeof(Map)),
                Bootstrap.Method(manager, "CallRawOrbitalTrader", typeof(Map)),
                Bootstrap.Method(manager, "CallFoodOrbitalTrader", typeof(Map)),
                Bootstrap.Method(manager, "CallTechOrbitalTrader", typeof(Map)),
                Bootstrap.Method(manager, "CallTributeCollectorOrbitalTrader", typeof(Map))
            };

            MP.RegisterSyncMethod(typeof(ImperiumConsoleActions), nameof(ExecuteTransaction));
            MP.RegisterSyncMethod(typeof(ImperiumConsoleActions), nameof(ExecuteSupportAction));
            harmony.Patch(Bootstrap.Method(dialog, "RunExchange", exchangeKind),
                prefix: new HarmonyMethod(typeof(ImperiumConsoleActions), nameof(QueueExchange)));
            harmony.Patch(Bootstrap.Method(dialog, "RunRecruitment"),
                prefix: new HarmonyMethod(typeof(ImperiumConsoleActions), nameof(QueueRecruitment)));
            harmony.Patch(Bootstrap.Method(dialog, "RunProceedToImperium"),
                prefix: new HarmonyMethod(typeof(ImperiumConsoleActions), nameof(QueueProceed)));
            harmony.Patch(supportActions[0],
                prefix: new HarmonyMethod(typeof(ImperiumConsoleActions), nameof(QueueNetworkUpgrade)));
            for (var i = 1; i < supportActions.Length; i++)
                harmony.Patch(supportActions[i], prefix: new HarmonyMethod(typeof(ImperiumConsoleActions), nameof(QueueSupport)));
        }

        private static bool QueueExchange(object __instance, object __0)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            ExecuteTransaction((Map)getMap.Invoke(__instance, null), (Pawn)getPawn.Invoke(__instance, null),
                (Faction)getFaction.Invoke(null, null), 0, Convert.ToInt32(__0));
            return false;
        }

        private static bool QueueRecruitment(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            ExecuteTransaction((Map)getMap.Invoke(__instance, null), (Pawn)getPawn.Invoke(__instance, null),
                (Faction)getFaction.Invoke(null, null), 1, 0);
            return false;
        }

        private static bool QueueProceed(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            ExecuteTransaction((Map)getMap.Invoke(__instance, null), null,
                (Faction)getFaction.Invoke(null, null), 2, 0);
            return false;
        }

        public static void ExecuteTransaction(Map map, Pawn pawn, Faction faction, int action, int kind)
        {
            if (map == null || !Find.Maps.Contains(map) || faction == null) return;
            object result;
            switch (action)
            {
                case 0:
                    if (pawn == null || pawn.Destroyed || !Enum.IsDefined(exchangeKind, kind)) return;
                    result = exchange.Invoke(null, new object[] { map, pawn, faction, Enum.ToObject(exchangeKind, kind) });
                    break;
                case 1:
                    if (pawn == null || pawn.Destroyed) return;
                    result = recruit.Invoke(null, new object[] { map, pawn, faction });
                    break;
                case 2:
                    result = proceed.Invoke(null, new object[] { map, faction });
                    break;
                default:
                    return;
            }
            notifyFailure.Invoke(null, new[] { result });
        }

        private static bool QueueNetworkUpgrade()
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            ExecuteSupportAction(0, null);
            return false;
        }

        private static bool QueueSupport(Map map, MethodBase __originalMethod, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            var index = Array.IndexOf((Array)supportActions, __originalMethod);
            if (index < 0) return true;
            ExecuteSupportAction(index, map);
            __result = false;
            return false;
        }

        public static void ExecuteSupportAction(int action, Map map)
        {
            if (action < 0 || action >= supportActions.Length) return;
            if (action != 0 && (map == null || !Find.Maps.Contains(map))) return;
            var component = gameInstance.GetValue(null, null);
            var manager = component == null ? null : support.GetValue(component, null);
            if (manager == null) return;
            supportActions[action].Invoke(manager, action == 0 ? null : new object[] { map });
        }

    }
}
