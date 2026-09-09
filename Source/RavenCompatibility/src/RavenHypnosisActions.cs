using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    internal static class RavenHypnosisActions
    {
        private static ISyncMethod execute;
        private static Type worldType;
        private static MethodInfo active, cooldown;
        internal static void Apply(Harmony harmony)
        {
            var dialog = AccessTools.TypeByName("RavenRace.Features.Hypnosis.Dialog_HypnosisControl");
            var utility = AccessTools.TypeByName("RavenRace.Features.Hypnosis.Commands.HypnosisCommandUtility");
            worldType = AccessTools.TypeByName("RavenRace.Features.Hypnosis.WorldComponent_Hypnosis");
            active = AccessTools.DeclaredMethod(utility, "IsCommandActive");
            cooldown = AccessTools.DeclaredMethod(utility, "GetCooldownEndTick");
            if (active == null || cooldown == null || worldType == null) throw new MissingMethodException("Raven hypnosis command helpers");
            execute = MP.RegisterSyncMethod(typeof(RavenHypnosisActions), nameof(Execute));
            harmony.Patch(AccessTools.DeclaredMethod(dialog, "ExecuteHypnosisCommand"),
                prefix: new HarmonyMethod(typeof(RavenHypnosisActions), nameof(Before)));
        }
        private static bool Before(object __instance, Pawn __0, Def __1)
        {
            if (!MP.InInterface) return true;
            if (__0 == null || __0.Dead || __0.Downed || !__0.Spawned)
            {
                Reject(2); return false;
            }
            var last = (Dictionary<int, int>)AccessTools.Field(__instance.GetType(), "lastCommandTicks").GetValue(__instance);
            int now = RavenHypnosisClocks.PawnTick(__0);
            if (last.TryGetValue(__0.thingIDNumber, out int tick) && now >= tick && now - tick < 60)
            {
                Reject(3); return false;
            }
            last[__0.thingIDNumber] = now; // UI debounce remains local to this window.
            var master = (Pawn)AccessTools.Field(__instance.GetType(), "master").GetValue(__instance);
            if (master == null || master.ageTracker.AgeBiologicalYears < 18 || __0.ageTracker.AgeBiologicalYears < 18) return false;
            execute.DoSync(null, __0.Map, master.thingIDNumber, __0.thingIDNumber, __1);
            return false;
        }
        private static void Reject(int index) => Messages.Message(
            ("RavenRace_Hypnosis_Dialog_HypnosisControl_Message_" + index).Translate(), MessageTypeDefOf.RejectInput, false);
        private static T Field<T>(Def command, string name) where T : Def => (T)AccessTools.Field(command.GetType(), name).GetValue(command);
        // Exact mutation body of ExecuteHypnosisCommand, without window state. The
        // ordered job stays inside this command so undrafting and job creation replay together.
        private static void Execute(Map map, int masterId, int pawnId, Def command)
        {
            var pawn = map?.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == pawnId);
            var master = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).FirstOrDefault(p => p.thingIDNumber == masterId)
                ?? Find.WorldPawns.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == masterId);
            if (master == null || pawn == null || command == null || pawn.Dead || pawn.Downed || !pawn.Spawned) { Reject(2); return; }
            if (master.ageTracker.AgeBiologicalYears < 18 || pawn.ageTracker.AgeBiologicalYears < 18) return;
            var world = AccessTools.Property(worldType, "Instance").GetValue(null);
            var relations = (Dictionary<int, List<int>>)AccessTools.Field(worldType, "relationMap").GetValue(world);
            if (!relations.TryGetValue(master.thingIDNumber, out var slaves) || !slaves.Contains(pawn.thingIDNumber)) return;
            if ((bool)active.Invoke(null, new object[] { pawn, command })) { Reject(4); return; }
            if (RavenHypnosisClocks.PawnTick(pawn) < (int)cooldown.Invoke(null, new object[] { pawn, command })) { Reject(5); return; }
            var job = Field<JobDef>(command, "jobDef");
            if (job != null)
            {
                if (pawn.Drafted) pawn.drafter.Drafted = false;
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job), (JobTag)0, false);
            }
            else
            {
                var hediff = Field<HediffDef>(command, "activeHediffDef");
                if (hediff != null) HealthUtility.AdjustSeverity(pawn, hediff, 1f);
                var thought = Field<ThoughtDef>(command, "outcomeThoughtDef");
                if (thought != null) pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
            }
            FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.PsycastAreaEffect, 0.42f);
            Messages.Message("RavenRace_Hypnosis_Dialog_HypnosisControl_Message_6".Translate(command.label, pawn.LabelShort), MessageTypeDefOf.TaskCompletion, true);
        }
    }
}
