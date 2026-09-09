using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_WolfeinAllegianceMp
    {
        private const string Ns = "WolfeinAllegiance.";
        private static ISyncMethod targetedPermit;
        private static ISyncMethod departure;
        private static readonly FieldInfo Caller = AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "caller");
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "map");
        private static readonly FieldInfo Free = AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "free");

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || !ModsConfig.IsActive("leopoko.wolfeinallegiance")) return;
            WolfeinSessionSettings.ApplyAllegiance(harmony);
            targetedPermit = MP.RegisterSyncMethod(typeof(Patch_WolfeinAllegianceMp), nameof(ExecuteTargetedPermit));
            harmony.Patch(Required(RequiredType("GameComponent_Allegiance"), "ScheduleAction", typeof(int), typeof(Action)),
                prefix: new HarmonyMethod(typeof(WolfeinScheduledActions), nameof(WolfeinScheduledActions.SchedulePrefix)));
            foreach (string name in new[] { "Artillery", "Artillery2", "CarpetBombing", "DangerClose",
                "EMPBombardment", "FoodDrop", "LaborHelp", "NightRaid", "RegularArmy", "WelfareProgram", "WolfeinShuttle" })
            {
                Type type = RequiredType("PermitWorker_" + name);
                if (FactionField(type) == null) throw new MissingFieldException(type.FullName, "callingFaction/calledFaction");
                MethodInfo order = Required(type, "OrderForceTarget", typeof(LocalTargetInfo));
                harmony.Patch(order, prefix: new HarmonyMethod(typeof(Patch_WolfeinAllegianceMp), nameof(TargetedPrefix)));
            }
            Register("PermitWorker_AssaultTeam", "SpawnAssaultTeam", typeof(Map), typeof(Pawn), typeof(Faction), typeof(bool));
            Register("PermitWorker_DroneSwarm", "SpawnDrones", typeof(Map), typeof(Pawn), typeof(Faction), typeof(bool));
            Register("PermitWorker_ShuttleHack", "ExecuteShuttleHack", typeof(Map), typeof(Pawn), typeof(Faction), typeof(bool));
            Register("PermitWorker_SpyIntel", "GenerateRandomQuest", typeof(Map), typeof(Pawn), typeof(Faction), typeof(bool));
            Register("WorldObjectComp_SupplyDelivery", "Fulfill", typeof(Caravan));
            Register("WorldObjectComp_PrepSupplyDelivery", "Fulfill", typeof(Caravan));
            Register("QuestPart_SideChoiceDialog", "DoChoose", typeof(string));
            departure = MP.RegisterSyncMethod(typeof(Patch_WolfeinAllegianceMp), nameof(Depart));
            harmony.Patch(Required(RequiredType("VictoryShuttleTracker"), "TriggerDeparture", RequiredType("VictoryShuttleTracker+VictoryShuttleInfo")),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinAllegianceMp), nameof(DeparturePrefix)));
            Log.Message("[MP-MeowOnlineShop] Wolfein Allegiance: 11 targeted permit paths and 7 action executors installed.");
        }

        private static Type RequiredType(string name)
        {
            return AccessTools.TypeByName(Ns + name) ?? throw new TypeLoadException(Ns + name);
        }

        private static bool DeparturePrefix(object __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !MP.InInterface) return true;
            departure.DoSync(null, AccessTools.Field(__0.GetType(), "shuttle").GetValue(__0));
            return false;
        }

        public static void Depart(Thing shuttle)
        {
            if (shuttle == null || shuttle.Destroyed) return;
            Type tracker = RequiredType("VictoryShuttleTracker");
            object[] args = { shuttle, null };
            if ((bool)AccessTools.Method(tracker, "TryGetInfo").Invoke(null, args))
                AccessTools.Method(tracker, "TriggerDeparture").Invoke(null, new[] { args[1] });
        }

        private static MethodInfo Required(Type type, string name, params Type[] args)
        {
            return AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        }

        private static void Register(string type, string name, params Type[] args)
        {
            MP.RegisterSyncMethod(Required(RequiredType(type), name, args), null);
        }

        private static FieldInfo FactionField(Type type)
        {
            return AccessTools.Field(type, "callingFaction") ?? AccessTools.Field(type, "calledFaction");
        }

        private static bool TargetedPrefix(RoyalTitlePermitWorker_Targeted __instance, LocalTargetInfo __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !MP.InInterface) return true;
            Type type = __instance.GetType();
            FieldInfo firstField = AccessTools.Field(type, "firstTarget");
            LocalTargetInfo first = firstField == null ? LocalTargetInfo.Invalid : (LocalTargetInfo)firstField.GetValue(__instance);
            if (firstField != null)
            {
                // First click and invalid second clicks only update the local targeter.
                if (!first.IsValid || !__0.IsValid || first.Cell == __0.Cell) return true;
                float maxLength = (float)AccessTools.Property(type, "MaxBombLength").GetValue(__instance);
                if ((__0.Cell - first.Cell).LengthHorizontal > maxLength) return true;
            }
            Faction faction = FactionField(type).GetValue(__instance) as Faction;
            Pawn caller = Caller.GetValue(__instance) as Pawn;
            Map map = MapField.GetValue(__instance) as Map;
            if (caller == null || map == null || faction == null || !__0.IsValid)
            {
                Log.Warning("[MP-MeowOnlineShop] Wolfein permit rejected: incomplete targeting context.");
                return false;
            }
            targetedPermit.DoSync(null, __instance.def, caller, map, faction,
                (bool)Free.GetValue(__instance), first, __0);
            firstField?.SetValue(__instance, LocalTargetInfo.Invalid);
            return false;
        }

        public static void ExecuteTargetedPermit(RoyalTitlePermitDef def, Pawn caller, Map map,
            Faction faction, bool free, LocalTargetInfo first, LocalTargetInfo target)
        {
            if (def?.Worker == null || caller == null || map == null || faction == null || caller.MapHeld != map) return;
            // A fresh worker prevents another player's command from replacing local target selection.
            var worker = (RoyalTitlePermitWorker_Targeted)Activator.CreateInstance(def.Worker.GetType());
            worker.def = def;
            Caller.SetValue(worker, caller);
            MapField.SetValue(worker, map);
            Free.SetValue(worker, free);
            FactionField(worker.GetType()).SetValue(worker, faction);
            AccessTools.Field(worker.GetType(), "firstTarget")?.SetValue(worker, first);
            worker.OrderForceTarget(target);
        }
    }
}
