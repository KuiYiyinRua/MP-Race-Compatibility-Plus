using System;
using System.Collections.Generic;
using System.Globalization;
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

public sealed class SmeltedTraceState : GameComponent
{
    public int Cycle=-1, Start=-1, Round, Shots, Hits;
    public List<int> FixturePawns = new List<int>();
    public Dictionary<int,int> StageShots = new Dictionary<int,int>(), StageHits = new Dictionary<int,int>();
    public SmeltedTraceState(Game g) { }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref Cycle,"cycle",-1); Scribe_Values.Look(ref Start,"start",-1);
        Scribe_Values.Look(ref Round,"round"); Scribe_Values.Look(ref Shots,"shots"); Scribe_Values.Look(ref Hits,"hits");
        Scribe_Collections.Look(ref FixturePawns,"fixturePawns",LookMode.Value);
        Scribe_Collections.Look(ref StageShots,"stageShots",LookMode.Value,LookMode.Value);
        Scribe_Collections.Look(ref StageHits,"stageHits",LookMode.Value,LookMode.Value);
    }
    public override void GameComponentUpdate() => SmeltedTraceProbe.Update();
}

[StaticConstructorOnStartup]
public static class SmeltedTraceProbe
{
    static readonly bool Enabled=GenCommandLine.CommandLineArgPassed("smeltedtraceprobe");
    static readonly bool Client=GenCommandLine.CommandLineArgPassed("slclient");
    static readonly string Root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static readonly int CycleArg=GenCommandLine.TryGetCommandLineArg("slcycle",out var cycleText)?int.Parse(cycleText):0;
    static readonly bool Beacon=GenCommandLine.CommandLineArgPassed("milianbeacon");
    [ThreadStatic] static Stack<string> Generating;
    static readonly bool Soak=GenCommandLine.CommandLineArgPassed("slsoak");
    static readonly PropertyInfo RandState=AccessTools.Property(typeof(Rand),"StateCompressed");
    static Type Smart;
    static MethodInfo Draw, BPos;
    static FieldInfo Offset;
    static ISyncMethod BeginMethod, RoundMethod;
    static bool LoadingReplay, LoadedReplay;
    static bool Hosted, Ready, SentBegin, SentRound, Failed, Done;
    static int LocalCycle=CycleArg, AdvanceUntil=-1, Checkpoint, JoinPointStage, PauseUntil;
    static float NextFocus;
    static string Peer => Client?"client"+CycleArg:"host";
    static SmeltedTraceState S => Current.Game.GetComponent<SmeltedTraceState>();
    static string FileAt(string name) => Path.Combine(Root,name);
    static ulong Rng => (ulong)RandState.GetValue(null);
    static void Check(bool ok,string reason) { if(!ok)throw new Exception(reason); }
    static string Snapshot() => Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";"+
        string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState));
    static void Seed() => Rand.PushState(261001);
    static void Unseed() => Rand.PopState();
    static SmeltedTraceProbe()
    {
        if(!Enabled)return;
        Smart=AccessTools.TypeByName("SmeltedLoong.Bullet_SmartTrace");
        Check(Smart!=null,"target assembly absent");
        Draw=AccessTools.DeclaredMethod(Smart,"DrawAt"); BPos=AccessTools.DeclaredMethod(Smart,"BPos");
        Offset=AccessTools.DeclaredField(Smart,"randOffset");
        BeginMethod=MP.RegisterSyncMethod(typeof(SmeltedTraceProbe),nameof(Begin));
        RoundMethod=MP.RegisterSyncMethod(typeof(SmeltedTraceProbe),nameof(Exercise));
        var h=new Harmony("local.meow.smeltedtraceprobe");
        h.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(Update)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(Seed)),finalizer:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(Unseed)));
        h.Patch(AccessTools.DeclaredMethod(Smart,"InitRandOffset"),postfix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(Initialized)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("SmeltedLoong.Verb_FateShoot"),"TryCastShot"),postfix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(Shot)));
        h.Patch(AccessTools.DeclaredMethod(typeof(Bullet),"Impact"),prefix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(Impact)));
        if(Beacon)
        {
            foreach(var name in new[]{"Milira.Milira_MilianPawnGenerator_Patch","MilianModification.Milira_MilianPawnGenerator_Patch","PLAMilira.PLAMilira_MilianPawnGenerator_Patch"})
                h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName(name),"Postfix"),prefix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(GenerationEnter)),finalizer:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(GenerationExit)));
            h.Patch(AccessTools.PropertyGetter(typeof(Map),"PlayerWealthForStoryteller"),prefix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(WealthRead)));
            var threat=typeof(StorytellerUtility).GetMethods(BindingFlags.Static|BindingFlags.Public).Single(m=>m.Name=="DefaultThreatPointsNow"&&m.GetParameters()[0].ParameterType==typeof(IIncidentTarget));
            h.Patch(threat,prefix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(ThreatRead)));
            h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("Milira.CompDelayedPawnSpawnOnWakeup"),"GeneratePawns"),postfix:new HarmonyMethod(typeof(SmeltedTraceProbe),nameof(Generated)));
        }
        Log.Message("SL RUN label="+Path.GetFileName(Root)+" peer="+Peer+" hashes="+File.ReadAllText(FileAt("candidate.sha256"))+
            " coreMvid="+AccessTools.TypeByName("MP_MeowOnlineShop.Patch_SmeltedLoongMp").Module.ModuleVersionId+" nativeMvid="+Smart.Module.ModuleVersionId);
    }
    static void GenerationEnter(MethodBase __originalMethod)
    {
        if(Generating==null)Generating=new Stack<string>(); Generating.Push(__originalMethod.DeclaringType.FullName);
    }
    static Exception GenerationExit(Exception __exception) { Generating.Pop();return __exception; }
    static void WealthRead(Map __instance) => GenerationRead(__instance,"wealth");
    static void ThreatRead(IIncidentTarget __0) => GenerationRead(__0 as Map,"threat");
    static void GenerationRead(Map actual,string field)
    {
        if(!MP.IsInMultiplayer||Generating==null||Generating.Count==0)return;
        var expected=Generating.Peek().StartsWith("PLAMilira.")?Find.Maps.Where(m=>m.IsPlayerHome).OrderBy(m=>m.uniqueID).FirstOrDefault():Mp.MapContext??Find.Maps.Where(m=>m.IsPlayerHome).OrderBy(m=>m.uniqueID).FirstOrDefault()??Find.Maps.OrderBy(m=>m.uniqueID).First();
        Log.Message("MB MAP_READ cycle="+S.Cycle+" round="+S.Round+" generator="+Generating.Peek()+" field="+field+" actual="+(actual?.uniqueID??-1)+" expected="+expected.uniqueID+" view="+Find.CurrentMap.uniqueID);
        Check(actual==expected,"GENERATION_USED_VIEW_MAP");
    }
    static void Generated(object __instance,List<Thing> __result)
    {
        var comp=(ThingComp)__instance;
        if(!MP.IsInMultiplayer||comp.parent.def.defName!="Milira_DropBeacon")return;
        Check(__result.Count>0,"beacon generated zero reinforcements");
        foreach(Pawn p in __result)
        {
            S.FixturePawns.Add(p.thingIDNumber);
            Log.Message("MB PAWN cycle="+S.Cycle+" round="+S.Round+" map="+comp.parent.Map.uniqueID+" id="+p.thingIDNumber+" kind="+p.kindDef.defName+" hediffs="+string.Join(",",p.health.hediffSet.hediffs.Select(h=>h.def.defName))+
                " weapon="+(p.equipment.Primary?.def.defName??"none")+" apparel="+string.Join(",",p.apparel.WornApparel.Select(a=>a.def.defName)));
        }
        Log.Message("MB GENERATED cycle="+S.Cycle+" round="+S.Round+" map="+comp.parent.Map.uniqueID+" count="+__result.Count);
    }
    static void Initialized(Thing __instance)
    {
        if(!Enabled||S==null)return;
        var o=(Vector3)Offset.GetValue(__instance);
        Log.Message("SL OFFSET id="+__instance.thingIDNumber+" x="+o.x.ToString("R",CultureInfo.InvariantCulture)+" z="+o.z.ToString("R",CultureInfo.InvariantCulture));
    }
    static void Shot(Verb __instance,bool __result)
    {
        if(!Enabled||S==null||S.Start<0||!__result)return;
        S.Shots++;int id=__instance.Caster.Map.uniqueID;
        S.StageShots.TryGetValue(id,out var count);S.StageShots[id]=count+1;
    }
    static void Impact(Bullet __instance, Thing hitThing)
    {
        if(!Enabled||S==null||__instance.def.defName!="NY_LoongBellTone")return;
        if(hitThing is Pawn p && p.RaceProps.IsMechanoid)
        {
            S.Hits++;int id=__instance.Map.uniqueID;S.StageHits.TryGetValue(id,out var count);S.StageHits[id]=count+1;
        }
        Log.Message("SL IMPACT projectile="+__instance.thingIDNumber+" hit="+(hitThing?.thingIDNumber??-1)+" def="+(hitThing?.def.defName??"none"));
    }
    public static void Begin(int cycle)
    {
        S.Cycle=cycle; S.Start=TickPatch.Timer; S.Round=0; S.Shots=0; S.Hits=0;
        S.StageShots.Clear();S.StageHits.Clear();
        Log.Message("SL BEGIN cycle="+cycle+" tick="+S.Start+" maps="+Find.Maps.Count);
    }
    public static void Exercise(int cycle,int round)
    {
        Check(cycle==S.Cycle&&round==S.Round+1,"round ordering");
        if(S.Round>0)AssertStage();
        S.StageShots.Clear();S.StageHits.Clear();
        foreach(var map in TestMaps())
        {
            foreach(var pawn in map.mapPawns.AllPawnsSpawned.Where(p=>S.FixturePawns.Contains(p.thingIDNumber)).ToArray()){pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);pawn.DeSpawn();Find.WorldPawns.PassToWorld(pawn,PawnDiscardDecideMode.KeepForever);}
            var origin=map.AllCells.OrderBy(c=>c.z).ThenBy(c=>c.x).First(c=>c.x>=10&&c.z>=10&&c.x<map.Size.x-35&&c.z<map.Size.z-30&&GenRadial.RadialCellsAround(c+new IntVec3(0,0,12),4f,true).All(p=>p.InBounds(map)&&p.Standable(map)&&!p.GetThingList(map).Any(t=>t is Building))&&
                GenSight.PointsOnLineOfSight(c,c+new IntVec3(24,0,0)).All(p=>p.Standable(map)&&!p.GetThingList(map).Any(t=>t is Building)));
            var dest=origin+new IntVec3(24,0,0);
            foreach(var cell in GenSight.PointsOnLineOfSight(origin,dest))
                foreach(var t in cell.GetThingList(map).ToArray())
                    if(t is Plant||t is Building)t.Destroy(DestroyMode.Vanish);
            var shooter=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,mustBeCapableOfViolence:true,canGeneratePawnRelations:false));
            GenSpawn.Spawn(shooter,origin,map); shooter.drafter.Drafted=true;
            var target=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Mech_Scyther"),Faction.OfMechanoids,forceGenerateNewPawn:true));
            GenSpawn.Spawn(target,dest,map);
            S.FixturePawns.Add(shooter.thingIDNumber); S.FixturePawns.Add(target.thingIDNumber);
            var wait=JobMaker.MakeJob(JobDefOf.Wait); wait.expiryInterval=700; target.jobs.StartJob(wait,JobCondition.InterruptForced);
            var weapon=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("NY_LoongBell"));
            shooter.equipment.AddEquipment(weapon);
            var projectile=(Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("NY_LoongBellTone"),origin,map);
            projectile.Launch(shooter,origin.ToVector3Shifted(),target,target,ProjectileHitFlags.IntendedTarget,false,weapon);
            if(Beacon)
            {
                var faction=Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("Milira_Faction"));
                var owner=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Milian_Mechanoid_KnightI"),faction,forceGenerateNewPawn:true));
                S.FixturePawns.Add(owner.thingIDNumber);
                var spot=origin+new IntVec3(0,0,12); GenSpawn.Spawn(owner,spot,map);
                var pack=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Milira_DropBeaconPack"));owner.apparel.Wear(pack);
                var reloadable=pack.AllComps.First(c=>c.GetType().FullName=="AncotLibrary.CompApparelReloadable_DeployThing");
                var deploy=((CompApparelVerbOwner)reloadable).VerbTracker.PrimaryVerb;
                Check(deploy.TryStartCastOn(owner),"native beacon verb failed");
                Check(map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Milira_DropBeacon")).Any(t=>t.Faction==faction),"native beacon missing");
                Log.Message("MB DEPLOY cycle="+cycle+" round="+round+" map="+map.uniqueID+" owner="+owner.thingIDNumber);
            }
            var before=Rng;
            if(!Client)Draw.Invoke(projectile,new object[]{projectile.DrawPos,false});
            var after=Rng;
            Log.Message("SL DRAW_RNG cycle="+cycle+" round="+round+" map="+map.uniqueID+" localDraw="+(!Client)+" before="+before+" after="+after);
            Check(before==after,"DRAW_CONSUMED_SHARED_RAND");
            // Re-enter the same native curve method in both sync-command and
            // interface contexts. The command must not exempt random isolation.
            BPos.Invoke(projectile,new object[]{0.25f}); Check(before==Rng,"CURVE_CONSUMED_SHARED_RAND");
            var o=(Vector3)Offset.GetValue(projectile);
            Log.Message("SL ASSERT cycle="+cycle+" round="+round+" map="+map.uniqueID+" projectile="+projectile.thingIDNumber+" mech="+target.thingIDNumber+
                " offset="+o.x.ToString("R",CultureInfo.InvariantCulture)+","+o.z.ToString("R",CultureInfo.InvariantCulture)+" rng="+Rng);
            var attack=JobMaker.MakeJob(JobDefOf.AttackStatic,target); attack.expiryInterval=650;
            shooter.jobs.TryTakeOrderedJob(attack,JobTag.Misc);
        }
        S.Round=round;
        Log.Message("SL EXECUTED cycle="+cycle+" round="+round+" tick="+TickPatch.Timer);
    }
    static void AssertStage()
    {
        foreach(var map in TestMaps())
        {
            S.StageShots.TryGetValue(map.uniqueID,out var shots);S.StageHits.TryGetValue(map.uniqueID,out var hits);
            Check(shots>0&&hits>0,"native fire/impact missing on map="+map.uniqueID+" round="+S.Round);
            Log.Message("SL STAGE cycle="+S.Cycle+" round="+S.Round+" map="+map.uniqueID+" shots="+shots+" mechHits="+hits);
        }
    }
    static Map[] TestMaps() => Find.Maps.Where(m=>!m.IsPocketMap&&m.Size.x>=60&&m.Size.z>=60).OrderBy(m=>m.uniqueID).ToArray();
    static bool AllPlaying()
    {
        foreach(var player in Mp.session.players)
            if(AccessTools.Field(player.GetType(),"status").GetValue(player).ToString()!="Playing")return false;
        return true;
    }
    public static void Update()
    {
        if(!Enabled)return;
        if(File.Exists(FileAt("quit"))||Client&&File.Exists(FileAt(Peer+".quit"))){Application.Quit();return;}
        if(Failed||(Current.ProgramState!=ProgramState.Playing&&Current.ProgramState!=ProgramState.Entry)||LongEventHandler.AnyEventNowOrWaiting)return;
        try
        {
            if(!Client&&!LoadingReplay&&Current.ProgramState==ProgramState.Entry)
            {
                LoadingReplay=true;Mp.username="MilianReplayHost";
                ClientUtil.DoubleLongEvent(()=>Replay.LoadReplay(new FileInfo(FileAt("Input.zip")),true,
                    ()=>{LoadedReplay=true;Log.Message("MB REPLAY_LOADED "+Snapshot());},
                    ()=>{throw new Exception("Replay load cancelled");}),"MpLoading");return;
            }
            if(!Client&&!Hosted&&LoadedReplay&&Current.ProgramState==ProgramState.Playing&&!TickPatch.Simulating)
            {
                Hosted=true;Check(Mp.WorldComp.spectatorFaction!=null,"BASELINE_INVALID missing spectator faction");
                Mp.AsyncWorldTime.SetTimeEverywhere(TimeSpeed.Paused);
                var settings=new ServerSettings{gameName="MilianReplay",direct=true,directAddress="127.0.0.1:31040",lan=false,steam=false,multifaction=false,asyncTime=false,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};
                Check((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}),"replay host init failed");
                Check(Mp.LocalServer!=null,"replay server absent");HostUtil.HostServer(settings,true);return;
            }
            if(!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            Check(!Mp.session.desynced,"desync");
            if(!Client&&File.Exists(FileAt("cycle.txt")))
            {
                var next=int.Parse(File.ReadAllText(FileAt("cycle.txt")));
                if(next!=LocalCycle)
                {
                    Check(Done,"transition before complete");LocalCycle=next;Ready=Done=SentBegin=SentRound=false;
                    AdvanceUntil=TickPatch.Timer+200; JoinPointStage=0; Mp.Client.Send(new ClientFreezePacket(false));
                    Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);
                    foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)TimeSpeed.Superfast);
                    Log.Message("SL HOST_ONLY_ADVANCE cycle="+next+" from="+TickPatch.Timer+" until="+AdvanceUntil);return;
                }
            }
            if(!Client&&AdvanceUntil>=0)
            {
                if(TickPatch.Timer<AdvanceUntil)return;
                if(JoinPointStage==0)
                {
                    // Joining requires a new authoritative snapshot. Pause
                    // physical simulation while server commands keep running;
                    // a frozen server cannot execute CreateJoinPoint.
                    Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Paused);
                    foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)TimeSpeed.Paused);
                    PauseUntil=TickPatch.Timer+30;JoinPointStage=1;return;
                }
                if(JoinPointStage==1)
                {
                    if(TickPatch.Timer<PauseUntil)return;
                    Check(Mp.LocalServer.worldData.TryStartJoinPointCreation(true),"manual join point rejected");
                    JoinPointStage=2;Log.Message("SL JOIN_POINT_REQUESTED cycle="+LocalCycle);return;
                }
                if(Mp.LocalServer.worldData.CreatingJoinPoint)return;
                if(!TickPatch.Frozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
                File.WriteAllText(FileAt("host.frozen"+LocalCycle),Snapshot());AdvanceUntil=-1;
                Log.Message("SL HOST_ADVANCED_FROZEN cycle="+LocalCycle+" "+Snapshot());return;
            }
            if(Done)return;
            if(!Client&&!Ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
            if(Client&&!Ready)
            {
                if(!TickPatch.Frozen||Mp.session.players.Count!=2||!AllPlaying())return;
                Ready=true;File.WriteAllText(FileAt("client.ready"+CycleArg),Snapshot());
                Log.Message("SL CLIENT_LOADED_FROZEN cycle="+CycleArg+" "+Snapshot());
            }
            if(Mp.session.players.Count!=2||!AllPlaying()||!File.Exists(FileAt("client.ready"+LocalCycle)))return;
            if(!Client&&!Ready)
            {
                Check(Snapshot()==File.ReadAllText(FileAt("client.ready"+LocalCycle)),"BASELINE_LOAD_DRIFT");
                Log.Message("SL BASELINE_EQUAL cycle="+LocalCycle+" "+Snapshot());Ready=true;
                Mp.Client.Send(new ClientFreezePacket(false));
                Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);
                foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)TimeSpeed.Superfast);
            }
            if(TickPatch.Frozen)return;
            if(S.Cycle!=LocalCycle)
            {
                if(Client&&!SentBegin){Log.Message("SL DISPATCH begin cycle="+LocalCycle);SentBegin=BeginMethod.DoSync(null,LocalCycle);}return;
            }
            int elapsed=TickPatch.Timer-S.Start;
            if(S.Round<3&&elapsed>=500+S.Round*1000)
            {
                if(Client&&!SentRound){Log.Message("SL DISPATCH round="+(S.Round+1)+" cycle="+LocalCycle);SentRound=RoundMethod.DoSync(null,LocalCycle,S.Round+1);}return;
            }
            SentRound=false;
            if(Client&&Time.realtimeSinceStartup>=NextFocus)
            {
                Current.Game.CurrentMap=Beacon?TestMaps().Last():Find.Maps[(Current.Game.CurrentMap==Find.Maps[0]?1:0)];NextFocus=Time.realtimeSinceStartup+1f;
            }
            if(elapsed>=Checkpoint){Log.Message("SL CHECKPOINT cycle="+LocalCycle+" elapsed="+elapsed+" tick="+TickPatch.Timer+" shots="+S.Shots+" mechHits="+S.Hits+" players=2 desynced=False");Checkpoint=elapsed+5000;}
            int duration=Soak&&LocalCycle==3?130000:10000;
            if(elapsed>=duration)
            {
                Check(S.Round==3&&S.Shots>=6&&S.Hits>=6,"native fire/impact incomplete");
                AssertStage();
                Log.Message("SL FUNCTIONAL cycle="+LocalCycle+" shots="+S.Shots+" mechHits="+S.Hits+" rounds="+S.Round);
                Log.Message("SL COMPLETE cycle="+LocalCycle+" elapsed="+elapsed+" desynced=False");
                Done=true;File.WriteAllText(FileAt(Peer+".complete"+LocalCycle),"PASS "+TickPatch.Timer);
                if(!Client)Mp.Client.Send(new ClientFreezePacket(true));
            }
        }
        catch(Exception e){Failed=true;File.WriteAllText(FileAt(Peer+".failed"),e.ToString());Log.Error("SL FAILED "+e);}
    }
}
