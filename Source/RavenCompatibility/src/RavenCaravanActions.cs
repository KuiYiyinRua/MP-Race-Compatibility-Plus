using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using Verse;
using Runtime = Multiplayer.Client.Multiplayer;

namespace MP_MeowOnlineShop
{
    internal static class RavenCaravanActions
    {
        private static Type worldType;
        internal static void Apply(Harmony harmony)
        {
            worldType = AccessTools.TypeByName("RavenRace.WorldComponent_Fusang");
            foreach (var name in new[] { "Dialog_FusangCaravan", "Dialog_FusangLogistics" })
            {
                var type = AccessTools.TypeByName("RavenRace.Features.FusangOrganization.UI." + name);
                harmony.Patch(AccessTools.DeclaredMethod(type, "TryCallCaravan"),
                    prefix: new HarmonyMethod(typeof(RavenCaravanActions), nameof(Before)));
            }
            MP.RegisterSyncMethod(typeof(RavenCaravanActions), nameof(Execute));
        }
        private static bool Before(Window __instance, object[] __args, Thing ___radio, Faction ___fusangFaction)
        {
            if (!MP.InInterface) return true;
            if (___radio?.Map == null) return false;
            Execute(___radio, ___radio.Map.uniqueID, ___fusangFaction, __args.Length == 0 ? null : __args[0] as TraderKindDef);
            __instance.Close();
            return false;
        }
        private static void Execute(Thing radio, int originMapId, Faction faction, TraderKindDef kind)
        {
            if (radio == null || !radio.Spawned || radio.Destroyed || radio.Map.uniqueID != originMapId || faction == null) return;
            if (kind != null && !kind.defName.StartsWith("Raven_Trader_", StringComparison.Ordinal)) return;
            var world = Find.World.components.First(c => c.GetType() == worldType);
            if (!(bool)AccessTools.DeclaredMethod(worldType, "CanTradeNow").Invoke(world, null)) { Result(false); return; }
            var incident = DefDatabase<IncidentDef>.GetNamedSilentFail("Raven_Incident_TraderCaravanArrival");
            if (incident == null) { Result(false); return; }
            var parms = new IncidentParms { target = radio.Map, faction = faction, traderKind = kind, forced = true };
            if (!incident.Worker.TryExecute(parms)) { Result(false); return; }
            int now = MP.IsInMultiplayer ? Runtime.AsyncWorldTime.worldTicks : Find.TickManager.TicksGame;
            var settings = AccessTools.Property(AccessTools.TypeByName("RavenRace.RavenRaceMod"), "Settings").GetValue(null);
            float days = (float)AccessTools.Field(settings.GetType(), "tradeCaravanCooldownDays").GetValue(settings);
            AccessTools.Field(worldType, "lastTradeTick").SetValue(world, now);
            AccessTools.Field(worldType, "tradeCooldownTicks").SetValue(world, (int)(days * 60000f));
            Result(true);
        }
        private static void Result(bool success) => Messages.Message(
            ("RavenRace_Fusang_FusangComm_UIPanels_Message_" + (success ? "2" : "3")).Translate(),
            success ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, true);
    }
}
