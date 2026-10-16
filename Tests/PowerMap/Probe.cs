using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
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
using Mp=Multiplayer.Client.Multiplayer;

public sealed class MoteProbeComponent : GameComponent
{
    public int round=-1,until,startWorld,startServer;
    public List<int> starts=new List<int>();
    public MoteProbeComponent(Game game) { }
    public override void GameComponentUpdate() => MoteProbe.Update();
    public override void ExposeData(){Scribe_Values.Look(ref round,"moteRound",-1);Scribe_Values.Look(ref until,"moteUntil");Scribe_Values.Look(ref startWorld,"moteStartWorld");Scribe_Values.Look(ref startServer,"moteStartServer");Scribe_Collections.Look(ref starts,"moteMapStarts",LookMode.Value);}
}

[StaticConstructorOnStartup]
public static class MoteProbe
{
    static readonly bool enabled=GenCommandLine.CommandLineArgPassed("powerprobe");
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static readonly bool minimal=GenCommandLine.CommandLineArgPassed("moteminimal");
    static readonly bool client=GenCommandLine.CommandLineArgPassed("moteclient");
    static readonly bool baseline=GenCommandLine.CommandLineArgPassed("motebaseline");
    static readonly bool asyncTime=GenCommandLine.CommandLineArgPassed("moteasync");
    static readonly bool twoMaps=GenCommandLine.CommandLineArgPassed("motetwomaps");
    static readonly bool legacyPower=GenCommandLine.CommandLineArgPassed("powerlegacy");
    static readonly bool commander=GenCommandLine.CommandLineArgPassed("motecommander");
    static ISyncMethod emit;
    static bool ready,sent,completed,freezeSent;
    static readonly bool rejoin=GenCommandLine.CommandLineArgPassed("powerrejoin");
    static int cycle=GenCommandLine.TryGetCommandLineArg("powercycle",out string cycleArg)?int.Parse(cycleArg):0;
    static readonly string port=GenCommandLine.TryGetCommandLineArg("powerport",out string portArg)?portArg:"31183";
    static int checkedRound=-1,checkedAgeRound=-1,checkpoint;
    static bool captureStarted;
    static int localRound=-1;
    static readonly Dictionary<int,int> localStarts=new Dictionary<int,int>();
    static readonly Dictionary<int,FleckDef[]> fixtures=new Dictionary<int,FleckDef[]>();
    static readonly Dictionary<int,int> mapCalls=new Dictionary<int,int>();
    static readonly Dictionary<int,int> fleckCalls=new Dictionary<int,int>();
    static readonly Dictionary<int,int> powerCalls=new Dictionary<int,int>();
    static readonly FieldInfo powerMap=AccessTools.Field(typeof(PowerNetManager),"map");
    static string PowerState()=>string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+string.Join(",",m.listerThings.AllThings.Where(t=>t.def.defName=="WoodFiredGenerator"||t.def.defName=="StandingLamp").OrderBy(t=>t.thingIDNumber).Select(t=>t.ThingID+":"+t.TryGetComp<CompPowerTrader>().PowerOn))));
    static void PowerTick(PowerNetManager __instance)
    {
        var map=(Map)powerMap.GetValue(__instance);
        if(!MP.IsInMultiplayer)return;
        Check(ReferenceEquals(AsyncTimeComp.tickingMap,map),"Power tick outside owning map context");
        powerCalls.TryGetValue(map.uniqueID,out int n);powerCalls[map.uniqueID]=n+1;
    }
    static void BuildPowerFixtures(int round)
    {
        Type ct=AccessTools.TypeByName("MP_MeowOnlineShop.MultifactionTpsOptimizeComponent");
        object policy=Current.Game.components.First(c=>c.GetType()==ct);
        AccessTools.Field(ct,"experimentalTickSchedulerEnabled").SetValue(policy,true);
        AccessTools.Field(ct,"schedulerMode").SetValue(policy,round%3);
        AccessTools.Field(ct,"aggressiveIdleBaseIntervalTicks").SetValue(policy,160);
        AccessTools.Field(ct,"aggressiveIdlePerMapFactorTicks").SetValue(policy,26);
        foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
        {
            var origin=map.Center+new IntVec3(8,0,0);
            for(int x=-2;x<=12;x++)for(int z=-2;z<=3;z++)
            {
                var cell=origin+new IntVec3(x,0,z);
                foreach(var t in cell.GetThingList(map).ToArray())if(!(t is Pawn))t.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
            }
            var gen=GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodFiredGenerator")),origin,map);
            gen.SetFaction(Faction.OfPlayer);gen.TryGetComp<CompRefuelable>().Refuel(75);
            for(int x=2;x<=9;x++){var wire=GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PowerConduit")),origin+new IntVec3(x,0,0),map);wire.SetFaction(Faction.OfPlayer);}
            var lamp=GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("StandingLamp")),origin+new IntVec3(10,0,0),map);lamp.SetFaction(Faction.OfPlayer);
            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            Check(lamp.TryGetComp<CompPowerTrader>().PowerNet!=null,"fixture lamp not connected");
            Check(ReferenceEquals(gen.TryGetComp<CompPowerTrader>().PowerNet,lamp.TryGetComp<CompPowerTrader>().PowerNet),"fixture separate grids");
            Log.Message("POWER_PROBE WIRED round="+round+" map="+map.uniqueID+" generator="+gen.ThingID+" lamp="+lamp.ThingID);
        }
    }
    static MoteProbeComponent State=>Current.Game.GetComponent<MoteProbeComponent>();
    static string Peer=>client?"client":"host";
    static string At(string n)=>Path.Combine(root,n);
    static bool loading,loaded,hosted,failed;
    static int fogTicks,fogSpawns;
    static PropertyInfo randState;
    static Type fogType;
    static float started=-1,nextSample;
    static readonly Dictionary<Mote,int> calls=new Dictionary<Mote,int>();
    static readonly Dictionary<string,int> spawns=new Dictionary<string,int>();
    static MoteProbe()
    {
        if(!enabled)return;
        var h=new Harmony("local.meow.moteprobe");
        h.Patch(AccessTools.Method(typeof(PowerNetManager),nameof(PowerNetManager.PowerNetsTick)),prefix:new HarmonyMethod(typeof(MoteProbe),nameof(PowerTick)));
        h.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(MoteProbe),nameof(Update)));
        h.Patch(AccessTools.Method(typeof(Mote),nameof(Mote.SpawnSetup)),postfix:new HarmonyMethod(typeof(MoteProbe),nameof(Spawn)));
        h.Patch(AccessTools.Method(typeof(Thing),nameof(Thing.DoTick)),prefix:new HarmonyMethod(typeof(MoteProbe),nameof(Tick)));
        h.Patch(AccessTools.Method(typeof(FleckManager),nameof(FleckManager.FleckManagerTick)),prefix:new HarmonyMethod(typeof(MoteProbe),nameof(FleckTick)));
        h.Patch(AccessTools.Method(typeof(Map),nameof(Map.MapPostTick)),prefix:new HarmonyMethod(typeof(MoteProbe),nameof(MapTick)){priority=Priority.First});
        if(minimal){emit=MP.RegisterSyncMethod(typeof(MoteProbe),nameof(Emit));h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(MoteProbe),nameof(Seed)),finalizer:new HarmonyMethod(typeof(MoteProbe),nameof(Unseed)));}
        if(commander)
        {
            fogType=AccessTools.TypeByName("Nivarian.HediffComp_NivarianVisual") ?? throw new TypeLoadException("native icy fog");
            randState=AccessTools.Property(typeof(Rand),"StateCompressed");
            h.Patch(AccessTools.Method(typeof(HediffWithComps),"PostTick"),prefix:new HarmonyMethod(typeof(MoteProbe),nameof(FogBefore)){priority=Priority.First},finalizer:new HarmonyMethod(typeof(MoteProbe),nameof(FogAfter)){priority=Priority.Last});
            RequireOwner("Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_MovingBase","OrbitTarget","local.nivarian.commander.native-orbit-random");
            RequireOwner("Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret","CompTick","local.nivarian.commander.native-turret-idle-random");
        }
        var core=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_MoteLifecycleMp");
        Log.Message("MOTE_PROBE BOOT label="+Path.GetFileName(root)+" peer="+Peer+" version="+core.Assembly.GetName().Version+" core="+core.Module.ModuleVersionId+" candidateSHA256="+(File.Exists(At("candidate.sha256"))?File.ReadAllText(At("candidate.sha256")).Trim():"diagnostic"));
    }
    static void Spawn(Mote __instance)
    {
        if(!MP.IsInMultiplayer)return;
        if(__instance.def.defName=="Mote_IcyFog")fogSpawns++;
        string key=__instance.Map.uniqueID+":"+__instance.def.defName;
        spawns.TryGetValue(key,out int count);spawns[key]=count+1;
        calls[__instance]=0;
        if(count<2)Log.Message("MOTE_PROBE SPAWN "+key+" spawn="+__instance.spawnTick+" map="+__instance.Map.AsyncTime().mapTicks+" world="+Mp.AsyncWorldTime.worldTicks+" realtime="+__instance.def.mote.realTime+" lifespan="+__instance.def.mote.Lifespan+" ticker="+__instance.def.tickerType);
    }
    static void Tick(Thing __instance)
    {
        if(__instance is Mote m && calls.TryGetValue(m,out int count))calls[m]=count+1;
    }
    static void Seed()=>Rand.PushState(261011);
    static void Unseed()=>Rand.PopState();
    static void RequireOwner(string type,string method,string owner)
    {
        var target=AccessTools.DeclaredMethod(AccessTools.TypeByName(type),method);
        var transpilers=Harmony.GetPatchInfo(target)?.Transpilers;
        Check(transpilers!=null&&transpilers.Any(p=>p.owner==owner),"NMC native random owner missing: "+method);
        Check(!transpilers.Any(p=>p.owner=="meow.trio.nivarian-simulation-random"),"duplicate Meow random owner: "+method);
        Log.Message("MOTE_PROBE NMC_OWNER retained="+owner);
    }
    static void FogBefore(HediffWithComps __instance,out ulong? __state)
    {
        __state=MP.IsInMultiplayer&&__instance.comps.Any(c=>fogType.IsInstanceOfType(c))?(ulong?)randState.GetValue(null):null;
    }
    static void FogAfter(ulong? __state)
    {
        if(!__state.HasValue)return;
        Check(__state.Value==(ulong)randState.GetValue(null),"Nivarian icy fog changed shared Rand");fogTicks++;
    }
    static void FleckTick(FleckManager __instance){int id=__instance.parent.uniqueID;fleckCalls.TryGetValue(id,out int n);fleckCalls[id]=n+1;}
    static void MapTick(Map __instance){if(AsyncTimeComp.tickingMap!=__instance)return;int id=__instance.uniqueID;mapCalls.TryGetValue(id,out int n);mapCalls[id]=n+1;}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static string Snapshot()=>Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";"+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState));
    static void Write(string name,string text){File.WriteAllText(At(name),text);Log.Message("MOTE_PROBE "+name+" "+text);}
    public static void Emit(int round)
    {
        if(commander&&round==0)foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
        {
            var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Nivarian_Reinforcement"),Faction.OfPlayer,forceGenerateNewPawn:true));
            GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(map.Center,map,5),map);
            if(!pawn.health.hediffSet.hediffs.OfType<HediffWithComps>().Any(h=>h.comps.Any(c=>fogType.IsInstanceOfType(c))))pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Nivarian_NivarianVisual"));
        }
        BuildPowerFixtures(round);
        State.round=round;State.startWorld=Mp.AsyncWorldTime.worldTicks;State.startServer=TickPatch.Timer;
        State.starts=Find.Maps.Select(m=>m.AsyncTime().mapTicks).ToList();State.until=State.startWorld+(rejoin&&round<11?10000:int.Parse(GenCommandLine.TryGetCommandLineArg("motesoakticks",out string s)?s:"10100"));
        Log.Message("MOTE_PROBE EXECUTED round="+round+" maps="+Find.Maps.Count);
    }
    static void CreateLocalFixtures()
    {
        int round=State.round;localRound=round;localStarts.Clear();
        mapCalls.Clear();fleckCalls.Clear();powerCalls.Clear();fixtures.Clear();
        foreach(var map in Find.Maps)
        {
            localStarts[map.uniqueID]=map.AsyncTime().mapTicks;
            var types=new[]{FleckDefOf.ExplosionFlash,FleckDefOf.Smoke,FleckDefOf.SleepZ};var defs=new FleckDef[types.Length];fixtures[map.uniqueID]=defs;
            var time=TimeSnapshot.GetAndSetFromMap(map);
            try
            {
                for(int i=0;i<types.Length;i++)
                {
                    var def=(FleckDef)AccessTools.Method(typeof(object),"MemberwiseClone").Invoke(types[i],null);
                    def.defName="MoteProbe"+round+"_"+map.uniqueID+"_"+i;def.fadeInTime=0;def.solidTime=1;def.fadeOutTime=0.5f;def.realTime=false;def.collide=false;defs[i]=def;
                    for(int j=0;j<3;j++)map.flecks.CreateFleck(new FleckCreationData{def=def,spawnPosition=(map.Center+new IntVec3(i*2+j,0,0)).ToVector3Shifted(),scale=1,velocitySpeed=0,airTimeLeft=10,ageTicksOverride=-1});
                }
            }
            finally{time?.Set();}
            Check(Data(map).Count==9,"native creation failed");
        }
        Log.Message("MOTE_PROBE LOCAL_EMIT round="+round+" maps="+Find.Maps.Count+" nativeStaticAndThrown=9/map");
    }
    static List<FleckStatic> Data(Map map)
    {
        var result=new List<FleckStatic>();
        foreach(var system in map.flecks.Systems)
        {
            var field=AccessTools.Field(system.GetType(),"dataGametime");if(field==null)continue;
            foreach(var item in (IEnumerable)field.GetValue(system))
            {
                FleckStatic data=item is FleckStatic st?st:item is FleckThrown th?th.baseData:default;
                if(data.def!=null&&data.def.defName.StartsWith("MoteProbe"))result.Add(data);
            }
        }
        return result;
    }
    static void MinimalUpdate()
    {
        if(completed||failed||LongEventHandler.AnyEventNowOrWaiting)return;
        if(!client&&!hosted&&Current.ProgramState==ProgramState.Playing)
        {
            hosted=true;
            if(twoMaps)
            {
                Map first=Find.CurrentMap;PlanetTile tile=TileFinder.RandomStartingTile();
                for(int a=0;a<30&&Find.WorldObjects.AllWorldObjects.Any(w=>w.Tile==tile);a++)tile=TileFinder.RandomStartingTile();
                var settlement=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);settlement.Tile=tile;settlement.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(settlement);
                MapGenerator.GenerateMap(new IntVec3(50,1,50),settlement,first.generatorDef);Current.Game.CurrentMap=first;
            }
            Type comp=AccessTools.TypeByName("MP_MeowOnlineShop.MultifactionTpsOptimizeComponent");
            object saved=Current.Game.components.First(c=>c.GetType()==comp);
            AccessTools.Field(comp,"experimentalTickSchedulerEnabled").SetValue(saved,true);
            // Host's normal save/reload resets local settings only before MP starts.
            object settings=AccessTools.Property(AccessTools.TypeByName("MP_MeowOnlineShop.MpMeowOnlineShopMod"),"Settings").GetValue(null);
            AccessTools.Field(settings.GetType(),"enableExperimentalTickScheduler").SetValue(settings,true);
            AccessTools.Field(settings.GetType(),"schedulerMode").SetValue(settings,2);
            var server=new ServerSettings{gameName="MoteLifecycle",direct=true,directAddress="127.0.0.1:"+port,lan=false,steam=false,multifaction=false,asyncTime=asyncTime,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};
            HostWindow.HostProgrammatically(server);return;
        }
        if(Current.ProgramState!=ProgramState.Playing||!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
        Check(!Mp.session.desynced,"desync");
        if(!client&&!ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
        if(client&&!ready){if(!TickPatch.Frozen)return;ready=true;Write("client.ready",Snapshot());}
        if(Mp.session.players.Count!=2||!File.Exists(At("client.ready")))return;
        if(!client&&!ready)
        {
            Check(Snapshot()==File.ReadAllText(At("client.ready")),"BASELINE_LOAD_DRIFT");ready=true;Write("host.ready",Snapshot());
            Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Ultrafast);
            foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)TimeSpeed.Ultrafast);
        }
        if(TickPatch.Frozen)
        {
            if(!client && freezeSent && rejoin && cycle<3)
            {
                int next=cycle+1;
                if(!File.Exists(At("host.rejoin"+next)))Write("host.rejoin"+next,Snapshot()+"|power="+PowerState());
                if(!File.Exists(At("rejoin"+next+".started"))||!File.Exists(At("client.ready"))||Mp.session.players.Count!=2)return;
                Check(Snapshot()==File.ReadAllText(At("client.ready")),"REJOIN_BASELINE_LOAD_DRIFT cycle="+next);
                Write("host.rejoin"+next+".ready",Snapshot());cycle=next;freezeSent=false;checkedRound=-1;sent=false;
                Mp.Client.Send(new ClientFreezePacket(false));
                Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Ultrafast);
                foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)TimeSpeed.Ultrafast);
                return;
            }
            if(freezeSent||File.Exists(At("host.final"))){if(commander){Check(fogTicks>0&&fogSpawns>0,"native icy fog not reached");Log.Message("MOTE_PROBE NMC_FOG randPreserved=True ticks="+fogTicks+" spawned="+fogSpawns);}Write(Peer+".final",Snapshot()+"|power="+PowerState());Write(Peer+".complete","PASS desynced=False sharedWorld="+(Mp.AsyncWorldTime.worldTicks-State.startWorld)+" sharedServer="+(TickPatch.Timer-State.startServer));completed=true;}
            return;
        }
        // Let the host's pre-host scheduler gate cache expire before comparing
        // the saved policy with a fresh client's first MP-only cache.
        if(State.round<cycle*3){if(client&&!sent&&TickPatch.Timer>=1000)sent=emit.DoSync(null,cycle*3);return;}
        // MP deliberately suppresses transient command feedback on non-issuers.
        // Emit through the native API on each peer after command replay, then
        // measure against its actual local emission tick; no feedback bypass.
        if(localRound!=State.round)CreateLocalFixtures();
        int elapsed=Find.Maps.Select(m=>m.AsyncTime().mapTicks-localStarts[m.uniqueID]).Min();
        if(State.round==0&&!captureStarted){captureStarted=true;Current.Game.CurrentMap=Find.Maps[0];Find.CameraDriver.JumpToCurrentMapLoc(Find.CurrentMap.Center.ToVector3Shifted());ScreenCapture.CaptureScreenshot(At(Peer+".spawned.png"));}
        if(checkedAgeRound!=State.round&&elapsed>=30&&elapsed<90)
        {
            foreach(var map in Find.Maps){var data=Data(map);Check(data.Count==9,"native fixture absent");int mapElapsed=map.AsyncTime().mapTicks-localStarts[map.uniqueID];if(!baseline)Check(data.All(d=>d.ageTicks==mapElapsed),"native fleck age differs from actual map ticks");Log.Message("MOTE_PROBE AGE round="+State.round+" map="+map.uniqueID+" elapsed="+mapElapsed+" ageTicks="+data[0].ageTicks+" calls="+(fleckCalls.TryGetValue(map.uniqueID,out int c)?c:0));}
            checkedAgeRound=State.round;
        }
        if(checkedRound!=State.round&&elapsed>=600)
        {
            Log.Message("POWER_PROBE STATE round="+State.round+" "+PowerState());
            foreach(var map in Find.Maps)
            {
                var fixturesOnMap=map.listerThings.AllThings.Where(t=>t.def.defName=="WoodFiredGenerator"||t.def.defName=="StandingLamp").ToArray();
                Check(fixturesOnMap.Length==2,"missing power fixture");
                foreach(var t in fixturesOnMap)Check(t.TryGetComp<CompPowerTrader>().PowerOn,"POWER_STARTUP_STARVED map="+map.uniqueID+" thing="+t.ThingID);
                int pc=powerCalls.TryGetValue(map.uniqueID,out int pn)?pn:0;
                if(!legacyPower)Check(pc==mapCalls[map.uniqueID],"Power advancement not once per map tick: "+pc+"/"+mapCalls[map.uniqueID]);
                Log.Message("POWER_PROBE ASSERT round="+State.round+" map="+map.uniqueID+" generator=True lamp=True powerCalls="+pc+" mapCalls="+mapCalls[map.uniqueID]);
                var data=Data(map);int fc=fleckCalls.TryGetValue(map.uniqueID,out int c)?c:0,mc=mapCalls.TryGetValue(map.uniqueID,out int n)?n:0;
                if(baseline&&twoMaps&&!asyncTime){Check(data.Count>0&&fc<mc,"baseline did not reproduce");Write(Peer+".reproduced","map="+map.uniqueID+" lingering="+data.Count+" mapCalls="+mc+" fleckCalls="+fc);}
                else{Check(data.Count==0,"flecks linger past lifespan");Check(fc==mc,"fleck advancement not once per map tick: map="+map.uniqueID+" fleck="+fc+" map="+mc);Log.Message("MOTE_PROBE ASSERT round="+State.round+" map="+map.uniqueID+" removed=9 calls="+fc+"/"+mc);}
            }
            checkedRound=State.round;
            if(State.round==0)ScreenCapture.CaptureScreenshot(At(Peer+".expired.png"));
            if(baseline){completed=true;Write(Peer+".complete","BASELINE_REPRODUCED");return;}
            sent=false;
        }
        if(checkedRound==State.round&&State.round%3<2&&TickPatch.Timer-State.startServer>=700){if(client&&!sent)sent=emit.DoSync(null,State.round+1);return;}
        if(State.round%3==2&&Mp.AsyncWorldTime.worldTicks>=State.until&&!client&&!freezeSent){freezeSent=true;Mp.Client.Send(new ClientFreezePacket(true));}
        if(TickPatch.Timer>=checkpoint){checkpoint=TickPatch.Timer+10000;Log.Message("MOTE_PROBE CHECKPOINT timer="+TickPatch.Timer+" players=2 desynced=False");}
        if(client&&Find.Maps.Count>1)Current.Game.CurrentMap=Find.Maps[(TickPatch.Timer/100)%Find.Maps.Count];
    }
    public static void Update()
    {
        if(!enabled)return;
        if(File.Exists(Path.Combine(root,"quit"))||File.Exists(At(Peer+".quit"))){Application.Quit();return;}
        if(minimal){try{MinimalUpdate();}catch(Exception e){failed=true;Log.Error("MOTE_PROBE FAILED "+e);Write(Peer+".failed",e.ToString());}return;}
        if(failed||LongEventHandler.AnyEventNowOrWaiting)return;
        try
        {
            if(!loading&&Current.ProgramState==ProgramState.Entry)
            {
                loading=true;Mp.username="MoteDiagnostic";
                ClientUtil.DoubleLongEvent(()=>Replay.LoadReplay(new FileInfo(Path.Combine(root,"Input.zip")),true,()=>{loaded=true;Log.Message("MOTE_PROBE LOADED");},()=>{throw new Exception("Replay cancelled");}),"MpLoading");return;
            }
            if(loaded&&!hosted&&Current.ProgramState==ProgramState.Playing&&!TickPatch.Simulating)
            {
                hosted=true;
                var settings=new ServerSettings{gameName="MoteDiagnostic",direct=true,directAddress="127.0.0.1:"+port,lan=false,steam=false,multifaction=false,asyncTime=GenCommandLine.CommandLineArgPassed("moteasync"),syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};
                if(!(bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}))throw new Exception("host preinit");
                HostUtil.HostServer(settings,true);return;
            }
            if(!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            if(started<0){started=Time.realtimeSinceStartup;nextSample=started;Mp.AsyncWorldTime.SetTimeEverywhere(TimeSpeed.Fast);Log.Message("MOTE_PROBE RUN async="+Mp.GameComp.asyncTime+" maps="+Find.Maps.Count);}
            if(Time.realtimeSinceStartup<nextSample)return;nextSample+=5;
            Log.Message("MOTE_PROBE SAMPLE elapsed="+(Time.realtimeSinceStartup-started)+" world="+Mp.AsyncWorldTime.worldTicks+" timer="+TickPatch.Timer+" maps="+string.Join(";",Find.Maps.Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().ActualRateMultiplier(TimeSpeed.Fast))));
            foreach(var group in RealTime.moteList.allMotes.Where(m=>m.Spawned).GroupBy(m=>m.Map.uniqueID+":"+m.def.defName))
            {
                Mote m=group.OrderBy(v=>v.spawnRealTime).First();calls.TryGetValue(m,out int count);
                var snapshot=TimeSnapshot.GetAndSetFromMap(m.Map);
                try{Log.Message("MOTE_PROBE LIVE "+group.Key+" count="+group.Count()+" age="+m.AgeSecs+" wall="+(Time.realtimeSinceStartup-m.spawnRealTime)+" ticks="+count+" spawn="+m.spawnTick+" now="+Find.TickManager.TicksGame+" lifetime="+m.def.mote.Lifespan);}
                finally{snapshot?.Set();}
            }
            foreach(var m in calls.Keys.Where(m=>m.Destroyed).ToArray())calls.Remove(m);
            if(Time.realtimeSinceStartup-started>=45){File.WriteAllText(Path.Combine(root,"host.complete"),"DIAGNOSTIC_COMPLETE");Application.Quit();}
        }
        catch(Exception e){failed=true;Log.Error("MOTE_PROBE FAILED "+e);File.WriteAllText(Path.Combine(root,"host.failed"),e.ToString());}
    }
}
