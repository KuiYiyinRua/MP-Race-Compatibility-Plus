using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class ResearchProbe
{
    static bool client, requested, prepared, sent, advance, finished, failed;
    static int round, start;
    static float deadline;
    static ISyncMethod prepare;
    static Type tabType, gcType;
    static Building_WorkTable table, otherTable;
    static string before;
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static object GC => Current.Game.components.First(c => c.GetType() == gcType);
    static object Get(string field) => AccessTools.Field(gcType, field).GetValue(GC);
    static RecipeDef Recipe => DefDatabase<RecipeDef>.GetNamed("Milira_ResearchMilianComponent");
    static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    static IntVec3 FreeCell(Map map, ThingDef def)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 20);
            var footprint = GenAdj.OccupiedRect(cell, Rot4.North, def.Size);
            if (footprint.Cells.All(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null)) return cell;
        }
        for (int z = 1; z < map.Size.z - 1; z++)
        for (int x = 1; x < map.Size.x - 1; x++)
        {
            var cell = new IntVec3(x, 0, z);
            var footprint = GenAdj.OccupiedRect(cell, Rot4.North, def.Size);
            if (footprint.Cells.All(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null)) return cell;
        }
        throw new Exception("no free research bench footprint");
    }
    static void RequireFixtures()
    {
        Require(table != null && table.Spawned && !table.Destroyed && table.Map != null,
            "primary research table lost: " + table?.ThingID + " spawned=" + table?.Spawned + " destroyed=" + table?.Destroyed);
        Require(otherTable != null && otherTable.Spawned && !otherTable.Destroyed && otherTable.Map == table.Map,
            "secondary research table lost: " + otherTable?.ThingID + " spawned=" + otherTable?.Spawned + " destroyed=" + otherTable?.Destroyed);
    }
    internal static void Init()
    {
        client = GenCommandLine.CommandLineArgPassed("twclient");
        tabType = AccessTools.TypeByName("MilianModification.ITab_Pawn_MilianComponentResearch");
        gcType = AccessTools.TypeByName("MilianModification.MiliraGameComponent_MilianComponentRecipe");
        prepare = MP.RegisterSyncMethod(typeof(ResearchProbe), nameof(Prepare));
    }
    public static void Prepare(int n)
    {
        round = n; sent = advance = false;
        var map = Find.Maps.First(m => m.IsPlayerHome);
        if (table == null)
        {
            var def = DefDatabase<ThingDef>.AllDefs.Single(d => d.thingClass.FullName == "MilianModification.Building_MilianComponentTable");
            table = (Building_WorkTable)GenSpawn.Spawn(ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)), FreeCell(map, def), map);
            otherTable = (Building_WorkTable)GenSpawn.Spawn(ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)), FreeCell(map, def), map);
            table.SetFaction(Faction.OfPlayer); otherTable.SetFaction(Faction.OfPlayer);
        }
        RequireFixtures();
        if (!otherTable.BillStack.Bills.Any(b => b.recipe == Recipe)) otherTable.BillStack.AddBill(BillUtility.MakeNewBill(Recipe));
        AccessTools.Field(gcType, "minResearchSkill").SetValue(GC, 6 + n);
        before = State(); start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; prepared = true;
        Log.Message("RESEARCH PREPARE round=" + round + " primary=" + table.ThingID + " at=" + table.Position +
            " secondary=" + otherTable.ThingID + " at=" + otherTable.Position + " " + before);
    }
    static string State()
    {
        var resources = (Dictionary<ThingDef, int>)Get("tmpResources");
        var tables = ((IEnumerable)Get("milianWorkTables")).Cast<Building_WorkTable>().OrderBy(t => t.thingIDNumber);
        return "resources=" + string.Join(",", resources.OrderBy(e => e.Key.defName).Select(e => e.Key.defName + ":" + e.Value)) +
            ";ingredients=" + string.Join(",", Recipe.ingredients.Select(i => string.Join("+", i.filter.AllowedThingDefs.Select(d => d.defName).OrderBy(x => x)) + ":" + i.GetBaseCount())) +
            ";skill=" + string.Join(",", Recipe.skillRequirements.Select(s => s.skill.defName + ":" + s.minLevel)) +
            ";bills=" + string.Join(",", tables.Select(t => t.ThingID + ":" + string.Join("+", t.BillStack.Bills.Where(b => b.recipe == Recipe).Select(b => b.GetUniqueLoadID()))));
    }
    internal static bool Update()
    {
        if (finished) return true;
        if (failed) return false;
        try
        {
            Require(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                sent = true;
                RequireFixtures();
                Current.Game.CurrentMap = table.Map; Find.Selector.ClearSelection(); Find.Selector.Select(table);
                foreach (var name in new[] { "num_SunPlateSteel", "num_SplendidSteel", "num_Plasteel", "num_ComponentIndustrial", "num_ComponentSpacer", "num_SolarCrystal", "num_SolarFuel" })
                    AccessTools.Field(tabType, name).SetValue(null, 0);
                AccessTools.Field(tabType, "num_Plasteel").SetValue(null, 3 + round);
                AccessTools.Method(tabType, "TryAddResearchBill").Invoke(Activator.CreateInstance(tabType), null);
                Require(State() == before, "native submission mutated local state before command execution");
                Log.Message("RESEARCH DISPATCH round=" + round + " native TryAddResearchBill rollback=True");
            }
            Require(Time.realtimeSinceStartup < deadline, "research timeout");
            if (TickPatch.Timer - start < 350) return false;
            var resources = (Dictionary<ThingDef, int>)Get("tmpResources");
            Require(resources[ThingDefOf.Plasteel] == 3 + round, "resources missing");
            Require(Recipe.skillRequirements.Single().minLevel == 6 + round, "native skill requirement lost");
            Require(Recipe.ingredients.Count == 2 && Recipe.ingredients.Single(i => i.filter.Allows(ThingDefOf.Plasteel)).GetBaseCount() == 3 + round, "ingredients differ");
            Require(table.BillStack.Bills.Count(b => b.recipe == Recipe) == 1 && otherTable.BillStack.Bills.All(b => b.recipe != Recipe), "native replacement of other table bills failed");
            string path = Path.Combine(Root, Peer + ".research" + round), other = Path.Combine(Root, (client ? "host" : "client") + ".research" + round);
            if (!File.Exists(path)) { File.WriteAllText(path, State()); Log.Message("RESEARCH ASSERT round=" + round + " " + State()); }
            if (!File.Exists(other)) return false;
            Require(File.ReadAllText(path) == File.ReadAllText(other), "research peer state mismatch");
            if (round == 2) { finished = true; Log.Message("RESEARCH ACTIONS_PASS rounds=3"); return true; }
            if (client && !advance) { advance = true; prepare.DoSync(null, round + 1); }
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED RESEARCH " + e); }
        return false;
    }
}
