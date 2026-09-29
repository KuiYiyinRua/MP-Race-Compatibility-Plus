using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
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

public sealed class TradeWindowProbeState : GameComponent
{
    public TradeWindowProbeState(Game g) { }
    public override void GameComponentUpdate() => TradeWindowProbe.Update();
}

[StaticConstructorOnStartup]
public static class TradeWindowProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("tradewindowprobe");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static bool hosted, ready, failed, done, sent;
    static int round = -1, observed = -1, manual = -1, clicked = -1, finishAt = -1;
    static Caravan caravan;
    static Caravan otherCaravan;
    static Settlement settlement;
    static ISyncMethod prepare, startTravel;
    static float next, actionDeadline;
    static int nextCheckpoint;
    static int travelLogged = -1;
    static FieldInfo setting;
    static object settings;
    static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
    static string Snapshot() => Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" +
        string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState));
    static string Marker(string peer, string phase, int n) => Path.Combine(root, peer + "." + phase + n);
    static void Seed() => Rand.PushState(260926);
    static void Unseed() => Rand.PopState();

    static TradeWindowProbe()
    {
        if (!enabled) return;
        prepare = MP.RegisterSyncMethod(typeof(TradeWindowProbe), nameof(Prepare));
        startTravel = MP.RegisterSyncMethod(typeof(TradeWindowProbe), nameof(StartTravel));
        var h = new Harmony("local.meow.trade.window.probe");
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(TradeWindowProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(TradeWindowProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(TradeWindowProbe), nameof(Unseed)));
        h.Patch(AccessTools.Method(typeof(Caravan_PathFollower), "PatherTickInterval"), prefix: new HarmonyMethod(typeof(TradeWindowProbe), nameof(FastTravel)));
        h.Patch(AccessTools.PropertyGetter(typeof(Caravan), nameof(Caravan.NightResting)), postfix: new HarmonyMethod(typeof(TradeWindowProbe), nameof(TestTravelRest)));
        var mod = AccessTools.TypeByName("MP_MeowOnlineShop.MpMeowOnlineShopMod");
        settings = AccessTools.Property(mod, "Settings").GetValue(null);
        setting = AccessTools.Field(settings.GetType(), "tradeWindowOnlyForInitiator");
        Log.Message("TRADE_WINDOW START peer=" + Peer + " mvid=" + mod.Module.ModuleVersionId + " candidate=" + File.ReadAllText(Path.Combine(root, "candidate.sha256")));
    }

    public static void Prepare(int n)
    {
        foreach (var trade in Mp.WorldComp.trading.ToArray()) Mp.WorldComp.RemoveTradeSession(trade);
        Find.WindowStack.TryRemove(typeof(TradingWindow), false);
        if (caravan == null)
        {
            settlement = Find.WorldObjects.Settlements.First(s => s.Faction != null && !s.Faction.IsPlayer && !s.Faction.HostileTo(Faction.OfPlayer) && !s.HasMap && s.CanTradeNow && s.Visitable);
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                validatorPostGear: p => !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled));
            Require(!pawn.skills.GetSkill(SkillDefOf.Social).TotallyDisabled, "negotiator Social disabled");
            var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = 1000;
            pawn.inventory.innerContainer.TryAdd(silver);
            caravan = CaravanMaker.MakeCaravan(new[] { pawn }, Faction.OfPlayer, settlement.Tile, true);
        }
        round = n;
        actionDeadline = Time.realtimeSinceStartup + 30;
        if (n >= 4 && n <= 6)
        {
            var neighbors = new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(settlement.Tile, neighbors);
            caravan.Tile = neighbors.First(t => Find.WorldPathGrid.Passable(t) && caravan.CanReach(t));
        }
        if (n == 7)
        {
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, settlement.Faction, forceGenerateNewPawn: true));
            pawn.trader = new Pawn_TraderTracker(pawn) { traderKind = settlement.TraderKind };
            var goods = ThingMaker.MakeThing(ThingDefOf.Silver); goods.stackCount = 100;
            pawn.inventory.innerContainer.TryAdd(goods);
            otherCaravan = CaravanMaker.MakeCaravan(new[] { pawn }, settlement.Faction, settlement.Tile, true);
            Require(otherCaravan.CanTradeNow, "NPC caravan fixture cannot trade");
        }
        if (n == 10) { finishAt = TickPatch.Timer + 10000; nextCheckpoint = TickPatch.Timer + 2000; }
        Log.Message("TRADE_WINDOW PREPARE round=" + n + " tick=" + TickPatch.Timer + " caravan=" + caravan.ID + " settlement=" + settlement.ID);
    }

    public static void StartTravel()
    {
        Require(caravan.pather.StartPath(settlement.Tile, new CaravanArrivalAction_Trade(settlement)), "native StartPath rejected");
        Require(Mp.WorldComp.trading.Count == 0, "delayed arrival must not trade inline");
        Log.Message("TRADE_WINDOW ROUTE_ISSUED round=" + round + " mine=" + MP.IsExecutingSyncCommandIssuedBySelf);
    }

    static void OpenEncounterTrade()
    {
        Find.WindowStack.Add(new Dialog_Trade(caravan.PawnsListForReading[0], otherCaravan));
    }

    static void FastTravel(Caravan_PathFollower __instance)
    {
        // Test fixture acceleration on both peers. The native tick still performs
        // movement and PatherArrived outside any command/issuer scope.
        if (enabled && round >= 4 && round <= 6 && caravan != null && ReferenceEquals(__instance, caravan.pather))
        {
            __instance.nextTileCostLeft = 0;
            if (travelLogged != round)
            {
                travelLogged = round;
                Log.Message("TRADE_WINDOW TRAVEL_FIXTURE round=" + round + " moving=" + __instance.Moving + " paused=" + __instance.Paused + " cantMove=" + caravan.CantMove + " mass=" + caravan.ImmobilizedByMass + " downed=" + caravan.AllOwnersDowned + " mental=" + caravan.AllOwnersHaveMentalBreak);
                Require(!caravan.CantMove, "test caravan cannot move");
            }
        }
    }

    static void TestTravelRest(Caravan __instance, ref bool __result)
    {
        if (enabled && round >= 4 && round <= 6 && ReferenceEquals(__instance, caravan)) __result = false;
    }

    public static void Update()
    {
        if (!enabled) return;
        if (File.Exists(Path.Combine(root, "quit"))) { Application.Quit(); return; }
        if (failed || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting || Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 0.1f;
        try
        {
            if (!client && !hosted && !MP.IsInMultiplayer)
            {
                hosted = true; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Mp.username = "TradeWindowHost";
                Require(HostWindow.HostProgrammatically(new ServerSettings { gameName = "TradeWindow", direct = true, directAddress = "127.0.0.1:30996", lan = false, steam = false, multifaction = false, asyncTime = false, syncConfigs = false, pauseOnJoin = false, autoJoinPoint = 0, autosaveInterval = 0, desyncTraces = false }), "host rejected");
                return;
            }
            if (!MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
            Require(!Mp.session.desynced, "desync");
            if (!client && !ready && !TickPatch.serverFrozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
            if (client && !ready)
            {
                if (!TickPatch.Frozen) return;
                ready = true; File.WriteAllText(Path.Combine(root, "client.ready"), Snapshot());
            }
            if (Mp.session.players.Count != 2 || !File.Exists(Path.Combine(root, "client.ready"))) return;
            if (!client && !ready)
            {
                Require(Snapshot() == File.ReadAllText(Path.Combine(root, "client.ready")), "BASELINE_LOAD_DRIFT");
                ready = true; Mp.Client.Send(new ClientFreezePacket(false));
                Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)TimeSpeed.Superfast);
                foreach (var map in Find.Maps) Mp.Client.SendCommand(CommandType.MapTimeSpeed, map.uniqueID, (byte)TimeSpeed.Superfast);
            }
            if (TickPatch.Frozen) return;
            if (round == -1) { if (client && !sent) sent = prepare.DoSync(null, 0); return; }
            if (round == 10)
            {
                if (TickPatch.Timer >= nextCheckpoint)
                {
                    Log.Message("TRADE_WINDOW SHARED_CHECKPOINT tick=" + TickPatch.Timer + " target=" + finishAt + " players=" + Mp.session.players.Count + " desynced=" + Mp.session.desynced);
                    nextCheckpoint += 2000;
                }
                if (TickPatch.Timer >= finishAt)
                {
                    Require(Mp.WorldComp.trading.Count == 0, "sessions not cleaned"); done = true;
                    File.WriteAllText(Path.Combine(root, Peer + ".complete"), "10000 shared ticks; desynced=False");
                    Log.Message("TRADE_WINDOW COMPLETE tick=" + TickPatch.Timer + " desynced=False");
                }
                return;
            }
            // The preference is local presentation state; test disabled on both peers in round 3.
            setting.SetValue(settings, round != 3);
            if (!File.Exists(Marker(Peer, "ready", round)))
            {
                Find.World.renderer.wantedMode = round >= 4 ? WorldRenderMode.None : WorldRenderMode.Planet;
                if (round >= 7)
                {
                    var node = new DiaNode("Trade encounter fixture");
                    node.options.Add(new DiaOption("Trade") { action = OpenEncounterTrade, resolveTree = true });
                    Find.WindowStack.Add(new Dialog_NodeTree(node));
                    AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.SyncUtil"), "isDialogNodeTreeOpen").SetValue(null, true);
                }
                File.WriteAllText(Marker(Peer, "ready", round), "ready");
            }
            if (!File.Exists(Marker("host", "ready", round)) || !File.Exists(Marker("client", "ready", round))) return;
            if (client && clicked != round)
            {
                clicked = round;
                if (round >= 7) AccessTools.Method(typeof(DiaOption), "Activate").Invoke(((DiaNode)AccessTools.Field(typeof(Dialog_NodeTree), "curNode").GetValue(Find.WindowStack.WindowOfType<Dialog_NodeTree>())).options[0], null);
                else if (round >= 4) startTravel.DoSync(null);
                else
                {
                    Require(CaravanVisitUtility.SettlementVisitedNow(caravan) == settlement, "fixture not visiting settlement");
                    var command = (Command_Action)CaravanVisitUtility.TradeCommand(caravan, settlement.Faction, settlement.TraderKind);
                    Require(!command.Disabled, "trade command disabled");
                    Require(BestCaravanPawnUtility.FindBestNegotiator(caravan) != null, "no negotiator");
                    Log.Message("TRADE_WINDOW DISPATCH_STATE interface=" + MP.InInterface + " caravan=" + caravan.ID + " negotiator=" + BestCaravanPawnUtility.FindBestNegotiator(caravan).thingIDNumber);
                    command.action();
                }
                Log.Message("TRADE_WINDOW REAL_UI_DISPATCH round=" + round);
                return;
            }
            if (Mp.WorldComp.trading.Count == 0) { Require(Time.realtimeSinceStartup < actionDeadline, "trade session creation timeout round=" + round); return; }
            if (observed != round)
            {
                bool open = Find.WindowStack.IsOpen<TradingWindow>();
                Require(open == (client || round == 3), "auto window round=" + round + " peer=" + Peer + " actual=" + open);
                if (round >= 4 && round <= 6) Require(Find.World.renderer.wantedMode == (client ? WorldRenderMode.Planet : WorldRenderMode.None), "arrival camera moved wrong peer");
                observed = round;
                var rows = new List<string>();
                foreach (var t in Mp.WorldComp.trading)
                    rows.Add(((ILoadReferenceable)t.trader).GetUniqueLoadID() + ":" + t.playerNegotiator.thingIDNumber + ":" + t.deal.AllTradeables.Count);
                string state = string.Join(";", rows);
                File.WriteAllText(Marker(Peer, "observed", round), state);
                Log.Message("TRADE_WINDOW AUTO_ASSERT round=" + round + " open=" + open + " state=" + state);
            }
            if (!File.Exists(Marker("host", "observed", round)) || !File.Exists(Marker("client", "observed", round))) return;
            if (manual != round)
            {
                Require(File.ReadAllText(Marker("host", "observed", round)) == File.ReadAllText(Marker("client", "observed", round)), "shared session mismatch");
                Find.WindowStack.TryRemove(typeof(TradingWindow), false);
                if (round >= 7) Mp.WorldComp.trading[0].OpenWindow();
                else ((Command_Action)CaravanVisitUtility.TradeCommand(caravan)).action();
                Require(Find.WindowStack.IsOpen<TradingWindow>(), "manual reopen failed");
                Find.WindowStack.TryRemove(typeof(TradingWindow), false);
                Mp.WorldComp.trading[0].OpenWindow();
                Require(Find.WindowStack.IsOpen<TradingWindow>(), "manual session open failed");
                Find.WindowStack.TryRemove(typeof(TradingWindow), false);
                manual = round; File.WriteAllText(Marker(Peer, "manual", round), "PASS");
                Log.Message("TRADE_WINDOW MANUAL_ASSERT round=" + round);
            }
            if (client && File.Exists(Marker("host", "manual", round)) && File.Exists(Marker("client", "manual", round)))
            {
                prepare.DoSync(null, round + 1); next = Time.realtimeSinceStartup + 1;
            }
        }
        catch (Exception e) { failed = true; Log.Error("TRADE_WINDOW FAILED " + e); File.WriteAllText(Path.Combine(root, Peer + ".failed"), e.ToString()); }
    }
}
