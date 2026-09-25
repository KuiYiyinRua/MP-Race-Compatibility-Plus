using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using MpClient = Multiplayer.Client.Multiplayer;

namespace Meow.FactionStoryIsolation
{
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        internal const string HarmonyId = "meow.factionstory.isolation";
        internal static bool Ready;

        static Bootstrap()
        {
            if (!MP.enabled) return;
            var harmony = new Harmony(HarmonyId);
            try
            {
                var mod = AccessTools.TypeByName("MP_MeowOnlineShop.MpMeowOnlineShopMod");
                IsolationSession.SettingsProperty = AccessTools.Property(mod, "Settings")
                    ?? throw new MissingMemberException("MpMeowOnlineShopMod.Settings");
                IsolationSession.PreferenceField = AccessTools.Field(IsolationSession.SettingsProperty.PropertyType, "enableFactionStoryRoutingIsolation");
                if (IsolationSession.PreferenceField?.FieldType != typeof(bool))
                    throw new MissingFieldException("enableFactionStoryRoutingIsolation: bool");

                // Resolve every required target before installing any hook.
                var targets = AccessTools.PropertyGetter(typeof(Storyteller), "AllIncidentTargets")
                    ?? throw new MissingMethodException("Storyteller.AllIncidentTargets");
                var acceptable = Require(typeof(QuestNode_GetMap), "IsAcceptableMap", typeof(bool), typeof(Map), typeof(Slate));
                var run = Require(typeof(QuestNode_GetMap), "RunInt", typeof(void));
                RoutingPatches.TryFindMap = Require(typeof(QuestNode_GetMap), "TryFindMap", typeof(bool), typeof(Slate), typeof(Map).MakeByRefType());
                var serverSettings = AccessTools.TypeByName("Multiplayer.Common.ServerSettings")
                    ?? throw new TypeLoadException("Multiplayer.Common.ServerSettings");
                var host = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.HostUtil"), "HostServer", new[] { serverSettings, typeof(bool) })
                    ?? throw new MissingMethodException("HostUtil.HostServer(ServerSettings, bool)");
                // Verify the installed MP entry point and filter after its owner.
                var mpTargets = Require(AccessTools.TypeByName("Multiplayer.Client.AsyncTime.StorytellerTargetsPatch"), "Postfix", typeof(void), typeof(List<IIncidentTarget>));
                if (targets.ReturnType != typeof(List<IIncidentTarget>)) throw new MissingMethodException("AllIncidentTargets return type changed");
                var createQuest = Require(typeof(Quest), "MakeRaw", typeof(Quest));
                var exposeQuest = Require(typeof(Quest), "ExposeData", typeof(void));
                var acceptQuest = Require(typeof(Quest), "Accept", typeof(void), typeof(Pawn));
                var autoAccept = Require(typeof(Quest), "SetInitiallyAccepted", typeof(void));
                var asyncComp = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp")
                    ?? throw new TypeLoadException("Multiplayer.Client.AsyncTimeComp");
                var acceptContext = Require(AccessTools.TypeByName("Multiplayer.Client.Comp.SetContextForAccept"),
                    "Prefix", typeof(void), typeof(Quest), asyncComp.MakeByRefType());
                var hideOtherQuests = Require(AccessTools.TypeByName("Multiplayer.Client.Patches.MainTabWindow_QuestsShouldListNowPatch"),
                    "Prefix", typeof(bool), typeof(Quest), typeof(bool).MakeByRefType());
                harmony.Patch(host, prefix: new HarmonyMethod(typeof(IsolationSession), nameof(IsolationSession.CaptureHostPreferences)));
                harmony.Patch(targets, postfix: new HarmonyMethod(typeof(RoutingPatches), nameof(RoutingPatches.FilterTargets))
                    { after = new[] { "multiplayer" }, priority = Priority.Last });
                harmony.Patch(acceptable, prefix: new HarmonyMethod(typeof(RoutingPatches), nameof(RoutingPatches.CheckMap)));
                harmony.Patch(run, prefix: new HarmonyMethod(typeof(RoutingPatches), nameof(RoutingPatches.PrepareMap)));
                harmony.Patch(createQuest, postfix: new HarmonyMethod(typeof(QuestAcceptanceGuard), nameof(QuestAcceptanceGuard.Created)));
                harmony.Patch(exposeQuest, postfix: new HarmonyMethod(typeof(QuestAcceptanceGuard), nameof(QuestAcceptanceGuard.Expose)));
                harmony.Patch(acceptQuest, prefix: new HarmonyMethod(typeof(QuestAcceptanceGuard), nameof(QuestAcceptanceGuard.Accept))
                    { before = new[] { "multiplayer" }, priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(QuestAcceptanceGuard), nameof(QuestAcceptanceGuard.AcceptFinalizer)));
                harmony.Patch(autoAccept, prefix: new HarmonyMethod(typeof(QuestAcceptanceGuard), nameof(QuestAcceptanceGuard.AutoAccept))
                    { before = new[] { "multiplayer" }, priority = Priority.First });
                harmony.Patch(acceptContext, prefix: new HarmonyMethod(typeof(QuestAcceptanceGuard), nameof(QuestAcceptanceGuard.AcceptMapContext)));
                harmony.Patch(hideOtherQuests, prefix: new HarmonyMethod(typeof(QuestAcceptanceGuard), nameof(QuestAcceptanceGuard.KeepOtherFactionQuestsVisible)));
                Ready = true;
                Log.Message("[Meow.FactionStoryIsolation] Required targets resolved; routing and persisted quest acceptance ownership guards installed. Full quest lifecycle isolation is not implemented.");
            }
            catch (Exception error)
            {
                Ready = false;
                harmony.UnpatchAll(HarmonyId);
                Log.Error("[Meow.FactionStoryIsolation] REQUIRED TARGET FAILURE; stage 1 unavailable: " + error);
            }
        }

