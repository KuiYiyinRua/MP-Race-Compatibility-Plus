using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Mp=Multiplayer.Client.Multiplayer;
public class RavenResearchMPState:GameComponent {
 public int start=-1;public bool prepared,checkedDone;public Thing hub;
 public RavenResearchMPState(Game g){}
 public override void ExposeData(){Scribe_Values.Look(ref start,"rrStart",-1);Scribe_Values.Look(ref prepared,"rrPrepared");Scribe_Values.Look(ref checkedDone,"rrChecked");Scribe_References.Look(ref hub,"rrHub");}
 public override void GameComponentUpdate(){RavenResearchMPProbe.Update();}
}
[StaticConstructorOnStartup]
public static class RavenResearchMPProbe {
 static bool enabled=GenCommandLine.CommandLineArgPassed("rrpair"),client=GenCommandLine.CommandLineArgPassed("rrclient"),hosted,ready,dispatched,checkedOnce,earlyChecked,done,failed;
 static string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath),peer=client?"client":"host";
 static Type T=AccessTools.TypeByName("RavenRace.Features.CentralHub.GameComponent_RavenCentralHubSystem");
 static RavenResearchMPState S=>Current.Game.GetComponent<RavenResearchMPState>();
 static object Read(object o,string n)=>AccessTools.Field(o.GetType(),n).GetValue(o);
 static void Set(object o,string n,object v)=>AccessTools.Field(o.GetType(),n).SetValue(o,v);
 static object Prop(object o,string n)=>AccessTools.Property(o.GetType(),n).GetValue(o);
 static object Call(object o,string n,params object[] a)=>AccessTools.Method(o.GetType(),n,a.Select(x=>x.GetType()).ToArray()).Invoke(o,a);
 static void Require(bool v,string msg){if(!v)throw new Exception(msg);}
 static RavenResearchMPProbe(){if(!enabled)return;MP.RegisterSyncMethod(typeof(RavenResearchMPProbe),nameof(Setup));MP.RegisterSyncMethod(typeof(RavenResearchMPProbe),nameof(Check));Log.Message("RAVEN_PAIR START peer="+peer+" candidate="+AccessTools.TypeByName("MP_MeowOnlineShop.RavenCompatibilityBootstrap").Assembly.ManifestModule.ModuleVersionId);}
 static Def Research(string name){var t=AccessTools.TypeByName("RavenRace.Features.Research.RavenResearchProjectDef");return (Def)AccessTools.Method(typeof(DefDatabase<>).MakeGenericType(t),"GetNamed").Invoke(null,new object[]{name,true});}
 public static void Setup(){try{
  Require(!S.prepared,"duplicate setup");var map=Find.Maps[0];var def=DefDatabase<ThingDef>.GetNamed("Raven_Building_SunRaiserInjector");
  var cell=GenRadial.RadialCellsAround(map.Center,45,true).First(c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).ExpandedBy(1).Cells.All(x=>x.InBounds(map)&&x.Standable(map)&&x.GetThingList(map).All(t=>t is Plant)));
  var hub=(ThingWithComps)ThingMaker.MakeThing(def);hub.SetFaction(map.ParentFaction);GenSpawn.Spawn(hub,cell,map);S.hub=hub;
  var storage=hub.AllComps.First(c=>c.GetType().Name=="CompRavenCentralResourceStorage");var resources=new Dictionary<ThingDef,int>{{DefDatabase<ThingDef>.GetNamed("Raven_IndustrialCore_Blue"),2097}};
  Require((bool)AccessTools.Method(storage.GetType(),"TryStoreResources").Invoke(storage,new object[]{resources}),"stock seed rejected");
  var system=Current.Game.GetComponent(T);Call(system,"MarkCompleted",Research("RavenResearch_IndustrialLiquidProcessing"));
  var project=Research("RavenResearch_IndustrialCentrifugation");var args=new object[]{project,null};Require((bool)AccessTools.Method(T,"TryEnqueue").Invoke(system,args),"native enqueue failed "+args[1]);
  var entry=((IList)Read(system,"researchQueue"))[0];Set(entry,"workDone",237f);Call(entry,"SetSpentCount",resources.Keys.First(),47);Set(system,"lastResearchTick",Mp.AsyncWorldTime.worldTicks+1000000);
  S.prepared=true;S.start=TickPatch.Timer;Log.Message("RAVEN_PAIR SETUP tick="+S.start+" hub="+hub.ThingID+" future="+Read(system,"lastResearchTick"));
 }catch(Exception e){Fail(e);throw;}}
 public static void Check(){try{
  var system=Current.Game.GetComponent(T);var entry=((IList)Read(system,"researchQueue"))[0];float work=(float)Read(entry,"workDone");int spent=(int)Read(((IList)Read(entry,"spentCosts"))[0],"count");
  var hub=(ThingWithComps)S.hub;var storage=hub.AllComps.First(c=>c.GetType().Name=="CompRavenCentralResourceStorage");long stock=(long)Call(storage,"StoredCount",DefDatabase<ThingDef>.GetNamed("Raven_IndustrialCore_Blue"));
  Require(work>240&&spent>47,"research/cost did not advance");Require(stock+spent==2144,"resource conservation");Require((int)Read(system,"lastResearchTick")<=Mp.AsyncWorldTime.worldTicks+1,"future timestamp retained");Require(!Mp.session.desynced,"desync");
  string receipt="world="+Mp.AsyncWorldTime.worldTicks+";work="+work+";spent="+spent+";stock="+stock+";last="+Read(system,"lastResearchTick")+";hub="+hub.ThingID;
  File.WriteAllText(Path.Combine(root,peer+".receipt"),receipt);Log.Message("RAVEN_PAIR CHECK "+receipt);S.checkedDone=TickPatch.Timer-S.start>=10000;
 }catch(Exception e){Fail(e);throw;}}
 static string Snapshot()=>"world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";"+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState));
 static void Fail(Exception e){failed=true;Log.Error("RAVEN_PAIR FAILED "+e);File.WriteAllText(Path.Combine(root,peer+".failed"),e.ToString());}
 public static void Update(){if(!enabled)return;if(File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}if(failed||done||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
 try{
  foreach(var w in Find.WindowStack.Windows.Where(w=>w.forcePause).ToArray())w.Close(false);
  if(!client&&!hosted&&!MP.IsInMultiplayer){hosted=true;Mp.username="RavenPairHost";Require(HostWindow.HostProgrammatically(new ServerSettings{gameName="RavenResearchPair",direct=true,directAddress="127.0.0.1:30992",lan=false,steam=false,multifaction=false,asyncTime=true,syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false}),"host failed");return;}
  if(!MP.IsInMultiplayer||Mp.Client==null||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
  Require(!Mp.session.desynced,"desync");
  if(!client&&(Mp.LocalServer==null||!Mp.LocalServer.FullyStarted||Mp.session.players.Count<1))return;
  if(!client&&!ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
  if(client&&!ready){if(!TickPatch.Frozen)return;ready=true;File.WriteAllText(Path.Combine(root,"client.ready"),Snapshot());}
  if(Mp.session.players.Count!=2||!File.Exists(Path.Combine(root,"client.ready")))return;
  if(!client&&!ready){Require(Snapshot()==File.ReadAllText(Path.Combine(root,"client.ready")),"frozen baseline mismatch");Log.Message("RAVEN_PAIR BASELINE "+Snapshot());ready=true;Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)TimeSpeed.Superfast);}
  if(client&&!dispatched&&!TickPatch.Frozen){dispatched=true;Setup();}
  if(client&&S.prepared&&!earlyChecked&&TickPatch.Timer-S.start>=500){earlyChecked=true;Check();}
  if(client&&S.prepared&&!checkedOnce&&TickPatch.Timer-S.start>=10000){checkedOnce=true;Check();}
  if(S.checkedDone){done=true;File.WriteAllText(Path.Combine(root,peer+".complete"),"PASS");Log.Message("RAVEN_PAIR COMPLETE ticks="+(TickPatch.Timer-S.start)+" desynced="+Mp.session.desynced);}
 }catch(Exception e){Fail(e);}}
}
