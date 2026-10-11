using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianReinforcementTarget
    {
        static Type letterType, workerType;
        static FieldInfo mapField, status, incident;
        static MethodInfo target, drop, complete;
        static ISyncMethod dispatch;
        internal static void Apply(Harmony harmony)
        {
            letterType = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianAid");
            workerType = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Incidents.IncidentWorker_NivarianDropPodReinforcement");
            mapField = AccessTools.Field(letterType, "targetMap") ?? throw new MissingFieldException(letterType.FullName, "targetMap"); status = AccessTools.Field(letterType, "stat") ?? throw new MissingFieldException(letterType.FullName, "stat"); incident = AccessTools.Field(letterType, "incidentDef") ?? throw new MissingFieldException(letterType.FullName, "incidentDef");
            target = NivarianUpdateActions.RequiredMethod(workerType, "BeginDropCellTargeting", typeof(Map), typeof(Action<IntVec3>), typeof(Action));
            drop = NivarianUpdateActions.RequiredMethod(workerType, "TryDropReinforcementsAt", typeof(Map), typeof(IntVec3), letterType);
            complete = NivarianUpdateActions.RequiredMethod(letterType, "CompleteAccept");
            dispatch = MP.RegisterSyncMethod(typeof(NivarianReinforcementTarget), nameof(Dispatch));
            harmony.Patch(NivarianUpdateActions.RequiredMethod(letterType, "ExecuteAccept"),
                prefix: new HarmonyMethod(typeof(NivarianReinforcementTarget), nameof(Accept)) { priority = Priority.First + 2 });
            Log.Message("[TaleNivarianCompat] reinforcement targeting remains local; selected cell, planned roster and acceptance replay in map command.");
        }
        static bool Pending(Letter letter) => letter != null && letterType.IsInstanceOfType(letter) && Convert.ToInt32(status.GetValue(letter)) == 0;
        static bool Accept(Letter __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            var def = incident.GetValue(__instance) as IncidentDef;
            if (def?.Worker == null || !workerType.IsInstanceOfType(def.Worker)) return true;
            var map = mapField.GetValue(__instance) as Map;
            if (Pending(__instance) && map != null && Find.Maps.Contains(map) && map.IsPlayerHome)
            {
                CameraJumper.TryJump(map.Center, map);
                // Do not mutate AwaitingDropCellSelection while a player previews or cancels a target.
                target.Invoke(def.Worker, new object[] { map, new Action<IntVec3>(cell => dispatch.DoSync(null, map, __instance, cell)), null });
            }
            return false;
        }
        static void Dispatch(Map map, Letter letter, IntVec3 cell)
        {
            if (map == null || !Find.Maps.Contains(map) || !cell.InBounds(map) || map.ParentFaction != Faction.OfPlayer
                || !Pending(letter) || !ReferenceEquals(mapField.GetValue(letter), map) || ((LetterWithTimeout)letter).TimeoutPassed) return;
            var def = incident.GetValue(letter) as IncidentDef;
            if (def?.Worker != null && workerType.IsInstanceOfType(def.Worker)
                && (bool)drop.Invoke(def.Worker, new object[] { map, cell, letter })) complete.Invoke(letter, null);
        }
    }
}

