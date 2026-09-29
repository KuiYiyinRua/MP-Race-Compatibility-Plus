using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Mp=Multiplayer.Client.Multiplayer;

public class RavenResearchProbeComponent:GameComponent {
 public RavenResearchProbeComponent(Game g){}
 public override void GameComponentUpdate(){RavenResearchProbe.Update();}
}
[StaticConstructorOnStartup]
public static class RavenResearchProbe {
 static bool enabled=GenCommandLine.CommandLineArgPassed("ravenresearchprobe"),loaded,finished;
 static int start=-1,next; static float baselineWork; static int baselineSpent; static bool regression=GenCommandLine.CommandLineArgPassed("rrfixed"); static string root=GenFilePaths.SaveDataFolderPath;
 static Type T=AccessTools.TypeByName("RavenRace.Features.CentralHub.GameComponent_RavenCentralHubSystem");
 static object Read(object o,string n)=>AccessTools.Field(o.GetType(),n).GetValue(o);
 static object Call(object o,string n,params object[] a)=>AccessTools.Method(o.GetType(),n).Invoke(o,a);
 static object Prop(object o,string n)=>AccessTools.Property(o.GetType(),n).GetValue(o);
 static RavenResearchProbe(){if(!enabled)return; new Harmony("local.raven.research.probe").Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(RavenResearchProbe),nameof(Load))); if(regression) new Harmony("local.raven.research.reverse").CreateReversePatcher(AccessTools.Method(T,"GameComponentTick"),new HarmonyMethod(typeof(RavenResearchProbe),nameof(OriginalTick))).Patch(); if(regression)new Harmony("local.raven.research.capture").Patch(AccessTools.Method(T,"GameComponentTick"),prefix:new HarmonyMethod(typeof(RavenResearchProbe),nameof(BeforeFirstTick)){priority=Priority.First}); new Harmony("local.raven.research.observation").Patch(AccessTools.Method(T,"GameComponentTick"),prefix:new HarmonyMethod(typeof(RavenResearchProbe),nameof(ObserveTick)){priority=Priority.First}); Log.Message("RAVEN_PROBE START candidateMvid="+AccessTools.TypeByName("MP_MeowOnlineShop.RavenCompatibilityBootstrap").Assembly.ManifestModule.ModuleVersionId);}
 static bool assertRecovery=GenCommandLine.CommandLineArgPassed("rrassert"); static int cycle,focusStage; static float savedWork; static bool captured, resumed; static bool solo=GenCommandLine.CommandLineArgPassed("rrmp"); static int probeCalls;
 static void ObserveTick(GameComponent __instance){if(probeCalls++<8)Log.Message("RAVEN_PROBE TICK raw="+Find.TickManager.TicksGame+" world="+Mp.AsyncWorldTime?.worldTicks+" last="+Read(__instance,"lastResearchTick")+" replay="+Mp.IsReplay+" stack="+new System.Diagnostics.StackTrace(1,false));}
 static void BeforeFirstTick(GameComponent __instance){if(!regression||captured)return;captured=true;var sys=__instance; int tick=Find.TickManager.TicksGame;var e=((IList)Read(sys,"researchQueue"))[0]; baselineWork=(float)Read(e,"workDone");var costs=(IList)Read(e,"spentCosts"); baselineSpent=(int)Read(costs[0],"count");int last=(int)Read(sys,"lastResearchTick");Require(last>tick,"fixture must retain actual future timestamp before first simulation tick");OriginalTick(sys);Require((float)Read(e,"workDone")==baselineWork&&(int)Read(sys,"lastResearchTick")==last,"old native tick unexpectedly advanced");Log.Message("RAVEN_PROBE OLD_NATIVE_STALLED now="+tick+" last="+last+" work="+baselineWork);}
 static void OriginalTick(GameComponent instance){throw new NotImplementedException();}
 static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
 static void HostReplay(){var settings=new ServerSettings{gameName="RavenSoloDiagnostic",direct=true,directAddress="127.0.0.1:30991",lan=false,steam=false,multifaction=false,asyncTime=true,syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};Mp.username="RavenProbe";Require((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}),"server preinitialization failed");HostUtil.HostServer(settings,true);}
 static void Load(){if(loaded||LongEventHandler.AnyEventNowOrWaiting)return;loaded=true;if(solo){Replay.LoadReplay(new FileInfo(cycle==0?Path.Combine(root,"Input.zip"):cycle==3?Path.Combine(root,"Latest.zip"):Path.Combine(root,"MpReplays/Recovered"+(cycle-1)+".zip")),true,HostReplay);}else GameDataSaveLoader.LoadGame("Input");}
 public static void Update(){
  if(!enabled||finished||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
  try{
   if(solo&&(Mp.IsReplay||!MP.IsInMultiplayer||TickPatch.Simulating||Mp.Client==null||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||Mp.LocalServer==null||!Mp.LocalServer.FullyStarted||Mp.session.players.Count<1))return;
   if(solo&&!resumed){resumed=true;Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)TimeSpeed.Superfast);Log.Message("RAVEN_PROBE SOLO_HOST players="+Mp.session.players.Count);}
   foreach(var w in Find.WindowStack.Windows.Where(w=>w.forcePause).ToArray()) w.Close(false);
   int tick=solo?Mp.AsyncWorldTime.worldTicks:Find.TickManager.TicksGame;
   if(start<0){start=tick;next=tick;Log.Message("RAVEN_PROBE LOADED maps="+Find.Maps.Count);if(assertRecovery){var sys=Current.Game.GetComponent(T);var first=((IList)Read(sys,"researchQueue"))[0];baselineWork=(float)Read(first,"workDone");baselineSpent=(int)Read(((IList)Read(first,"spentCosts"))[0],"count");Require(cycle==0||cycle==3||baselineWork>=savedWork,"progress lost during reload");Log.Message("RAVEN_PROBE CYCLE_BEGIN cycle="+cycle+" work="+baselineWork+" spent="+baselineSpent);}}
   if(assertRecovery){Require(!Mp.session.desynced,"host desynced");int focus=(tick-start)/150;if(focus>focusStage){focusStage=focus;Current.Game.CurrentMap=Find.Maps[focus%Find.Maps.Count];Log.Message("RAVEN_PROBE FOCUS map="+Find.CurrentMap.uniqueID);}}
   if(!solo)Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
   if(tick>=next){next=tick+60;var s=Current.Game.GetComponent(T);var hub=(ThingWithComps)Prop(s,"ActiveHub");
    var power=hub?.GetComp<CompPowerTrader>(); var storage=hub?.AllComps.FirstOrDefault(c=>c.GetType().Name=="CompRavenCentralResourceStorage");
    Log.Message("RAVEN_PROBE STATE tick="+tick+" last="+Read(s,"lastResearchTick")+" paused="+Read(s,"researchPaused")+" hub="+hub?.ThingID+" map="+hub?.Map?.uniqueID+" power="+power?.PowerOn+" net="+(power?.PowerNet!=null)+" wants="+(hub!=null&&FlickUtility.WantsToBeOn(hub))+" broken="+(hub!=null&&BreakdownableUtility.IsBrokenDown(hub))+" canHub="+AccessTools.Method(T,"HubCanResearch").Invoke(null,new object[]{hub}));
    foreach(var e in (IList)Read(s,"researchQueue")){var p=(Def)Read(e,"project");Log.Message("RAVEN_PROBE ENTRY "+p.defName+" work="+Read(e,"workDone")+" prereq="+Call(s,"PrerequisitesCompleted",p)+" canAdvance="+AccessTools.Method(T,"CanAdvance").Invoke(null,new object[]{e,storage,(float)Prop(s,"ResearchSpeedPerDay")/1000f})+" active="+Call(s,"IsActive",p));}
   }
   if(tick-start>=600){if(assertRecovery){var sys=Current.Game.GetComponent(T);var e=((IList)Read(sys,"researchQueue"))[0];float work=(float)Read(e,"workDone");int spent=(int)Read(((IList)Read(e,"spentCosts"))[0],"count");Require(work>=baselineWork+4,"research did not advance at native rate");Require(spent>baselineSpent,"native cost not consumed");Require((int)Read(sys,"lastResearchTick")<=Mp.AsyncWorldTime.worldTicks+1,"research clock ahead of world");savedWork=work;Autosaving.SaveGameToFile_Overwrite("Recovered"+cycle,false);Require(File.Exists(Path.Combine(root,"MpReplays/Recovered"+cycle+".zip")),"recovery save absent");Log.Message("RAVEN_PROBE CYCLE_PASS cycle="+cycle+" work="+work+" spent="+spent+" ticks="+(tick-start)+" desynced="+Mp.session.desynced);if(++cycle<4){Mp.StopMultiplayer();GenScene.GoToMainMenu();loaded=false;resumed=false;start=-1;focusStage=0;probeCalls=0;return;}}if(regression&&!assertRecovery){var sys=Current.Game.GetComponent(T);var e=((IList)Read(sys,"researchQueue"))[0];Require((float)Read(e,"workDone")>baselineWork,"research did not recover");Require((int)Read(((IList)Read(e,"spentCosts"))[0],"count")>baselineSpent,"native cost not consumed");Require((int)Read(sys,"lastResearchTick")<=tick,"future timestamp not recovered");GameDataSaveLoader.SaveGame("Recovered");Log.Message("RAVEN_PROBE RECOVERED_AND_SAVED work="+Read(e,"workDone"));}finished=true;File.WriteAllText(Path.Combine(root,"complete"),(regression||assertRecovery)?"PASS":"diagnostic");Log.Message("RAVEN_PROBE COMPLETE");Application.Quit();}
  }catch(Exception e){finished=true;Log.Error("RAVEN_PROBE FAILED "+e);File.WriteAllText(Path.Combine(root,"failed"),e.ToString());Application.Quit();}
 }
}
