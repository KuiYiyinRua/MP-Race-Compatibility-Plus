using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class PerspectiveOwnershipFixture : GameComponent
{
    public Pawn OfflinePawn, HostPawn, ClientPawn, AlternatePawn;
    public int Cycle = -1, Step, Until;
    public PerspectiveOwnershipFixture(Game game) { }
    public override void GameComponentUpdate() => PerspectiveOwnershipProbe.Update();
    public override void ExposeData()
    {
        Scribe_References.Look(ref OfflinePawn, "offlinePawn");
        Scribe_References.Look(ref HostPawn, "hostPawn");
        Scribe_References.Look(ref ClientPawn, "clientPawn");
        Scribe_References.Look(ref AlternatePawn, "alternatePawn");
        Scribe_Values.Look(ref Cycle, "cycle", -1);
        Scribe_Values.Look(ref Step, "step");
        Scribe_Values.Look(ref Until, "until");
    }
}

[StaticConstructorOnStartup]
public static class PerspectiveOwnershipProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("perspectiveownershipprobe");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("psclient");
    static readonly bool twoMaps = GenCommandLine.CommandLineArgPassed("pstwomaps");
    static readonly bool asyncTime = GenCommandLine.CommandLineArgPassed("psasync");
    static readonly bool replay = GenCommandLine.CommandLineArgPassed("psreplay");
    static readonly bool saved = GenCommandLine.CommandLineArgPassed("pssave");
    static readonly bool baseline = GenCommandLine.CommandLineArgPassed("psbaseline");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static readonly int cycle = int.Parse(Arg("pscycle", "0"));
    static readonly int finalCycle = int.Parse(Arg("pslastcycle", "3"));
    static readonly int soakTicks = int.Parse(Arg("pssoakticks", "120000"));
    static readonly string address = "127.0.0.1:" + Arg("psport", "31105");
    static Type patch, registry, original;
    static ISyncMethod assert, begin;
    static bool hosted, loaded, failed, ready, hostClaimSent, beginSent, sent, frozenRequested, joinPointStarted, done;
    static int readyCycle = -1, begunCycle = -1, closingCycle = -1, dispatchAt, lastEpoch = -1, lastFocus = -1, checkpoint;
    static int offMapBefore = -1;
    static bool offMapVerified;
    static float offMapStarted;
    static readonly System.Collections.Generic.HashSet<string> healthReceipts = new System.Collections.Generic.HashSet<string>();
    static float started = Time.realtimeSinceStartup, lastProgress = Time.realtimeSinceStartup;
    static int lastTick = -1;
    static PerspectiveOwnershipFixture S => Current.Game?.GetComponent<PerspectiveOwnershipFixture>();
    static object C => Current.Game?.components.FirstOrDefault(c => c.GetType() == registry);
    static string Peer => client ? "client" + cycle : "host";
    static int Steps => twoMaps ? 13 : 7;
    static Pawn TargetPawn(int step) => twoMaps && step >= 6 && step < 12 ? S.AlternatePawn : S.ClientPawn;
    static string At(string name) => Path.Combine(root, name);
    static string Arg(string name, string fallback) => GenCommandLine.TryGetCommandLineArg(name, out string value) ? value : fallback;
    static object Get(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static object Call(Type type, object obj, string name, params object[] args) => AccessTools.Method(type, name).Invoke(obj, args);
    static object Owner(string owner) => Call(registry, C, "ForOwner", owner);
    static object PawnState(Pawn pawn) => Call(registry, C, "ForPawn", pawn);
    static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    static int Epoch(object record) => record == null ? -1 : (int)Get(record, "epoch");
    static string Registry() => string.Join(";", ((IEnumerable)Get(C, "controlled")).Cast<object>()
        .Select(r => Get(r, "owner") + ":" + ((Pawn)Get(r, "pawn")).thingIDNumber + ":" + Get(r, "epoch") + ":" +
            (AccessTools.Field(r.GetType(), "ownerPlayerId")?.GetValue(r) ?? -1)).OrderBy(v => v, StringComparer.Ordinal));
    static string Snapshot() => "world=" + Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" +
        string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState)) + ";claims=" + Registry();
    static void Receipt(string name, string value) { File.WriteAllText(At(name), value); Log.Message("PS_OWN " + name + " " + value); }
    static PerspectiveOwnershipProbe()
    {
        if (!enabled) return;
        patch = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_PerspectiveShiftMp");
        registry = AccessTools.TypeByName("MP_MeowOnlineShop.PerspectiveShiftMpComponent");
        original = AccessTools.TypeByName("PerspectiveShift.State");
        Check(patch != null && registry != null && original != null, "required target missing");
        assert = MP.RegisterSyncMethod(typeof(PerspectiveOwnershipProbe), nameof(AssertAction));
        begin = MP.RegisterSyncMethod(typeof(PerspectiveOwnershipProbe), nameof(BeginCycle));
        var h = new Harmony("local.meow.perspectiveownershipprobe");
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(PerspectiveOwnershipProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(PerspectiveOwnershipProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(PerspectiveOwnershipProbe), nameof(Unseed)));
        Log.Message("PS_OWN START label=" + Path.GetFileName(root) + " peer=" + Peer + " coreMvid=" + patch.Module.ModuleVersionId + " hashes=" + File.ReadAllText(At("inputs.json")));
    }
    static void Seed() => Rand.PushState(261005);
    static void Unseed() => Rand.PopState();
    static void SetMode(string mode)
    {
        var field = AccessTools.Field(original, "CurrentMode"); field.SetValue(null, Enum.Parse(field.FieldType, mode));
        var settings = AccessTools.Field(AccessTools.TypeByName("PerspectiveShift.PerspectiveShiftMod"), "settings").GetValue(null);
        Set(settings, "totalFreedom", false);
    }
    static void SeedOldClaims()
    {
        var pawns = Find.CurrentMap.mapPawns.FreeColonists.OrderBy(p => p.thingIDNumber).Take(3).ToArray();
        Check(pawns.Length == 3, "three colonists required");
        S.OfflinePawn = pawns[0]; S.ClientPawn = pawns[1]; S.HostPawn = pawns[2];
        S.AlternatePawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn:true));
        GenSpawn.Spawn(S.AlternatePawn, CellFinder.RandomClosewalkCellNear(S.OfflinePawn.Position, S.OfflinePawn.Map, 5), S.OfflinePawn.Map);
        LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_DefendPoint(S.OfflinePawn.Position), S.OfflinePawn.Map, new[] { S.OfflinePawn, S.AlternatePawn });
        ClaimFixture("LongOffline", S.OfflinePawn); ClaimFixture("PsClient", S.ClientPawn);
        S.ClientPawn.drafter.Drafted = true;
        object state = Owner("PsClient"); Set(state, "pendingFire", true); Set(state, "pendingFireUntil", int.MaxValue);
        Call(registry, C, "SetMoveIntent", "PsClient", S.ClientPawn, Epoch(state), 1, 1, 0, true, false);
        SetMode("Dynamic"); Call(original, null, "SetAvatar", S.HostPawn, false); SetMode("Authentic");
        if (twoMaps)
        {
            Map first=Find.CurrentMap;
            PlanetTile tile=TileFinder.RandomStartingTile();
            for(int attempt=0;attempt<30 && Find.WorldObjects.AllWorldObjects.Any(w=>w.Tile==tile);attempt++) tile=TileFinder.RandomStartingTile();
            Check(!Find.WorldObjects.AllWorldObjects.Any(w=>w.Tile==tile),"no free tile");
            var settlement=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.Tile=tile; settlement.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(settlement);
            Map other=MapGenerator.GenerateMap(new IntVec3(80,1,80),settlement,first.generatorDef);
            S.ClientPawn.DeSpawn(); GenSpawn.Spawn(S.ClientPawn, other.Center, other);
            Current.Game.CurrentMap=first;
            Check(first.IsPlayerHome && other.IsPlayerHome,"two home maps required");
        }
        Log.Message("PS_OWN SEEDED " + Registry());
        GameDataSaveLoader.SaveGame("PerspectiveOldOwnership");
    }
    static void ClaimFixture(string owner, Pawn pawn)
    {
        var method = AccessTools.Method(registry, "Claim");
        method.Invoke(C, method.GetParameters().Length == 2 ? new object[] { owner, pawn } : new object[] { owner, pawn, -1 });
    }
    static ServerSettings Settings() => new ServerSettings { gameName="PerspectiveOwnership", direct=true, directAddress=address, lan=false, steam=false, multifaction=false, asyncTime=asyncTime, syncConfigs=false, pauseOnJoin=false, pauseOnLetter=PauseOnLetter.Never, autoJoinPoint=0, autosaveInterval=0, desyncTraces=GenCommandLine.CommandLineArgPassed("pstraces") };
    static void HostReplay()
    {
        Check((bool)AccessTools.Method(typeof(HostWindow), "TryStartLocalServer").Invoke(null, new object[] { Settings() }), "host preinit");
        HostUtil.HostServer(Settings(), true); hosted = true;
    }
    static void Speed(TimeSpeed speed)
    {
        Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)speed);
        foreach (var map in Find.Maps) Mp.Client.SendCommand(CommandType.MapTimeSpeed, map.uniqueID, (byte)speed);
    }
    static void NativeClaim(Pawn pawn)
    {
        Current.Game.CurrentMap = pawn.Map;
        SetMode("Authentic");
        var command = pawn.GetGizmos().OfType<Command_Action>().FirstOrDefault(c => c.defaultLabel == "PS_TakeControl".Translate().ToString() || c.defaultLabel == "PS_SwapCharacter".Translate().ToString());
        Check(command != null && !(bool)AccessTools.Field(typeof(Gizmo), "disabled").GetValue(command), "native take-control gizmo absent");
        Log.Message("PS_OWN NATIVE_CLAIM peer=" + Peer + " pawn=" + pawn.thingIDNumber + " map=" + pawn.Map.uniqueID);
        command.action();
    }
    public static void BeginCycle(int requested)
    {
        Check(Owner("PsClient") == null, "rejoining client inherited a previous claim");
        Check(Owner("PsHost") != null, "another player's claim was cleared by rejoin");
        Check(PawnState(S.OfflinePawn) == null, "offline lease returned");
        S.Cycle = requested; S.Step = 0; S.Until = 0;
        begunCycle = requested;
        Log.Message("PS_OWN BEGIN cycle=" + requested + " claims=" + Registry());
    }
    public static void AssertAction(int requested, int step, int staleEpoch, int expectedPlayerId, Pawn pawn)
    {
        Check(requested == S.Cycle && step == S.Step, "action ordering");
        object state = Owner("PsClient");
        if (step % 2 == 0)
        {
            Check(state != null && ReferenceEquals(Get(state, "pawn"), pawn), "claim not applied");
            Check((int)Get(state, "ownerPlayerId") == expectedPlayerId, "wrong connection bound");
            Check(ReferenceEquals(state, PawnState(pawn)), "pawn-owner mismatch");
            if (staleEpoch >= 0) Check(Epoch(state) > staleEpoch, "epoch reused");
            if (staleEpoch >= 0)
            {
                int beforeSequence=(int)Get(state,"lastAppliedInputSequence");
                Call(patch,null,"SyncSetMoveIntent","PsClient",pawn,staleEpoch,int.MaxValue,1,0,true,false);
                Check((int)Get(state,"lastAppliedInputSequence")==beforeSequence,"stale command affected new lease");
            }
            object protectedState=Owner("PsHost");
            Call(patch,null,"SyncClaimAvatarWithMode","PsClient",S.HostPawn,false);
            Check(ReferenceEquals(Owner("PsHost"),protectedState) && ReferenceEquals(Owner("PsClient"),state),"other owner's pawn stolen");
            Check((bool)Call(original,null,"IsAvatar",pawn),"registry identity false before draft");
            if (step < Steps-1) pawn.drafter.Drafted = true;
        }
        else
        {
            Check(state == null && PawnState(pawn) == null, "release not applied");
            Check(!pawn.Drafted, "release left pawn drafted");
            Check(Call(registry, C, "EnsureState", "PsClient", pawn, staleEpoch) == null, "stale input resurrected lease");
            if (pawn == S.AlternatePawn) Check(pawn.GetLord() != null && pawn.GetLord().ownedPawns.Count(p => p == pawn) == 1, "lord restoration not exactly once");
        }
        Check(Owner("PsHost") != null, "other owner lost");
        Log.Message("PS_OWN ASSERT cycle=" + requested + " step=" + step + " claims=" + Registry());
        S.Step++;
        if (S.Step == Steps) S.Until = Mp.AsyncWorldTime.worldTicks + (requested == finalCycle ? soakTicks : 10000);
    }
    static void ExportReplay(string name)
    {
        var file = new FileInfo(At(name + ".zip"));
        Replay.ForSaving(file).WriteCurrentData();
    }
    static bool VerifyOffMapStop()
    {
        if (!client || !twoMaps || offMapVerified) return true;
        object state = Owner("PsClient");
        if (offMapBefore < 0)
        {
            Current.Game.CurrentMap = Find.Maps.First(m => m != S.ClientPawn.Map);
            offMapBefore = (int)Get(state, "lastAppliedInputSequence");
            offMapStarted = Time.realtimeSinceStartup;
            AccessTools.Field(patch, "sentInput").SetValue(null, false);
            // Exercise the exact native physics prefix which sends heartbeat
            // input while the camera is on a different map (R07 failure).
            Call(patch, null, "UpdatePhysicsPrefix", Get(state, "runtimeAvatar"));
            Receipt(Peer + ".offmap.sent", "camera=" + Find.CurrentMap.uniqueID + ";pawnMap=" + S.ClientPawn.Map.uniqueID + ";before=" + offMapBefore);
            return false;
        }
        Check(Time.realtimeSinceStartup - offMapStarted < 20, "off-map stop heartbeat was not acknowledged");
        if ((int)Get(state, "lastAppliedInputSequence") <= offMapBefore) return false;
        Check((int)Get(state, "moveX") == 0 && (int)Get(state, "moveZ") == 0, "off-map pawn still has movement intent");
        offMapVerified = true;
        Receipt(Peer + ".offmap.complete", "ack=" + Get(state, "lastAppliedInputSequence") + ";claims=" + Registry());
        return true;
    }
    static void VerifyOwnershipLifetime(string owner, Pawn pawn)
    {
        if (pawn == null)
        {
            // Scribe omits destroyed references when the final join point
            // reloads. Accept only a pawn whose death this very process already
            // verified; a missing living pawn or resurrected claim still fails.
            Check(healthReceipts.Contains(owner) && Owner(owner) == null,
                "fixture lost unverified pawn reference for " + owner);
            return;
        }
        bool gone = pawn.Dead || pawn.Destroyed || pawn.IsKidnapped();
        object state = Owner(owner);
        Check(gone ? state == null : state != null && ReferenceEquals(Get(state, "pawn"), pawn),
            "ownership disagrees with pawn health for " + owner);
        if (gone && healthReceipts.Add(owner))
            Receipt(Peer + ".health." + owner, "pawn=" + pawn.thingIDNumber + ";dead=" + pawn.Dead + ";destroyed=" + pawn.Destroyed + ";kidnapped=" + pawn.IsKidnapped() + ";claims=" + Registry());
    }
    public static void Update()
    {
        if (!enabled) return;
        if (File.Exists(At("quit")) || (client && File.Exists(At(Peer + ".quit")))) { Application.Quit(); return; }
        if (failed || done) return;
        try
        {
            Check(Time.realtimeSinceStartup - started < 2200, "overall timeout");
            if (replay && !client && !loaded && !LongEventHandler.AnyEventNowOrWaiting)
            { loaded = true; Replay.LoadReplay(new FileInfo(At("input.zip")), true, HostReplay); return; }
            if (saved && !client && !loaded && !LongEventHandler.AnyEventNowOrWaiting)
            { loaded=true; GameDataSaveLoader.LoadGame("PerspectiveOldOwnership"); return; }
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            if (!client && !hosted && !MP.IsInMultiplayer)
            {
                if(!saved) SeedOldClaims(); hosted = true; Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                Check(HostWindow.HostProgrammatically(Settings()), "host rejected"); return;
            }
            if (!MP.IsInMultiplayer || Mp.Client?.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
            LateUpdate();
            if (waiting) return;
            Check(!Mp.session.desynced, "desync");
            if (!client && !ready)
            {
                if (!TickPatch.Frozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                Log.Message("PS_OWN HOST_AFTER_NEW_ROOM " + Registry());
                if (baseline) { Receipt("baseline.reproduced", "claims=" + Registry()); Check(Registry() != "", "baseline did not reproduce"); done=true; return; }
                Check(Registry() == "", "old claims survived new room");
                Check(!S.ClientPawn.Drafted, "old draft state survived reset");
                Check(S.OfflinePawn.GetLord() != null, "old lord not restored");
                Check(AccessTools.Field(original, "Avatar").GetValue(null) == null, "serialized singleton assigned to host");
                ready = true;
            }
            int requested = client ? cycle : (File.Exists(At("cycle.txt")) ? int.Parse(File.ReadAllText(At("cycle.txt"))) : 0);
            if (readyCycle != requested)
            {
                if (!client)
                {
                    if (!TickPatch.Frozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                    if (!File.Exists(At("host.frozen" + requested))) Receipt("host.frozen" + requested, Snapshot());
                }
                else if (TickPatch.Frozen && !File.Exists(At("client.frozen" + requested)))
                    Receipt("client.frozen" + requested, Snapshot());
                if (Mp.session.players.Count != 2 || !File.Exists(At("host.frozen" + requested)) || !File.Exists(At("client.frozen" + requested))) return;
                Check(File.ReadAllText(At("host.frozen" + requested)) == File.ReadAllText(At("client.frozen" + requested)), "BASELINE_LOAD_DRIFT");
                readyCycle = requested; frozenRequested = joinPointStarted = false; closingCycle=-1;
                if (!client) { Mp.Client.Send(new ClientFreezePacket(false)); Speed(TimeSpeed.Superfast); }
            }
            if (TickPatch.Frozen) return;
            if (TickPatch.Timer != lastTick) { lastTick=TickPatch.Timer; lastProgress=Time.realtimeSinceStartup; }
            Check(Time.realtimeSinceStartup-lastProgress < 180, "simulation stalled");
            if (!client && !hostClaimSent) { hostClaimSent=true; NativeClaim(S.HostPawn); return; }
            if (Owner("PsHost") == null && begunCycle != requested) return;
            if (client && !beginSent)
            {
                if (Owner("PsClient") != null) return;
                beginSent=begin.DoSync(null,cycle); return;
            }
            // An input replay can already contain the same cycle number and a
            // completed Step. Wait for this process's fresh synchronized begin.
            if (begunCycle != requested || S.Cycle != requested) return;
            if (client && S.Step < Steps)
            {
                if (!sent)
                {
                    if (S.Step % 2 == 0) NativeClaim(TargetPawn(S.Step));
                    else { lastEpoch=Epoch(Owner("PsClient")); Call(original,null,"ClearAvatar"); }
                    sent=true; dispatchAt=TickPatch.Timer+35; return;
                }
                if (TickPatch.Timer < dispatchAt) return;
                sent=false; assert.DoSync(null,cycle,S.Step,lastEpoch,Mp.session.playerId,TargetPawn(S.Step)); dispatchAt=TickPatch.Timer+35;
                // Wait for the assertion's shared step before dispatching again.
                sent=true; waitingStep=S.Step; waiting=true; return;
            }
            if (S.Step < Steps) return;
            Check(Mp.session.players.Count==2,"unexpected disconnect during shared simulation");
            VerifyOwnershipLifetime("PsHost", S.HostPawn);
            VerifyOwnershipLifetime("PsClient", S.ClientPawn);
            if (!VerifyOffMapStop()) return;
            if (Mp.AsyncWorldTime.worldTicks>=checkpoint) { checkpoint=Mp.AsyncWorldTime.worldTicks+10000; Log.Message("PS_OWN CHECKPOINT cycle="+requested+" networkTick="+TickPatch.Timer+" worldTick="+Mp.AsyncWorldTime.worldTicks+" until="+S.Until+" players=2 desynced=False claims="+Registry()); }
            if (client && twoMaps && TickPatch.Timer/2000!=lastFocus)
            { lastFocus=TickPatch.Timer/2000; Current.Game.CurrentMap=Find.Maps[lastFocus%Find.Maps.Count]; }
            if (Mp.AsyncWorldTime.worldTicks < S.Until) return;
            if (client) { if(!File.Exists(At(Peer+".actionscomplete")))Receipt(Peer+".actionscomplete",Registry()); return; }
            if (!File.Exists(At("client"+requested+".actionscomplete"))) return;
            if (!frozenRequested) { Speed(TimeSpeed.Paused); frozenRequested=true; dispatchAt=TickPatch.Timer+45; return; }
            if (TickPatch.Timer<dispatchAt) return;
            if (!joinPointStarted) { Check(Mp.LocalServer.worldData.TryStartJoinPointCreation(true),"joinpoint rejected"); joinPointStarted=true; return; }
            if (Mp.LocalServer.worldData.CreatingJoinPoint) return;
            if (!TickPatch.Frozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
        }
        catch(Exception e) { failed=true; Receipt(Peer+".failed",e.ToString()); Log.Error("PS_OWN FAILED "+e); }
    }
    static int waitingStep;
    static bool waiting;
    // A separate tick from Update handles frozen completion and command acks.
    public static void LateUpdate()
    {
        if (!enabled || failed || !MP.IsInMultiplayer || S==null) return;
        if (waiting && S.Step != waitingStep) { waiting=false; sent=false; }
        if (S.Step != Steps || !TickPatch.Frozen || !File.Exists(At("client"+S.Cycle+".actionscomplete"))) return;
        if (!client && joinPointStarted && !Mp.LocalServer.worldData.CreatingJoinPoint && closingCycle != S.Cycle)
        {
            closingCycle=S.Cycle; ExportReplay("claims-cycle"+S.Cycle); Receipt("host.complete"+S.Cycle,Snapshot());
        }
        if (client && File.Exists(At("host.complete"+cycle)) && !File.Exists(At(Peer+".complete")))
        {
            Check(Snapshot()==File.ReadAllText(At("host.complete"+cycle)),"joinpoint changed shared claims or RNG");
            Receipt(Peer+".complete",Snapshot());
        }
    }
}
