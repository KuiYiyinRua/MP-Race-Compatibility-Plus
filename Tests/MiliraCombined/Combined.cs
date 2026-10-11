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

internal static class MiliraCombined
{
    static bool client, requested, prepared, sent, failed, finished, advance;
    static int round, start;
    static float deadline;
    static ISyncMethod prepare;
    static readonly List<ThingComp> switches = new List<ThingComp>();
    static ThingComp support, funnel;
    static Pawn actor;
    static string baseline;
    static readonly string[] toggles = { "fireAtWill", "focusFire", "autoFireAtFocus", "autoFire", "onlyAutoFireWhileDraft", "droneWorkEnabled" };
    static bool[] expected, expectedSwitches;
    static int selected;
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static object Get(object o, string n) => AccessTools.Field(o.GetType(), n).GetValue(o);
    static void Set(object o, string n, object value) => AccessTools.Field(o.GetType(), n).SetValue(o, value);
    static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
    internal static void Init(Harmony h)
    {
        client = GenCommandLine.CommandLineArgPassed("twclient");
        prepare = MP.RegisterSyncMethod(typeof(MiliraCombined), nameof(Prepare));
    }
    static ThingComp MakeWeapon(Map map, string className)
    {
        var def = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsWeapon && d.modContentPack.PackageIdPlayerFacing == "sleepycot.wingsofdemocracy" && d.comps != null &&
            d.comps.Any(c => c.compClass.FullName == className)).OrderBy(d => d.defName).FirstOrDefault();
        if (def == null) { Log.Message("COMBINED INACTIVE component=" + className + " activeWeaponDefs=0"); return null; }
        var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, mustBeCapableOfViolence: true));
        GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 16), map);
        pawn.equipment.DestroyAllEquipment();
        var weapon = (ThingWithComps)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
        pawn.equipment.AddEquipment(weapon);
        pawn.drafter.Drafted = true;
        Log.Message("COMBINED FIXTURE " + className + " def=" + def.defName + " pawn=" + pawn.ThingID);
        return weapon.AllComps.Single(c => c.GetType().FullName == className);
    }
    public static void Prepare(int n)
    {
        round = n; sent = advance = false;
        var map = Find.Maps.First(m => m.IsPlayerHome);
        if (actor == null)
        {
            switches.Add(MakeWeapon(map, "AncotLibrary.CompRangeWeaponVerbSwitch"));

            Require(switches.All(c => c != null), "active Wings weapon missing");
            support = MakeWeapon(map, "PLAMilira.Comp_ChangeSupport");
            actor = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopIII_ArsenalCarrier"), Faction.OfPlayer);
            GenSpawn.Spawn(actor, CellFinder.RandomClosewalkCellNear(map.Center, map, 16), map);
            var all = actor.AllComps.Concat(actor.apparel.WornApparel.SelectMany(a => a.AllComps))
                .Concat(actor.equipment.AllEquipmentListForReading.SelectMany(e => e.AllComps));
            funnel = all.FirstOrDefault(c => c.GetType().FullName == "FunnelBit.CompTurretGun_FunnelBit");
            Require(funnel != null, "Fianchetto has no native funnel comp");
            actor.drafter.Drafted = true;
        }
        expectedSwitches = switches.Select(c => !(bool)Get(c, "switched")).ToArray();
        expected = toggles.Select(t => !(bool)Get(funnel, t)).ToArray();
        selected = support == null ? -1 : (round + 1) % ((IList)AccessTools.Property(support.GetType(), "SupportLists").GetValue(support)).Count;
        baseline = State();
        start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; prepared = true;
        Log.Message("COMBINED PREPARE round=" + n + " state=" + baseline);
    }
    static string State() => string.Join(";", switches.Select(c => c.parent.ThingID + ":" + Get(c, "switched") + ":" +
        string.Join(",", c.parent.GetComp<CompEquippable>().AllVerbs.Select(v => v.verbProps.label + "/" + v.verbProps.defaultProjectile?.defName)))) +
        ";support=" + (support == null ? "inactive" : Get(support, "choice")) + ";funnel=" + string.Join(",", toggles.Select(t => Get(funnel, t).ToString()));
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
                foreach (var comp in switches) ((IEnumerable<Gizmo>)AccessTools.Method(comp.GetType(), "GetWeaponGizmos").Invoke(comp, null)).OfType<Command>().Single().ProcessInput(null);
                if (support != null) {
                var action = (Command_Action)support.CompGetGizmosExtra().Single();
                action.action();
                var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                menu.Close(false);
                action.action();
                menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                ((List<FloatMenuOption>)Get(menu, "options"))[selected].action();
                menu.Close(false);
                }
                var layout = Activator.CreateInstance(AccessTools.TypeByName("FunnelBit.Gizmo_FunnelBitLayout"));
                Set(layout, "pawn", actor); Set(layout, "launcher", actor); Set(layout, "weapon", funnel.parent); Set(layout, "turretComp", funnel);
                var toggle = AccessTools.Method(layout.GetType(), "ToggleFireAtWill");
                var enumType = AccessTools.TypeByName("FunnelBit.FunnelGizmoToggleTarget");
                foreach (int value in new[] { 2, 3, 4, 5, 6, 7 }) toggle.Invoke(layout, new[] { Enum.ToObject(enumType, value) });
                Require(State() == baseline, "native UI mutated local state before synchronized execution");
                Log.Message("COMBINED DISPATCH round=" + round + " native switches/support reopen/six funnel toggles");
            }
            Require(Time.realtimeSinceStartup < deadline, "action timeout round=" + round);
            if (TickPatch.Timer - start < 400) return false;
            for (int i = 0; i < switches.Count; i++) Require((bool)Get(switches[i], "switched") == expectedSwitches[i], "switch did not toggle exactly once");
            Require(support == null || (int)Get(support, "choice") == selected, "support selection absent");
            for (int i = 0; i < toggles.Length; i++) Require((bool)Get(funnel, toggles[i]) == expected[i], "funnel toggle " + toggles[i]);
            var path = Path.Combine(Root, Peer + ".combined" + round);
            if (!File.Exists(path)) { File.WriteAllText(path, State()); Log.Message("COMBINED ASSERT round=" + round + " " + State()); }
            var other = Path.Combine(Root, (client ? "host" : "client") + ".combined" + round);
            if (!File.Exists(other)) return false;
            Require(File.ReadAllText(path) == File.ReadAllText(other), "peer state mismatch");
            if (round == 2) { finished = true; Log.Message("COMBINED ACTIONS_PASS rounds=3"); return true; }
            if (client && !advance) { advance = true; prepare.DoSync(null, round + 1); }
        }
        catch (Exception e) { failed = true; File.WriteAllText(Path.Combine(Root, Peer + ".failed"), e.ToString()); Log.Error("COMBINED FAILED " + e); }
        return false;
    }
}
