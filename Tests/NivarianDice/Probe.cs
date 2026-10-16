using System;
using System.Collections;
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
using Mp=Multiplayer.Client.Multiplayer;

public sealed class DiceFixture:GameComponent
{
 public int cycle=-1,step,until,cargoExpected;
 public DiceFixture(Game g){}
 public override void ExposeData(){Scribe_Values.Look(ref cycle,"diceCycle",-1);Scribe_Values.Look(ref step,"diceStep");Scribe_Values.Look(ref until,"diceUntil");Scribe_Values.Look(ref cargoExpected,"cargoExpected");}
 public override void GameComponentUpdate()=>DiceProbe.Update();
}
[StaticConstructorOnStartup]
public static class DiceProbe
{
 static bool enabled=GenCommandLine.CommandLineArgPassed("nivarianupdateprobe"),client=GenCommandLine.CommandLineArgPassed("nuclient"),async=GenCommandLine.CommandLineArgPassed("nuasync");
 static int maps=int.Parse(Arg("numaps","1")),cycle=int.Parse(Arg("nucycle","0")),soak=int.Parse(Arg("nusoak","10000"));
 static string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath),peer=client?"client"+cycle:"host";
 static bool hosted,failed,done,sent,waiting,joinpoint,pausing,began;
 static int ready=-1,dispatched=-1,due,pauseAt,checkpoint;
 static float next,started=Time.realtimeSinceStartup;
 static ISyncMethod prepare,verify;
 static bool baselineLoading;
 static string Hash(string p){using(var sha=System.Security.Cryptography.SHA256.Create())using(var stream=File.OpenRead(p))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
 static void Inputs(){var asm=AccessTools.TypeByName("Meow.TaleNivarianCompatibility.Bootstrap").Assembly;Check(Hash(Path.Combine(Application.dataPath,"..","Mods","Candidate","1.6","Assemblies","Meow.TaleNivarianCompatibility.dll"))==Arg("nucandidate",""),"candidate hash mismatch");Check(Hash(Path.Combine(GenFilePaths.SaveDataFolderPath,"Config","ModsConfig.xml"))==Arg("numodhash",""),"ordered mod config hash mismatch");Log.Message("NU_INPUT run="+root+" version="+asm.GetName().Version+" sha="+Arg("nucandidate","")+" mvid="+asm.ManifestModule.ModuleVersionId+" mods="+Arg("numodhash","")+" input="+Arg("nuinput",""));}
 static void OpenDice(){var panel=Activator.CreateInstance(AccessTools.TypeByName("Nivarian_Race.Code.UI.UplinkNiraMetricsTabPanel"));for(int i=0;i<3;i++)Call(panel,"OnTabSelected");Log.Message("NU_PANEL reopened=3 interface="+MP.InInterface+" state="+State());}
 static Map Home=>Find.AnyPlayerHomeMap;
 static int CargoCount=>Home.listerThings.AllThings.Count(t=>t.def.defName=="RKU_DrillingCargoPod");
 static void Cargo(){var bulletDef=DefDatabase<ThingDef>.GetNamed("RKU_DrillingCargoPodBullet");S.cargoExpected=CargoCount+3;for(int i=0;i<3;i++){var start=Home.Center+new IntVec3(-18,0,i*4);var target=Home.Center+new IntVec3(18,0,i*4);var bullet=(Projectile)ThingMaker.MakeThing(bulletDef);Set(bullet,"rKU_DrillingCargoPod",ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RKU_DrillingCargoPod")));GenSpawn.Spawn(bullet,start,Home);bullet.Launch(null,new LocalTargetInfo(target),new LocalTargetInfo(target),ProjectileHitFlags.None);Log.Message("NU_CARGO_LAUNCH id="+bullet.thingIDNumber+" map="+Home.uniqueID+" target="+target);}}
 static Type metricsType;
 static DiceFixture S=>Current.Game.GetComponent<DiceFixture>();
 static object Metrics=>Current.Game.components.Single(c=>c.GetType()==metricsType);
 static IList Dice=>(IList)Get(Metrics,"holoDice");
 static string Arg(string n,string d)=>GenCommandLine.TryGetCommandLineArg(n,out string v)?v:d;
 static object Get(object o,string f)=>AccessTools.Field(o.GetType(),f).GetValue(o);
 static void Set(object o,string f,object v)=>AccessTools.Field(o.GetType(),f).SetValue(o,v);
 static object Call(object o,string m,params object[] args)=>AccessTools.Method(o.GetType(),m).Invoke(o,args);
 static string At(string n)=>Path.Combine(root,n);
 static void Check(bool b,string n){if(!b)throw new Exception(n);}
 static string State()=>S.cycle+":"+S.step+";day="+Get(Metrics,"lastHoloDiceDay")+";rank="+Get(Metrics,"currentRankLevel")+";dice="+string.Join(";",Dice.Cast<object>().Select(d=>Get(d,"rolled")+":"+Get(d,"consumed")+":"+string.Join(",",((IList)Get(d,"options")).Cast<object>().Select(o=>Get(o,"incidentDefName")))));
 static string Snapshot()=>"world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";maps="+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState))+";state="+State();
 static void Write(string n,string value){File.WriteAllText(At(peer+"."+n),value);Log.Message("NU_MARK "+peer+"."+n+" "+value);}
 static DiceProbe(){if(!enabled)return;Inputs();Log.Message("DICE_INPUT_MODULE "+typeof(UnityEngine.Input).Assembly.FullName);AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve+=(sender,args)=>{var name=new System.Reflection.AssemblyName(args.Name).Name;var path=Path.Combine(Application.dataPath,"Managed",name+".dll");return File.Exists(path)?System.Reflection.Assembly.ReflectionOnlyLoadFrom(path):null;};System.Reflection.Assembly.ReflectionOnlyLoadFrom(Path.Combine(Application.dataPath,"Managed","UnityEngine.InputLegacyModule.dll"));metricsType=AccessTools.TypeByName("Nivarian.GameComp_NivarianNiraMetrics");prepare=MP.RegisterSyncMethod(typeof(DiceProbe),nameof(Prepare));verify=MP.RegisterSyncMethod(typeof(DiceProbe),nameof(Verify));var h=new Harmony("meow.dice.probe");h.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(DiceProbe),nameof(Update)));h.Patch(AccessTools.Method(metricsType,"RollHoloDie"),prefix:new HarmonyMethod(typeof(DiceProbe),nameof(BeforeRoll)){priority=Priority.Last},finalizer:new HarmonyMethod(typeof(DiceProbe),nameof(AfterRoll)));h.Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(DiceProbe),nameof(Update)));h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(DiceProbe),nameof(Seed)),finalizer:new HarmonyMethod(typeof(DiceProbe),nameof(Unseed)));Log.Message("NU_PROBE START peer="+peer+" label="+root+" candidateMvid="+AccessTools.TypeByName("Meow.TaleNivarianCompatibility.Bootstrap").Module.ModuleVersionId);}
 static void Seed(ref string seedString){seedString="NivarianDice261013";Rand.PushState(261013);}
 static void Unseed()=>Rand.PopState();
 static void BeforeRoll(object __instance,int __0){if(!MP.InInterface&&GenCommandLine.CommandLineArgPassed("dicecontextassert")){Check(Find.CurrentMap==Find.AnyPlayerHomeMap,"incident query must use native home target");Check(Find.TickManager.TicksGame==Mp.AsyncWorldTime.worldTicks,"dice action must use world clock");}Log.Message("DICE_BEFORE peer="+peer+" interface="+MP.InInterface+" idx="+__0+" home="+Find.AnyPlayerHomeMap?.uniqueID+" current="+Find.CurrentMap?.uniqueID+" faction="+Faction.OfPlayer.loadID+" "+State());}
 static void AfterRoll(Exception __exception){Log.Message("DICE_AFTER peer="+peer+" interface="+MP.InInterface+" error="+__exception+" "+State());}
 static void Prepare(int c){S.cycle=c;S.step=0;S.until=Mp.AsyncWorldTime.worldTicks+soak;began=true;sent=false;Set(Metrics,"currentRankLevel",5);Set(Metrics,"lastHoloDiceDay",GenDate.DaysPassedSinceSettle);Set(Find.Storyteller,"def",DefDatabase<StorytellerDef>.GetNamed("Nivarian_Nira"));Set(Metrics,"lastHoloDiceDay",-1);Set(Metrics,"lastAssessmentTick",Find.TickManager.TicksGame);Set(Metrics,"nextEvaluationTick",Find.TickManager.TicksGame+300000);Dice.Clear();Call(Metrics,"RefreshHoloDice");Check(Dice.Count==5,"native daily refresh must generate rank-five dice");if(GenCommandLine.CommandLineArgPassed("nucargo"))Cargo();Log.Message("DICE_PREPARE "+State());}
 static void Verify(int c,int step){Check(S.cycle==c&&S.step==step,"stage mismatch");var d=Dice[step<3?step:step-3];Check((bool)Get(d,step<3?"rolled":"consumed"),"native action absent");Check(((IList)Get(d,"options")).Count==5,"roll must produce five options");Log.Message("NU_ASSERT cycle="+c+" step="+step+" "+State());S.step++;}
 static void Speed(TimeSpeed s){Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)s);if(async)foreach(var m in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,m.uniqueID,(byte)(m==Find.Maps.Last()?TimeSpeed.Paused:s));}
 public static void Update(){if(!enabled)return;if(File.Exists(At("quit"))||File.Exists(At(peer+".quit"))){Application.Quit();return;}if(done||failed||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+.1f;try{
 Check(Time.realtimeSinceStartup-started<3600,"overall timeout");if(!client&&!baselineLoading&&GenCommandLine.CommandLineArgPassed("nurws")&&Current.ProgramState==ProgramState.Entry&&!LongEventHandler.AnyEventNowOrWaiting){baselineLoading=true;LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("NivarianInput"),"Loading baseline",false,null);return;}if(Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
 if(!client&&!hosted&&!MP.IsInMultiplayer){hosted=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;if(maps==2&&Find.Maps.Count==1){var first=Find.CurrentMap;var tiles=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(first.Tile,tiles);var tile=tiles.OrderBy(t=>t.tileId).First(t=>!Find.WorldObjects.AnyMapParentAt(t)&&Find.WorldPathGrid.Passable(t));var parent=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=tile;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);MapGenerator.GenerateMap(new IntVec3(80,1,80),parent,first.generatorDef);Current.Game.CurrentMap=first;}Check(HostWindow.HostProgrammatically(new ServerSettings{gameName="Dice",direct=true,directAddress="127.0.0.1:"+Arg("nuport","31173"),lan=false,steam=false,multifaction=false,asyncTime=async,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=async}),"host rejected");return;}
 if(!MP.IsInMultiplayer||Mp.Client?.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;Check(!Mp.session.desynced,"desync");int requested=client?cycle:(File.Exists(At("cycle.txt"))?int.Parse(File.ReadAllText(At("cycle.txt"))):0);
 if(ready!=requested){if(!TickPatch.Frozen){if(!client)Mp.Client.Send(new ClientFreezePacket(true));return;}if(!File.Exists(At(peer+".frozen"+requested)))Write("frozen"+requested,Snapshot());if(Mp.session.players.Count!=2||!File.Exists(At("host.frozen"+requested))||!File.Exists(At("client"+requested+".frozen"+requested)))return;Check(File.ReadAllText(At("host.frozen"+requested))==File.ReadAllText(At("client"+requested+".frozen"+requested)),"BASELINE_LOAD_DRIFT");ready=requested;began=sent=waiting=joinpoint=pausing=false;dispatched=-1;if(!client){Mp.Client.Send(new ClientFreezePacket(false));Speed(TimeSpeed.Superfast);}return;}
 if(TickPatch.Frozen&&joinpoint&&!client&&!Mp.LocalServer.worldData.CreatingJoinPoint){if(!File.Exists(At("host.complete"+requested)))Write("complete"+requested,Snapshot());return;}
 if(TickPatch.Frozen&&client&&File.Exists(At("host.complete"+requested))){Check(Snapshot()==File.ReadAllText(At("host.complete"+requested)),"completion mismatch");Write("complete",Snapshot());done=true;return;}
 if(TickPatch.Frozen)return;Check(Mp.session.players.Count==2,"unexpected disconnect");if(!began){if(client&&!sent)sent=prepare.DoSync(null,cycle);return;}
 if(S.step<5){if(!client)return;if(waiting){if(S.step==dispatched)return;waiting=sent=false;}if(!sent){Current.Game.CurrentMap=Find.Maps.OrderBy(m=>m.uniqueID).Last();Log.Message("NU_DISPATCH step="+S.step+" interface="+MP.InInterface);if(S.step<3){OpenDice();Call(Metrics,"RollHoloDie",S.step);}else if(S.step==3)Call(Metrics,"SkipHoloDie",0);else Call(Metrics,"TriggerHoloDieOption",1,0);dispatched=S.step;due=TickPatch.Timer+100;sent=true;return;}if(!(bool)Get(Dice[S.step<3?S.step:S.step-3],S.step<3?"rolled":"consumed")){Check(TickPatch.Timer<due+2000,"native dice command not executed");return;}if(TickPatch.Timer<due)return;verify.DoSync(null,cycle,S.step);waiting=true;return;}
 if(client&&maps==2)Current.Game.CurrentMap=Find.Maps[(TickPatch.Timer/150)%Find.Maps.Count];if(Mp.AsyncWorldTime.worldTicks>=checkpoint){checkpoint=Mp.AsyncWorldTime.worldTicks+10000;Log.Message("NU_CHECKPOINT world="+Mp.AsyncWorldTime.worldTicks+" until="+S.until+" players=2 desynced=False");}if(Mp.AsyncWorldTime.worldTicks<S.until)return;if(GenCommandLine.CommandLineArgPassed("nucargo")){Check(CargoCount==S.cargoExpected,"native cargo delivery count mismatch");Check(!Home.listerThings.AllThings.Any(t=>t.def.defName=="RKU_DrillingCargoPodBullet"),"cargo projectile still alive");}if(client){if(!File.Exists(At(peer+".actionscomplete")))Write("actionscomplete",State());return;}if(!File.Exists(At("client"+requested+".actionscomplete")))return;if(!pausing){Speed(TimeSpeed.Paused);pausing=true;pauseAt=TickPatch.Timer+45;return;}if(TickPatch.Timer<pauseAt)return;if(!joinpoint){Check(Mp.LocalServer.worldData.TryStartJoinPointCreation(true),"joinpoint rejected");joinpoint=true;return;}if(!Mp.LocalServer.worldData.CreatingJoinPoint)Mp.Client.Send(new ClientFreezePacket(true));
 }catch(Exception e){failed=true;Write("failed",e.ToString());Log.Error("NU_FAILED "+e);}}
}
