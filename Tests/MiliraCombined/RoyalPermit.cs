using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;

internal static class RoyalPermitProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static bool requested, prepared, localReady, sent, advance, complete, failed;
    static int round, start, drops;
    static float deadline;
    static Pawn caller, otherCaller;
    static Faction faction;
    static RoyalTitlePermitDef permit;
    static ISyncMethod prepare;
    static MethodInfo order;
    static string[] acquiredBefore;
    static string podReceipt;
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static object Get(object o, string field) => AccessTools.Field(o.GetType(), field).GetValue(o);
    static object GameComp => Current.Game.components.Single(c => c.GetType().FullName == "MilianModification.MiliraGameComponent_MilianComponentRecipe");
    static string[] Acquired => ((IEnumerable)Get(GameComp, "milianComponentAcquire")).Cast<Def>().Select(d => d.defName).OrderBy(s => s).ToArray();
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static void Init(Harmony h)
    {
        var worker = AccessTools.TypeByName("MilianModification.RoyalTitlePermitWorker_DropMilianComponent");
        order = AccessTools.DeclaredMethod(worker, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
        Check(order != null, "native permit target unresolved");
        prepare = MP.RegisterSyncMethod(typeof(RoyalPermitProbe), nameof(Prepare));
        h.Patch(AccessTools.Method(typeof(DropPodUtility), "MakeDropPodAt", new[] { typeof(IntVec3), typeof(Map), typeof(ActiveTransporterInfo), typeof(Faction) }),
            postfix: new HarmonyMethod(typeof(RoyalPermitProbe), nameof(Drop)));
    }
    static void Drop(IntVec3 __0, Map __1, ActiveTransporterInfo __2)
    {
        if (!prepared || complete || failed || __0 != Target || __1 != caller.Map) return;
        drops++;
        podReceipt = __1.uniqueID + ":" + __0 + ":" + string.Join(",", __2.innerContainer.Select(t => t.def.defName + ":" + t.stackCount));
    }
    static IntVec3 Target => caller.Map.Center + new IntVec3(45 + round * 3, 0, -45);
    public static void Prepare(int n)
    {
        round = n; localReady = sent = advance = false; drops = 0; podReceipt = "";
        permit = DefDatabase<RoyalTitlePermitDef>.GetNamed("Milira_DropMilianComponentAndUnlockRecipe");
        faction = Find.FactionManager.AllFactions.Single(f => f.def.defName == "Milira_AngelismChurch");
        if (caller == null)
        {
            var map = Find.Maps.First(m => m.IsPlayerHome);
            caller = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            otherCaller = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            foreach (var pawn in new[] { caller, otherCaller })
            {
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
                pawn.royalty.SetTitle(faction, DefDatabase<RoyalTitleDef>.GetNamed("Milira_ChurchTitleII"), false, false, false);
                pawn.royalty.AddPermit(permit, faction);
            }
        }
        foreach (var pawn in new[] { caller, otherCaller }) { pawn.royalty.SetFavor(faction, 20, false); pawn.royalty.GetPermit(permit, faction).Notify_Used(); }
        acquiredBefore = Acquired; start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; prepared = true;
        Log.Message("ROYAL PREPARE round=" + round + " caller=" + caller.ThingID + " other=" + otherCaller.ThingID + " faction=" + faction.GetUniqueLoadID());
    }
    internal static bool Update()
    {
        if (complete) return true;
        if (failed) return false;
        try
        {
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            Check(Time.realtimeSinceStartup < deadline, "royal permit action timeout");
            if (!localReady && TickPatch.Timer - start > 40)
            {
                Current.Game.CurrentMap = caller.Map;
                var patches = Harmony.GetPatchInfo(order);
                Check(!patches.Prefixes.Any(p => p.PatchMethod.DeclaringType?.Name == "AriandelMiliraImperium_Compat"), "broad Imperium permit interceptor still installed");
                Check(patches.Prefixes.Count(p => p.PatchMethod.DeclaringType?.Name == "MilianModification_StateSync") == 1, "missing/duplicate exact Milian permit interceptor");
                var pawn = client ? caller : otherCaller;
                var option = permit.Worker.GetRoyalAidOptions(caller.Map, pawn, faction).Single();
                Check(option.action != null, "native royal menu disabled: " + option.Label);
                option.action(); localReady = true;
                if (!client) File.WriteAllText(Path.Combine(Root, "host.permitReady" + round), "native competing targeter ready");
            }
            if (client && localReady && !sent && File.Exists(Path.Combine(Root, "host.permitReady" + round)))
            {
                sent = true;
                order.Invoke(permit.Worker, new object[] { new LocalTargetInfo(Target) });
                Find.Targeter.StopTargeting();
                Check(Acquired.SequenceEqual(acquiredBefore) && caller.royalty.GetFavor(faction) == 20 && drops == 0, "royal UI mutated before sync");
                Log.Message("ROYAL native target queued round=" + round);
            }
            if (TickPatch.Timer - start < 350) return false;
            Check(caller.royalty.GetFavor(faction) == 20 - permit.royalAid.favorCost, "royal favor not charged once");
            Check(otherCaller.royalty.GetFavor(faction) == 20, "other player's favor changed");
            Check(drops == 1 && Acquired.Except(acquiredBefore).Count() == 1, "royal drop/unlock count mismatch");
            Check(ReferenceEquals(Get(permit.Worker, "caller"), client ? caller : otherCaller) && ReferenceEquals(Get(permit.Worker, "faction"), faction) && ReferenceEquals(Get(permit.Worker, "map"), caller.Map), "local targeting context was overwritten caller=" + ((Pawn)Get(permit.Worker, "caller"))?.ThingID + " expected=" + (client ? caller : otherCaller).ThingID);
            string receipt = string.Join(",", Acquired.Except(acquiredBefore)) + ";favor=" + caller.royalty.GetFavor(faction) + ";used=" + caller.royalty.GetPermit(permit, faction).LastUsedTick + ";pod=" + podReceipt;
            string path = Path.Combine(Root, Peer + ".royal" + round);
            if (!File.Exists(path)) { File.WriteAllText(path + ".tmp", receipt); File.Move(path + ".tmp", path); Log.Message("ROYAL ASSERT " + round + " " + receipt); }
            string other = Path.Combine(Root, (client ? "host" : "client") + ".royal" + round);
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(other) == receipt, "royal peer state mismatch");
            if (round == 2) { Find.Targeter.StopTargeting(); complete = true; return true; }
            if (client && !advance) { advance = true; prepare.DoSync(null, round + 1); }
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED ROYAL " + e); }
        return false;
    }
}
