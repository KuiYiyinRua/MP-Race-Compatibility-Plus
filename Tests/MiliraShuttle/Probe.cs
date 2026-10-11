using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class MiliraShuttleFixture : GameComponent
{
    public Building_PassengerShuttle shuttle;
    public Pawn pilot;
    public Map home;
    public PlanetTile worldTile;
    public int stage, round, until, cycle=-1;
    public MiliraShuttleFixture(Game game) { }
    public override void GameComponentUpdate() => MiliraShuttleProbe.Update();
    public override void ExposeData()
    {
        Scribe_References.Look(ref shuttle,"msShuttle"); Scribe_References.Look(ref pilot,"msPilot");
        Scribe_References.Look(ref home,"msHome"); Scribe_Values.Look(ref worldTile,"msWorldTile");
        Scribe_Values.Look(ref stage,"msStage"); Scribe_Values.Look(ref round,"msRound"); Scribe_Values.Look(ref until,"msUntil");
        Scribe_Values.Look(ref cycle,"msCycle",-1);
    }
}

[StaticConstructorOnStartup]
public static class MiliraShuttleProbe
{
    static readonly bool enabled=GenCommandLine.CommandLineArgPassed("milirashuttleprobe");
    static readonly bool client=GenCommandLine.CommandLineArgPassed("msclient");
    static readonly bool replay=GenCommandLine.CommandLineArgPassed("msreplay");
    static readonly bool async=GenCommandLine.CommandLineArgPassed("msasync");
    static readonly bool stale=GenCommandLine.CommandLineArgPassed("msstale");
    static readonly bool inspect=GenCommandLine.CommandLineArgPassed("msinspect");
    static readonly bool blocked=GenCommandLine.CommandLineArgPassed("msblocked");
    static readonly bool mainThreadLoad=GenCommandLine.CommandLineArgPassed("msmainthreadload");
    static readonly bool existing=GenCommandLine.CommandLineArgPassed("msexisting");
    static bool recoveryObserved;
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static readonly int soak=int.Parse(Arg("mssoak","10000"));
    static readonly int maps=int.Parse(Arg("msmaps","2"));
    static readonly int cycle=int.Parse(Arg("mscycle","0"));
    static readonly int cycles=int.Parse(Arg("mscycles","0"));
    static bool loaded,hosted,ready,failed,done,sent,checking,launched,freezing;
    static int observed=-1, diagnosticCount;
    static int inspectStart=-1,managerErrors,requireChecks;
    static int readyCycle=-1,begunCycle=-1,pauseAt;
    static bool beginSent,joinPointStarted,paused;
    static float started=Time.realtimeSinceStartup,next,actionDeadline;
    static ISyncMethod prepare,check,begin;
    static MiliraShuttleFixture S=>Current.Game?.GetComponent<MiliraShuttleFixture>();
    static string Peer=>client?"client"+cycle:"host";
    static string Arg(string key,string fallback)=>GenCommandLine.TryGetCommandLineArg(key,out string v)?v:fallback;
    static string At(string name)=>Path.Combine(root,name);
    static void Require(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    static void Write(string name,string value){File.WriteAllText(At(Peer+"."+name),value);Log.Message("MS_PROBE "+Peer+" "+name+" "+value);}
    static string Queue(TransportShip ship)=>ship==null?"null":ship.loadID+"/"+ship.started+"/"+ship.curJob?.GetType().Name+":"+ship.curJob?.jobState+"/"+string.Join(",",((List<ShipJob>)AccessTools.Field(typeof(TransportShip),"shipJobs").GetValue(ship)).Select(j=>j.GetType().Name+":"+j.loadID+":"+j.jobState));
    static string State()=>"world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";maps="+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState))+";stage="+S.stage+";round="+S.round;
    static string Objects()=>"shuttle="+S.shuttle?.thingIDNumber+":"+S.shuttle?.Spawned+":"+S.shuttle?.Destroyed+":"+S.shuttle?.Map?.uniqueID+":"+S.shuttle?.ParentHolder?.GetType().Name+";pawn="+S.pilot?.thingIDNumber+":"+S.pilot?.Spawned+":"+S.pilot?.Destroyed+":"+S.pilot?.Map?.uniqueID+":"+S.pilot?.ParentHolder?.GetType().Name+";queue="+Queue(S.shuttle?.ShuttleComp.shipParent);
    static MiliraShuttleProbe()
    {
        if(!enabled)return;
        prepare=MP.RegisterSyncMethod(typeof(MiliraShuttleProbe),nameof(Prepare));
        check=MP.RegisterSyncMethod(typeof(MiliraShuttleProbe),nameof(Check));
        begin=MP.RegisterSyncMethod(typeof(MiliraShuttleProbe),nameof(BeginCycle));
        var h=new Harmony("local.meow.milirashuttleprobe");
        h.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(Update)));
        h.Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(Update)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(Seed)),finalizer:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(Unseed)));
        h.Patch(AccessTools.Method(typeof(ShipJob_Arrive),"TryStart"),prefix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(ArrivalBefore)),finalizer:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(ArrivalAfter)));
        h.Patch(AccessTools.Method(typeof(TransportShip),"Tick"),postfix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(ShipTick)));
        h.Patch(AccessTools.Method(typeof(Thing),nameof(Thing.Destroy)),prefix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(DestroyBefore)));
        h.Patch(AccessTools.Method(typeof(TransportShipManager),nameof(TransportShipManager.ShipObjectsTick)),finalizer:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(ManagerAfter)));
        h.Patch(AccessTools.PropertyGetter(typeof(CompShuttle),nameof(CompShuttle.AllRequiredThingsLoaded)),prefix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(RequiredBefore)));
        if(replay&&mainThreadLoad)foreach(var method in AccessTools.GetDeclaredMethods(typeof(LongEventHandler)).Where(m=>m.Name=="QueueLongEvent"&&m.GetParameters().Any(p=>p.Name=="doAsynchronously")))h.Patch(method,prefix:new HarmonyMethod(typeof(MiliraShuttleProbe),nameof(LoadOnMainThread)));
        Write("startup","run="+root+" coreMvid="+AccessTools.TypeByName("MP_MeowOnlineShop.Patch_AncotAerocraftMp").Module.ModuleVersionId+" input="+File.ReadAllText(At("inputs.json")));
    }
    static void Seed()=>Rand.PushState(261009);
    static void LoadOnMainThread(System.Reflection.MethodBase __originalMethod,object[] __args){if(hosted||!__args.OfType<string>().Contains("LoadingLongEvent"))return;var p=__originalMethod.GetParameters();for(int i=0;i<p.Length;i++)if(p[i].Name=="doAsynchronously"&&__args[i] is bool b&&b){__args[i]=false;Log.Message("MS_DIAG MAIN_THREAD_LOAD "+__originalMethod);}}
    static void Unseed()=>Rand.PopState();
    static bool Target(TransportShip ship)=>ship?.shipThing?.def.defName.StartsWith("Milira_")==true;
    static void ArrivalBefore(ShipJob_Arrive __instance){if(Target(__instance.transportShip)&&diagnosticCount++<80)Log.Message("MS_DIAG ARRIVE_BEGIN "+Queue(__instance.transportShip)+" spawned="+__instance.transportShip.shipThing.Spawned+" holder="+__instance.transportShip.shipThing.ParentHolder?.GetType().Name+" cell="+__instance.cell);}
    static Exception ArrivalAfter(ShipJob_Arrive __instance,Exception __exception){if(Target(__instance.transportShip)&&diagnosticCount<80)Log.Message("MS_DIAG ARRIVE_END exception="+__exception+" "+Queue(__instance.transportShip)+" holder="+__instance.transportShip.shipThing.ParentHolder?.GetType().Name);return __exception;}
    static void ShipTick(TransportShip __instance){if(Target(__instance)&&diagnosticCount<80&&Find.TickManager.TicksGame%1000==0)Log.Message("MS_DIAG SHIP_TICK "+Queue(__instance));}
    static void DestroyBefore(Thing __instance){if(S!=null&&(__instance==S.shuttle||__instance==S.pilot)){failed=true;Write("failed","fixture destroyed: "+__instance.ThingID+" "+Environment.StackTrace);Log.Warning("MS_DIAG DESTROY "+__instance.ThingID+" "+Environment.StackTrace);}}
    static void RequiredBefore(CompShuttle __instance){if((inspect||blocked)&&requireChecks++<10)Log.Message("MS_DIAG REQUIRED parent="+__instance.parent.ThingID+" spawned="+__instance.parent.Spawned+" map="+__instance.parent.Map?.uniqueID+" ship="+Queue(__instance.shipParent)+" requiredColonist="+__instance.requiredColonistCount+" nullPocketMaps="+__instance.parent.Map?.ChildPocketMaps.Count(m=>m==null));}
    static Exception ManagerAfter(Exception __exception){if(__exception!=null&&managerErrors++<3){Log.Error("MS_DIAG MANAGER_ERROR "+__exception);foreach(var ship in Find.TransportShipManager.AllTransportShips)Log.Message("MS_DIAG MANAGER_SHIP "+Queue(ship)+" thing="+ship.shipThing.ThingID+" spawned="+ship.shipThing.Spawned+" map="+ship.shipThing.Map?.uniqueID+" cachedShuttleParent="+ship.ShuttleComp.parent.ThingID+" cachedShuttleMap="+ship.ShuttleComp.parent.Map?.uniqueID);}return __exception;}
    static ServerSettings Settings()=>new ServerSettings{gameName="MiliraShuttleRegression",direct=true,directAddress="127.0.0.1:"+Arg("msport","31109"),lan=false,steam=false,multifaction=false,asyncTime=async,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=true};
    static void HostReplay(){Require((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{Settings()}),"host preinit");HostUtil.HostServer(Settings(),true);hosted=true;}
    static void Speed(TimeSpeed speed)=>Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)speed);
    static IntVec3 Cell(Map map,ThingDef def)=>CellRect.WholeMap(map).Cells.OrderBy(c=>c.DistanceToSquared(map.Center)).ThenBy(c=>c.z).ThenBy(c=>c.x).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(1).Cells.All(v=>v.InBounds(map)&&v.Standable(map)&&!v.Roofed(map)&&v.GetThingList(map).All(t=>!(t is Building)&&!(t is Pawn))));
    public static void BeginCycle(int current){S.cycle=current;S.stage=0;S.round=0;S.until=0;S.shuttle=null;S.pilot=null;begunCycle=current;Log.Message("MS_ASSERT BEGIN cycle="+current);}
    public static void Prepare()
    {
        Require(S.stage==0,"unexpected prepare");
        var ordered=Find.Maps.OrderBy(m=>m.uniqueID).ToArray();S.home=existing?ordered.First(m=>m.uniqueID==1):ordered[S.round%ordered.Length];
        string defName=(existing?S.round:(S.round/2))%2==0?"Milira_WyvernFighter":"Milira_HarrierFighter";
        if(existing){S.shuttle=S.home.listerThings.AllThings.OfType<Building_PassengerShuttle>().Single(t=>t.thingIDNumber==(S.round==0?3748879:76031));Require(!S.shuttle.Destroyed,"saved shuttle destroyed");}
        else{S.shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));S.shuttle.SetFaction(S.home.ParentFaction);GenSpawn.Spawn(S.shuttle,Cell(S.home,S.shuttle.def),S.home);}
        Pawn pilot=null;
        for(int attempt=0;attempt<50&&pilot==null;attempt++){
            var candidate=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,S.home.ParentFaction,forceGenerateNewPawn:true,mustBeCapableOfViolence:true,fixedBiologicalAge:28,fixedChronologicalAge:28));
            if(!candidate.Dead&&!candidate.Downed&&!StatDefOf.PilotingAbility.Worker.IsDisabledFor(candidate)&&candidate.GetStatValue(StatDefOf.PilotingAbility)>0.1f)pilot=candidate;
            else candidate.Destroy(DestroyMode.Vanish);
        }
        Require(pilot!=null,"fixture could not generate a qualified pilot");S.pilot=pilot;
        GenSpawn.Spawn(S.pilot,CellFinder.RandomClosewalkCellNear(S.shuttle.Position,S.home,5),S.home);
        S.pilot.DeSpawn();Require(S.shuttle.TransporterComp.innerContainer.TryAdd(S.pilot,false),"boarding");
        S.shuttle.RefuelableComp.Refuel(100);S.shuttle.LaunchableComp.lastLaunchTick=-1;
        S.shuttle.TransporterComp.groupID=Find.UniqueIDsManager.GetNextTransporterGroupID();
        var neighbors=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(S.home.Tile,neighbors);
        S.worldTile=neighbors.OrderBy(t=>t.tileId).First(t=>!Find.WorldObjects.AnyMapParentAt(t)&&Find.WorldPathGrid.Passable(t));
        if(blocked&&S.round==0&&S.cycle==0){
            // Recreate the exact saved shape: an unloaded pocket-map parent on
            // map 0 and a newer quest shuttle requiring one colonist. Vanilla
            // processes this newer ship first, before the player aerocraft.
            var pocket=(PocketMapParent)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("PocketMap"));
            pocket.sourceMap=ordered[0];Find.World.pocketMaps.Add(pocket);
            var quest=ThingMaker.MakeThing(ThingDefOf.Shuttle);quest.SetFaction(S.home.ParentFaction);
            GenSpawn.Spawn(quest,Cell(ordered[0],quest.def),ordered[0]);
            var comp=quest.TryGetComp<CompShuttle>();comp.requiredColonistCount=1;
            var ship=TransportShipMaker.MakeTransportShip(DefDatabase<TransportShipDef>.GetNamed("Ship_Shuttle"),null,quest);comp.shipParent=ship;
            var job=ShipJobMaker.MakeShipJob(ShipJobDefOf.FlyAway);job.jobState=ShipJobState.Working;ship.AddJob(job);ship.started=true;
            Log.Message("MS_DIAG BLOCKER "+Queue(ship)+" nullPocketMaps="+ordered[0].ChildPocketMaps.Count(m=>m==null));
        }
        if(stale){
            var ship=S.shuttle.ShuttleComp.shipParent;
            var old=(ShipJob_Arrive)ShipJobMaker.MakeShipJob(ShipJobDefOf.Arrive);old.cell=S.shuttle.Position;old.mapParent=S.home.Parent;old.jobState=ShipJobState.Working;old.transportShip=ship;ship.curJob=old;
            var queued=(ShipJob_Arrive)ShipJobMaker.MakeShipJob(ShipJobDefOf.Arrive);queued.cell=S.shuttle.Position;queued.mapParent=S.home.Parent;queued.jobState=ShipJobState.Working;ship.AddJob(queued);
        }
        S.stage=1;Log.Message("MS_ASSERT PREPARED round="+S.round+" map="+S.home.uniqueID+" "+Objects());
    }
    public static void Check(int stage)
    {
        Require(stage==S.stage,"stale assertion");
        if(stage==1){
            var caravan=Find.WorldObjects.Caravans.FirstOrDefault(c=>c.Shuttle==S.shuttle);
            Require(caravan!=null&&caravan.PawnsListForReading.Contains(S.pilot),"world caravan lost shuttle or pilot");
            S.stage=2;
        }else{
            Require(S.shuttle!=null&&!S.shuttle.Destroyed&&S.shuttle.Spawned&&S.shuttle.Map==S.home,"arrival lost shuttle "+Objects());
            Require(S.pilot!=null&&!S.pilot.Dead&&!S.pilot.Destroyed&&S.pilot.Spawned&&S.pilot.Map==S.home,"arrival lost passenger "+Objects());
            S.round++;S.stage=S.round==(existing?2:S.cycle==0?12:Math.Max(2,maps*2))?3:0;
            if(S.stage==3)S.until=Mp.AsyncWorldTime.worldTicks+(S.cycle==cycles?soak:10000);
        }
        Log.Message("MS_ASSERT CHECK stage="+stage+" round="+S.round+" "+Objects());
    }
    public static void Update()
    {
        if(!enabled)return;
        if(File.Exists(At("quit"))||File.Exists(At(Peer+".quit"))){Application.Quit();return;}
        if(failed||done||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+0.15f;
        try{
            Require(Time.realtimeSinceStartup-started<3600,"overall timeout");
            if(replay&&!client&&!loaded&&Current.ProgramState==ProgramState.Entry&&!LongEventHandler.AnyEventNowOrWaiting){loaded=true;Replay.LoadReplay(new FileInfo(At("input.zip")),true,HostReplay);return;}
            if(Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
            if(!client&&!hosted&&!MP.IsInMultiplayer){
                hosted=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(maps==2){var first=Find.CurrentMap;var neighbors=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(first.Tile,neighbors);var tile=neighbors.OrderBy(t=>t.tileId).First(t=>!Find.WorldObjects.AnyMapParentAt(t)&&Find.WorldPathGrid.Passable(t));var parent=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=tile;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);MapGenerator.GenerateMap(new IntVec3(80,1,80),parent,first.generatorDef);Current.Game.CurrentMap=first;}
                Require(HostWindow.HostProgrammatically(Settings()),"host rejected");return;
            }
            if(!MP.IsInMultiplayer||Mp.Client?.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            Require(!Mp.session.desynced,"desync");
            if(inspect||(existing&&!recoveryObserved)){
                if(inspectStart<0){inspectStart=Mp.AsyncWorldTime.worldTicks;Write("inspectbaseline",State());foreach(var ship in Find.TransportShipManager.AllTransportShips)Log.Message("MS_DIAG INITIAL_SHIP "+Queue(ship)+" thing="+ship.shipThing.ThingID+" map="+ship.shipThing.Map?.uniqueID);Mp.Client.Send(new ClientFreezePacket(false));Speed(TimeSpeed.Normal);return;}
                if(Mp.AsyncWorldTime.worldTicks>=inspectStart+2000){foreach(var ship in Find.TransportShipManager.AllTransportShips)Log.Message("MS_DIAG FINAL_SHIP "+Queue(ship)+" thing="+ship.shipThing.ThingID+" spawned="+ship.shipThing.Spawned+" map="+ship.shipThing.Map?.uniqueID);Write("inspected",State()+";managerErrors="+managerErrors);Require(managerErrors==0,"saved transport manager still failing");recoveryObserved=true;if(inspect)done=true;}return;
            }
            int requested=client?cycle:(File.Exists(At("cycle.txt"))?int.Parse(File.ReadAllText(At("cycle.txt"))):0);
            if(readyCycle!=requested)ready=false;
            if(!ready){
                if(!TickPatch.Frozen){if(!client)Mp.Client.Send(new ClientFreezePacket(true));return;}
                if(!File.Exists(At(Peer+".baseline"+requested)))Write("baseline"+requested,State());
                Require(Mp.session.players.Count==1,"single hosted player required");
                ready=true;readyCycle=requested;freezing=joinPointStarted=paused=false;beginSent=false;observed=-1;
                if(!client){Mp.Client.Send(new ClientFreezePacket(false));Speed(TimeSpeed.Superfast);}return;
            }
            Require(Mp.session.players.Count==1,"single hosted player required");
            if(!client&&!beginSent&&!TickPatch.Frozen){beginSent=begin.DoSync(null,requested);return;}
            if(begunCycle!=requested||S.cycle!=requested)return;
            if(S.stage==3){
                Require(managerErrors==0,"transport manager error during regression");
                if(Mp.AsyncWorldTime.worldTicks<S.until)return;
                if(!client){
                    if(!paused){paused=true;Speed(TimeSpeed.Paused);pauseAt=TickPatch.Timer+40;return;}
                    if(TickPatch.Timer<pauseAt)return;
                    if(!joinPointStarted){Require(Mp.LocalServer.worldData.TryStartJoinPointCreation(true),"join point rejected");joinPointStarted=true;return;}
                    if(Mp.LocalServer.worldData.CreatingJoinPoint)return;
                    if(!freezing){freezing=true;Mp.Client.Send(new ClientFreezePacket(true));return;}
                }
                if(!TickPatch.Frozen)return;
                if(!File.Exists(At("host.complete"+requested))&&!client)Write("complete"+requested,State()+";"+Objects());
                if(client&&File.Exists(At("host.complete"+requested))&&!File.Exists(At(Peer+".complete"))){Require(State()+";"+Objects()==File.ReadAllText(At("host.complete"+requested)),"terminal state mismatch");Write("complete",State()+";"+Objects());}return;
            }
            if(client||TickPatch.Frozen)return;
            int progress=S.round*10+S.stage;
            if(progress!=observed){observed=progress;sent=checking=launched=false;actionDeadline=Time.realtimeSinceStartup+150;}
            Require(Time.realtimeSinceStartup<actionDeadline,"action timeout "+Objects());
            if(S.stage==0){if(!sent)sent=prepare.DoSync(null);return;}
            if(!launched){
                if(S.stage==1){Current.Game.CurrentMap=S.home;var launchReport=S.shuttle.LaunchableComp.CanLaunch();Require(launchReport,"launch blocked: "+launchReport.Reason+" "+Objects()+" fuel="+S.shuttle.FuelLevel);S.shuttle.LaunchableComp.TryLaunch(S.worldTile,new TransportersArrivalAction_FormCaravan("MessageShuttleArrived"));}
                else{var caravan=Find.WorldObjects.Caravans.First(c=>c.Shuttle==S.shuttle);if(!S.shuttle.LaunchableComp.CanLaunch(S.shuttle.FuelLevel))return;CaravanShuttleUtility.LaunchShuttle(caravan,S.home.Tile,new TransportersArrivalAction_LandInSpecificCell(S.home.Parent,Cell(S.home,S.shuttle.def),Rot4.North,true));}
                launched=true;Log.Message("MS_PROBE DISPATCH stage="+S.stage+" round="+S.round);return;
            }
            bool settled=S.stage==1?Find.WorldObjects.Caravans.Any(c=>c.Shuttle==S.shuttle):S.shuttle.Spawned&&S.pilot.Spawned;
            if(settled&&!checking)checking=check.DoSync(null,S.stage);
        }catch(Exception e){failed=true;Write("failed",e.ToString());Log.Error("MS_PROBE FAILED "+e);}
    }
}




