using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Persistent;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class PausedMapFogState : GameComponent
{
    public List<Pawn> fogPawns = new List<Pawn>();
    public List<Pawn> negotiators = new List<Pawn>(), traders = new List<Pawn>();
    public Pawn negotiator => negotiators[Math.Min(negotiators.Count - 1, Math.Max(phase, 0) / 9)];
    public Pawn trader => traders[Math.Min(traders.Count - 1, Math.Max(phase, 0) / 9)];
    public Caravan caravan, encounter;
    public int cycle = -1, phase = -1, start, end, trades;
    public PausedMapFogState(Game game) { }
    public override void GameComponentUpdate() => PausedMapFogProbe.Update();
    public override void ExposeData()
    {
        Scribe_Collections.Look(ref fogPawns, "pfPawns", LookMode.Reference);
        Scribe_Collections.Look(ref negotiators, "pfNegotiators", LookMode.Reference); Scribe_Collections.Look(ref traders, "pfTraders", LookMode.Reference);
        Scribe_References.Look(ref caravan, "pfCaravan"); Scribe_References.Look(ref encounter, "pfEncounter");
        Scribe_Values.Look(ref cycle, "pfCycle", -1); Scribe_Values.Look(ref phase, "pfPhase", -1); Scribe_Values.Look(ref start, "pfStart");
        Scribe_Values.Look(ref end, "pfEnd"); Scribe_Values.Look(ref trades, "pfTrades");
    }
}

