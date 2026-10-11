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

internal static class UninstallationJobProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Pawn worker, milian;
    static Map map;
    static ThingDef componentDef;
    static HediffDef hediffDef;
    static IDictionary uninstall, installed;
    static object slot;
    static int baselineReturned, start;
    static bool requested, prepared, sent, done, failed;
    static float deadline;
    static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    static int ReturnedCount() => map.listerThings.ThingsOfDef(componentDef).Count(t => t.Spawned);

    internal static void Init() => prepare = MP.RegisterSyncMethod(typeof(UninstallationJobProbe), nameof(Prepare));

    public static void Prepare()
    {
        map = Find.Maps.First(m => m.IsPlayerHome);
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
        var recipe = (IDictionary)AccessTools.Field(modificationType, "componentSlotInRecipe").GetValue(modification);
        uninstall = (IDictionary)AccessTools.Field(modificationType, "slotUninstallRecipe").GetValue(modification);
        installed = (IDictionary)AccessTools.Field(modificationType, "componentSlotInstalled").GetValue(modification);
        recipe.Add(slot, componentDef);
        var utility = AccessTools.TypeByName("MilianModification.MilianModificationUtility");
        AccessTools.Method(utility, "InstallComponent", new[] { typeof(Pawn), componentDef.GetType() })
            .Invoke(null, new object[] { milian, componentDef });
        Check(installed.Contains(slot) && installed[slot] == componentDef &&
            milian.health.hediffSet.HasHediff(hediffDef), "native uninstall fixture install failed");
        uninstall.Add(slot, componentDef);
        baselineReturned = ReturnedCount();
        prepared = true; start = TickPatch.Timer; deadline = UnityEngine.Time.realtimeSinceStartup + 150;
        Log.Message("UNINSTALL_JOB PREPARE worker=" + worker.ThingID + " milian=" + milian.ThingID +
            " slot=" + slot + " returnedBaseline=" + baselineReturned);
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
                var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Milira_UninstallMilianFitting"), milian);
                job.count = 1;
                var before = worker.CurJob;
                bool acceptedLocally = worker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                Check(ReferenceEquals(before, worker.CurJob) && installed.Contains(slot) &&
                    milian.health.hediffSet.HasHediff(hediffDef) && ReturnedCount() == baselineReturned,
                    "uninstallation mutated locally before sync");
                sent = true;
                Log.Message("UNINSTALL_JOB DISPATCH native ordered job rollback=True acceptedLocally=" + acceptedLocally);
            }
            Check(UnityEngine.Time.realtimeSinceStartup < deadline, "uninstallation job timeout");
            if (installed.Contains(slot) || milian.health.hediffSet.HasHediff(hediffDef) ||
                ReturnedCount() <= baselineReturned) return false;
            string receipt = milian.ThingID + ";component=" + componentDef.defName +
                ";installed=False;hediff=False;returned=True";
            string local = Path.Combine(root, Peer + ".uninstalljob");
            string other = Path.Combine(root, (client ? "host" : "client") + ".uninstalljob");
            if (!File.Exists(local)) { File.WriteAllText(local + ".tmp", receipt); File.Move(local + ".tmp", local); }
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(local) == File.ReadAllText(other), "uninstallation job peer receipt mismatch");
            done = true;
            Log.Message("UNINSTALL_JOB ACTION_PASS " + receipt);
            return true;
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED UNINSTALL_JOB " + e); }
        return false;
    }
}
