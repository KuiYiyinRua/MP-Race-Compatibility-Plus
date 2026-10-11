using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class CodexProbeComponent : GameComponent
{
    public CodexProbeComponent(Game game) { }
    public override void GameComponentUpdate() => CodexProbe.Update();
}

[StaticConstructorOnStartup]
public static class CodexProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("monolyncodexprobe");
    static readonly bool combined = GenCommandLine.CommandLineArgPassed("monolyncombined");
    static int Stages => 7;
    static readonly bool client = GenCommandLine.CommandLineArgPassed("monolynclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static bool hosted, ready, prepared, sent, failed, done, dispatched, verifying, staging, gearSent, openSent;
    static bool menuDiagnostic;
    static int stage, verified = -1, next, finish = -1;
    static string expectedMechWeapon;
    static Pawn wearer, mech;
    static Apparel codex;
    static ThingComp storage;
    static ThingWithComps knife;
    static ISyncMethod prepare, verify, gear, open;
    static int preparedTick;
    static IList Weapons => (IList)AccessTools.Field(storage.GetType(), "weapons").GetValue(storage);
    static HediffDef Marker => DefDatabase<HediffDef>.GetNamed("MNArsenalCodex");
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Receipt(string name, string value) => File.WriteAllText(Path.Combine(root, name), value);
    static void Fail(Exception e) { failed = true; Receipt(Peer + ".failed", e.ToString()); Log.Error("CODEX_PROBE FAILED " + e); }

    static CodexProbe()
    {
        if (!enabled) return;
        open = MP.RegisterSyncMethod(typeof(CodexProbe), nameof(OpenNative));
        prepare = MP.RegisterSyncMethod(typeof(CodexProbe), nameof(Prepare));
        verify = MP.RegisterSyncMethod(typeof(CodexProbe), nameof(Verify));
        gear = MP.RegisterSyncMethod(typeof(CodexProbe), nameof(PrepareGear));
        var h = new Harmony("local.meow.codex-probe");
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(CodexProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(UIRoot_Play), "UIRootOnGUI"), postfix: new HarmonyMethod(typeof(CodexProbe), nameof(OnGUI)));
        h.Patch(AccessTools.Method(typeof(RimWorld.Planet.WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(CodexProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(CodexProbe), nameof(Unseed)));
        h.Patch(AccessTools.Method(typeof(MapGenerator), nameof(MapGenerator.GenerateMap)), prefix: new HarmonyMethod(typeof(CodexProbe), nameof(MapSize)));
    }
    static void Seed() => Rand.PushState(261013);
    static void Unseed() => Rand.PopState();
    static void MapSize(ref IntVec3 __0) => __0 = new IntVec3(80, 1, 80);
    static string Snapshot() => Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" +
        string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState)) +
        (prepared ? ";" + State() : "");
    static string State() => "wearer=" + wearer.ThingID + ":" + wearer.equipment.Primary?.ThingID +
        ";stored=" + string.Join(",", Weapons.Cast<Thing>().Select(t => t.ThingID)) +
        ";knife=" + knife.ThingID + ":" + knife.Spawned + ":" + knife.Destroyed +
        ";codex=" + codex.ThingID + ":" + codex.Spawned +
        ";marker=" + string.Join(",", wearer.health.hediffSet.hediffs.Where(h => h.def == Marker).Select(h => h.GetUniqueLoadID())) +
        ";mech=" + mech.ThingID + ":" + mech.equipment.Primary?.def.defName + ":" + mech.equipment.Primary?.ThingID;

    public static void Prepare()
    {
        var map = Find.Maps[0];
        wearer = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Monolyn_BasicStartPawn"), Faction.OfPlayer,
            forcedXenotype: DefDatabase<XenotypeDef>.GetNamed("MonolynXenotype")));
        GenSpawn.Spawn(wearer, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
        foreach (var old in wearer.apparel.WornApparel.Where(a => a.AllComps.Any(c => c.GetType().FullName == "ASEL.CompWeaponCodexStorage")).ToArray())
        { wearer.apparel.Remove(old); old.Destroy(); }
        if (wearer.equipment.Primary != null) { var old = wearer.equipment.Primary; wearer.equipment.Remove(old); old.Destroy(); }
        codex = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MonolynWeaponCodex"));
        wearer.apparel.Wear(codex);
        storage = codex.AllComps.Single(c => c.GetType().FullName == "ASEL.CompWeaponCodexStorage");
        knife = (ThingWithComps)ThingMaker.MakeThing(ThingDefOf.MeleeWeapon_Knife, ThingDefOf.Steel);
        wearer.equipment.AddEquipment(knife);
        foreach (var name in new[] { "MNWeaponCodex", "ChangeCrossbow" }) wearer.abilities.GainAbility(DefDatabase<AbilityDef>.GetNamed(name));
        mech = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("MNC_Excavator"), Faction.OfPlayer);
        GenSpawn.Spawn(mech, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
        Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("MNConstructII"), false, null, false);
        wearer.drafter.Drafted = true;
        mech.drafter.Drafted = true;
        preparedTick = TickPatch.Timer;
        prepared = true; next = TickPatch.Timer + 100;
        Log.Message("CODEX_PROBE PREPARED " + State());
    }
    public static void PrepareGear(int value)
    {
        wearer.drafter.Drafted = true; mech.drafter.Drafted = true;
        if (value % 7 == 0)
        {
            var map = Find.Maps[(value / 7) % Find.Maps.Count];
            foreach (var pawn in new[] { wearer, mech })
                if (pawn.Map != map) { pawn.DeSpawn(); GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map); }
            if (codex.Spawned) { codex.DeSpawn(); wearer.apparel.Wear(codex); }
            if (knife.Spawned) { knife.DeSpawn(); wearer.equipment.AddEquipment(knife); }
        }
        if (value % 7 == 5)
        {
            Apparel dropped;
            Check(wearer.apparel.TryDrop(codex, out dropped, wearer.Position), "Cannot put codex on ground for its native eject gizmo");
        }
        if (value % 7 == 6) expectedMechWeapon = mech.equipment.Primary?.def.defName == "Gun_ExcavatorTurretB" ? "Gun_ExcavatorTurret" : "Gun_ExcavatorTurretB";
        staging = true;
    }
    static Ability Ability(Pawn pawn, string name) => pawn.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed(name));
    public static void OpenNative(Pawn pawn, string name)
    {
        Check(MP.IsExecutingSyncCommand, "Native menu must execute in replicated command");
        var comp = Ability(pawn, name).comps.OfType<CompAbilityEffect>().Single(c => c.GetType().Name.StartsWith("CompAbilityEffect_WeaponCodex"));
        comp.Apply(pawn, pawn);
        Log.Message("CODEX_PROBE OPEN_NATIVE pawn=" + pawn.ThingID + " issuer=" + MP.IsExecutingSyncCommandIssuedBySelf);
    }
    static FloatMenu Open(Pawn pawn, string name)
    {
        Current.Game.CurrentMap = pawn.Map;
        Find.Selector.ClearSelection(); Find.Selector.Select(pawn, false, false);
        var comp = Ability(pawn, name).comps.OfType<CompAbilityEffect>().Single(c => c.GetType().Name.StartsWith("CompAbilityEffect_WeaponCodex"));
        var before = Snapshot();
        comp.Apply(pawn, pawn);
        Check(Snapshot() == before, "Opening native codex menu mutated local state/Rand");
        return Find.WindowStack.Windows.OfType<FloatMenu>().Last();
    }
    static FloatMenuOption Choice(FloatMenu menu, string method, string field = null, object value = null)
    {
        var options = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
        var matching = options.Where(o => o.action != null && o.action.Method.Name == method &&
            (field == null || (value is Ability ability
                ? (AccessTools.Field(o.action.Target.GetType(), field).GetValue(o.action.Target) as Ability)?.def == ability.def
                : ReferenceEquals(AccessTools.Field(o.action.Target.GetType(), field).GetValue(o.action.Target), value)))).ToArray();
        if (matching.Length == 0)
        {
            var detail = string.Join(" | ", options.Select(o => o.Label + ":" + o.action?.Method.Name));
            foreach (var pawn in new[] { wearer, mech })
                foreach (var a in pawn.abilities.AllAbilitiesForReading.Where(a => a.def.defName == "ChangeCrossbow" || a.def.defName.StartsWith("MNChange")))
                { string reason; Log.Message("CODEX_PROBE AVAIL pawn=" + pawn.ThingID + " ability=" + a.def.defName + " disabled=" + a.GizmoDisabled(out reason) + " reason=" + reason + " downed=" + pawn.Downed); }
            throw new InvalidOperationException("Native menu option missing: " + method + "; offered=" + detail);
        }
        return matching[0];
    }
    public static void OnGUI()
    {
        if (enabled && client && openSent && !dispatched && !menuDiagnostic && TickPatch.Timer > next + 500)
        {
            menuDiagnostic = true;
            var component = Current.Game.components.FirstOrDefault(c => c.GetType().Name == "MonolynCodexMenuComponent");
            var queue = component == null ? null : AccessTools.Field(component.GetType(), "pending").GetValue(component);
            Log.Message("CODEX_PROBE MENU_WAIT interface=" + MP.InInterface + " staging=" + staging + " prepared=" + prepared + " frozen=" + TickPatch.Frozen + " component=" + component + " pending=" + (queue == null ? "null" : AccessTools.Property(queue.GetType(), "Count").GetValue(queue)) + " windows=" + string.Join(",", Find.WindowStack.Windows.Select(w => w.GetType().FullName)));
        }
        if (!enabled || !client || failed || done || !prepared || stage >= Stages || dispatched || !staging ||
            TickPatch.Timer < next || !MP.InInterface || TickPatch.Frozen || Event.current.type != EventType.Repaint) return;
        try
        {
            var phase = stage % 7;
            FloatMenu menu;
            if (combined && phase != 5)
            {
                var pawn = phase == 6 ? mech : wearer;
                if (!openSent)
                {
                    Current.Game.CurrentMap = pawn.Map;
                    Find.Selector.ClearSelection(); Find.Selector.Select(pawn, false, false);
                    openSent = open.DoSync(null, pawn, phase == 6 ? "MNWeaponCodexMech" : "MNWeaponCodex");
                    menuDiagnostic = false;
                    return;
                }
                menu = Find.WindowStack.Windows.OfType<FloatMenu>().LastOrDefault();
                if (menu == null) return;
            }
            dispatched = true;
            if (phase == 5)
            {
                var command = storage.CompGetGizmosExtra().OfType<Command_Action>().Single(); command.action();
                menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                Choice(menu, "<CompGetGizmosExtra>b__1").Chosen(false, menu);
            }
            else
            {
                menu = combined ? Find.WindowStack.Windows.OfType<FloatMenu>().Last() : Open(phase == 6 ? mech : wearer, phase == 6 ? "MNWeaponCodexMech" : "MNWeaponCodex");
                if (phase == 0 || phase == 4) Choice(menu, "<Apply>b__0").Chosen(false, menu);
                else if (phase == 1 || phase == 3) Choice(menu, "<Apply>b__4", "twc", knife).Chosen(false, menu);
                else if (phase == 2) Choice(menu, "<Apply>b__3", "ability", Ability(wearer, "ChangeCrossbow")).Chosen(false, menu);
                else Choice(menu, "<Apply>b__2", "ability", Ability(mech, expectedMechWeapon == "Gun_ExcavatorTurret" ? "MNChangeBeamCannon" : "MNChangeRepeater")).Chosen(false, menu);
            }
            menu.Close(false);
            next = TickPatch.Timer + 250;
            Log.Message("CODEX_PROBE DISPATCH stage=" + stage);
        }
        catch (Exception e) { Fail(e); }
    }
    public static void Verify(int value)
    {
        switch (value % 7)
        {
            case 0: case 4: Check(wearer.equipment.Primary == null && Weapons.Contains(knife), "Store did not transfer original weapon"); break;
            case 1: case 3: Check(wearer.equipment.Primary == knife && !Weapons.Contains(knife), "Retrieve did not equip original weapon"); break;
            case 2: Check(wearer.equipment.Primary?.def.defName == "Dogma_Crossbow" && Weapons.Contains(knife), "Dogma switch did not preserve stored weapon"); break;
            case 5: Check(knife.Spawned && !knife.Destroyed && !Weapons.Contains(knife), "Ground eject lost/duplicated weapon"); break;
            case 6: Check(mech.equipment.Primary?.def.defName == expectedMechWeapon, "Mech native switch job did not finish"); break;
        }
        Check(Weapons.Cast<Thing>().Distinct().Count() == Weapons.Count, "Duplicated stored weapon");
        Log.Message("CODEX_PROBE ASSERT stage=" + value + " " + State());
        verified = value;
        if (value == Stages - 1) finish = TickPatch.Timer + 1000;
    }
    public static void Update()
    {
        if (!enabled) return;
        if (File.Exists(Path.Combine(root, "quit"))) { Application.Quit(); return; }
        if (failed || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            if (!client && !hosted && !MP.IsInMultiplayer)
            {
                if (GenCommandLine.CommandLineArgPassed("monolyntwomaps"))
                {
                    var first = Find.CurrentMap; var tiles = new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(first.Tile, tiles);
                    var tile = tiles.OrderBy(t => t.tileId).First(t => !Find.WorldObjects.AnyMapParentAt(t) && Find.WorldPathGrid.Passable(t));
                    var parent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement); parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                    MapGenerator.GenerateMap(new IntVec3(80, 1, 80), parent, first.generatorDef); Current.Game.CurrentMap = first;
                }
                hosted = true; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; Mp.username = "CodexHost";
                Check(HostWindow.HostProgrammatically(new ServerSettings { gameName = "Codex", direct = true, directAddress = "127.0.0.1:31245", lan = false, steam = false,
                    multifaction = false, debugMode = combined, asyncTime = combined, syncConfigs = false, pauseOnJoin = false, autoJoinPoint = 0, autosaveInterval = 0, desyncTraces = combined }), "Host failed"); return;
            }
            if (!MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
            Check(!Mp.session.desynced, "Desync");
            if (!ready)
            {
                if (!client && !TickPatch.Frozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                if (!TickPatch.Frozen) return;
                Receipt(Peer + ".ready", Snapshot());
                if (!File.Exists(Path.Combine(root, "host.ready")) || !File.Exists(Path.Combine(root, "client.ready"))) return;
                Check(File.ReadAllText(Path.Combine(root, "host.ready")) == File.ReadAllText(Path.Combine(root, "client.ready")), "BASELINE_LOAD_DRIFT");
                ready = true;
                if (!client) { Mp.Client.Send(new ClientFreezePacket(false)); Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)TimeSpeed.Superfast);
                    foreach (var map in Find.Maps) Mp.Client.SendCommand(CommandType.MapTimeSpeed, map.uniqueID, (byte)TimeSpeed.Superfast); }
            }
            if (finish >= 0 && TickPatch.Timer >= finish)
            {
                if (!TickPatch.Frozen) { if (!client) Mp.Client.Send(new ClientFreezePacket(true)); return; }
                Check(verified == Stages - 1, "Incomplete codex actions"); Receipt(Peer + ".complete", Snapshot()); done = true; return;
            }
            if (TickPatch.Frozen) return;
            if (!prepared) { if (client && !sent) sent = prepare.DoSync(null); return; }
            if (client && stage >= Stages) return;
            if (client && !dispatched && !staging) { if (!gearSent) gearSent = gear.DoSync(null, stage); return; }
            if (client && dispatched && TickPatch.Timer >= next && !verifying) verifying = verify.DoSync(null, stage);
            if (client && dispatched && verified == stage) { stage++; dispatched = verifying = staging = gearSent = openSent = false; next = TickPatch.Timer + 50; }
        }
        catch (Exception e) { Fail(e); }
    }
}
