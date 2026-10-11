using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class ComponentDataJobProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Pawn worker;
    static ThingWithComps data, fitting;
    static ThingDef component;
    static IEnumerable acquired;
    static bool Acquired(ThingDef def) => acquired.Cast<ThingDef>().Contains(def);
    static bool requested, prepared, sent, disassembleSent, finished, failed;
    static int round, start, applied, destroyed;
    static float deadline;
    static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

    internal static void Init(Harmony harmony)
    {
        prepare = MP.RegisterSyncMethod(typeof(ComponentDataJobProbe), nameof(Prepare));
        harmony.Patch(AccessTools.Method(AccessTools.TypeByName("MilianModification.CompMilianComponentData"), "DataApplied"),
            postfix: new HarmonyMethod(typeof(ComponentDataJobProbe), nameof(AfterApplied)));
    }

    static void AfterApplied(ThingComp __instance)
    {
        if (__instance.parent != data) return;
        applied++;
        Log.Message("COMPONENT_DATA EXECUTED round=" + round + " data=" + data.ThingID + " acquired=" + Acquired(component));
    }

    public static void Prepare(int n)
    {
        round = n; sent = disassembleSent = false; applied = destroyed = 0;
        var map = Find.Maps.First(m => m.IsPlayerHome);
        var bench = map.listerThings.AllThings.First(t => t.def.defName == "Milira_ComponentBench");
        worker = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, validatorPostGear: p => !p.WorkTypeIsDisabled(WorkTypeDefOf.Research) &&
                !p.WorkTagIsDisabled(WorkTags.Intellectual) && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)));
        GenSpawn.Spawn(worker, CellFinder.RandomClosewalkCellNear(bench.Position, map, 5), map);
        var gameComp = Current.Game.components.First(c => c.GetType().FullName == "MilianModification.MiliraGameComponent_MilianComponentRecipe");
        acquired = (IEnumerable)AccessTools.Field(gameComp.GetType(), "milianComponentAcquire").GetValue(gameComp);
        var all = (IEnumerable)AccessTools.Field(gameComp.GetType(), "milianComponentAll").GetValue(gameComp);
        component = all.Cast<ThingDef>().OrderBy(d => d.defName).First(d => !Acquired(d) &&
            d.recipeMaker != null && d.recipeMaker.factionPrerequisiteTags != null && d.recipeMaker.factionPrerequisiteTags.Count > 0 &&
            (d.recipeMaker.researchPrerequisites == null || d.recipeMaker.researchPrerequisites.All(r => r.IsFinished)));
        data = (ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("Milian_ComponentData"));
        var dataComp = data.AllComps.First(c => c.GetType().FullName == "MilianModification.CompMilianComponentData");
        AccessTools.Field(dataComp.GetType(), "milianComponentDef").SetValue(dataComp, component);
        GenSpawn.Spawn(data, CellFinder.RandomClosewalkCellNear(worker.Position, map, 3), map);
        fitting = (ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("MilianFitting_BackupBattery"));
        GenSpawn.Spawn(fitting, CellFinder.RandomClosewalkCellNear(worker.Position, map, 4), map);
        start = TickPatch.Timer; deadline = UnityEngine.Time.realtimeSinceStartup + 180; prepared = true;
        Log.Message("COMPONENT_DATA PREPARE round=" + round + " worker=" + worker.ThingID + " data=" + data.ThingID + " component=" + component.defName);
    }

    internal static bool Update()
    {
        if (finished) return true;
        if (failed) return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            Check(UnityEngine.Time.realtimeSinceStartup < deadline, "component data/disassembly timeout round=" + round + " job=" + worker.CurJob);
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                var comp = data.AllComps.First(c => c.GetType().FullName == "MilianModification.CompMilianComponentData");
                var option = comp.CompFloatMenuOptions(worker).Single(o => o.action != null);
                Check(!option.Disabled, "data menu disabled: " + option.Label);
                var before = worker.CurJob;
                option.action();
                Check(ReferenceEquals(before, worker.CurJob) && !Acquired(component) && !data.Destroyed, "data menu local mutation before sync");
                sent = true;
                Log.Message("COMPONENT_DATA DISPATCH round=" + round + " native FloatMenu action rollback=True");
            }
            if (applied == 0 || !data.Destroyed || !Acquired(component)) return false;
            Check(applied == 1 && acquired.Cast<ThingDef>().Count(d => d == component) == 1, "data acquisition duplicated");
            if (client && !disassembleSent)
            {
                var comp = fitting.AllComps.First(c => c.GetType().FullName == "MilianModification.CompMilianComponent");
                var option = comp.CompFloatMenuOptions(worker).Single(o => o.action != null);
                var before = worker.CurJob;
                option.action();
                Check(ReferenceEquals(before, worker.CurJob) && !fitting.Destroyed, "disassembly local mutation before sync");
                disassembleSent = true;
                Log.Message("COMPONENT_DISASSEMBLY DISPATCH round=" + round + " native FloatMenu action rollback=True");
            }
            if (!fitting.Destroyed) return false;
            destroyed = 1;
            string receipt = "round=" + round + ";worker=" + worker.ThingID + ";component=" + component.defName +
                ";applied=" + applied + ";acquired=True;dataConsumed=True;fittingDestroyed=" + destroyed;
            string local = Path.Combine(root, Peer + ".componentdata" + round);
            string other = Path.Combine(root, (client ? "host" : "client") + ".componentdata" + round);
            if (!File.Exists(local)) { File.WriteAllText(local + ".tmp", receipt); File.Move(local + ".tmp", local); Log.Message("COMPONENT_DATA ACTION_PASS " + receipt); }
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(local) == File.ReadAllText(other), "component data peer receipt mismatch");
            if (round == 2) { finished = true; return true; }
            if (client && round < 2) prepare.DoSync(null, round + 1);
            prepared = false; requested = true;
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED COMPONENT_DATA " + e); }
        return false;
    }
}
