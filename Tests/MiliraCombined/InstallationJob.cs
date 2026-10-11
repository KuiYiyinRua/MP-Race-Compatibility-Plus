using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using Verse;
using Verse.AI;
using Mp = Multiplayer.Client.Multiplayer;

internal static class InstallationJobProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Pawn worker, milian;
    static Thing fitting;
    static ThingDef componentDef;
    static HediffDef hediffDef;
    static IDictionary recipe, installed;
    static object slot;
    static bool requested, prepared, sent, done, failed;
    static int start;
    static float deadline;
    static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

    internal static void Init() => prepare = MP.RegisterSyncMethod(typeof(InstallationJobProbe), nameof(Prepare));

    public static void Prepare()
    {
        var map = Find.Maps.First(m => m.IsPlayerHome);
        milian = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
        var center = CellFinder.RandomClosewalkCellNear(map.Center, map, 12);
        GenSpawn.Spawn(milian, center, map);
        worker = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, validatorPostGear: p => p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)));
        GenSpawn.Spawn(worker, CellFinder.RandomClosewalkCellNear(center, map, 6), map);
        componentDef = DefDatabase<ThingDef>.GetNamed("MilianFitting_BackupBattery");
        hediffDef = DefDatabase<HediffDef>.GetNamed("MilianFitting_BackupBattery");
        var data = AccessTools.Field(componentDef.GetType(), "milianComponent").GetValue(componentDef);
        slot = ((IEnumerable)AccessTools.Field(data.GetType(), "possibleSlots").GetValue(data)).Cast<object>().First();
        var modificationType = AccessTools.TypeByName("MilianModification.CompModification");
        var modification = milian.AllComps.Single(c => c.GetType() == modificationType);
        recipe = (IDictionary)AccessTools.Field(modificationType, "componentSlotInRecipe").GetValue(modification);
        installed = (IDictionary)AccessTools.Field(modificationType, "componentSlotInstalled").GetValue(modification);
        Check(!recipe.Contains(slot) && !installed.Contains(slot), "installation slot not empty");
        recipe.Add(slot, componentDef);
        fitting = GenSpawn.Spawn(ThingMaker.MakeThing(componentDef),
            CellFinder.RandomClosewalkCellNear(worker.Position, map, 4), map);
        fitting.stackCount = 1;
        prepared = true; start = TickPatch.Timer; deadline = UnityEngine.Time.realtimeSinceStartup + 150;
        Log.Message("INSTALL_JOB PREPARE worker=" + worker.ThingID + " milian=" + milian.ThingID +
            " fitting=" + fitting.ThingID + " slot=" + slot);
    }

    internal static bool Update()
    {
        if (done) return true;
        if (failed) return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null); return false; }
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Milira_InstallMilianFitting"), milian, fitting);
                job.count = 1;
                var before = worker.CurJob;
                bool acceptedLocally = worker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                Check(ReferenceEquals(before, worker.CurJob) && !installed.Contains(slot) &&
                    !milian.health.hediffSet.HasHediff(hediffDef), "installation mutated locally before sync");
                sent = true;
                Log.Message("INSTALL_JOB DISPATCH native ordered job rollback=True acceptedLocally=" + acceptedLocally);
            }
            Check(UnityEngine.Time.realtimeSinceStartup < deadline, "installation job timeout");
            if (!installed.Contains(slot) || !milian.health.hediffSet.HasHediff(hediffDef) || !fitting.Destroyed)
                return false;
            string receipt = milian.ThingID + ";component=" + componentDef.defName +
                ";installed=True;hediff=True;consumed=True";
            string local = Path.Combine(root, Peer + ".installjob");
            string other = Path.Combine(root, (client ? "host" : "client") + ".installjob");
            if (!File.Exists(local)) { File.WriteAllText(local + ".tmp", receipt); File.Move(local + ".tmp", local); }
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(local) == File.ReadAllText(other), "installation job peer receipt mismatch");
            done = true;
            Log.Message("INSTALL_JOB ACTION_PASS " + receipt);
            return true;
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED INSTALL_JOB " + e); }
        return false;
    }
}
