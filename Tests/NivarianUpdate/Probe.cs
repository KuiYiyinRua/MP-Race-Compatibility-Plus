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
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class NivarianUpdateFixture : GameComponent
{
    public List<ThingWithComps> nodes = new List<ThingWithComps>(), inputs = new List<ThingWithComps>();
    public List<ThingWithComps> dusts = new List<ThingWithComps>(), factories = new List<ThingWithComps>(), rods = new List<ThingWithComps>();
    public List<Letter> aids = new List<Letter>();
    public List<ThingWithComps> fuelTargets = new List<ThingWithComps>();
    public List<Pawn> animals = new List<Pawn>();
    public Letter letter;
    public int cycle=-1, step, until;
    public NivarianUpdateFixture(Game game) { }
    public override void GameComponentUpdate() => NivarianUpdateProbe.Update();
    public override void ExposeData()
    {
        Scribe_Collections.Look(ref nodes,"nuNodes",LookMode.Reference);
        Scribe_Collections.Look(ref inputs,"nuInputs",LookMode.Reference);
        Scribe_Collections.Look(ref dusts,"nuDusts",LookMode.Reference);
        Scribe_Collections.Look(ref factories,"nuFactories",LookMode.Reference);
        Scribe_Collections.Look(ref rods,"nuRods",LookMode.Reference);
        Scribe_Collections.Look(ref aids,"nuAids",LookMode.Reference);
        Scribe_Collections.Look(ref fuelTargets,"nuFuelTargets",LookMode.Reference);
        Scribe_Collections.Look(ref animals,"nuAnimals",LookMode.Reference);
        Scribe_References.Look(ref letter,"nuLetter");
        Scribe_Values.Look(ref cycle,"nuCycle",-1); Scribe_Values.Look(ref step,"nuStep"); Scribe_Values.Look(ref until,"nuUntil");
    }
}

public sealed class NativeSettingsProbeWindow : Window
{
    readonly object mod;
    public NativeSettingsProbeWindow(object mod) { this.mod=mod; doCloseX=true; }
    public override Vector2 InitialSize => new Vector2(720,500);
    public override void DoWindowContents(Rect rect) => NivarianUpdateProbe.SettingsGui(mod,rect);
}

public sealed class NativeGuardProbeWindow : Window
{
    readonly Gizmo gizmo;
    public NativeGuardProbeWindow(Gizmo gizmo) { this.gizmo=gizmo; doCloseX=true; }
    public override Vector2 InitialSize => new Vector2(220,160);
    public override void DoWindowContents(Rect rect) => AccessTools.Method(gizmo.GetType(),"DrawExtra").Invoke(gizmo,new object[]{new Rect(10,10,120,85)});
}