        private static MethodInfo Require(Type type, string name, Type result, params Type[] args)
        {
            var method = type == null ? null : AccessTools.DeclaredMethod(type, name, args);
            if (method == null || method.ReturnType != result) throw new MissingMethodException(type?.FullName, name);
            return method;
        }
    }

    internal static class RoutingPatches
    {
        internal static MethodInfo TryFindMap;
        private static bool Active => Bootstrap.Ready && MP.IsInMultiplayer
            && MpClient.GameComp != null && MpClient.GameComp.multifaction
            && (MpClient.Ticking || MpClient.ExecutingCmds)
            && Current.Game?.GetComponent<IsolationSession>()?.RoutingEnabled == true;

        private static bool Owned(Faction target)
        {
            // This is MP's simulation faction, never RealPlayerFaction/current view.
            var owner = Faction.OfPlayer;
            return RoutingPolicy.AllowsOwnedTarget(owner?.loadID ?? -1, owner?.def.isPlayer == true, target?.loadID ?? -1);
        }

        internal static void FilterTargets(List<IIncidentTarget> __result)
        {
            if (!Active || __result == null) return;
            __result.RemoveAll(target => target is Map map ? !Owned(map.Parent?.Faction)
                : target is Caravan caravan && !Owned(caravan.Faction));
            // World/unknown targets keep existing behavior in this partial repair.
            // They must not be advertised as explicitly public/strictly isolated.
        }

        internal static bool CheckMap(Map map, ref bool __result)
        {
            if (!Active || Owned(map?.Parent?.Faction)) return true;
            __result = false;
            return false; // Reject before vanilla infestation checks consume Rand.
        }

        internal static void PrepareMap(QuestNode_GetMap __instance)
        {
            if (!Active) return;
            var slate = QuestGen.slate;
            string key = __instance.storeAs.GetValue(slate);
            if (!slate.TryGet<Map>(key, out var existing) || Owned(existing?.Parent?.Faction)) return;
            // Vanilla RunInt retains an invalid old slate map if TryFindMap fails.
            // Select once using vanilla conditions and leave it for vanilla RunInt.
            object[] args = { slate, null };
            if (!(bool)TryFindMap.Invoke(__instance, args))
                throw new InvalidOperationException("[Meow.FactionStoryIsolation] Quest generation aborted: foreign receiving map and no legal map for faction " + Faction.OfPlayer.loadID);
            slate.Set(key, (Map)args[1]);
        }
    }
}
