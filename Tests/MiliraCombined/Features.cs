using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;

internal static class MiliraFeatures
{
    internal static readonly bool Enabled=GenCommandLine.CommandLineArgPassed("milirafeatures");
    static bool client, sent, prepared, drawing, sliderClicked, hairClicked, failed, advancing;
    static int round, start, mapStart, began=-1, nextCheck;
    static float deadline;
    static Pawn mech, caller;
    static Map targetMap;
    static ThingComp carrier, hair;
    static Window hairWindow, fixtureWindow;
    static Gizmo storageGizmo;
    static ISyncMethod prepare;
    static string root=>Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string peer=>client?"client":"host";
    static readonly string[] strikes={"Harrier_EMP_Sweep","Harrier_Napalm_Sweep","Harrier_Cluster_Bomb_Sweep",
        "Harrier_Airstrike_Sweep","Harrier_Smoke_Sweep","Harrier_Gas_Sweep","Harrier_Airburst_Sweep","Harrier_FiveKG_Sweep","Harrier_Strafing_Sweep"};
    static int launched, split, spawned;
    static string curveReceipt="";
    static Thing strikeMarker;
    static int markerTicks;
    static void Require(bool ok,string msg){if(!ok)throw new Exception(msg);}
    static object Field(object obj,string name)=>AccessTools.Field(obj.GetType(),name).GetValue(obj);
    internal static void Init(Harmony h)
    {
        if(!Enabled)return;
        client=GenCommandLine.CommandLineArgPassed("twclient");
        AccessTools.Field(AccessTools.TypeByName("PLAMilira.ModSettings_PLAMilira"),"PLAMilira_ModSetting_Harrier_Sound").SetValue(null,client);
        Log.Message("MILIRA_FEATURE SOUND_SETTING client="+client+" muted="+client);
        prepare=MP.RegisterSyncMethod(typeof(MiliraFeatures),nameof(Prepare));
        h.Patch(AccessTools.Method(typeof(Widgets),"DraggableBar"),prefix:new HarmonyMethod(typeof(MiliraFeatures),nameof(Drag)));
        h.Patch(AccessTools.Method(typeof(Widgets),"ButtonInvisible",new[]{typeof(Rect),typeof(bool)}),prefix:new HarmonyMethod(typeof(MiliraFeatures),nameof(HairClick)));
        foreach(string n in new[]{"Map_Sweep_Plane","Map_Sweep_Plane_Strafing","Projectile_TrackingBullet","Projectile_TrackingBulletNormal","Projectile_Plane","Projectile_WASP"})
        {
            var t=AccessTools.TypeByName("PLAMilira."+n);
            h.Patch(AccessTools.GetDeclaredMethods(t).Single(m=>m.Name=="Launch"),postfix:new HarmonyMethod(typeof(MiliraFeatures),nameof(Launched)){priority=Priority.Last});
        }
        h.Patch(AccessTools.Method(AccessTools.TypeByName("PLAMilira.Map_Sweep_Plane"),"SpawnSplitManagerAndFire"),postfix:new HarmonyMethod(typeof(MiliraFeatures),nameof(Split)));
        h.Patch(AccessTools.Method(AccessTools.TypeByName("PLAMilira.MapSweepStrike"),"SpawnSetup"),postfix:new HarmonyMethod(typeof(MiliraFeatures),nameof(Spawned)));
        h.Patch(AccessTools.Method(AccessTools.TypeByName("PLAMilira.MapSweepStrike"),"Tick"),prefix:new HarmonyMethod(typeof(MiliraFeatures),nameof(MarkerTick)));
        h.Patch(AccessTools.Method(typeof(Thing),"Destroy"),prefix:new HarmonyMethod(typeof(MiliraFeatures),nameof(MarkerDestroy)));
    }
    static void Launched(Thing __instance)
    {
        if(!prepared)return;
        Require((bool)Field(__instance,"OnceFlag"),"curve was not initialized on launch");
        launched++;
        curveReceipt+=__instance.ThingID+":"+Field(__instance,"RandNew")+";";
    }
    static void Split(){if(prepared)split++;}
    static string MarkerState(Thing thing)=>thing.ThingID+" spawned="+thing.Spawned+" destroyed="+thing.Destroyed+" ticks="+markerTicks+" next="+Field(thing,"ticksToNextStrike")+" done="+Field(thing,"strikesDone")+" max="+Field(thing,"MaxStrikes")+" ttl="+Field(thing,"ticksToDestroy");
    static void Spawned(Thing __instance){if(prepared&&round>=3){Require(__instance.Map==targetMap,"strike spawned on caller/view map instead of target map");spawned++;strikeMarker=__instance;markerTicks=0;Log.Message("MILIRA_FEATURE SPAWN "+MarkerState(__instance)+" map="+__instance.Map.uniqueID);}}
    static void MarkerTick(Thing __instance){if(__instance!=strikeMarker)return;markerTicks++;if(markerTicks==1||markerTicks==20||markerTicks==90||markerTicks==600)Log.Message("MILIRA_FEATURE MARKER_TICK "+MarkerState(__instance));}
    static void MarkerDestroy(Thing __instance){if(__instance==strikeMarker)Log.Message("MILIRA_FEATURE MARKER_DESTROY "+MarkerState(__instance)+" stack="+Environment.StackTrace);}
    static bool Drag(ref float __7)
    {
        if(!drawing||sliderClicked||Event.current.type!=EventType.Repaint)return true;
        __7=(round+1)*0.25f;sliderClicked=true;Log.Message("MILIRA_FEATURE NATIVE_SLIDER round="+round);return false;
    }
    static bool HairClick(Rect butRect,ref bool __result)
    {
        if(!drawing||hairClicked||Event.current.type!=EventType.Repaint)return true;
        __result=Mathf.RoundToInt(butRect.x)==(round+1)*100;
        if(__result){hairClicked=true;Log.Message("MILIRA_FEATURE NATIVE_HAIR round="+round);}
        return false;
    }
    public static void Prepare(int n)
    {
        round=n;sent=advancing=false;sliderClicked=hairClicked=false;launched=split=spawned=0;curveReceipt="";
        fixtureWindow?.Close(false);hairWindow?.Close(false);fixtureWindow=hairWindow=null;
        var homeMaps=Find.Maps.Where(m=>m.IsPlayerHome).OrderBy(m=>m.uniqueID).ToArray();
        Require(homeMaps.Length==(MiliraActionsProbe.MultiMap?2:1),"feature home-map count="+homeMaps.Length);
        targetMap=MiliraActionsProbe.MultiMap&&n>=3&&((n-3)/3)%2==1?homeMaps[1]:homeMaps[0];
        if(mech!=null&&(!mech.Spawned||mech.Map!=targetMap||mech.Dead||mech.Destroyed))
        {
            Log.Message("MILIRA_FEATURE FIXTURE_LOST round="+n+" mech="+mech.ThingID+" spawned="+mech.Spawned+" dead="+mech.Dead+" destroyed="+mech.Destroyed+" map="+(mech.Map==null?"null":mech.Map.uniqueID.ToString()));
            mech=null;carrier=hair=null;
        }
        if(mech==null)
        {
            mech=PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"),Faction.OfPlayer);
            GenSpawn.Spawn(mech,CellFinder.RandomClosewalkCellNear(targetMap.Center,targetMap,10),targetMap);
            carrier=mech.AllComps.Single(c=>c.GetType().FullName=="AncotLibrary.CompThingCarrier_Custom");
            hair=mech.AllComps.Single(c=>c.GetType().FullName=="Milira.CompMilianHairSwitch");
            Require(((System.Collections.IList)AccessTools.Property(hair.GetType(),"frontHairPaths").GetValue(hair)).Count>=4,"hair fixture variants");
        }
        if(caller==null||caller.Dead||caller.Destroyed||caller.Map==null||n>=3)
            caller=Find.Maps.Where(m=>m!=targetMap).SelectMany(m=>m.mapPawns.FreeColonists).FirstOrDefault()??targetMap.mapPawns.FreeColonists.First();
        start=TickPatch.Timer;mapStart=targetMap.AsyncTime().mapTicks;if(began<0)began=start;nextCheck=start;deadline=Time.realtimeSinceStartup+180;prepared=true;
        Log.Message("MILIRA_FEATURE PREPARE "+n+" mech="+mech.ThingID+" targetMap="+targetMap.uniqueID+" callerMap="+caller.Map?.uniqueID);
    }
    sealed class Fixture:Window
    {
        public override Vector2 InitialSize=>new Vector2(740,430);
        public override void DoWindowContents(Rect rect)
        {
            drawing=true;
            try{
                storageGizmo.GizmoOnGUI(new Vector2(10,10),200,default(GizmoRenderParms));
                AccessTools.Method(hairWindow.GetType(),"DrawScrollHairSwitch").Invoke(hairWindow,new object[]{mech,new Rect(0,100,700,110)});
            }finally{drawing=false;}
        }
    }
    internal static void Update()
    {
        if(failed)return;
        try{
            Require(!Mp.session.desynced,"desync");
            if(!prepared){if(client&&!sent)sent=prepare.DoSync(null,0);return;}
            if(client&&!sent&&TickPatch.Timer-start>60)
            {
                sent=true;
                Current.Game.CurrentMap=targetMap;
                if(round<3)
                {
                    storageGizmo=(Gizmo)Activator.CreateInstance(AccessTools.TypeByName("AncotLibrary.ThingCarrierGizmo"),carrier);
                    ((Command_Action)hair.CompGetGizmosExtra().Single()).action();
                    hairWindow=Find.WindowStack.Windows.Single(w=>w.GetType().FullName=="Milira.Dialog_MilianHairStyleConfig");
                    Find.WindowStack.TryRemove(hairWindow,false);
                    ((Action<Color>)Field(hairWindow,"action"))(new Color((round+1)*0.2f,0.3f,0.4f,1));
                    fixtureWindow=new Fixture();Find.WindowStack.Add(fixtureWindow);
                }
                else
                {
                    var window=(Window)Activator.CreateInstance(AccessTools.TypeByName("PLAMilira.Dialog_Harrier_ArrowWindow"),caller);
                    AccessTools.Method(window.GetType(),"StartTargetingWithThing").Invoke(window,new object[]{strikes[round-3],0,"test",0,10f});
                    var action=(Action<LocalTargetInfo>)AccessTools.Field(typeof(Targeter),"action").GetValue(Find.Targeter);
                    Require(action!=null,"native target callback missing");
                    var cell=targetMap.Center+new IntVec3(Math.Min(55,targetMap.Size.x/4),0,Math.Min(45,targetMap.Size.z/4));
                    Require(cell.InBounds(targetMap),"native target outside map="+targetMap.uniqueID+" cell="+cell+" size="+targetMap.Size);
                    action(new LocalTargetInfo(cell));Find.Targeter.StopTargeting();
                    Find.CameraDriver.JumpToCurrentMapLoc(cell.ToVector3Shifted());
                    Log.Message("MILIRA_FEATURE NATIVE_TARGET "+strikes[round-3]+" interface="+MP.InInterface);
                }
            }
            Require(Time.realtimeSinceStartup<deadline,"feature action timeout round="+round);
            if(TickPatch.Timer>=nextCheck){nextCheck+=1000;Log.Message("MILIRA_FEATURE CHECK round="+round+" elapsed="+(TickPatch.Timer-began)+" launch="+launched+" split="+split+" desynced="+Mp.session.desynced);}
            int delay=round<3?400:1400;
            if(TickPatch.Timer-start<delay)return;
            string receipt;
            if(round<3)
            {
                Require((int)Field(carrier,"maxToFill")==150*(round+1),"storage mismatch "+Field(carrier,"maxToFill"));
                Require((int)Field(hair,"num")==round+1,"hair mismatch "+Field(hair,"num"));
                Require((Color)Field(hair,"colorOverride")==new Color((round+1)*0.2f,0.3f,0.4f,1),"color mismatch");
                receipt=Field(carrier,"maxToFill")+":"+Field(hair,"num")+":"+Field(hair,"frontHairPath")+":"+Field(hair,"colorOverride");
            }
            else{Require(spawned==1&&launched>0,"strike not reached spawn="+spawned+" launched="+launched+" mapTicks="+(targetMap.AsyncTime().mapTicks-mapStart)+" speed="+targetMap.AsyncTime().DesiredTimeSpeed+" marker="+(strikeMarker==null?"null":MarkerState(strikeMarker)));receipt=spawned+":"+launched+":"+split+":"+curveReceipt;}
            string path=Path.Combine(root,peer+".feature"+round);
            if(!File.Exists(path)){File.WriteAllText(path,receipt);Log.Message("MILIRA_FEATURE ASSERT "+round+" "+receipt);}
            var other=Path.Combine(root,(client?"host":"client")+".feature"+round);
            if(!File.Exists(other))return;
            Require(File.ReadAllText(path)==File.ReadAllText(other),"feature peer mismatch round="+round);
            if(round>=11){if(!File.Exists(Path.Combine(root,peer+".actionscomplete"))){File.WriteAllText(Path.Combine(root,peer+".actionscomplete"),"desynced=False ticks="+(TickPatch.Timer-began));Log.Message("MILIRA_FEATURE ACTIONS_COMPLETE desynced=False");} ColdProbe.Start();}
            else if(client&&!advancing){advancing=true;prepare.DoSync(null,round+1);}
        }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(root,peer+".failed"),e.ToString());Log.Error("MILIRA_ACTIONS FAILED "+e);}
    }
}