[StaticConstructorOnStartup]
public static class NivarianUpdateProbe
{
    static readonly bool enabled=GenCommandLine.CommandLineArgPassed("nivarianupdateprobe");
    static readonly bool client=GenCommandLine.CommandLineArgPassed("nuclient"), async=GenCommandLine.CommandLineArgPassed("nuasync");
    static readonly int maps=int.Parse(Arg("numaps","1")), cycle=int.Parse(Arg("nucycle","0")), cycles=int.Parse(Arg("nucycles","0")), soak=int.Parse(Arg("nusoak","10000"));
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static bool hosted, failed, done, sent, waiting, began, joinpoint, pausing, baselineLoading;
    static int readyCycle=-1, dispatched=-1, due, pauseAt, checkpoint;
    static bool settingsGui, settingValue, resumedExpiry;
    static int disabledChecks, allianceChecks, settingsRow;
    static float next, started=Time.realtimeSinceStartup;
    static Window sliderWindow;
    static float sliderValue;
    static ISyncMethod begin, verify;
    static Type nodeType,inputType,animalType,letterType,dustType,containerType,rodType,aidType;
    static IncidentDef settingsDef;
    static NivarianUpdateFixture S=>Current.Game?.GetComponent<NivarianUpdateFixture>();
    static int Total=>maps*3*16;
    static string Peer=>client?"client"+cycle:"host";
    static string At(string name)=>Path.Combine(root,name);
    static string Arg(string name,string fallback)=>GenCommandLine.TryGetCommandLineArg(name,out string value)?value:fallback;
    static Type T(string name)=>AccessTools.TypeByName(name)??throw new TypeLoadException(name);
    static void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    static object Get(object value,string field)=>AccessTools.Field(value.GetType(),field).GetValue(value);
    static object Call(object value,string method,params object[] args)=>AccessTools.Method(value.GetType(),method).Invoke(value,args);
    static ThingComp Comp(ThingWithComps thing,Type type)=>thing.AllComps.First(c=>type.IsInstanceOfType(c));
    static string Snapshot()=>"world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";maps="+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState))+";state="+State();
    static string State()=>S.cycle+":"+S.step+";"+string.Join(";",S.nodes.Select((n,i)=>n.thingIDNumber+":"+Get(Comp(n,nodeType),"allowContainerService")+":"+S.inputs[i].thingIDNumber+":"+Get(Comp(S.inputs[i],inputType),"enabled")+":"+Get(Comp(S.inputs[i],inputType),"scanRadius")+":"+Get(Comp(S.animals[i],animalType),"guardRadius")+":"+Get(Comp(S.animals[i],animalType),"guardPoint")+":"+Get(Comp(S.dusts[i],dustType),"_selectedChunkDef")+":"+Get(Comp(S.factories[i],containerType),"searchRadius")+":"+Get(Comp(S.rods[i],rodType),"switchOn")+":"+Get(Comp(S.rods[i],rodType),"reverseMode")+":"+Get(Comp(S.rods[i],rodType),"artificialOn")+":"+Plan(S.aids[i])))+";settings="+SettingsState();
    static object Settings=>AccessTools.Field(T("Nivarian.NivarianMod"),"Settings").GetValue(null);
    static string SettingsState()=>(settingsDef==null?"unselected":settingsDef.defName+":"+Call(Settings,"IsAidIncidentDisabled",settingsDef)+":"+Call(Settings,"AidIncidentRequiresAlliance",settingsDef))+";fuel="+string.Join(",",S.fuelTargets.Select(t=>t.thingIDNumber+":"+t.GetComp<CompRefuelable>().Fuel.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))+";ledger="+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>string.Join(",",((IEnumerable)Get(m.components.Single(c=>c.GetType().Name=="CannonOrderLedger"),"orders")).Cast<object>().Select(o=>Get(o,"orderId")+":"+Get(o,"state")+":"+Get(o,"closeReason")))));
    static string Plan(Letter letter)=>letter==null?"none":Convert.ToInt32(Get(letter,"stat"))+":"+Get(letter,"plannedReinforcementCount")+":"+string.Join(",",((IEnumerable)Get(letter,"plannedGuestSlots")).Cast<object>().Select(s=>((Def)Get(s,"guestDef")).defName+"="+Get(s,"count")));
    static void Write(string name,string value){File.WriteAllText(At(Peer+"."+name),value);Log.Message("NU_PROBE "+Peer+" "+name+" "+value);}
    static NivarianUpdateProbe()
    {
        if(!enabled)return;
        nodeType=T("Nivarian_Race.Code.Comps.BuildingComps.CompCannonNode");inputType=T("Nivarian_Race.Code.Comps.BuildingComps.CompCannonInput");
        animalType=T("Nivarian_Race.Code.Comps.ThingComps.Comp_AnimalOrders");letterType=T("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianHostVisitors");
        dustType=T("Nivarian_Race.Code.Comps.BuildingComps.CompDustCatcher");containerType=T("Nivarian_Race.Code.Comps.BuildingComps.CompUniversalContainer");rodType=T("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianLightningRod");aidType=T("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianAid");
        settingsDef=DefDatabase<IncidentDef>.GetNamed("Nivarian_ColdShelterJoinQuest");
        if(GenCommandLine.CommandLineArgPassed("nuguestfixture")){
            var database=typeof(DefDatabase<>).MakeGenericType(T("Nivarian_Race.Code.Defs.NivarianGuestReinforcementDef"));
            var guestDef=AccessTools.Method(database,"GetNamed",new[]{typeof(string),typeof(bool)}).Invoke(null,new object[]{"NivarianGuest_ZuoYaoRavenRace",true});
            AccessTools.Field(guestDef.GetType(),"pawnKind").SetValue(guestDef,"Raven_FusangGuard");
            Check((bool)Call(guestDef,"IsAvailable"),"test-only nonempty guest contract");
            Log.Message("NU_GUEST_FIXTURE test-only pawnKind=Raven_FusangGuard; installed XML unchanged");
        }
        begin=MP.RegisterSyncMethod(typeof(NivarianUpdateProbe),nameof(Begin)); verify=MP.RegisterSyncMethod(typeof(NivarianUpdateProbe),nameof(Verify));
        if(GenCommandLine.CommandLineArgPassed("nurws")){AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (sender,args) => {var name=new System.Reflection.AssemblyName(args.Name).Name;var path=Path.Combine(Application.dataPath,"Managed",name+".dll");if(!File.Exists(path))return null;Log.Message("NU_DIAG reflection-only dependency resolved: "+name);return System.Reflection.Assembly.ReflectionOnlyLoadFrom(path);};System.Reflection.Assembly.ReflectionOnlyLoadFrom(Path.Combine(Application.dataPath,"Managed","UnityEngine.InputLegacyModule.dll"));}
        var h=new Harmony("local.meow.nivarianupdateprobe");
        h.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(Update)));
        if(GenCommandLine.CommandLineArgPassed("nurws"))foreach(var method in AccessTools.GetDeclaredMethods(typeof(LongEventHandler)).Where(m=>m.Name=="QueueLongEvent"&&m.GetParameters().Any(p=>p.Name=="doAsynchronously")&&m.GetParameters().Any(p=>p.Name=="textKey")))h.Patch(method,prefix:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(SceneLoad)),finalizer:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(SceneLoadAfter)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(Seed)),finalizer:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(Unseed)));
        foreach(var method in AccessTools.GetDeclaredMethods(typeof(Listing_Standard)).Where(m=>m.Name=="Slider"&&m.ReturnType==typeof(float)))h.Patch(method,prefix:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(Slider)));
        foreach(var method in AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m=>m.Name=="HorizontalSlider"&&m.ReturnType==typeof(float)&&m.GetParameters().Length>1&&m.GetParameters()[1].ParameterType==typeof(float)))h.Patch(method,prefix:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(Slider)));
        foreach(var method in AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m=>m.Name=="CheckboxLabeled"&&m.GetParameters().Any(p=>p.ParameterType==typeof(bool).MakeByRefType())))h.Patch(method,prefix:new HarmonyMethod(typeof(NivarianUpdateProbe),nameof(Checkbox)));
        Log.Message("NU_PROBE START peer="+Peer+" label="+root+" candidateMvid="+T("Meow.TaleNivarianCompatibility.Bootstrap").Module.ModuleVersionId+" inputsFile="+At("inputs.json"));
    }
    static void Seed()=>Rand.PushState(261011);
    static int sceneQueueDepth;
    static void SceneLoad(System.Reflection.MethodBase __originalMethod,object[] __args,out bool __state)
    {
        var p=__originalMethod.GetParameters();__state=false;
        for(int i=0;i<p.Length;i++)if(p[i].Name=="levelToLoad"&&__args[i] is string level&&!string.IsNullOrEmpty(level)){sceneQueueDepth++;__state=true;Log.Message("NU_DIAG ASYNC_SCENE retained "+level);}
        if(sceneQueueDepth>0)return;
        if(!__args.OfType<string>().Contains("LoadingLongEvent"))return;
        for(int i=0;i<p.Length;i++)if(p[i].Name=="doAsynchronously"){__args[i]=false;Log.Message("NU_DIAG MAIN_THREAD_DATA "+__originalMethod);}
    }
    static Exception SceneLoadAfter(Exception __exception,bool __state){if(__state)sceneQueueDepth--;return __exception;}
    static void Unseed()=>Rand.PopState();
    static bool Slider(ref float __result){if(sliderWindow==null||sliderWindow is NativeSettingsProbeWindow)return true;__result=sliderValue;return false;}
    static bool Checkbox(System.Reflection.MethodBase __originalMethod,object[] __args)
    {
        if(!settingsGui)return true;
        var label=__args.OfType<string>().FirstOrDefault();
        bool chosen=label=="Nivarian.Settings.AidIncidentDisabled".Translate().ToString()?disabledChecks++==settingsRow:label=="Nivarian.Settings.AidIncidentRequireAlliance".Translate().ToString()&&allianceChecks++==settingsRow;
        if(!chosen)return true;
        int index=Array.FindIndex(__originalMethod.GetParameters(),p=>p.ParameterType==typeof(bool).MakeByRefType());__args[index]=settingValue;return false;
    }
    public static void SettingsGui(object mod,Rect rect)
    {
        disabledChecks=allianceChecks=0;settingsGui=true;
        try {Call(mod,"DoSettingsWindowContents",rect);} finally{settingsGui=false;}
    }
    static ServerSettings HostingSettings()=>new ServerSettings{gameName="Nivarian update",direct=true,directAddress="127.0.0.1:"+Arg("nuport","31171"),lan=false,steam=false,multifaction=false,asyncTime=async,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};
    static void Speed(TimeSpeed speed)=>Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)speed);
    static ThingWithComps Spawn(string def,Map map,IntVec3 cell){var t=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def));t.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(t,cell,map);return t;}
    public static void Begin(int current)
    {
        Check(current>=0,"cycle");S.cycle=current;S.step=0;began=true;sent=waiting=false;dispatched=-1;
        if(S.nodes.Count==0)foreach(var map in Find.Maps.Where(m=>m.IsPlayerHome&&m.Size.x>=80).OrderBy(m=>m.uniqueID).Take(maps))
        {
            var cell=CellRect.WholeMap(map).Cells.OrderBy(c=>c.DistanceToSquared(map.Center)).First(c=>CellRect.CenteredOn(c,9).Cells.All(v=>v.InBounds(map)&&v.GetTerrain(map).passability!=Traversability.Impassable&&v.GetThingList(map).All(t=>!(t is Building)&&!(t is Pawn))));
            foreach(var plant in CellRect.CenteredOn(cell,9).Cells.SelectMany(c=>c.GetThingList(map)).OfType<Plant>().Distinct().ToArray())plant.Destroy(DestroyMode.Vanish);
            if(map.mapPawns.FreeColonistsSpawned.Count==0)GenSpawn.Spawn(PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true)),cell+new IntVec3(0,0,8),map);
            S.nodes.Add(Spawn("Nivarian_ItemCannon",map,cell));S.inputs.Add(Spawn("Nivarian_CannonInput",map,cell+IntVec3.East));
            S.dusts.Add(Spawn("Nivarian_DustCatcher",map,cell+new IntVec3(5,0,0)));S.factories.Add(Spawn("Nivarian_CoolantFactory",map,cell+new IntVec3(-5,0,4)));S.rods.Add(Spawn("Nivarian_LightningRod",map,cell+new IntVec3(5,0,4)));S.aids.Add(null);
            foreach(int x in new[]{-6,-3,0,3,6})Spawn("VanometricPowerCell",map,cell+new IntVec3(x,0,-5));
            foreach(var wire in CellRect.CenteredOn(cell,6).Cells.Where(c=>!c.GetThingList(map).OfType<ThingWithComps>().Any(t=>t.AllComps.OfType<CompPower>().Any(p=>p.Props.transmitsPower))))Spawn("PowerConduit",map,wire);
            var fuelTarget=Spawn("Campfire",map,cell+new IntVec3(4,0,-3));fuelTarget.GetComp<CompRefuelable>().ConsumeFuel(fuelTarget.GetComp<CompRefuelable>().Fuel);S.fuelTargets.Add(fuelTarget);
            foreach(var workerPawn in map.mapPawns.FreeColonistsSpawned)workerPawn.workSettings.SetPriority(WorkTypeDefOf.Hauling,0);
            for(int stock=0;stock<20;stock++){var wood=ThingMaker.MakeThing(ThingDefOf.WoodLog);wood.stackCount=75;GenSpawn.Spawn(wood,cell+new IntVec3(stock%5-2,0,4+stock/5),map);}
            Orphan(map,S.nodes.Last(),cell+new IntVec3(-4,0,-3));
            var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Nivarian_FrostDrakePKB"),Faction.OfPlayer,forceGenerateNewPawn:true));
            GenSpawn.Spawn(pawn,cell+new IntVec3(0,0,2),map);var armor=pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Nivarian_DrakeArmorWorn"));Check((bool)Call(((HediffWithComps)armor).comps.First(c=>c.GetType().Name=="HediffComp_WornThing"),"TryAcceptItem",ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Nivarian_DrakeArmor"))),"native armor holder");S.animals.Add(pawn);
            var lend=Activator.CreateInstance(T("Nivarian.QuestPart_LendColonistsToNivarian"));var colonist=map.mapPawns.FreeColonistsSpawned.First();Call(lend,"TryRememberLent",colonist);Call(lend,"TryRememberLent",colonist);Call(lend,"TryRememberLent",pawn);Check(((IList)Get(lend,"lentColonists")).Count==1,"native loan collection duplicate/humanlike predicate");
        }
        foreach(var sourceInput in S.inputs)AccessTools.Field(inputType,"enabled").SetValue(Comp(sourceInput,inputType),current%2==0);
        foreach(var sourceNode in S.nodes)AccessTools.Field(nodeType,"allowContainerService").SetValue(Comp(sourceNode,nodeType),current%2==1);
        Check(S.nodes.Count==maps,"eligible fixture map count");
        Log.Message("NU_ASSERT BEGIN cycle="+current+" "+State());
    }
    static void Orphan(Map map,ThingWithComps cannon,IntVec3 cell)
    {
        var ledger=map.components.Single(c=>c.GetType().Name=="CannonOrderLedger");var destination=Spawn("Campfire",map,cell);
        int before=map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount);
        var lineType=T("Nivarian_Race.Code.Logistics.Orders.CannonCargoOrderLine");var line=Activator.CreateInstance(lineType);
        AccessTools.Field(lineType,"def").SetValue(line,ThingDefOf.Steel);AccessTools.Field(lineType,"count").SetValue(line,25);AccessTools.Field(lineType,"originalCount").SetValue(line,25);
        var lines=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(lineType));lines.Add(line);
        var node=Comp(cannon,nodeType);var order=Call(ledger,"PlaceOrder",destination,lines,25,0,node,null);int id=(int)Get(order,"orderId");
        var steel=ThingMaker.MakeThing(ThingDefOf.Steel);steel.stackCount=25;var sink=Enum.ToObject(T("Nivarian_Race.Code.Comps.BuildingComps.CannonCargoSinkKind"),0);
        Check((bool)Call(node,"TryEnqueue",steel,destination,sink,-1,id,0,false),"native buffered cargo");destination.Destroy(DestroyMode.Vanish);
        Check((bool)Call(ledger,"TryCloseConfirmedOrphan",id,destination),"native orphan closure");Call(node,"DropConfirmedOrphanCargo",order);
        Check(Get(order,"state").ToString()=="Dropped"&&Get(order,"closeReason").ToString()=="DestinationGone"&&(int)Get(node,"incomingReservation")==0,"native orphan order terminal state");
        Check(map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount)==before+25,"orphan goods exactly conserved after duplicate ejection");
        Log.Message("NU_ASSERT_ORPHAN map="+map.uniqueID+" order="+id+" count=25 closed=True");
        // Exercise the native spawned flyer completion path with a vanished destination.
        destination=Spawn("Campfire",map,cell);AccessTools.Field(lineType,"count").SetValue(line,10);AccessTools.Field(lineType,"originalCount").SetValue(line,10);
        var flightLines=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(lineType));flightLines.Add(Call(line,"Clone"));
        var flightOrder=Call(ledger,"PlaceOrder",destination,flightLines,10,0,node,null);int flightId=(int)Get(flightOrder,"orderId");
        var payload=ThingMaker.MakeThing(ThingDefOf.Steel);payload.stackCount=10;var flyerType=T("Nivarian_Race.Code.Logistics.CannonCargoFlyer");
        Check((bool)AccessTools.Method(flyerType,"TryLaunchAbsorb").Invoke(null,new object[]{map,(cannon.Position+new IntVec3(0,0,8)).ToVector3Shifted(),payload,node,destination,sink,-1,flightId,0}),"native spawned freight launch");
        var flyer=map.listerThings.AllThings.Single(t=>flyerType.IsInstanceOfType(t)&&(int)Get(t,"orderId")==flightId);
        Check(flyer.Spawned&&((ThingOwner)Get(flyer,"cargo")).TotalStackCount==10,"native in-flight cargo holder");destination.Destroy(DestroyMode.Vanish);
        int flightBefore=map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount);Call(flyer,"DeliverHeldCargo");Call(flyer,"DeliverHeldCargo");
        Check(((ThingOwner)Get(flyer,"cargo")).Count==0&&map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount)==flightBefore+10,"native flight orphan conservation and duplicate completion");
        Check(Get(flightOrder,"state").ToString()=="Dropped"&&Get(flightOrder,"closeReason").ToString()=="DestinationGone","flight order must remain closed");flyer.Destroy(DestroyMode.Vanish);
        Log.Message("NU_ASSERT_FLIGHT_ORPHAN map="+map.uniqueID+" order="+flightId+" count=10 duplicateSafe=True");
    }
    static void Aid(int index)
    {
        if(S.aids[index]!=null&&Convert.ToInt32(Get(S.aids[index],"stat"))==0)return;
        var map=S.nodes[index].Map;var kind=DefDatabase<PawnKindDef>.GetNamed("Nivarian_Reinforcement");
        var faction=Find.FactionManager.AllFactions.Where(f=>!f.IsPlayer&&f.HostileTo(Faction.OfPlayer)&&f.def.humanlikeFaction).OrderBy(f=>f.loadID).First();
        var threats=new List<Pawn>();
        for(int i=0;i<6;i++){var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind,faction,forceGenerateNewPawn:true));GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(map.Center+new IntVec3(35,0,35),map,10),map);threats.Add(pawn);}
        LordMaker.MakeNewLord(faction,new LordJob_AssaultColony(faction),map,threats);
        Log.Message("NU_THREATS count="+map.attackTargetsCache.TargetsHostileToColony.Count(t=>GenHostility.IsActiveThreatToPlayer(t))+" power="+kind.combatPower+" factions="+string.Join(",",threats.Select(p=>p.Faction?.loadID+":"+p.ThreatDisabled(null))));
        var def=DefDatabase<IncidentDef>.AllDefsListForReading.Single(d=>d.Worker.GetType().Name=="IncidentWorker_NivarianDropPodReinforcement");
        Check((bool)Call(def.Worker,"TryExecuteWorker",StorytellerUtility.DefaultParmsNow(def.category,map)),"native roster letter generation");
        S.aids[index]=Find.Archive.ArchivablesListForReading.OfType<Letter>().Last(l=>aidType.IsInstanceOfType(l)&&ReferenceEquals(Get(l,"targetMap"),map));
        foreach(var pawn in threats)pawn.Destroy(DestroyMode.Vanish);
        Check((int)Get(S.aids[index],"plannedReinforcementCount")>0&&(GenCommandLine.CommandLineArgPassed("nurws")||(int)Get(S.aids[index],"plannedReinforcementCount")==6),"locked roster count");
        Log.Message("NU_ROSTER created map="+map.uniqueID+" plan="+Plan(S.aids[index]));
    }
    static void Guest(Map map)
    {
        var faction=Find.FactionManager.AllFactions.Where(f=>!f.IsPlayer&&!f.defeated&&!f.HostileTo(Faction.OfPlayer)).OrderBy(f=>f.loadID).First();
        var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,faction,forceGenerateNewPawn:true));
        GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(map.Center,map,6),map);
        var job=(LordJob)Activator.CreateInstance(T("Nivarian_Race.Code.Lords.LordJob_NivarianWasteCollector"),map.Center,true);
        var lord=LordMaker.MakeNewLord(faction,job,map,new[]{pawn});
        lord.GotoToil(lord.Graph.lordToils.First(t=>t.GetType().Name=="LordToil_NivarianWaitHostOffer"));
        S.letter=Find.Archive.ArchivablesListForReading.OfType<Letter>().Last(l=>letterType.IsInstanceOfType(l)&&ReferenceEquals(Get(l,"lord"),lord));
    }
    public static void Verify(ThingWithComps source,int current,int step)
    {
        Check(source != null && source == S.nodes[step/48], "verify map source"); Check(S.cycle==current&&S.step==step,"stage ordering");int mapIndex=step/48,round=(step%48)/16,action=step%16;
        var node=Comp(S.nodes[mapIndex],nodeType);var input=Comp(S.inputs[mapIndex],inputType);var animal=Comp(S.animals[mapIndex],animalType);
        if(action==0)Check((bool)Get(node,"allowContainerService")==((round+current*3)%2==0),"node toggle");
        if(action==1)Check((bool)Get(input,"enabled")==((round+current*3)%2==1),"input toggle");
        if(action==2)Check(Math.Abs((float)Get(input,"scanRadius")-(7+round))<0.01,"input radius");
        if(action==3)Check(Math.Abs((float)Get(animal,"guardRadius")-(5+round))<0.01,"guard radius");
        if(action==4)Check((IntVec3)Get(animal,"guardPoint")==S.animals[mapIndex].Map.Center,"guard point");
        if(action==5){Check(!( (IntVec3)Get(animal,"guardPoint")).IsValid&&S.animals[mapIndex].jobs.jobQueue.Count==0,"cancel all orders");Guest(S.nodes[mapIndex].Map);}
        if(action==6)Check(Convert.ToInt32(Get(S.letter,"stat"))==0,"open/cancel must preserve pending letter");
        if(action==7){bool accepted=round%2==0;Check(Convert.ToInt32(Get(S.letter,"stat"))==(accepted?1:2),"guest result");if(accepted)Check(((Lord)Get(S.letter,"lord")).LordJob.GetType().Name=="LordJob_NivarianEatThenLeave","native guest Lord transition");}
        if(action==8){var actual=(ThingDef)Get(Comp(S.dusts[mapIndex],dustType),"_selectedChunkDef");var options=ThingCategoryDefOf.StoneChunks.childThingDefs.OrderBy(d=>d.defName,StringComparer.Ordinal).ToArray();Check(actual==(round==1?null:options[round==0?0:1]),"native dust product");}
        if(action==9)Check((float)Get(Comp(S.factories[mapIndex],containerType),"searchRadius")==10+round*10,"ingredient search radius");
        if(action>=10&&action<=12){var comp=Comp(S.rods[mapIndex],rodType);var name=new[]{"switchOn","reverseMode","artificialOn"}[action-10];Check((bool)Get(comp,name)==((round+current*3)%2==0),"lightning rod "+name);Check(Get(comp,"_powerTrader") is CompPowerTrader power&&ReferenceEquals(power.parent,S.rods[mapIndex]),"lightning power cache rebuilt on current parent");if(action==12)Aid(mapIndex);}
        if(action==13){Check(Convert.ToInt32(Get(S.aids[mapIndex],"stat"))==1,"reinforcement selected-cell acceptance");var lord=S.nodes[mapIndex].Map.lordManager.lords.Where(l=>l.LordJob.GetType().Name=="LordJob_NivarianKiteAssistColony").OrderBy(l=>l.loadID).Last();Check(lord.ownedPawns.Count==(int)Get(S.aids[mapIndex],"plannedReinforcementCount"),"native planned roster replay once");var slots=((IEnumerable)Get(S.aids[mapIndex],"plannedGuestSlots")).Cast<object>().ToArray();int guests=slots.Sum(s=>(int)Get(s,"count"));foreach(var slot in slots){var guestDef=Get(slot,"guestDef");var kind=(PawnKindDef)AccessTools.Property(guestDef.GetType(),"ResolvedPawnKind").GetValue(guestDef);Check(lord.ownedPawns.Count(p=>p.kindDef==kind)==(int)Get(slot,"count"),"native guest roster");}Guest(S.nodes[mapIndex].Map);((LetterWithTimeout)S.letter).StartTimeout(600);}
        if(action==14)Check(Convert.ToInt32(Get(S.letter,"stat"))==2,"shared expiry after target map resumes");
        if(action==15){var wanted=(round+current*3)%2==0;Check((bool)Call(Settings,"IsAidIncidentDisabled",settingsDef)==wanted&&(bool)Call(Settings,"AidIncidentRequiresAlliance",settingsDef)==wanted,"shared per-incident settings UI");if(step==Total-1)Aid(0);}
        Log.Message("NU_ASSERT cycle="+current+" step="+step+" map="+S.nodes[mapIndex].Map.uniqueID+" action="+action+" node="+Get(node,"allowContainerService")+" input="+Get(input,"enabled")+" radius="+Get(input,"scanRadius")+" guard="+Get(animal,"guardRadius")+" point="+Get(animal,"guardPoint")+" letter="+(S.letter==null?-1:Convert.ToInt32(Get(S.letter,"stat"))));
        S.step++;if(S.step==Total){foreach(var sourceInput in S.inputs)AccessTools.Field(inputType,"enabled").SetValue(Comp(sourceInput,inputType),true);foreach(var sourceNode in S.nodes)AccessTools.Field(nodeType,"allowContainerService").SetValue(Comp(sourceNode,nodeType),true);S.until=Mp.AsyncWorldTime.worldTicks+(current==cycles?soak:10000);}
    }
    static void NativeAction(int step)
    {
        Log.Message("NU_INTERFACE value="+MP.InInterface+" ticking="+Mp.Ticking+" cmds="+Mp.ExecutingCmds+" map="+Find.CurrentMap?.uniqueID); int mapIndex=step/48,round=(step%48)/16,action=step%16;var node=Comp(S.nodes[mapIndex],nodeType);var input=Comp(S.inputs[mapIndex],inputType);var animal=Comp(S.animals[mapIndex],animalType);
        Current.Game.CurrentMap=S.nodes[mapIndex].Map;
        if(action==0||action==1)(action==0?node:input).CompGetGizmosExtra().OfType<Command_Toggle>().First().toggleAction();
        if(action==2){sliderValue=7+round;sliderWindow=(Window)Activator.CreateInstance(T("Nivarian_Race.Code.UI.Dialog_CannonInputSettings"),input);Find.WindowStack.Add(sliderWindow);}
        if(action==3){sliderValue=5+round;var gizmo=animal.CompGetGizmosExtra().First(g=>g.GetType().Name=="Gizmo_AnimalGuard");sliderWindow=new NativeGuardProbeWindow(gizmo);Find.WindowStack.Add(sliderWindow);}
        if(action==4){var gizmo=animal.CompGetGizmosExtra().First(g=>g.GetType().Name=="Gizmo_AnimalGuard");((Action<LocalTargetInfo>)Get(gizmo,"action"))(new LocalTargetInfo(S.animals[mapIndex].Map.Center));}
        if(action==5)animal.CompGetGizmosExtra().OfType<Command_Action>().Last().action();
        if(action==6){((ChoiceLetter)S.letter).OpenLetter();Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Last().Close();((ChoiceLetter)S.letter).OpenLetter();Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Last().Close();}
        if(action==7){var choices=((ChoiceLetter)S.letter).Choices.ToArray();choices[round%2==0?0:1].action();choices[round%2==0?0:1].action();}
        if(action==8){var comp=Comp(S.dusts[mapIndex],dustType);comp.CompGetGizmosExtra().OfType<Command_Action>().Single(g=>g.action.Method.Name=="OpenProductMenu").action();var menu=Find.WindowStack.Windows.OfType<FloatMenu>().Last();var options=(List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu),"options").GetValue(menu);var defs=ThingCategoryDefOf.StoneChunks.childThingDefs.OrderBy(d=>d.defName,StringComparer.Ordinal).ToArray();var target=round==1?null:defs[round==0?0:1];options.Single(o=>o.Label==(target==null?"Nivarian.DustCatcher.ProductRandom".Translate().ToString():target.LabelCap.ToString())).action();menu.Close();}
        if(action==9){sliderValue=10+round*10;sliderWindow=(Window)Activator.CreateInstance(T("Nivarian_Race.Code.UI.Dialog_UniversalContainerSettings"),Comp(S.factories[mapIndex],containerType));Find.WindowStack.Add(sliderWindow);}
        if(action>=10&&action<=12)Comp(S.rods[mapIndex],rodType).CompGetGizmosExtra().OfType<Command_Toggle>().ElementAt(action-10).toggleAction();
        if(action==13){var letter=(ChoiceLetter)S.aids[mapIndex];var plan=Plan(letter);letter.Choices.First().action();Check(Find.Targeter.IsTargeting&&Convert.ToInt32(Get(letter,"stat"))==0&&!((bool)Get(letter,"AwaitingDropCellSelection")),"local target preview");Find.Targeter.StopTargeting();Check(Plan(letter)==plan,"cancel preserves locked roster");letter.Choices.First().action();var actionTarget=(Action<LocalTargetInfo>)Get(Find.Targeter,"action");actionTarget(new LocalTargetInfo(S.nodes[mapIndex].Map.Center));actionTarget(new LocalTargetInfo(S.nodes[mapIndex].Map.Center));Find.Targeter.StopTargeting();}
        if(action==14){((ChoiceLetter)S.letter).OpenLetter();Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Last().Close();Find.LetterStack.RemoveLetter(S.letter);Check(Convert.ToInt32(Get(S.letter,"stat"))==0,"local hidden letter must not reject shared pending state");resumedExpiry=!(async&&maps==2);if(!resumedExpiry)Mp.Client.SendCommand(CommandType.MapTimeSpeed,S.nodes[mapIndex].Map.uniqueID,(byte)TimeSpeed.Paused);if(maps==2)Current.Game.CurrentMap=Find.Maps.First(m=>m!=S.nodes[mapIndex].Map);}
        if(action==15){var modType=T("Nivarian.NivarianMod");var method=AccessTools.GetDeclaredMethods(typeof(LoadedModManager)).Single(m=>m.Name=="GetMod"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0);var mod=method.MakeGenericMethod(modType).Invoke(null,null);var window=Get(mod,"settingsWindow");AccessTools.Field(window.GetType(),"selectedSection").SetValue(window,"Aid");var defs=((IEnumerable)AccessTools.Method(Settings.GetType(),"AllAidIncidentDefs").Invoke(null,null)).Cast<IncidentDef>().ToArray();settingsRow=Array.IndexOf(defs,settingsDef);Check(settingsRow>=0,"native settings row");settingValue=(round+cycle*3)%2==0;sliderWindow=new NativeSettingsProbeWindow(mod);Find.WindowStack.Add(sliderWindow);}
        Log.Message("NU_DISPATCH cycle="+cycle+" step="+step+" native action="+action);
    }
    public static void Update()
    {
        if(!enabled)return;if(File.Exists(At("quit"))||File.Exists(At(Peer+".quit"))){Application.Quit();return;}
        if(failed||done||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+0.1f;
        try
        {
            Check(Time.realtimeSinceStartup-started<3600,"overall timeout");
            if(!client&&!baselineLoading&&GenCommandLine.CommandLineArgPassed("nurws")&&Current.ProgramState==ProgramState.Entry&&!LongEventHandler.AnyEventNowOrWaiting){baselineLoading=true;Log.Message("NU_DIAG BASELINE_REQUEST");LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("NivarianInput"),"Loading baseline",false,null);return;}
            if(Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
            if(!client&&!hosted&&!MP.IsInMultiplayer)
            {
                hosted=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(maps==2){var first=Find.CurrentMap;var tiles=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(first.Tile,tiles);var tile=tiles.OrderBy(t=>t.tileId).First(t=>!Find.WorldObjects.AnyMapParentAt(t)&&Find.WorldPathGrid.Passable(t));var parent=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=tile;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);MapGenerator.GenerateMap(new IntVec3(80,1,80),parent,first.generatorDef);Current.Game.CurrentMap=first;}
                Check(HostWindow.HostProgrammatically(HostingSettings()),"host rejected");return;
            }
            if(!MP.IsInMultiplayer||Mp.Client?.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            Check(!Mp.session.desynced,"desync");
            int requested=client?cycle:(File.Exists(At("cycle.txt"))?int.Parse(File.ReadAllText(At("cycle.txt"))):0);
            if(readyCycle!=requested)
            {
                if(!TickPatch.Frozen){if(!client)Mp.Client.Send(new ClientFreezePacket(true));return;}
                if(!File.Exists(At(Peer+".frozen"+requested)))Write("frozen"+requested,Snapshot());
                if(Mp.session.players.Count!=2||!File.Exists(At("host.frozen"+requested))||!File.Exists(At("client"+requested+".frozen"+requested)))return;
                Check(File.ReadAllText(At("host.frozen"+requested))==File.ReadAllText(At("client"+requested+".frozen"+requested)),"BASELINE_LOAD_DRIFT");
                readyCycle=requested;began=sent=waiting=joinpoint=pausing=false;dispatched=-1;
                if(!client){Mp.Client.Send(new ClientFreezePacket(false));Speed(TimeSpeed.Superfast);if(async&&maps==2)Mp.Client.SendCommand(CommandType.MapTimeSpeed,Find.Maps.OrderBy(m=>m.uniqueID).Last().uniqueID,(byte)TimeSpeed.Normal);}return;
            }
            if(TickPatch.Frozen&&joinpoint&&!client&&!Mp.LocalServer.worldData.CreatingJoinPoint){if(!File.Exists(At("host.complete"+requested)))Write("complete"+requested,Snapshot());return;}
            if(TickPatch.Frozen&&client&&File.Exists(At("host.complete"+requested))){Check(Snapshot()==File.ReadAllText(At("host.complete"+requested)),"completion mismatch");Write("complete",Snapshot());done=true;return;}
            if(TickPatch.Frozen)return;Check(Mp.session.players.Count==2,"unexpected disconnect");
            if(!began){if(client&&!sent)sent=begin.DoSync(null,cycle);return;}
            if(S.step<Total)
            {
                if(!client)return;
                if(waiting){if(S.step==dispatched)return;waiting=sent=false;}
                if(!sent){NativeAction(S.step);dispatched=S.step;due=TickPatch.Timer+100;sent=true;return;}
                if(TickPatch.Timer<due)return;
                if(S.step%16==14)
                {
                    var map=S.nodes[S.step/48].Map;
                    if(!resumedExpiry){Check(map.AsyncTime().DesiredTimeSpeed==TimeSpeed.Paused&&Convert.ToInt32(Get(S.letter,"stat"))==0&&!((LetterWithTimeout)S.letter).TimeoutPassed,"paused target-map letter must remain pending while another map advances");Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)(S.step/48==0?TimeSpeed.Superfast:TimeSpeed.Normal));resumedExpiry=true;due=TickPatch.Timer+100;return;}
                    if(map.AsyncTime().mapTicks<((LetterWithTimeout)S.letter).disappearAtTick+10)return;
                }
                sliderWindow?.Close();sliderWindow=null;verify.DoSync(null,S.nodes[S.step/48],cycle,S.step);waiting=true;return;
            }
            if(Mp.AsyncWorldTime.worldTicks>=checkpoint){checkpoint=Mp.AsyncWorldTime.worldTicks+10000;Log.Message("NU_CHECKPOINT cycle="+requested+" world="+Mp.AsyncWorldTime.worldTicks+" until="+S.until+" players=2 desynced=False");}
            if(client&&maps==2)Current.Game.CurrentMap=Find.Maps[(TickPatch.Timer/1500)%Find.Maps.Count];
            if(Mp.AsyncWorldTime.worldTicks<S.until)return;
            Check(S.fuelTargets.All(t=>t.GetComp<CompRefuelable>().Fuel>0),"actual cannon delivery to fuel-only facility");
            foreach(var target in S.fuelTargets)Check(((IEnumerable)Get(target.Map.components.Single(c=>c.GetType().Name=="CannonOrderLedger"),"orders")).Cast<object>().Any(o=>ReferenceEquals(Get(o,"destBuilding"),target)&&Get(o,"closeReason").ToString()=="Delivered"),"native fuel delivery receipt");
            if(client){if(!File.Exists(At(Peer+".actionscomplete")))Write("actionscomplete",State());return;}
            if(!File.Exists(At("client"+requested+".actionscomplete")))return;
            if(!pausing){Speed(TimeSpeed.Paused);pausing=true;pauseAt=TickPatch.Timer+45;return;}
            if(TickPatch.Timer<pauseAt)return;
            if(!joinpoint){Check(Mp.LocalServer.worldData.TryStartJoinPointCreation(true),"joinpoint rejected");joinpoint=true;return;}
            if(!Mp.LocalServer.worldData.CreatingJoinPoint)Mp.Client.Send(new ClientFreezePacket(true));
        }
        catch(Exception e){failed=true;Write("failed",e.ToString());Log.Error("NU_FAILED "+e);}
    }
}