[StaticConstructorOnStartup]
public static class PausedMapFogProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("pausedmapfogprobe");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("pfclient");
    static readonly bool asyncTime = GenCommandLine.CommandLineArgPassed("pfasync");
    static readonly int maps = int.Parse(Arg("pfmaps", "2")), soak = int.Parse(Arg("pfsoak", "10000")), cycle = int.Parse(Arg("pfcycle", "0")), cycles = int.Parse(Arg("pfcycles", "0"));
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static bool hosted, sent, completed, failed, finishing, frozenSent, opening, drawing, clicked, awaitingAdvance, pausing, joinpoint;
    static int localPhase = -1, holdAt, expectedStock, lastCheckpoint, fogTicks, fogSpawns, phaseSpawns, readyCycle = -1, pauseAt;
    static float began = Time.realtimeSinceStartup, next;
    static ISyncMethod prepare, open, advance;
    static Type fogType;
    static PropertyInfo compressed;
    static string Peer => client ? "client" + cycle : "host";
    static int Total => maps * 9;
    static PausedMapFogState S => Current.Game?.GetComponent<PausedMapFogState>();
    static string Arg(string name, string fallback) => GenCommandLine.TryGetCommandLineArg(name, out string v) ? v : fallback;
    static string At(string name) => Path.Combine(root, name);
    static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    static void Write(string name, string value) { File.WriteAllText(At(Peer + "." + name), value); Log.Message("PF_PROBE " + Peer + " " + name + " " + value); }
    static void Fail(Exception ex) { failed = true; Write("failed", ex.ToString()); Log.Error("PF_FAILED " + ex); }
    static Type T(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
    static ulong RandState => (ulong)compressed.GetValue(null);
    static string Snapshot() => "world=" + Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";maps=" +
        string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState)) +
        ";cycle=" + S.cycle + ";phase=" + S.phase + ";trades=" + S.trades + ";pawns=" + string.Join(",", S.fogPawns.Select(p => p.thingIDNumber));
    static PausedMapFogProbe()
    {
        if (!enabled) return;
        fogType = T("Nivarian.HediffComp_NivarianVisual"); compressed = AccessTools.Property(typeof(Rand), "StateCompressed");
        prepare = MP.RegisterSyncMethod(typeof(PausedMapFogProbe), nameof(Prepare));
        open = MP.RegisterSyncMethod(typeof(PausedMapFogProbe), nameof(Open));
        advance = MP.RegisterSyncMethod(typeof(PausedMapFogProbe), nameof(Advance));
        var h = new Harmony("local.meow.pausedmapfogprobe");
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(Unseed)));
        h.Patch(AccessTools.Method(typeof(HediffWithComps), "PostTick"), prefix: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(FogBefore)) { priority = Priority.First },
            finalizer: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(FogAfter)) { priority = Priority.Last });
        h.Patch(AccessTools.Method(typeof(Mote), "SpawnSetup"), postfix: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(Spawned)));
        h.Patch(AccessTools.Method(typeof(Dialog_Trade), "DoWindowContents"), prefix: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(BeforeDraw)), finalizer: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(AfterDraw)));
        h.Patch(AccessTools.Method(typeof(Widgets), "ButtonText", new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) }), prefix: new HarmonyMethod(typeof(PausedMapFogProbe), nameof(Button)));
        Log.Message("PF_START peer=" + Peer + " label=" + root + " hashes=" + File.ReadAllText(At("candidate-hashes.txt")) +
            " trio=" + T("Meow.RaceTrioCompatibility.Bootstrap").Module.ModuleVersionId + " tale=" + T("Meow.TaleNivarianCompatibility.Bootstrap").Module.ModuleVersionId);
    }
    static void Seed() => Rand.PushState(261012);
    static void Unseed() => Rand.PopState();
    static void FogBefore(HediffWithComps __instance, out ulong? __state)
    {
        __state = MP.IsInMultiplayer && S != null && S.phase >= 0 && __instance.comps.Any(c => fogType.IsInstanceOfType(c)) ? RandState : (ulong?)null;
    }
    static void FogAfter(HediffWithComps __instance, ulong? __state)
    {
        if (!__state.HasValue) return;
        if (__state.Value != RandState) { Fail(new Exception("ICY_FOG_SHARED_RAND_CHANGED before=" + __state.Value + " after=" + RandState)); return; }
        fogTicks++;
        if (fogTicks <= 2) Log.Message("PF_FOG_REACHED pawn=" + __instance.pawn.thingIDNumber + " map=" + __instance.pawn.Map?.uniqueID + " colonist=" + __instance.pawn.IsColonist + " interface=" + MP.InInterface);
    }
    static void Spawned(Mote __instance) { if (MP.IsInMultiplayer && __instance.def.defName == "Mote_IcyFog") fogSpawns++; }
    static Pawn PawnAt(Map map, bool nivarian, Faction faction)
    {
        var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(nivarian ? DefDatabase<PawnKindDef>.GetNamed("Nivarian_Reinforcement") : PawnKindDefOf.Villager,
            faction, forceGenerateNewPawn: true, validatorPostGear: p => p.skills != null && !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled));
        GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 5), map);
        if (faction.IsPlayer) pawn.ideo.SetIdeo(Faction.OfPlayer.ideos.PrimaryIdeo);
        return pawn;
    }
    static void Stock(Pawn pawn, ThingDef def, int amount) { var t = ThingMaker.MakeThing(def); t.stackCount = amount; Check(pawn.inventory.innerContainer.TryAdd(t), "fixture inventory"); }
    static int TraderStock(int kind) => (kind == 1 ? S.trader.inventory.innerContainer : S.encounter.PawnsListForReading[0].inventory.innerContainer).Where(t => t.def == ThingDefOf.Steel).Sum(t => t.stackCount);
    public static void Prepare(int requested)
    {
        if (S.fogPawns.Count == 0)
        {
            var settlement = Find.WorldObjects.Settlements.Where(s => s.Faction != null && !s.Faction.IsPlayer && !s.Faction.HostileTo(Faction.OfPlayer) && s.Visitable && s.CanTradeNow && s.TraderKind.WillTrade(ThingDefOf.Steel))
                .OrderBy(s => s.ID).First();
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
            {
                var pawn = PawnAt(map, true, Faction.OfPlayer);
                if (!pawn.health.hediffSet.hediffs.OfType<HediffWithComps>().Any(h => h.comps.Any(c => fogType.IsInstanceOfType(c))))
                    pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Nivarian_NivarianVisual"));
                S.fogPawns.Add(pawn);
                var negotiator = PawnAt(map, false, Faction.OfPlayer); var trader = PawnAt(map, false, settlement.Faction);
                S.negotiators.Add(negotiator); S.traders.Add(trader);
                trader.trader = new Pawn_TraderTracker(trader) { traderKind = settlement.TraderKind };
                trader.Position = negotiator.Position + new IntVec3(1, 0, 0);
                negotiator.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 1000000)); trader.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 1000000));
                Stock(trader, ThingDefOf.Steel, 100); Stock(trader, ThingDefOf.Silver, 1000); Stock(negotiator, ThingDefOf.Silver, 1000);
                var money = ThingMaker.MakeThing(ThingDefOf.Silver); money.stackCount = ThingDefOf.Silver.stackLimit;
                var moneyCell = CellFinder.RandomClosewalkCellNear(negotiator.Position, map, 3); map.areaManager.Home[moneyCell] = true;
                GenSpawn.Spawn(money, moneyCell, map);
                Check(((ITrader)trader).ColonyThingsWillingToBuy(negotiator).Where(t => t.def == ThingDefOf.Silver).Sum(t => t.stackCount) >= 100, "NPC map trade currency unavailable");
            }
            var player = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                validatorPostGear: p => !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled));
            Stock(player, ThingDefOf.Silver, 1000);
            S.caravan = CaravanMaker.MakeCaravan(new[] { player }, Faction.OfPlayer, settlement.Tile, true);
            var merchant = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, settlement.Faction, forceGenerateNewPawn: true));
            merchant.trader = new Pawn_TraderTracker(merchant) { traderKind = settlement.TraderKind };
            Stock(merchant, ThingDefOf.Steel, 100); Stock(merchant, ThingDefOf.Silver, 1000);
            S.encounter = CaravanMaker.MakeCaravan(new[] { merchant }, settlement.Faction, settlement.Tile, true);
            Check(S.encounter.CanTradeNow, "caravan must be tradeable");
        }
        foreach (var pawn in S.fogPawns)
        {
            var need = pawn.needs.AllNeeds.First(n => n.GetType().Name == "Need_IcyCore"); need.CurLevel = 0.95f;
        }
        S.cycle = requested; S.phase = 0; S.trades = 0; S.start = TickPatch.Timer; S.end = S.start + (requested == cycles ? soak : 10000);
        Log.Message("PF_PREPARED cycle=" + requested + " maps=" + Find.Maps.Count + " pawns=" + S.fogPawns.Count + " shared=" + S.start);
    }
    public static void Open(int phase)
    {
        Check(S.phase == phase, "opening wrong phase"); int kind = phase % 3;
        Map target = S.negotiator.Map;
        if (kind == 0)
        {
            var ritual = Faction.OfPlayer.ideos.PrimaryIdeo.PreceptsListForReading.OfType<Precept_Ritual>().Single(r => r.def.defName == "RoleChange");
            new Command_Ritual(ritual, new TargetInfo(S.negotiator.Position, target)).ProcessInput(null);
            Check(target.MpComp().sessionManager.GetFirstOfType<RitualSession>() != null, "native role-change session absent");
        }
        else
        {
            var negotiator = kind == 1 ? S.negotiator : S.caravan.PawnsListForReading[0];
            ITrader merchant = kind == 1 ? (ITrader)S.trader : S.encounter;
            // This is the native window boundary called by the trade job and caravan encounter callback.
            Find.WindowStack.Add(new Dialog_Trade(negotiator, merchant));
            Check(Mp.WorldComp.trading.Count == 1, "native trade session absent");
        }
        Log.Message("PF_OPEN phase=" + phase + " kind=" + kind + " shared=" + TickPatch.Timer);
    }
    public static void Advance(int phase, int stock)
    {
        Check(S.phase == phase, "phase order"); int kind = phase % 3;
        if (kind != 0) { Check(TraderStock(kind) == stock - 1, "native trade must transfer exactly one Steel"); S.trades++; }
        Check(Mp.WorldComp.trading.Count == 0 && S.negotiator.Map.MpComp().sessionManager.GetFirstOfType<RitualSession>() == null, "closed session retained");
        foreach (var pawn in S.fogPawns) pawn.needs.AllNeeds.First(n => n.GetType().Name == "Need_IcyCore").CurLevel = 0.95f;
        S.phase++;
        Log.Message("PF_ASSERT cycle=" + S.cycle + " phase=" + phase + " kind=" + kind + " trades=" + S.trades + " shared=" + TickPatch.Timer);
    }
    static void BeforeDraw()
    {
        drawing = client && S != null && S.phase >= 0 && S.phase < Total && S.phase % 3 != 0 && !clicked && holdAt > 0 && TickPatch.Timer >= holdAt;
        if (!drawing) return;
        try
        {
            foreach (var row in TradeSession.deal.AllTradeables) row.ForceTo(0);
            var steel = TradeSession.deal.AllTradeables.Single(t => t.ThingDef == ThingDefOf.Steel);
            Check(steel.TraderWillTrade, "steel not tradeable"); steel.ForceTo(1); TradeSession.deal.UpdateCurrencyCount();
            Log.Message("PF_SELECTION phase=" + S.phase + " silver=" + TradeSession.deal.CurrencyTradeable.CountHeldBy(Transactor.Colony) + " steel=" + steel.CountToTransfer);
        }
        catch (Exception e) { Fail(e); drawing = false; }
    }
    static void AfterDraw() { drawing = false; }
    static bool Button(string label, ref bool __result)
    {
        if (!drawing || clicked || Event.current.type != EventType.Repaint || label != "AcceptButton".Translate().ToString()) return true;
        clicked = true; __result = true; Log.Message("PF_NATIVE_ACCEPT phase=" + S.phase); return false;
    }
    static void Speed(TimeSpeed speed) => Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)speed);
    public static void Update()
    {
        if (!enabled) return;
        if (File.Exists(At("quit")) || File.Exists(At(Peer + ".quit"))) { Application.Quit(); return; }
        if (failed || completed || Time.realtimeSinceStartup < next) return; next = Time.realtimeSinceStartup + 0.1f;
        try
        {
            Check(Time.realtimeSinceStartup - began < 2500, "overall timeout");
            if (LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Playing) return;
            if (!client && !hosted && !MP.IsInMultiplayer)
            {
                hosted = true; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (maps == 2)
                {
                    Map first = Find.CurrentMap; var tiles = new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(first.Tile, tiles);
                    var tile = tiles.OrderBy(t => t.tileId).First(t => !Find.WorldObjects.AnyMapParentAt(t) && Find.WorldPathGrid.Passable(t));
                    var parent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement); parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                    MapGenerator.GenerateMap(new IntVec3(60, 1, 60), parent, first.generatorDef); Current.Game.CurrentMap = first;
                }
                Check(HostWindow.HostProgrammatically(new ServerSettings { gameName = "PausedMapFog", direct = true, directAddress = "127.0.0.1:" + Arg("pfport", "31261"),
                    lan = false, steam = false, multifaction = false, asyncTime = asyncTime, syncConfigs = false, pauseOnJoin = false,
                    pauseOnLetter = PauseOnLetter.Never, autoJoinPoint = 0, autosaveInterval = 0, desyncTraces = soak <= 20000 }), "host rejected"); return;
            }
            if (!MP.IsInMultiplayer || Mp.Client?.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
            Check(!Mp.session.desynced, "desync");
            int requested = client ? cycle : (File.Exists(At("cycle.txt")) ? int.Parse(File.ReadAllText(At("cycle.txt"))) : 0);
            if (readyCycle != requested)
            {
                if (!TickPatch.Frozen) { if (!client) Mp.Client.Send(new ClientFreezePacket(true)); return; }
                if (!File.Exists(At(Peer + ".ready" + requested))) Write("ready" + requested, Snapshot());
                if (Mp.session.players.Count != 2 || !File.Exists(At("client" + requested + ".ready" + requested)) || !File.Exists(At("host.ready" + requested))) return;
                Check(File.ReadAllText(At("client" + requested + ".ready" + requested)) == File.ReadAllText(At("host.ready" + requested)), "BASELINE_LOAD_DRIFT"); readyCycle = requested;
                sent = finishing = frozenSent = pausing = joinpoint = false; localPhase = -1;
                if (!client)
                {
                    Mp.Client.Send(new ClientFreezePacket(false)); Speed(TimeSpeed.Superfast);
                    if (asyncTime) foreach (var map in Find.Maps) Mp.Client.SendCommand(CommandType.MapTimeSpeed, map.uniqueID, (byte)TimeSpeed.Superfast);
                }
                return;
            }
            if (TickPatch.Frozen)
            {
                if (!finishing && !File.Exists(At("host.final" + requested))) return;
                if (!client && Mp.LocalServer.worldData.CreatingJoinPoint) return;
                if (File.Exists(At(Peer + ".complete" + requested))) return;
                Write("final" + requested, Snapshot()); Check(S.trades == maps * 6, "missing trade outcomes");
                Check(fogTicks > 0 && fogSpawns > 0, "fog fixture unreachable ticks=" + fogTicks + " spawns=" + fogSpawns);
                Write("complete" + requested, "PASS shared=" + (TickPatch.Timer - S.start) + " fogTicks=" + fogTicks + " fogSpawns=" + fogSpawns); if (client || requested == cycles) completed = true; return;
            }
            Check(Mp.session.players.Count == 2, "unexpected disconnect");
            if (S.cycle != requested) { if (client && !sent) sent = prepare.DoSync(null, requested); return; }
            Current.Game.CurrentMap = client ? S.negotiator.Map : Find.Maps.First(m => maps == 1 || m != S.negotiator.Map);
            if (S.phase < Total && client)
            {
                if (localPhase != S.phase)
                {
                    localPhase = S.phase; opening = clicked = awaitingAdvance = false; phaseSpawns = fogSpawns; expectedStock = S.phase % 3 == 0 ? 0 : TraderStock(S.phase % 3);
                    if (asyncTime && maps == 2) Mp.Client.SendCommand(CommandType.MapTimeSpeed, S.negotiator.Map.uniqueID, (byte)TimeSpeed.Paused);
                    open.DoSync(null, S.phase); holdAt = TickPatch.Timer + 300; return;
                }
                if (awaitingAdvance) return;
                if (!opening)
                {
                    if (S.phase % 3 == 0)
                    {
                        var session = S.negotiator.Map.MpComp().sessionManager.GetFirstOfType<RitualSession>(); if (session == null) return;
                        if (!Find.WindowStack.Windows.OfType<RitualBeginProxy>().Any()) session.OpenWindow();
                        Check(Find.WindowStack.Windows.OfType<RitualBeginProxy>().Any(), "role UI absent");
                    }
                    else
                    {
                        if (Mp.WorldComp.trading.Count == 0) return;
                        if (!Find.WindowStack.Windows.OfType<TradingWindow>().Any()) Mp.WorldComp.trading[0].OpenWindow();
                        Check(Find.WindowStack.Windows.OfType<TradingWindow>().Any(), "trade UI absent");
                    }
                    opening = true; Log.Message("PF_UI phase=" + S.phase + " view=" + Find.CurrentMap.uniqueID + " paused=" + Find.TickManager.Paused); return;
                }
                if (TickPatch.Timer < holdAt) return;
                if (asyncTime && maps == 2) Check(fogSpawns > phaseSpawns, "running other-map fog absent while modal map paused");
                if (S.phase % 3 == 0)
                {
                    var proxy = Find.WindowStack.Windows.OfType<RitualBeginProxy>().FirstOrDefault(); proxy?.Close();
                    var session = S.negotiator.Map.MpComp().sessionManager.GetFirstOfType<RitualSession>(); if (session != null) { session.Remove(); return; }
                }
                else if (Mp.WorldComp.trading.Count != 0)
                {
                    Check(!clicked || TickPatch.Timer < holdAt + 600, "trade did not complete after native Accept; windows=" + string.Join(",", Find.WindowStack.Windows.Select(w => w.GetType().Name)));
                    return;
                }
                if (asyncTime && maps == 2) Mp.Client.SendCommand(CommandType.MapTimeSpeed, S.negotiator.Map.uniqueID, (byte)TimeSpeed.Superfast);
                advance.DoSync(null, S.phase, expectedStock); awaitingAdvance = true; holdAt = int.MaxValue; return;
            }
            if (S.phase < Total) return;
            if (client && maps == 2) Current.Game.CurrentMap = Find.Maps[(TickPatch.Timer / 500) % maps];
            if (TickPatch.Timer >= lastCheckpoint + 10000) { lastCheckpoint = TickPatch.Timer; Log.Message("PF_CHECK shared=" + (TickPatch.Timer - S.start) + " players=2 desynced=False"); }
            if (TickPatch.Timer < S.end) return;
            if (client) { if (!File.Exists(At(Peer + ".actions"))) Write("actions", Snapshot()); return; }
            if (!File.Exists(At("client" + requested + ".actions"))) return;
            if (!pausing)
            {
                Speed(TimeSpeed.Paused); if (asyncTime) foreach (var map in Find.Maps) Mp.Client.SendCommand(CommandType.MapTimeSpeed, map.uniqueID, (byte)TimeSpeed.Paused);
                pausing = true; pauseAt = TickPatch.Timer + 45; return;
            }
            if (TickPatch.Timer < pauseAt) return;
            if (!joinpoint) { Check(Mp.LocalServer.worldData.TryStartJoinPointCreation(true), "joinpoint rejected"); joinpoint = true; return; }
            if (Mp.LocalServer.worldData.CreatingJoinPoint) return;
            finishing = true; if (!frozenSent) { frozenSent = true; Mp.Client.Send(new ClientFreezePacket(true)); }
        }
        catch (Exception ex) { Fail(ex); }
    }
}
