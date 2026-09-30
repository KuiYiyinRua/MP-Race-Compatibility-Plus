using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
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

public sealed class SettlementTradeState : GameComponent
{
    public SettlementTradeState(Game g) { }
    public override void GameComponentUpdate() => SettlementTradeProbe.Update();
}

[StaticConstructorOnStartup]
public static class SettlementTradeProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("settlementprobe");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("stclient");
    static readonly bool old = GenCommandLine.CommandLineArgPassed("stold");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static Type patch;
    static ISyncMethod prepare;
    static bool hosted, ready, sent, opened, clicked, drawing, failed, done;
    static int round = -1, clickTick, executedTick = -1, transferCount, nextCheckpoint;
    static int finishAt = -1, startTick;
    static float began = Time.realtimeSinceStartup, deadline;
    static Caravan caravan;
    static Settlement settlement;
    static readonly List<Thing> targets = new List<Thing>();
    static Thing selected, actual;
    static string unrelatedStock;
    static int regenCount;
    static int TargetRounds => old ? 4 : 16;
    static bool Selling => !old && round % 2 == 1;
    static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
    static string Marker(string peer, int n) => Path.Combine(root, peer + ".assert" + n);
    static string Snapshot() => Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" +
        string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState));
    static string Goods() => string.Join(";", settlement.Goods.OrderBy(t => t.thingIDNumber).Select(t => t.ThingID + ":" + t.stackCount));
    static string Unrelated() => string.Join(";", settlement.Goods.Where(t => !targets.Contains(t) && t.def != ThingDefOf.Silver).OrderBy(t => t.thingIDNumber).Select(t => t.ThingID + ":" + t.stackCount));
    static void Seed() => Rand.PushState(260930);
    static void Unseed() => Rand.PopState();
    static readonly PropertyInfo randState = AccessTools.Property(typeof(Rand), "StateCompressed");
    static void State(string where) => Log.Message("SETTLEMENT STATE " + where + " round=" + round + " shared=" + TickPatch.Timer + " rand=" + randState.GetValue(null) + " world=" + Mp.AsyncWorldTime.randState);
    static void BeforeExecute() { if (enabled) State("execute-in"); }
    static void AfterExecute() { if (enabled) State("execute-out"); }

    static SettlementTradeProbe()
    {
        if (!enabled) return;
        patch = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_TradeExecutionSnapshot");
        prepare = MP.RegisterSyncMethod(typeof(SettlementTradeProbe), nameof(Prepare));
        fixture = MP.RegisterSyncMethod(typeof(SettlementTradeProbe), nameof(Fixture));
        var h = new Harmony("local.meow.settlement.trade.probe");
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Unseed)));
        h.Patch(AccessTools.Method(typeof(Dialog_Trade), "DoWindowContents"), prefix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(BeforeDraw)), finalizer: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(AfterDraw)));
        h.Patch(AccessTools.Method(typeof(Widgets), "ButtonText", new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) }), prefix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Button)));
        h.Patch(AccessTools.Method(patch, "SyncExecuteCanonicalTrade"), postfix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Executed)));
        h.Patch(AccessTools.Method(patch, "SyncExecuteCanonicalTrade"), prefix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(BeforeExecute)), finalizer: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(AfterExecute)));
        h.Patch(AccessTools.Method(typeof(Tradeable), "ResolveTrade"), prefix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Transfer)));
        h.Patch(AccessTools.DeclaredMethod(typeof(Tradeable_Pawn), "ResolveTrade"), prefix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Transfer)));
        h.Patch(AccessTools.Method(typeof(Settlement_TraderTracker), "RegenerateStock"), postfix: new HarmonyMethod(typeof(SettlementTradeProbe), nameof(Regenerated)));
        Log.Message("SETTLEMENT START peer=" + Peer + " version=" + patch.Assembly.GetName().Version + " mvid=" + patch.Module.ModuleVersionId + " hash=" + File.ReadAllText(Path.Combine(root, "candidate.sha256")));
    }

    static void Regenerated() { if (enabled && ready) regenCount++; }
    static Thing Furniture(string defName)
    {
        Thing inner = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName), ThingDefOf.Steel);
        inner.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
        return inner.MakeMinified();
    }
    static Thing Pack(string gene)
    {
        var p = (Genepack)ThingMaker.MakeThing(ThingDefOf.Genepack);
        var def = DefDatabase<GeneDef>.GetNamed(gene); Require(def != null, "missing fixture gene " + gene);
        p.Initialize(new List<GeneDef> { def });
        return p;
    }
    static Thing Book(string title)
    {
        var b = (Book)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Novel"));
        b.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
        b.GenerateBook(null, 3600000);
        AccessTools.Field(typeof(Book), "title").SetValue(b, title);
        return b;
    }

    public static void Prepare(int n)
    {
        State("prepare-in");
        Require(!failed, "previous assertion failed");
        foreach (var t in Mp.WorldComp.trading.ToArray()) Mp.WorldComp.RemoveTradeSession(t);
        Find.WindowStack.TryRemove(typeof(TradingWindow), false);
        if (caravan == null)
        {
            settlement = Find.WorldObjects.Settlements.First(s => s.Faction != null && !s.Faction.IsPlayer && !s.Faction.HostileTo(Faction.OfPlayer) && !s.HasMap && s.CanTradeNow && s.Visitable && s.TraderKind.WillTrade(ThingDefOf.Genepack));
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, validatorPostGear: p => !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled));
            var money = ThingMaker.MakeThing(ThingDefOf.Silver); money.stackCount = 50000;
            Require(pawn.inventory.innerContainer.TryAdd(money), "fixture funds");
            var food = ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack); food.stackCount = 200;
            Require(pawn.inventory.innerContainer.TryAdd(food), "fixture food");
            caravan = CaravanMaker.MakeCaravan(new[] { pawn }, Faction.OfPlayer, settlement.Tile, true);
            startTick = TickPatch.Timer;
        }
        round = n; sent = false; opened = false; clicked = false; transferCount = 0; actual = null; executedTick = -1;
        deadline = Time.realtimeSinceStartup + 90;
        if (n == TargetRounds) { finishAt = TickPatch.Timer + (old ? 1000 : 120000); nextCheckpoint = TickPatch.Timer + 10000; return; }
        Log.Message("SETTLEMENT PREPARE round=" + n + " tick=" + TickPatch.Timer + " trader=" + settlement.ID); State("prepare-out");
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
                hosted = true; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; Mp.username = "SettlementHost";
                Require(HostWindow.HostProgrammatically(new ServerSettings { gameName = "SettlementTrade", direct = true, directAddress = "127.0.0.1:30997", lan = false, steam = false, multifaction = false, asyncTime = false, syncConfigs = false, pauseOnJoin = false, autoJoinPoint = 0, autosaveInterval = 0, desyncTraces = old || GenCommandLine.CommandLineArgPassed("sttrace") }), "host rejected");
                return;
            }
            if (!MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
            Require(!Mp.session.desynced, "desync");
            if (!client && !ready && !TickPatch.serverFrozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
            if (client && !ready) { if (!TickPatch.Frozen) return; ready = true; File.WriteAllText(Path.Combine(root, "client.ready"), Snapshot()); Log.Message("SETTLEMENT CLIENT_LOADED_FROZEN " + Snapshot()); }
            if (Mp.session.players.Count != 2 || !File.Exists(Path.Combine(root, "client.ready"))) return;
            if (!client && !ready)
            {
                Require(Snapshot() == File.ReadAllText(Path.Combine(root, "client.ready")), "BASELINE_LOAD_DRIFT");
                Log.Message("SETTLEMENT BASELINE " + Snapshot()); ready = true; Mp.Client.Send(new ClientFreezePacket(false));
                Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)TimeSpeed.Superfast);
                foreach (var m in Find.Maps) Mp.Client.SendCommand(CommandType.MapTimeSpeed, m.uniqueID, (byte)TimeSpeed.Superfast);
            }
            if (TickPatch.Frozen) return;
            if (round == -1) { if (client && !sent) sent = prepare.DoSync(null, 0); return; }
            if (round == TargetRounds)
            {
                if (TickPatch.Timer >= nextCheckpoint) { Log.Message("SETTLEMENT CHECKPOINT tick=" + TickPatch.Timer + " target=" + finishAt + " players=2 desynced=False"); nextCheckpoint += 10000; }
                if (TickPatch.Timer >= finishAt) { Require(Mp.WorldComp.trading.Count == 0, "unclean sessions"); done = true; File.WriteAllText(Path.Combine(root, Peer + ".complete"), "PASS rounds=" + TargetRounds + " ticks=" + (TickPatch.Timer - startTick)); Log.Message("SETTLEMENT COMPLETE tick=" + TickPatch.Timer + " desynced=False"); }
                return;
            }
            Require(Time.realtimeSinceStartup < deadline, "action timeout round=" + round);
            if (client && !opened)
            {
                opened = true; Find.World.renderer.wantedMode = WorldRenderMode.Planet;
                Require(CaravanVisitUtility.SettlementVisitedNow(caravan) == settlement, "not visiting");
                ((Command_Action)CaravanVisitUtility.TradeCommand(caravan, settlement.Faction, settlement.TraderKind)).action();
                Log.Message("SETTLEMENT REAL_OPEN round=" + round);
            }
            if (client && File.Exists(Marker("host", round)) && File.Exists(Marker("client", round)) && !sent)
            {
                Require(File.ReadAllText(Marker("host", round)) == File.ReadAllText(Marker("client", round)), "receipt mismatch");
                selected = null; clickTick = 0; sent = prepare.DoSync(null, round + 1);
                return;
            }
            if (Mp.WorldComp.trading.Count == 0) return;
            if (targets.Count == 0 || selected == null)
            {
                // Fixture insertion is synchronized separately; never mutate shared stock from UI.
                if (client && !sent) sent = fixture.DoSync(null);
                return;
            }
            if (client && !clicked && Find.WindowStack.IsOpen<TradingWindow>() && clickTick == 0)
            {
                var s = Mp.WorldComp.trading[0]; s.CloseWindow(false); s.OpenWindow(false); clickTick = TickPatch.Timer + 30;
                Log.Message("SETTLEMENT CLOSE_REOPEN round=" + round);
            }
            string clickMarker = Path.Combine(root, "click" + round);
            if (old && round < 3 && File.Exists(clickMarker) && TickPatch.Timer >= int.Parse(File.ReadAllText(clickMarker)) + 50 && !File.Exists(Marker(Peer, round)))
            {
                Require(executedTick < 0 && transferCount == 0, "old duplicate unexpectedly executed");
                Receipt("REPRO_DUPLICATE selected=" + selected.ThingID + " goods=" + Goods());
            }
            Require(Time.realtimeSinceStartup - began < 5400, "overall timeout");
        }
        catch (Exception e) { Fail(e); }
    }

    static ISyncMethod fixture;

    public static void Fixture()
    {
        State("fixture-in");
        var s = Mp.WorldComp.trading[0];
        if (round == 0)
        {
            MpTradeSession.SetTradeSession(s);
            try
            {
                var key = AccessTools.Method(patch, "BuildTradeableKey");
                var collisions = s.deal.AllTradeables.Where(t => !t.IsCurrency).GroupBy(t => (string)key.Invoke(null, new object[] { t })).Where(g => g.Count() > 1);
                foreach (var group in collisions) Log.Message("SETTLEMENT NATURAL_COLLISION " + string.Join(";", group.Select(t => t.AnyThing.ThingID + ":" + t.AnyThing.LabelNoCount)));
            }
            finally { MpTradeSession.SetTradeSession(null); }
        }
        if (targets.Count == 0 || old)
        {
            targets.Clear();
            if (round == 0 || !old) { targets.Add(Furniture("SculptureSmall")); targets.Add(Furniture("Bed")); }
            if (round == 1 || !old) { targets.Add(Pack("Hair_BaldOnly")); targets.Add(Pack("Hair_LongOnly")); }
            if (round == 2 || !old) { targets.Add(Book("Fixture Alpha")); targets.Add(Book("Fixture Beta")); }
            if (round == 3 && old) { var t = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); t.stackCount = 7; targets.Add(t); }
            foreach (var t in targets) Require(settlement.trader.GetDirectlyHeldThings().TryAdd(t), "stock fixture insertion");
        }
        selected = targets[old ? 0 : round / 2];
        MpTradeSession.SetTradeSession(s);
        try
        {
            s.deal.recacheTrader = true; s.deal.recacheColony = true; s.deal.Recache();
            if (!old && targets.Count == 6)
            {
                // TraderWillTrade requires the active TradeSession context.
                targets.Add(s.deal.AllTradeables.First(t => t.TraderWillTrade && t.thingsTrader.Count == 1 && t.thingsTrader[0] is Pawn p && p.RaceProps.Animal).thingsTrader[0]);
                targets.Add(s.deal.AllTradeables.First(t => t.TraderWillTrade && t.thingsTrader.Count == 1 && t.thingsTrader[0] is MinifiedThing && t.thingsTrader[0].stackCount == 1 && !targets.Contains(t.thingsTrader[0])).thingsTrader[0]);
            }
        }
        finally { MpTradeSession.SetTradeSession(null); }
        unrelatedStock = Unrelated(); regenCount = 0; sent = false;
        Log.Message("SETTLEMENT FIXTURE round=" + round + " target=" + selected.ThingID + ":" + selected.def.defName);
        State("fixture-out");
    }

    static void BeforeDraw()
    {
        drawing = client && ready && !clicked && selected != null && clickTick > 0 && TickPatch.Timer >= clickTick;
        if (!drawing) return;
        try
        {
            foreach (var t in TradeSession.deal.AllTradeables) t.ForceTo(0);
            var row = TradeSession.deal.AllTradeables.First(t => (Selling ? t.thingsColony : t.thingsTrader).Contains(selected));
            Require(row.TraderWillTrade, "fixture not tradeable"); row.ForceTo(Selling ? -1 : 1);
            TradeSession.deal.UpdateCurrencyCount(); Log.Message("SETTLEMENT SELECT round=" + round + " target=" + selected.ThingID + " count=" + row.CountToTransfer);
        }
        catch (Exception e) { drawing = false; Fail(e); }
    }
    static void AfterDraw() { drawing = false; }
    static bool Button(string label, ref bool __result)
    {
        if (!drawing || clicked || label != "AcceptButton".Translate().ToString() || Event.current.type != EventType.Repaint) return true;
        clicked = true; clickTick = TickPatch.Timer; File.WriteAllText(Path.Combine(root, "click" + round), clickTick.ToString()); __result = true; Log.Message("SETTLEMENT CLICK_NATIVE_ACCEPT round=" + round); return false;
    }
    static void Transfer(Tradeable __instance)
    {
        if (!enabled || !MP.IsExecutingSyncCommand || __instance.IsCurrency || __instance.CountToTransfer == 0) return;
        transferCount++; actual = (Selling ? __instance.thingsColony : __instance.thingsTrader).First();
        Require(ReferenceEquals(actual, selected), "wrong physical transfer");
        Log.Message("SETTLEMENT TRANSFER round=" + round + " id=" + actual.ThingID + " count=" + __instance.CountToTransfer);
    }
    static void Executed()
    {
        if (!enabled) return;
        try
        {
            executedTick = TickPatch.Timer;
            if (old)
            {
                Require(round == 3 && transferCount == 0 && !settlement.Goods.Contains(selected) && selected.Destroyed, "old regen did not destroy selected stock");
                Receipt("REPRO_REGEN selected=" + selected.ThingID + " regen=" + regenCount + " goods=" + Goods()); return;
            }
            Require(transferCount == 1 && actual == selected && Mp.WorldComp.trading.Count == 0, "native outcome transferCount=" + transferCount + " sessionCount=" + Mp.WorldComp.trading.Count + " selected=" + selected.ThingID);
            Require(regenCount == 0, "stock regenerated during acceptance");
            Require(Selling ? settlement.Goods.Contains(selected) : selected is Pawn pawn ? caravan.PawnsListForReading.Contains(pawn) : caravan.AllThings.Contains(selected), "wrong physical ownership");
            Require(unrelatedStock == Unrelated(), "unrelated stock changed");
            Receipt("PASS selected=" + selected.ThingID + " selling=" + Selling + " stock=" + Goods());
        }
        catch (Exception e) { Fail(e); }
    }
    static void Receipt(string proof)
    {
        File.WriteAllText(Marker(Peer, round), proof); Log.Message("SETTLEMENT ASSERT round=" + round + " " + proof);
    }
    static void Fail(Exception e) { failed = true; Log.Error("SETTLEMENT FAILED " + e); File.WriteAllText(Path.Combine(root, Peer + ".failed"), e.ToString()); }
}
