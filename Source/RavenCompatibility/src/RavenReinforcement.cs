using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.Sound;

namespace MP_MeowOnlineShop
{
    internal static class RavenReinforcement
    {
        private static Type tierType, resourcesType;
        private static object military;
        internal static void Apply(Harmony harmony)
        {
            tierType = AccessTools.TypeByName("RavenRace.FusangReinforcementTierDef");
            resourcesType = AccessTools.TypeByName("RavenRace.FusangResourceManager");
            var resourceEnum = AccessTools.TypeByName("RavenRace.FusangResourceType");
            if (tierType == null || resourcesType == null || resourceEnum == null) throw new TypeLoadException("Raven reinforcement API");
            military = Enum.Parse(resourceEnum, "Military");
            foreach (string name in new[] { "Dialog_FusangLogistics", "Dialog_FusangReinforcement" })
            {
                var type = AccessTools.TypeByName("RavenRace.Features.FusangOrganization.UI." + name);
                harmony.Patch(AccessTools.DeclaredMethod(type, "TryCallReinforcement", new[] { tierType })
                    ?? throw new MissingMethodException(name, "TryCallReinforcement"),
                    prefix: new HarmonyMethod(typeof(RavenReinforcement), nameof(BeforeTargeting)));
            }
            MP.RegisterSyncMethod(typeof(RavenReinforcement), nameof(Execute));
        }

        private static object ResourceCall(string method, params object[] args)
            => AccessTools.DeclaredMethod(resourcesType, method).Invoke(null, args);
        private static int Cost(Def tier) => (int)AccessTools.DeclaredField(tierType, "militaryCost").GetValue(tier);
        private static IncidentDef Incident(Def tier) => (IncidentDef)AccessTools.DeclaredField(tierType, "incidentDef").GetValue(tier);

        private static bool BeforeTargeting(Window __instance, Def __0, Thing ___radio, Faction ___fusangFaction)
        {
            if (!MP.InInterface) return true;
            var tier = __0;
            var radio = ___radio;
            var faction = ___fusangFaction;
            var map = radio?.Map;
            if (tier == null || tier.GetType() != tierType || map == null || Incident(tier) == null) return false;
            if ((int)ResourceCall("GetAmount", military) < Cost(tier))
            {
                Messages.Message("RavenRace_Fusang_Dialog_FusangLogistics_NoMilitary".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            __instance.Close();
            var parameters = TargetingParameters.ForCell();
            parameters.canTargetLocations = true;
            Find.Targeter.BeginTargeting(parameters,
                target => Execute(radio, map.uniqueID, faction, tier, target.Cell),
                null, target => Find.CurrentMap == map && target.IsValid && target.Cell.InBounds(map));
            return false;
        }

        // No reservation is made while the local targeter is open. Cancel therefore has
        // no shared side effects; cost and incident are one replayed outcome.
        private static void Execute(Thing radio, int originMapId, Faction faction, Def tier, IntVec3 cell)
        {
            var map = radio?.Map;
            if (map == null || radio.Destroyed || !radio.Spawned || map.uniqueID != originMapId || faction == null || tier == null || tier.GetType() != tierType || !cell.InBounds(map)) return;
            var incident = Incident(tier);
            int cost = Cost(tier);
            if (incident == null || cost < 0) return;
            if (!(bool)ResourceCall("TryConsume", military, cost))
            {
                Messages.Message("RavenRace_Fusang_Dialog_FusangLogistics_NoMilitary".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            bool success = false;
            try
            {
                success = incident.Worker.TryExecute(new IncidentParms { target = map, faction = faction, forced = true, spawnCenter = cell });
            }
            finally
            {
                if (!success) ResourceCall("Add", military, cost);
            }
            if (success) SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
            else Messages.Message("RavenRace_Fusang_Dialog_FusangLogistics_ReinforcementFail".Translate(), MessageTypeDefOf.RejectInput);
        }
    }
}
