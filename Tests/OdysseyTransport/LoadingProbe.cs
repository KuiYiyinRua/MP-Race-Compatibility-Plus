using System;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Persistent;
using RimWorld;
using Verse;
using UnityEngine;
public static class LoadingProbe
{
 static object oldSession;static TransporterLoading OldSession=>(TransporterLoading)oldSession;static bool staleChecked;static ISyncMethod staleCheck;static Building_PassengerShuttle shuttle;static Thing cargo;static ISyncMethod setup,check;
 static bool requested,ready,done,checking;static int step,round;static float deadline;
 static TransporterLoading Session=>shuttle.Map.MpComp().sessionManager.GetFirstOfType<TransporterLoading>();
 public static void Install(){staleCheck=MP.RegisterSyncMethod(typeof(LoadingProbe),nameof(CheckStale));setup=MP.RegisterSyncMethod(typeof(LoadingProbe),nameof(Setup));check=MP.RegisterSyncMethod(typeof(LoadingProbe),nameof(Check));}
 static void Require(bool ok,string why){if(!ok)throw new Exception("loading: "+why);}
 public static void Setup(){
  var map=Find.Maps.OrderBy(m=>m.uniqueID).First();var def=DefDatabase<ThingDef>.GetNamed("PassengerShuttle");
  var cell=GenRadial.RadialCellsAround(map.Center,40,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(1).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  shuttle=(Building_PassengerShuttle)ThingMaker.MakeThing(def);shuttle.SetFaction(map.ParentFaction);GenSpawn.Spawn(shuttle,cell,map);
  cargo=ThingMaker.MakeThing(ThingDefOf.Plasteel);cargo.stackCount=31;GenSpawn.Spawn(cargo,CellFinder.RandomClosewalkCellNear(cell,map,4),map);cargo.SetForbidden(false);map.areaManager.Home[cargo.Position]=true;map.fogGrid.Unfog(cargo.Position);
  ready=true;
 }
 static void Open(){
  Current.Game.CurrentMap=shuttle.Map;Find.Selector.ClearSelection();Find.Selector.Select(shuttle);
  var cmd=shuttle.GetGizmos().OfType<Command_LoadToTransporter>().Single();Require(!cmd.Disabled,"load gizmo disabled");cmd.ProcessInput(null);
 }
 static void Count(int value){
  var session=Session;var transferable=session.transferables.FirstOrDefault(t=>t.things.Contains(cargo));Require(transferable!=null,"cargo absent from manifest");
  MP.WatchBegin();try{var reference=new MpTransferableReference(session,transferable);SyncFields.SyncTradeableCount.Watch(reference);reference.CountToTransfer=value;}finally{MP.WatchEnd();}
 }
 static int CountValue=>Session?.transferables.FirstOrDefault(t=>t.things.Contains(cargo))?.CountToTransfer??-1;
 public static void CheckStale(int expected,int previousId){Require(CountValue==expected,"stale session Reset mutated reopened manifest: "+CountValue);Require(Session.SessionId!=previousId,"reopened session reused identity");staleChecked=true;}
 public static void Check(){
  Require(Session==null,"cancel session retained");Require(!shuttle.TransporterComp.LoadingInProgressOrReadyToLaunch,"cancel loading retained");
  Require(cargo.Spawned&&cargo.stackCount==31,"cargo changed during canceled dialog");
  Log.Message("ODYSSEY_LOADING COMPLETE round="+round+" cargo="+cargo.stackCount+" group="+shuttle.TransporterComp.groupID);
  round++;step=0;checking=false;if(round==3){done=true;if(GenCommandLine.CommandLineArgPassed("opstaleonly")&&!GenCommandLine.CommandLineArgPassed("oppilotonly"))OdysseyProbe.Complete();}
 }
 public static bool Update(){
  if(done)return false;if(!GenCommandLine.CommandLineArgPassed("opclient"))return true;
  if(!requested){requested=setup.DoSync(null);deadline=Time.realtimeSinceStartup+90;return true;}
  Require(Time.realtimeSinceStartup<deadline,"timeout step="+step);if(!ready)return true;
  switch(step){
   case 0:Open();step++;break;
   case 1:if(Session!=null){Count(13+round);step++;}break;
   case 2:if(CountValue==13+round){Session.Reset();step++;}break;
   case 3:if(CountValue==0){oldSession=Session;staleChecked=false;Session.Remove();Find.WindowStack.WindowOfType<TransporterLoadingProxy>()?.Close(false);step++;}break;
   case 4:if(Session==null){Open();step++;}break;
   case 5:if(Session!=null){Require(CountValue==0,"reopened stale count");Count(17+round);step++;}break;
   case 6:if(CountValue==17+round){if(GenCommandLine.CommandLineArgPassed("opstaleonly")){OldSession.Reset();staleCheck.DoSync(null,17+round,OldSession.SessionId);step=8;break;}Session.Remove();Find.WindowStack.WindowOfType<TransporterLoadingProxy>()?.Close(false);step++;}break;
   case 8:if(staleChecked){Session.Remove();Find.WindowStack.WindowOfType<TransporterLoadingProxy>()?.Close(false);step=7;}break;
   case 7:if(Session==null&&!checking)checking=check.DoSync(null);break;
  }
  return true;
 }
}
