using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using Meow.FactionPurge;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class PurgeProbeState : GameComponent
{
    public int target = -1, host = -1, targetQuest=-1, retainedQuest=-1;
    public PurgeProbeState(Game game) { }
    public override void ExposeData() { Scribe_Values.Look(ref target,"purgeProbeTarget",-1); Scribe_Values.Look(ref host,"purgeProbeHost",-1); Scribe_Values.Look(ref targetQuest,"targetQuest",-1); Scribe_Values.Look(ref retainedQuest,"retainedQuest",-1); }
    public override void GameComponentUpdate() { PurgeProbe.Update(); }
}
[StaticConstructorOnStartup]
public static class PurgeProbe
{
    static bool enabled = GenCommandLine.CommandLineArgPassed("purgeprobe");
    static bool client = GenCommandLine.CommandLineArgPassed("purgeclient");
    static bool hosted, ready, failed, prepared, requested, testPause, testPauseThrow;
    static float next;
    static ISyncMethod prepareSync;
    static string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static PurgeProbeState S => Current.Game.GetComponent<PurgeProbeState>();
    static PurgeProbe()
    {
        if(!enabled) return;
        prepareSync=MP.RegisterSyncMethod(typeof(PurgeProbe),nameof(Prepare));
        new Harmony("meow.purgeprobe.diagnostics").Patch(AccessTools.Method(typeof(Root_Play), nameof(Root_Play.Update)),
            postfix: new HarmonyMethod(typeof(PurgeProbe), nameof(ObserveRoot)));
        Log.Message("PURGEPROBE START peer="+(client?"client":"host")+" mvid="+typeof(PurgeCommand).Assembly.ManifestModule.ModuleVersionId);
        if(!client) LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("PurgeBaseline"));
    }
    static void Assert(bool ok,string message) { if(!ok)throw new Exception(message); }
    static float nextRootTrace;
    static void ObserveRoot()
    {
        if(Time.realtimeSinceStartup<nextRootTrace)return;
        nextRootTrace=Time.realtimeSinceStartup+10;
        Log.Message("PURGEPROBE ROOT wait="+LongEventHandler.ShouldWaitForEvent+" pending="+AccessTools.Field(typeof(PurgeCommand),"Pending").GetValue(null)
            +" mutated="+AccessTools.Field(typeof(PurgeCommand),"Mutated").GetValue(null)+" result="+AccessTools.Field(typeof(PurgeCommand),"LastResult").GetValue(null)
            +" completion="+(Current.Game?.GetComponent<PurgeCompletion>()!=null));
    }
    public static void Update()
    {
        if(!enabled||failed||Time.realtimeSinceStartup<next||LongEventHandler.AnyEventNowOrWaiting)return;
        next=Time.realtimeSinceStartup+0.3f;
        if(File.Exists(Path.Combine(root,"quit"))) { Application.Quit(); return; }
        try
        {
            if(!hosted&&!client)
            {
                hosted=true;
                var settings=new ServerSettings {gameName="FactionPurge isolated",direct=true,directAddress="127.0.0.1:30931",lan=false,steam=false,multifaction=true,asyncTime=true,syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};
                Assert((bool)AccessTools.Method(typeof(HostWindow),"TryParseEndpoints").Invoke(null,new object[]{settings}),"endpoints");
                Assert((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}),"server");
                HostUtil.HostServer(settings,false); return;
            }
            if(Mp.Client==null||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            if(!client&&!prepared)
            {
                prepared=true;
                prepareSync.DoSync(null); return;
            }
            if(!client&&S.target>0&&!requested)
            {
                requested=true;
                Assert(!PurgePlan.Build(S.host,S.host).Allowed,"host faction must be protected");
                Assert(!PurgePlan.Build(Mp.WorldComp.spectatorFaction.loadID,S.host).Allowed,"spectator must be protected");
                var plan=PurgePlan.Build(S.target,S.host);
                Assert(plan.Allowed,"preflight: "+string.Join(" | ",plan.Blockers));
                Assert(plan.Bases.Count==2&&plan.Pawns.Count==2,"fixture counts");
                Assert(plan.Quests.Count==1&&plan.Quests[0].id==S.targetQuest,"quest ownership selection");
                // Exercise actual debug entry and actual request/backup/sync chain.
                var attribute=(DebugActionAttribute)Attribute.GetCustomAttribute(typeof(PurgeCommand).GetMethod("Open"),typeof(DebugActionAttribute));
                Assert(attribute!=null,"debug menu attribute absent");
                PurgeCommand.Open();
                Assert(Find.WindowStack.Windows.Any(w=>w is FloatMenu),"debug menu did not open");
                Find.WindowStack.TryRemove(typeof(FloatMenu));
                PurgeCommand.Preview(S.target);
                Assert(Find.WindowStack.Windows.Any(w=>w.GetType().Name=="ConfirmPurgeWindow"),"confirmation did not open");
                foreach(var w in Find.WindowStack.Windows.Where(w=>w.GetType().Name=="ConfirmPurgeWindow").ToArray())w.Close();
                PurgeCommand.Request(S.target,plan.Fingerprint);
                Log.Message("PURGEPROBE REQUEST faction="+S.target); return;
            }
            if(!client&&requested&&!ready&&Find.FactionManager.GetById(S.target)==null)
            {
                string[] saves=Directory.GetFiles(Mp.ReplaysDir,"After-FactionPurge-*.zip");
                if(saves.Length==0)return;
                Assert(Mp.WorldComp.factionData.ContainsKey(S.host),"host data lost");
                Assert(!Mp.WorldComp.factionData.ContainsKey(S.target),"target data left");
                Assert(Find.Maps.Count==2,"map still exists");
                Assert(!Find.QuestManager.QuestsListForReading.Any(q=>q.id==S.targetQuest),"target quest retained");
                Assert(Find.QuestManager.QuestsListForReading.Any(q=>q.id==S.retainedQuest),"other quest removed");
                ready=true;
                File.WriteAllText(Path.Combine(root,"purged.save"),saves.Single());
                Log.Message("PURGEPROBE PURGED maps="+Find.Maps.Count+" host="+S.host+" removed="+S.target);
            }
        }
        catch(Exception e) { failed=true;Log.Error("PURGEPROBE FAILED "+e);File.WriteAllText(Path.Combine(root,client?"client.failed":"host.failed"),e.ToString()); }
    }
    public static void Prepare()
    {
        LongEventHandler.QueueLongEvent(()=>
        {
            try
            {
                Rand.PushState(913100);
                try
                {
                    S.host=Faction.OfPlayer.loadID;
                    var creator=AccessTools.TypeByName("Multiplayer.Client.Factions.FactionCreator");
                    var method=AccessTools.Method(creator,"NewFactionWithIdeo");
                    var info=Activator.CreateInstance(method.GetParameters()[3].ParameterType,new object[]{null,null,null});
                    var faction=(Faction)method.Invoke(null,new object[]{"Purge target",Color.red,FactionDefOf.PlayerColony,info});
                    S.target=faction.loadID;
                    var settlement=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                    settlement.SetFaction(faction);
                    settlement.Tile=TileFinder.RandomStartingTile();
                    Find.WorldObjects.Add(settlement);
                    var generator=Find.CurrentMap.generatorDef;
                    using(new FactionScope(faction))
                    {
                        var map=MapGenerator.GenerateMap(new IntVec3(75,1,75),settlement,generator);
                        var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,faction,forceGenerateNewPawn:true));
                        GenSpawn.Spawn(pawn,map.Center,map);
                        var hostMap=Find.Maps.First(m=>m.ParentFaction.loadID==S.host);
                        var visitor=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Find.FactionManager.GetById(S.host),forceGenerateNewPawn:true));
                        var casket=(Building_CryptosleepCasket)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("CryptosleepCasket"));
                        GenSpawn.Spawn(casket,map.Center+new IntVec3(4,0,0),map);
                        Assert(casket.TryAcceptThing(visitor,false),"casket setup");
                        Assert(!PurgePlan.Build(S.target,S.host).Allowed,"nested foreign pawn must block deletion");
                        casket.EjectContents();visitor.DeSpawn();GenSpawn.Spawn(visitor,hostMap.Center,hostMap);
                        casket.Destroy(DestroyMode.Vanish);
                        var marker=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);
                        marker.SetFaction(faction);GenSpawn.Spawn(marker,hostMap.Center+new IntVec3(5,0,0),hostMap);
                        Assert(!PurgePlan.Build(S.target,S.host).Allowed,"target asset on retained map must block");
                        marker.Destroy(DestroyMode.Vanish);
                        var held=hostMap.GetComponent<PurgeProbeReference>();held.faction=faction;
                        Assert(!PurgePlan.Build(S.target,S.host).Allowed,"foreign MapComponent reference must block");
                        held.faction=null;
                        var lockType=typeof(Multiplayer.Client.Persistent.PauseLockSession);
                        bool hasLock=false;
                        foreach(var session in Mp.WorldComp.sessionManager.AllSessions) if(session.GetType()==lockType)hasLock=true;
                        if(!hasLock)Mp.WorldComp.sessionManager.AddSession(new Multiplayer.Client.Persistent.PauseLockSession(null));
                        var pauseDelegate=new PauseLockDelegate(TestPause);
                        Multiplayer.Client.Persistent.PauseLockSession.pauseLocks.Add(pauseDelegate);
                        var windows=Find.WindowStack.Windows.ToArray();testPause=true;
                        PurgeCommand.Open();
                        var refusal=Find.WindowStack.Windows.FirstOrDefault(w=>!windows.Contains(w)&&w is Dialog_MessageBox);
                        Assert(refusal!=null,"active legacy pause must refuse admin action");refusal.Close();
                        testPause=false;testPauseThrow=true;
                        windows=Find.WindowStack.Windows.ToArray();PurgeCommand.Open();
                        refusal=Find.WindowStack.Windows.FirstOrDefault(w=>!windows.Contains(w)&&w is Dialog_MessageBox);
                        Assert(refusal!=null,"throwing legacy callback must refuse without leaking exception");refusal.Close();
                        testPauseThrow=false;Multiplayer.Client.Persistent.PauseLockSession.pauseLocks.Remove(pauseDelegate);
                        Log.Message("PURGEPROBE LEGACY activeRefused=True callbackFaultRefused=True idleRegistryRetained=True");
                        var numericPlan=PurgePlan.Build(S.target,S.host);
                        File.WriteAllText(Path.Combine(root,"preflight-blockers.txt"),string.Join("\n",numericPlan.Blockers));
                        File.WriteAllText(Path.Combine(root,"fixture-pawns.txt"),string.Join("\n",PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Where(p=>p.Faction?.IsPlayer==true).Select(p=>p.ThingID+" faction="+p.Faction.loadID+" map="+p.MapHeld?.uniqueID+" "+p.LabelShort)));
                        Assert(numericPlan.Allowed,"numeric scan preflight: "+string.Join(" | ",numericPlan.Blockers));
                        S.targetQuest=MakeQuest(settlement,"Target fixture quest").id;
                        S.retainedQuest=MakeQuest(hostMap.Parent,"Retained fixture quest").id;
                        Log.Message("PURGEPROBE NEGATIVE nestedPawn=True externalAsset=True mapComponent=True");
                    }
                    foreach(var owner in new[]{faction,Find.FactionManager.GetById(S.host)})
                    {
                        var extra=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                        extra.SetFaction(owner);extra.Tile=TileFinder.RandomStartingTile();Find.WorldObjects.Add(extra);
                        using(new FactionScope(owner))
                        {
                            var extraMap=MapGenerator.GenerateMap(new IntVec3(75,1,75),extra,generator);
                            var extraPawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,owner,forceGenerateNewPawn:true));
                            GenSpawn.Spawn(extraPawn,extraMap.Center,extraMap);
                        }
                    }
                    Mp.game.ChangeRealPlayerFaction(Find.FactionManager.GetById(S.host),false);
                    Log.Message("PURGEPROBE PREPARED target="+S.target+" maps="+Find.Maps.Count);
                }
                finally {Rand.PopState();}
            }
            catch(Exception e){failed=true;Log.Error("PURGEPROBE FAILED prepare "+e);File.WriteAllText(Path.Combine(root,"host.failed"),e.ToString());}
        },"Preparing purge fixture",false,null);
    }
    static bool TestPause(Map map){if(testPauseThrow)throw new InvalidOperationException("Fixture pause callback failure");return testPause;}
    static Quest MakeQuest(MapParent parent,string name)
    {
        var quest=new Quest{id=Find.UniqueIDsManager.GetNextQuestID(),root=DefDatabase<QuestScriptDef>.AllDefsListForReading.First(),name=name};
        var part=new QuestPart_PawnsArrive();
        AccessTools.Field(part.GetType(),"mapParent").SetValue(part,parent);
        quest.AddPart(part);
        Find.QuestManager.Add(quest);
        Find.LetterStack.ReceiveLetter(name,"Fixture quest notice",LetterDefOf.NeutralEvent,null,null,quest);
        return quest;
    }
    sealed class FactionScope:IDisposable
    {
        Faction previous;
        public FactionScope(Faction faction){previous=Mp.RealPlayerFaction;Mp.game.ChangeRealPlayerFaction(faction,false);}
        public void Dispose(){Mp.game.ChangeRealPlayerFaction(previous,false);}
    }
}
public sealed class PurgeProbeReference:MapComponent
{
    public Faction faction;
    public IntPtr native = new IntPtr(1);
    public Vector3[] particles = new Vector3[60001];
    public PurgeProbeReference(Map map):base(map){}
    public override void ExposeData(){Scribe_References.Look(ref faction,"testFaction");}
}
