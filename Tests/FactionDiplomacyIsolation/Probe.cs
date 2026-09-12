using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class DiplomacyProbeState : GameComponent
{
    public int stage, start, stepTick;
    public List<Faction> players = new List<Faction>();
    public Map secondMap;
    public Caravan caravan;
    public Settlement settlement;
    public DiplomacyProbeState(Game game) { }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref stage, "dpStage");
        Scribe_Values.Look(ref start, "dpStart");
        Scribe_Values.Look(ref stepTick, "dpStep");
        Scribe_Collections.Look(ref players, "dpPlayers", LookMode.Reference);
        Scribe_References.Look(ref secondMap, "dpSecondMap");
        Scribe_References.Look(ref caravan, "dpCaravan");
        Scribe_References.Look(ref settlement, "dpSettlement");
    }
    public override void GameComponentUpdate() { DiplomacyProbe.Update(); }
}

[StaticConstructorOnStartup]
public static class DiplomacyProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("dpprobe");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("dpclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static bool hosted, frozen, ready, requested, handed, failed, done, freezeRequested, focusChanged;
    static int dispatched = -1;
    static float next;
    static int Clock => Mp.AsyncWorldTime.worldTicks;
    static DiplomacyProbeState S => Current.Game.GetComponent<DiplomacyProbeState>();
    static Faction Milira => Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == "Milira_Faction");
    static Faction Church => Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == "Milira_AngelismChurch");
    static DiplomacyProbe()
    {
        if (!enabled) return;
        MP.RegisterSyncMethod(typeof(DiplomacyProbe), nameof(Prepare));
        MP.RegisterSyncMethod(typeof(DiplomacyProbe), nameof(Check));
        MP.RegisterSyncMethod(typeof(DiplomacyProbe), nameof(Negotiate));
        var h = new Harmony("local.diplomacy.probe.commands");
        h.Patch(AccessTools.Method(typeof(AsyncWorldTimeComp), "ExecuteCmd"), postfix: new HarmonyMethod(typeof(DiplomacyProbe), nameof(CommandLog)));
        h.Patch(AccessTools.Method(typeof(SyncCoordinator), "FinishLocalOpinion"), postfix: new HarmonyMethod(typeof(DiplomacyProbe), nameof(OpinionLog)));
        Log.Message("DPPROBE START peer=" + (client ? "client" : "host"));
    }
    static void CommandLog(AsyncWorldTimeComp __instance, ScheduledCommand cmd) => Log.Message("DPPROBE CMD timer="+TickPatch.Timer+" world="+Clock+" type="+cmd.type+" rand="+__instance.randState+" sim="+TickPatch.Simulating);
    static void OpinionLog(ClientSyncOpinion __result) { if(__result!=null&&__result.commandRandomStates.Count>0) Log.Message("DPPROBE OPINION start="+__result.startTick+" timer="+TickPatch.Timer+" commands="+string.Join(",",__result.commandRandomStates)); }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Fail(Exception e)
    {
        failed = true;
        Log.Error("DPPROBE FAILED " + e);
        File.WriteAllText(Path.Combine(root, client ? "client.failed" : "host.failed"), e.ToString());
    }
    static Faction Create(string name, FactionDef def)
    {
        var m = AccessTools.Method("Multiplayer.Client.Factions.FactionCreator:NewFactionWithIdeo");
        return (Faction)m.Invoke(null, new object[] { name, Color.white, def,
            Activator.CreateInstance(m.GetParameters()[3].ParameterType, new object[] { null, null, null }) });
    }
    static void Pair(Faction p, Faction n, bool permanent, int? initial = null)
    {
        bool a = GoodwillSituationWorker_PermanentEnemy.ArePermanentEnemies(p, n);
        bool b = GoodwillSituationWorker_PermanentEnemy.ArePermanentEnemies(n, p);
        Assert(a == permanent && b == permanent, "permanent " + p.Name + "/" + n.def.defName + "=" + a + "/" + b);
        Assert(p.BaseGoodwillWith(n) == n.BaseGoodwillWith(p), "asymmetric base " + p.Name);
        Assert(p.GoodwillWith(n) == n.GoodwillWith(p), "asymmetric effective " + p.Name);
        Assert(p.RelationKindWith(n) == n.RelationKindWith(p), "asymmetric kind " + p.Name);
        if (initial.HasValue) Assert(p.BaseGoodwillWith(n) == initial.Value, "initial " + p.Name + "=" + p.BaseGoodwillWith(n));
        Log.Message("DPPROBE PAIR " + p.loadID + "/" + n.loadID + " permanent=" + a + " base=" +
            p.BaseGoodwillWith(n) + " effective=" + p.GoodwillWith(n) + " kind=" + p.RelationKindWith(n));
    }
    public static void Prepare()
    {
        try
        {
            foreach (var tuple in new[] {
                new { name="OrdinaryA", def=FactionDefOf.PlayerColony },
                new { name="OrdinaryB", def=FactionDefOf.PlayerColony },
                new { name="MiliraStart", def=DefDatabase<FactionDef>.GetNamed("Milira_PlayerFaction") },
                new { name="KiiroStart", def=DefDatabase<FactionDef>.GetNamed("Kiiro_PlayerFaction") } })
                S.players.Add(Create(tuple.name, tuple.def));
            Pair(S.players[0], Milira, true, -100);
            Pair(S.players[1], Milira, true, -100);
            Pair(S.players[2], Milira, false, 0);
            Pair(S.players[3], Milira, false, 0);
            Pair(S.players[2], Church, true, -100);
            Pair(S.players[3], Church, false, 0);
            Assert(!S.players[0].TryAffectGoodwillWith(Milira, 20, false, false), "ordinary gift bypassed lock");
            Assert(S.players[3].TryAffectGoodwillWith(Milira, -90, false, false), "Kiiro cannot declare war");
            Assert(S.players[3].HostileTo(Milira), "Kiiro war did not apply");
            S.players[3].RelationWith(Milira).kind=FactionRelationKind.Neutral;
            Milira.RelationWith(S.players[3]).kind=FactionRelationKind.Neutral;
            Find.GoodwillSituationManager.RecalculateAll(false);
            Assert(S.players[3].HostileTo(Milira),"recalculation used spectator instead of Kiiro");
            var adapter=AccessTools.TypeByName("Meow.FactionDiplomacy.Patch_MiliraMultifactionRelations");
            var recover=AccessTools.Method(adapter,"RecoverGoodwill");
            recover.Invoke(null,new object[]{Milira});
            var state=Current.Game.GetComponent(AccessTools.TypeByName("Meow.FactionDiplomacy.FactionDiplomacyState"));
            var recovery=(System.Collections.IEnumerable)AccessTools.Field(state.GetType(),"recovery").GetValue(state);
            object kiiroTimer=null;
            foreach(var r in recovery)
                if((Faction)AccessTools.Field(r.GetType(),"player").GetValue(r)==S.players[3] &&
                   (Faction)AccessTools.Field(r.GetType(),"other").GetValue(r)==Milira)kiiroTimer=r;
            Assert(kiiroTimer!=null,"missing pair recovery record");
            int natural;
            FactionContext.Push(S.players[3]);
            try { natural=Milira.NaturalGoodwill; } finally { FactionContext.Pop(); }
            int targetValue=natural>=0 ? -100 : 100;
            Assert(S.players[3].TryAffectGoodwillWith(Milira,targetValue>0?300:-300,false,false),"cannot arrange recovery fixture");
            Log.Message("DPPROBE RECOVERY_FIXTURE natural="+natural+" base="+S.players[3].BaseGoodwillWith(Milira)+" canChange="+Milira.CanChangeGoodwillFor(S.players[3],targetValue>0?-10:10));
            AccessTools.Field(kiiroTimer.GetType(),"timer").SetValue(kiiroTimer,2999999);
            int beforeRecovery=S.players[3].BaseGoodwillWith(Milira);
            recover.Invoke(null,new object[]{Milira});
            Assert(Math.Abs(S.players[3].BaseGoodwillWith(Milira)-natural)<Math.Abs(beforeRecovery-natural),"recovery threshold did not apply to Kiiro; timer="+AccessTools.Field(kiiroTimer.GetType(),"timer").GetValue(kiiroTimer));
            Assert(S.players[1].BaseGoodwillWith(Milira)==-100,"recovery leaked to ordinary B");
            Assert((int)AccessTools.Field(kiiroTimer.GetType(),"timer").GetValue(kiiroTimer)==0,"recovery timer not reset");
            Assert(S.players[3].TryAffectGoodwillWith(Milira,-300,false,false),"restore Kiiro war failed");
            Log.Message("DPPROBE RECALC_AND_RECOVERY_PASS");
            var neutral=Find.FactionManager.AllFactionsListForReading.Where(f=>!f.IsPlayer&&f!=Milira&&f!=Church&&f.HasGoodwill&&
                !GoodwillSituationWorker_PermanentEnemy.ArePermanentEnemies(S.players[0],f)).OrderBy(f=>f.loadID).First();
            int otherBefore=S.players[1].BaseGoodwillWith(neutral);
            for(int i=0;i<3;i++) Assert(S.players[0].TryAffectGoodwillWith(neutral,10,false,false),"generic goodwill failed");
            Assert(S.players[1].BaseGoodwillWith(neutral)==otherBefore,"generic goodwill leaked to B");
            Assert(S.players[0].TryAffectGoodwillWith(S.players[1],100,false,false),"player pair gift failed");
            Assert(S.players[0].GoodwillWith(S.players[1])==100&&S.players[1].GoodwillWith(S.players[0])==100,"player pair max polluted");
            FactionContext.Push(S.players[3]);
            try {
                var home=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                home.Tile=TileFinder.RandomStartingTile();home.SetFaction(S.players[3]);Find.WorldObjects.Add(home);
                S.secondMap=GetOrGenerateMapUtility.GetOrGenerateMap(home.Tile,new IntVec3(100,1,100),null);
            } finally {FactionContext.Pop();}
            S.start = Clock; S.stepTick = Clock; S.stage=1;
            Log.Message("DPPROBE EXECUTED prepare");
        }
        catch(Exception e){ Fail(e); throw; }
    }
    public static void Negotiate()
    {
        try {
            // The installed mod ships this legacy callback without attaching its comp
            // to any Def. Exercise the state transition explicitly, never call it a UI test.
            var type=AccessTools.TypeByName("Meow.FactionDiplomacy.Patch_MiliraMultifactionRelations");
            var overallType=AccessTools.TypeByName("Milira.MiliraGameComponent_OverallControl");
            var overall=Current.Game.GetComponent(overallType);
            AccessTools.Method(type,"Set0").Invoke(null,new object[]{overall,true});
            AccessTools.Method(overallType,"CheckMiliraPermanentEnemyStatus").Invoke(overall,null);
            Milira.SetRelation(new FactionRelation(S.players[0],FactionRelationKind.Neutral){baseGoodwill=-60});
            AccessTools.Method(type,"AfterHandOver").Invoke(null,new object[]{true});
            Log.Message("DPPROBE EXECUTED synthetic negotiation");
        } catch(Exception e){Fail(e);throw;}
    }
    public static void Check(int stage)
    {
        try
        {
            if(stage==2)
            {
                Pair(S.players[0],Milira,false,-60);
                Pair(S.players[1],Milira,true,-100);
                Assert(S.players[0].CanChangeGoodwillFor(Milira,5),"negotiated gift still blocked");
                Assert(S.players[0].TryAffectGoodwillWith(Milira,5,false,false),"negotiated gift failed");
                Pair(S.players[0],Milira,false);
            }
            Pair(S.players[1],Milira,true,-100);
            Pair(S.players[2],Milira,false,0);
            Pair(S.players[2],Church,true,-100);
            Pair(S.players[3],Milira,false);
            Assert(S.players[3].HostileTo(Milira),"legitimate war erased");
            // Alternate explicit pair queries while executing as a third player.
            for(int i=0;i<3;i++) {
                Assert(S.players[2].GoodwillWith(Milira)==0,"Milira read polluted");
                Assert(S.players[1].GoodwillWith(Milira)==-100,"ordinary read polluted");
                Assert(S.players[0].GoodwillWith(Milira)<=-60,"thaw cap missing");
            }
            S.stage=stage; S.stepTick=Clock;
            Log.Message("DPPROBE EXECUTED check="+stage+" tick="+Clock+" desynced="+Mp.session.desynced);
        }
        catch(Exception e){Fail(e);throw;}
    }
    public static void Update()
    {
        if(!enabled)return;
        if(File.Exists(Path.Combine(root,"quit"))||(client&&File.Exists(Path.Combine(root,"quit-client")))){Application.Quit();return;}
        if(failed||done||Current.ProgramState!=ProgramState.Playing||LongEventHandler.ShouldWaitForEvent||Time.realtimeSinceStartup<next)return;
        next=Time.realtimeSinceStartup+0.15f;
        try
        {
            Assert(Time.realtimeSinceStartup<2400,"timeout");
            if(!client&&!hosted&&!MP.IsInMultiplayer)
            {
                hosted=true;
                Assert((bool)AccessTools.Property(AccessTools.TypeByName("Meow.FactionDiplomacy.Patch_MiliraMultifactionRelations"),"TargetAvailable").GetValue(null,null),"patch not active");
                GameDataSaveLoader.SaveGame("DiplomacyBaseline");
                Mp.username="DiplomacyHost";
                Assert(HostWindow.HostProgrammatically(new ServerSettings{gameName="Diplomacy0912",direct=true,
                    directAddress="127.0.0.1:30794",lan=false,steam=false,multifaction=true,asyncTime=false,
                    syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false}),"host rejected");
                return;
            }
            if(!MP.IsInMultiplayer || Mp.Client.State!=ConnectionStateEnum.ClientPlaying)return;
            Assert(!Mp.session.desynced,"desync");
            if(!client && File.Exists(Path.Combine(root,"joinpoint")))
            {
                File.Delete(Path.Combine(root,"joinpoint"));
                Mp.Client.Send(ClientChatPacket.Create("/joinpoint"));
                Log.Message("DPPROBE DISPATCH joinpoint"); return;
            }
            if(!client&&!freezeRequested&&File.Exists(Path.Combine(root,"freeze")))
            {
                freezeRequested=true;ready=false;Mp.Client.Send(new ClientFreezePacket(true));return;
            }
            if(!client&&freezeRequested&&TickPatch.Frozen&&!File.Exists(Path.Combine(root,"host.frozen")))
                File.WriteAllText(Path.Combine(root,"host.frozen"),Clock.ToString());
            if(!client&&!frozen){frozen=true;Mp.Client.Send(new ClientFreezePacket(true));return;}
            if(client&&!ready&&TickPatch.Simulating)return;
            if(client&&!ready){ready=true;if(S.stage>=3){for(int i=0;i<S.players.Count;i++)Pair(S.players[i],Milira,i==1);Log.Message("DPPROBE REJOIN_RECORDS_PASS stage="+S.stage+" maps="+Find.Maps.Count);}File.WriteAllText(Path.Combine(root,"client.ready"),"ready");Log.Message("DPPROBE CLIENT_LOADED_FROZEN");}
            if(!File.Exists(Path.Combine(root,"client.ready"))||Mp.session.players.Count!=2)return;
            if(!client&&!ready){ready=true;freezeRequested=false;if(File.Exists(Path.Combine(root,"freeze")))File.Delete(Path.Combine(root,"freeze"));Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);}
            if(S.stage>=3 && Clock-S.start>=120000)
            {
                done=true;Log.Message("DPPROBE COMPLETE tick="+Clock+" desynced="+Mp.session.desynced);
                File.WriteAllText(Path.Combine(root,client?"client.complete":"host.complete"),Clock.ToString());return;
            }
            if(!client)return;
            if(S.stage>=3&&!focusChanged){focusChanged=true;Current.Game.CurrentMap=S.secondMap;Log.Message("DPPROBE LOCAL_FOCUS map="+S.secondMap.uniqueID);}
            if(S.stage==0&&!requested){requested=true;Log.Message("DPPROBE DISPATCH prepare");Prepare();return;}
            if(S.stage==1&&Mp.RealPlayerFaction!=S.players[0]){Mp.Client.Send(new ClientSetFactionPacket(Mp.session.playerId,S.players[0].loadID));return;}
            if(S.stage==1&&!handed)
            {
                handed=true;
                Log.Message("DPPROBE DISPATCH synthetic negotiation fixture (installed hand-over comp is unattached)");Negotiate();return;
            }
            if(S.stage==1&&handed&&Clock-S.stepTick>400&&dispatched!=2){dispatched=2;Check(2);return;}
            if(S.stage==2&&Clock-S.stepTick>2000&&dispatched!=3){dispatched=3;Check(3);return;}
            if(S.stage>=3 && Clock-S.stepTick>10000 && dispatched!=S.stage+1){dispatched=S.stage+1;Check(dispatched);}
        }
        catch(Exception e){Fail(e);}
    }
}
