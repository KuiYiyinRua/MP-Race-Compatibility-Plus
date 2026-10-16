using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.API;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;
using UnityEngine;
using Mp=Multiplayer.Client.Multiplayer;

public sealed class PowerDiagnosisComponent : GameComponent
{
    public PowerDiagnosisComponent(Game game) { }
    public override void GameComponentUpdate() => PowerDiagnosis.Update();
}

[StaticConstructorOnStartup]
public static class PowerDiagnosis
{
    static readonly bool enabled=GenCommandLine.CommandLineArgPassed("powerdiagnosis");
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static bool loading,loaded,done,hosting,lateSample;
    static float loadedAt;
    static int runStart=-1,checkpoint=1000;
    static ISyncMethod fixture,cut;
    static bool fixtureSent,cutSent;
    static float captureAt=-1;
    static string captureId;
    static int overlaySamples;
    static PowerDiagnosis()
    {
        if(!enabled)return;
        fixture=MP.RegisterSyncMethod(typeof(PowerDiagnosis),nameof(BuildFixtures));
        cut=MP.RegisterSyncMethod(typeof(PowerDiagnosis),nameof(CutFixturePower));
        var harmony=new Harmony("local.meow.powerdiagnosis");
        harmony.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(PowerDiagnosis),nameof(Update)));
        harmony.Patch(AccessTools.Method(typeof(CompPowerTrader),"UpdateOverlays"),prefix:new HarmonyMethod(typeof(PowerDiagnosis),nameof(OverlayBefore)));
        Log.Message("POWER_DIAG BOOT run="+root);
    }
    static void Write(string text) { Log.Message("POWER_DIAG "+text);File.AppendAllText(Path.Combine(root,"diagnosis.txt"),text+Environment.NewLine); }
    static string Try(Func<object> read) {try{return (read()?.ToString()??"<null>").Replace("\n"," | ");}catch(Exception e){return "ERROR "+e;}}
    static void OverlayBefore(CompPowerTrader __instance)
    {
        if(overlaySamples>=20||(__instance.parent.ThingID!="StandingLamp261133"&&__instance.parent.ThingID!="CommsConsole307492"&&__instance.parent.ThingID!="ElectricStove274836"))return;
        overlaySamples++;Write("OVERLAY_UPDATE id="+__instance.parent.ThingID+" state="+Current.ProgramState+" scribe="+Scribe.mode+" raw="+AccessTools.Field(typeof(CompPowerTrader),"powerOnInt").GetValue(__instance)+" logical="+__instance.PowerOn+" net="+(__instance.PowerNet!=null));
    }
    static void Owners(MethodBase m)
    {
        if(m==null)return;
        var p=Harmony.GetPatchInfo(m);
        Write("PATCHES "+m.DeclaringType.FullName+"."+m.Name+" => "+(p==null?"none":string.Join(";",p.Prefixes.Select(x=>"pre:"+x.owner+":"+x.PatchMethod.DeclaringType+"."+x.PatchMethod.Name).Concat(p.Postfixes.Select(x=>"post:"+x.owner+":"+x.PatchMethod.DeclaringType+"."+x.PatchMethod.Name)).Concat(p.Transpilers.Select(x=>"il:"+x.owner+":"+x.PatchMethod.DeclaringType+"."+x.PatchMethod.Name)))));
    }
    static void Dump()
    {
        foreach(var a in AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name.Contains("Meow")||a.GetName().Name.Contains("Nivarian")))Write("ASSEMBLY "+a.FullName+" mvid="+a.ManifestModule.ModuleVersionId);
        Owners(AccessTools.PropertyGetter(typeof(CompPowerTrader),"PowerOn"));Owners(AccessTools.PropertySetter(typeof(CompPowerTrader),"PowerOn"));
        Owners(AccessTools.PropertyGetter(typeof(CompPower),"PowerNet"));
        Owners(AccessTools.Method(typeof(CompPowerTrader),"CompInspectStringExtra"));Owners(AccessTools.Method(typeof(CompPower),"CompInspectStringExtra"));
        Owners(AccessTools.Method(typeof(ThingWithComps),"GetInspectString"));Owners(AccessTools.Method(typeof(ThingWithComps),"InspectStringPartsFromComps"));
        Owners(AccessTools.Method(typeof(InspectPaneFiller),"DrawInspectStringFor"));Owners(AccessTools.Method(typeof(InspectPaneFiller),"DrawInspectString"));
        Owners(AccessTools.Method(typeof(PowerNetManager),"PowerNetsTick"));Owners(AccessTools.Method(typeof(Map),"MapPostTick"));
        Map previous=Find.CurrentMap;
        foreach(var focus in Find.Maps.OrderBy(m=>m.uniqueID))
        {
            Current.Game.CurrentMap=focus;
            foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
            {
                var et=AccessTools.TypeByName("NivarianCommander.FieldEnergy");
                object energy=et==null?null:AccessTools.Method(et,"For").Invoke(null,new object[]{map});
                Write("MAP focus="+focus.uniqueID+" map="+map.uniqueID+" elecDisabled="+map.gameConditionManager.ElectricityDisabled(map)+" fieldActive="+Try(()=>AccessTools.Property(et,"Active").GetValue(energy))+" leases="+Try(()=>AccessTools.Property(et,"HasPowerLeases").GetValue(energy)));
                foreach(var t in map.listerThings.AllThings.Where(t=>t.def.defName=="WoodFiredGenerator"||t.def.defName=="StandingLamp"||t.def.defName=="CommsConsole"||t.def.defName=="ElectricStove"||t.def.defName=="Nivarian_LightningRod"||t.def.defName=="NMC_NutrientEnergyTower"||t.def.defName=="Battery").OrderBy(t=>t.thingIDNumber))
                {
                    var c=t.TryGetComp<CompPowerTrader>();var p=t.TryGetComp<CompPower>();var n=p?.PowerNet;
                    Write("THING focus="+focus.uniqueID+" map="+map.uniqueID+" id="+t.ThingID+" pos="+t.Position+" powerType="+p?.GetType()+" raw="+Try(()=>c==null?null:AccessTools.Field(typeof(CompPowerTrader),"powerOnInt").GetValue(c))+" getter="+c?.PowerOn+" output="+c?.PowerOutput+" needsIcon="+Try(()=>c==null?null:AccessTools.Field(typeof(CompPowerTrader),"overlayNeedsPower").GetValue(c))+" parent="+p?.connectParent?.parent?.ThingID+" netMap="+n?.Map?.uniqueID+" comps="+n?.powerComps.Count+" batteries="+n?.batteryComps.Count+" gain="+Try(()=>n?.CurrentEnergyGainRate())+" stored="+Try(()=>n?.CurrentStoredEnergy())+" idle="+p?.Props.idlePowerDraw+" always="+p?.Props.alwaysDisplayAsUsingPower);
                    Write("INSPECT id="+t.ThingID+" comp="+Try(()=>p.CompInspectStringExtra())+" whole="+Try(()=>t.GetInspectString()));
                    Write("FUNCTION id="+t.ThingID+" comms="+(t is Building_CommsConsole comms?(bool?)comms.CanUseCommsNow:null)+" bills="+(t is Building_WorkTable table?(bool?)table.CurrentlyUsableForBills():null)+" glows="+t.TryGetComp<CompGlower>()?.Glows);
                    if(c!=null&&energy!=null)Write("FIELD id="+t.ThingID+" lost="+Try(()=>AccessTools.Method(et,"GridSupplyLost").Invoke(energy,new object[]{c}))+" connector="+Try(()=>AccessTools.Method(et,"HasFieldConnector").Invoke(energy,new object[]{n}))+" native="+Try(()=>AccessTools.Method(et,"HasNativeSource").Invoke(energy,new object[]{n}))+" netWatts="+Try(()=>AccessTools.Method(et,"NetWatts").Invoke(energy,new object[]{n})));
                    if(t is ThingWithComps twc)foreach(var comp in twc.AllComps)Write("COMP id="+t.ThingID+" type="+comp.GetType()+" inspect="+Try(()=>comp.CompInspectStringExtra()));
                }
            }
        }
        Current.Game.CurrentMap=previous;
        Write("COMPLETE snapshot-only diagnostic; no multiplayer release validation");
        File.WriteAllText(Path.Combine(root,"complete"),"snapshot");
    }
    public static void BuildFixtures()
    {
        foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
        {
            var origin=new IntVec3(25,0,25);
            for(int x=-2;x<=15;x++)for(int z=-2;z<=9;z++)
            {
                var cell=origin+new IntVec3(x,0,z);
                foreach(var thing in cell.GetThingList(map).ToArray())if(!(thing is Pawn))thing.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
            }
            Action<string,int,int> spawn=(name,x,z)=>
            {
                var thing=GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name)),origin+new IntVec3(x,0,z),map);
                thing.SetFaction(Faction.OfPlayer);
                if(name=="WoodFiredGenerator")thing.TryGetComp<CompRefuelable>().Refuel(75);
                Write("FIXTURE map="+map.uniqueID+" thing="+thing.ThingID);
            };
            spawn("WoodFiredGenerator",0,0);
            for(int x=2;x<=12;x++)spawn("HiddenConduit",x,0);
            spawn("StandingLamp",7,1);spawn("CommsConsole",9,2);spawn("ElectricStove",12,2);spawn("Battery",5,1);
        }
    }
    public static void CutFixturePower()
    {
        var map=Find.Maps.OrderBy(m=>m.uniqueID).Last();
        foreach(var thing in map.listerThings.AllThings.Where(t=>t.Position.x>=23&&t.Position.x<=40&&t.Position.z>=23&&t.Position.z<=34).ToArray())
        {
            if(thing.def.defName=="WoodFiredGenerator"){var fuel=thing.TryGetComp<CompRefuelable>();fuel.ConsumeFuel(fuel.Fuel);}
            if(thing.def.defName=="Battery")thing.TryGetComp<CompPowerBattery>().SetStoredEnergyPct(0);
        }
        Write("CUT_FIXTURE_POWER map="+map.uniqueID);
    }
    public static void Update()
    {
        if(!enabled)return;
        if(File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}
        if(LongEventHandler.AnyEventNowOrWaiting)return;
        try
        {
            if(!loading&&Current.ProgramState==ProgramState.Entry)
            {
                loading=true;Mp.username="PowerDiagnosis";
                ClientUtil.DoubleLongEvent(()=>Replay.LoadReplay(new FileInfo(Path.Combine(root,"Input.zip")),true,()=>{loaded=true;loadedAt=Time.realtimeSinceStartup;Write("REPLAY_LOADED");},()=>{throw new Exception("Replay cancelled");}),"MpLoading");return;
            }
            if(!loaded||Current.ProgramState!=ProgramState.Playing||TickPatch.Simulating)return;
            if(File.Exists(Path.Combine(root,"inspect.request")))
            {
                string requested=File.ReadAllText(Path.Combine(root,"inspect.request")).Trim();
                File.Delete(Path.Combine(root,"inspect.request"));
                captureId=new[]{"CommsConsole307492","ElectricStove274836","StandingLamp261133"}.Contains(requested)?requested:"CommsConsole307492";
                var t=Find.Maps.SelectMany(m=>m.listerThings.AllThings).First(t=>t.ThingID==captureId);
                Current.Game.CurrentMap=t.Map;Find.Selector.ClearSelection();Find.Selector.Select(t);Find.CameraDriver.JumpToCurrentMapLoc(t.Position.ToVector3Shifted());captureAt=Time.realtimeSinceStartup+3;
            }
            // CaptureScreenshot completes at frame end. Dump changes focus across
            // both maps, which clears selection; do not run it in this frame.
            if(captureAt>0&&Time.realtimeSinceStartup>=captureAt){captureAt=-1;ScreenCapture.CaptureScreenshot(Path.Combine(root,"inspect-"+captureId+".png"));Write("STAGE inspected selected="+Find.Selector.SingleSelectedThing?.ThingID);}
            if(!done){done=true;Write("STAGE initial");Dump();return;}
            if(!lateSample&&Time.realtimeSinceStartup-loadedAt>=90){lateSample=true;Write("STAGE late");Dump();File.WriteAllText(Path.Combine(root,"late.complete"),"done");return;}
            if(File.Exists(Path.Combine(root,"dump"))){File.Delete(Path.Combine(root,"dump"));Write("STAGE manual");Dump();}
            if(!hosting&&File.Exists(Path.Combine(root,"host.request")))
            {
                hosting=true;Mp.AsyncWorldTime.SetTimeEverywhere(TimeSpeed.Paused);
                var settings=new ServerSettings {gameName="PowerDiagnosis",direct=true,directAddress="127.0.0.1:31185",lan=false,steam=false,multifaction=false,asyncTime=false,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};
                if(!(bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}))throw new Exception("Host pre-init failed");
                HostUtil.HostServer(settings,true);Write("HOST_REQUESTED");return;
            }
            if(hosting&&Mp.Client?.State==ConnectionStateEnum.ClientPlaying)
            {
                if(!fixtureSent&&File.Exists(Path.Combine(root,"fixture.request"))){fixtureSent=fixture.DoSync(null);}
                if(!cutSent&&File.Exists(Path.Combine(root,"off.request"))){cutSent=cut.DoSync(null);}
                if(runStart==-1&&File.Exists(Path.Combine(root,"run")))
                {
                    runStart=Mp.AsyncWorldTime.worldTicks;Write("RUN_START tick="+runStart);
                    Mp.Client.Send(new ClientFreezePacket(false));
                    Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Fast);
                    foreach(var map in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)TimeSpeed.Fast);
                }
                if(runStart>=0&&Mp.AsyncWorldTime.worldTicks-runStart>=checkpoint)
                {
                    Write("STAGE tick"+checkpoint+" world="+Mp.AsyncWorldTime.worldTicks+" maps="+string.Join(";",Find.Maps.Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks)));
                    Dump();checkpoint+=1000;
                    if(checkpoint>10000){Mp.Client.Send(new ClientFreezePacket(true));File.WriteAllText(Path.Combine(root,"run.complete"),"host-only 10000");runStart=-2;}
                }
            }
        }
        catch(Exception e){done=true;Write("FAILED "+e);File.WriteAllText(Path.Combine(root,"failed"),e.ToString());}
    }
}
