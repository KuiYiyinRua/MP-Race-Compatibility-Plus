using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
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

public class TradeMismatchState:GameComponent {
 public TradeMismatchState(Game g){}
 public override void GameComponentUpdate(){TradeMismatchProbe.Update();}
}
[StaticConstructorOnStartup]
public static class TradeMismatchProbe {
 static bool enabled=GenCommandLine.CommandLineArgPassed("tradeprobe"),client=GenCommandLine.CommandLineArgPassed("tradeclient"),loaded,ready,opened,clicked,done,failed;
 static string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath),peer=client?"client":"host";
 static Type patch=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_TradeExecutionSnapshot");
 static double began=Time.realtimeSinceStartup; static int executedAt=-1;
 static string selected; static bool drawing;
 static bool regression=GenCommandLine.CommandLineArgPassed("tradefixed"),preparing,prepared;
 static int phase,probeStart=-1,openTick=-1,transferCount,nextCheckpoint=1000;
 static readonly int[] targets={371081,371081,371073,371073,371091,371091,371045,371045,371032,371032};
 static Pawn traderPawn,negotiator; static Thing expected;
 static bool legacyControl=GenCommandLine.CommandLineArgPassed("tradecontrol");
 static Type original; static Harmony controlHarmony; static Thing controlTransferred;
 static TradeMismatchProbe(){if(!enabled)return;var h=new Harmony("local.trade.mismatch.probe");
  h.Patch(AccessTools.Method(typeof(Root_Entry),"Update"),postfix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(Load)));
  h.Patch(AccessTools.Method(typeof(Dialog_Trade),"DoWindowContents"),prefix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(BeforeDraw)),finalizer:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(AfterDraw)));
  h.Patch(AccessTools.Method(typeof(Widgets),"ButtonText",new[]{typeof(Rect),typeof(string),typeof(bool),typeof(bool),typeof(bool),typeof(TextAnchor?)}),prefix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(Button)));
  h.Patch(AccessTools.Method(patch,"SyncExecuteCanonicalTrade"),postfix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(Executed)));
  h.Patch(AccessTools.Method(typeof(Tradeable),"ResolveTrade"),prefix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(Transfer)));
  MP.RegisterSyncMethod(typeof(TradeMismatchProbe),nameof(Prepare));
  if(legacyControl){
   phase=-1;original=Assembly.Load(File.ReadAllBytes(Path.Combine(root,"Original.dll"))).GetType(patch.FullName);
   Require(original!=null&&original.Assembly!=patch.Assembly,"original control assembly must be distinct");
   controlHarmony=new Harmony("local.trade.original.control");
   controlHarmony.Patch(AccessTools.Method(patch,"BuildIntentPayload"),prefix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(OldBuild)));
   controlHarmony.Patch(AccessTools.Method(patch,"NormalizeTradeables"),prefix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(OldNormalize)));
   controlHarmony.Patch(AccessTools.Method(patch,"TryApplyIntent"),prefix:new HarmonyMethod(typeof(TradeMismatchProbe),nameof(OldApply)));
   Log.Message("TRADE_PROBE ORIGINAL_CONTROL assembly="+original.Assembly.FullName+" mvid="+original.Assembly.ManifestModule.ModuleVersionId);
  }
  Log.Message("TRADE_PROBE START peer="+peer+" candidate="+patch.Assembly.ManifestModule.ModuleVersionId+" provenance="+File.ReadAllText(Path.Combine(root,"candidate.sha256")));
 }
 static void Require(bool v,string m){if(!v)throw new Exception(m);}
 static bool OldBuild(object deal,ref string __result){if(phase!=-1)return true;__result=(string)AccessTools.Method(original,"BuildIntentPayload").Invoke(null,new[]{deal});return false;}
 static bool OldNormalize(IList rawTradeables){if(phase!=-1)return true;AccessTools.Method(original,"NormalizeTradeables").Invoke(null,new object[]{rawTradeables});return false;}
 static bool OldApply(IList tradeables,string payload,ref bool __result){if(phase!=-1)return true;__result=(bool)AccessTools.Method(original,"TryApplyIntent").Invoke(null,new object[]{tradeables,payload});return false;}
 static int TargetId=>targets[Math.Max(0,phase)];
 static void HostReplay(){Mp.username="TradeHost";var s=new ServerSettings{gameName="TradeMismatch",direct=true,directAddress="127.0.0.1:30993",lan=false,steam=false,multifaction=false,asyncTime=false,syncConfigs=false,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false};Require((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{s}),"host preinit");HostUtil.HostServer(s,true);}
 static void Load(){if(!enabled||client||loaded||LongEventHandler.AnyEventNowOrWaiting)return;loaded=true;Replay.LoadReplay(new FileInfo(Path.Combine(root,"input.zip")),true,HostReplay);}
 static string Snapshot()=>"world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";"+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState));
 static string Identity(Tradeable t)=>string.Join(",",t.thingsColony.Select(x=>"C:"+x.ThingID+":"+x.LabelNoCount).Concat(t.thingsTrader.Select(x=>"T:"+x.ThingID+":"+x.LabelNoCount)));
 public static void Prepare(){try{
  if(phase>=0&&phase%2==1&&expected!=null&&expected.Spawned){
   Log.Message("TRADE_PROBE RESALE_FIXTURE target="+expected.ThingID+" position="+expected.Position+" homeBefore="+expected.Map.areaManager.Home[expected.Position]+" sellable="+TradeUtility.PlayerSellableNow(expected,traderPawn));
   expected.Map.areaManager.Home[expected.Position]=true;
  }
  var s=Mp.WorldComp.trading.FirstOrDefault();
  if(traderPawn==null){Require(s!=null,"original saved session absent");traderPawn=(Pawn)s.trader;negotiator=s.playerNegotiator;probeStart=TickPatch.Timer;}
  if(s==null)s=MpTradeSession.TryCreate(traderPawn,negotiator,false);
  Require(s!=null,"session recreation failed");
  MpTradeSession.SetTradeSession(s);
  try{
   if(phase<=0){var money=s.deal.AllTradeables.First(t=>t.IsCurrency).thingsColony.First();money.stackCount=50000;Log.Message("TRADE_PROBE FUNDING_FIXTURE thing="+money.ThingID+" count=50000");}
   s.deal.recacheColony=true;s.deal.recacheTrader=true;s.deal.Recache();
   expected=s.deal.AllTradeables.SelectMany(t=>t.thingsColony.Concat(t.thingsTrader)).First(t=>t.thingIDNumber==TargetId);
   Log.Message("TRADE_PROBE PREPARE phase="+phase+" target="+expected.ThingID+":"+expected.LabelNoCount);
  }finally{MpTradeSession.SetTradeSession(null);}
  prepared=true;opened=false;clicked=false;executedAt=-1;openTick=-1;transferCount=0;
 }catch(Exception e){Fail(e);throw;}}
 static void BeforeDraw(){drawing=client&&ready&&!clicked&&(!regression||prepared&&openTick>=0&&TickPatch.Timer-openTick>=20);if(!drawing)return;try{
  var deal=TradeSession.deal;Require(deal!=null,"draw deal");
  var rows=deal.AllTradeables.Where(t=>!t.IsCurrency&&t.TraderWillTrade&&t.thingsTrader.Any(x=>x is MinifiedThing)).ToArray();Require(rows.Length>1,"save has no minified choices");
  foreach(var t in deal.AllTradeables)t.ForceTo(0);
  var row=regression?deal.AllTradeables.First(t=>t.thingsColony.Concat(t.thingsTrader).Any(x=>x.thingIDNumber==TargetId)):rows.FirstOrDefault(t=>t.thingsTrader.Any(x=>x.thingIDNumber==371081))??rows[0];row.ForceTo(regression&&phase>=0&&phase%2==1?-1:1);selected=Identity(row);
  File.WriteAllText(Path.Combine(root,"selected.txt"),selected);Log.Message("TRADE_PROBE SELECT "+selected+" count="+row.CountToTransfer);
 }catch(Exception e){Fail(e);drawing=false;}}
 static void AfterDraw(){drawing=false;}
 static bool Button(string label,ref bool __result){if(!drawing||clicked||label!="AcceptButton".Translate().ToString()||Event.current.type!=EventType.Repaint)return true;clicked=true;__result=true;Log.Message("TRADE_PROBE CLICK_NATIVE_ACCEPT");return false;}
 static void Transfer(Tradeable __instance){if(!enabled||!MP.IsExecutingSyncCommand||__instance.IsCurrency||__instance.CountToTransfer==0)return;var row=Identity(__instance);Log.Message("TRADE_PROBE TRANSFER "+row+" count="+__instance.CountToTransfer);File.AppendAllText(Path.Combine(root,peer+".transfers"),row+" count="+__instance.CountToTransfer+"\n");if(regression){transferCount++;if(phase==-1){controlTransferred=__instance.thingsTrader.First();return;}if(!(phase%2==0?__instance.thingsTrader:__instance.thingsColony).Contains(expected)||__instance.CountToTransfer!=(phase%2==0?1:-1))Fail(new Exception("wrong transfer identity/count before native transfer phase="+phase));}}
 static void Executed(){if(!enabled)return;executedAt=TickPatch.Timer;Log.Message("TRADE_PROBE EXECUTED tick="+executedAt);if(!regression)return;try{
  Require(!failed&&transferCount==1,"wrong/extra transfer row");Require(Mp.WorldComp.trading.Count==0,"native trade rejected");
  if(phase==-1){
   Require(controlTransferred!=null&&controlTransferred!=expected&&traderPawn.Goods.Contains(expected)&&!traderPawn.Goods.Contains(controlTransferred)&&controlTransferred.Spawned,"original control did not reproduce wrong physical purchase");
   var proof="selected="+expected.ThingID+":"+expected.LabelNoCount+" actual="+controlTransferred.ThingID+":"+controlTransferred.LabelNoCount;
   File.WriteAllText(Path.Combine(root,peer+".original-control"),proof);Log.Message("TRADE_PROBE REPRODUCED "+proof);
   controlHarmony.Unpatch(AccessTools.Method(patch,"BuildIntentPayload"),AccessTools.Method(typeof(TradeMismatchProbe),nameof(OldBuild)));
   controlHarmony.Unpatch(AccessTools.Method(patch,"NormalizeTradeables"),AccessTools.Method(typeof(TradeMismatchProbe),nameof(OldNormalize)));
   controlHarmony.Unpatch(AccessTools.Method(patch,"TryApplyIntent"),AccessTools.Method(typeof(TradeMismatchProbe),nameof(OldApply)));
   var more=traderPawn.Goods.OfType<MinifiedThing>().Where(x=>x!=expected).OrderBy(x=>x.thingIDNumber).Take(2).ToArray();Require(more.Length==2,"regression targets");targets[2]=targets[3]=more[0].thingIDNumber;targets[4]=targets[5]=more[1].thingIDNumber;
   phase=0;prepared=false;preparing=false;Log.Message("TRADE_PROBE ORIGINAL_CONTROL_REMOVED");return;
  }
  bool inTrader=traderPawn.Goods.Contains(expected);
  Require(phase%2==0?!inTrader&&expected.Spawned&&expected.Map==negotiator.Map:inTrader,"wrong item ownership after native trade phase="+phase+" expected="+expected.ThingID);
  var receipt="phase="+phase+" target="+expected.ThingID+" inTrader="+inTrader+" spawned="+expected.Spawned+" count="+expected.stackCount+" goods="+string.Join(",",traderPawn.Goods.OrderBy(x=>x.thingIDNumber).Select(x=>x.ThingID+":"+x.stackCount));
  File.AppendAllText(Path.Combine(root,peer+".receipts"),receipt+"\n");Log.Message("TRADE_PROBE ASSERT "+receipt);
  phase++;prepared=false;preparing=false;
 }catch(Exception e){Fail(e);}}
 static void Fail(Exception e){failed=true;Log.Error("TRADE_PROBE FAILED "+e);File.WriteAllText(Path.Combine(root,peer+".failed"),e.ToString());}
 public static void Update(){if(!enabled)return;if(File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}if(done||failed||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;try{
  if(!MP.IsInMultiplayer||Mp.IsReplay||Mp.Client==null||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
  Require(!Mp.session.desynced,"desync");if(!client&&(Mp.LocalServer==null||!Mp.LocalServer.FullyStarted||Mp.session.players.Count<1))return;
  if(!client&&!ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
  if(client&&!ready){if(!TickPatch.Frozen)return;File.WriteAllText(Path.Combine(root,"client.ready"),Snapshot());ready=true;}
  if(Mp.session.players.Count!=2||!File.Exists(Path.Combine(root,"client.ready")))return;
  if(!client&&!ready){Require(Snapshot()==File.ReadAllText(Path.Combine(root,"client.ready")),"frozen baseline mismatch");Log.Message("TRADE_PROBE BASELINE "+Snapshot());ready=true;Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Normal);}
  if(client&&regression&&!prepared&&!preparing&&phase<targets.Length&&!TickPatch.Frozen){preparing=true;Prepare();}
  if(client&&!opened&&!TickPatch.Frozen&&(!regression||prepared)){opened=true;var s=Mp.WorldComp.trading.Single();Current.Game.CurrentMap=s.playerNegotiator.Map;s.OpenWindow();Log.Message("TRADE_PROBE OPEN session="+s.SessionId+" trader="+s.trader.TraderName);if(regression){s.CloseWindow(false);s.OpenWindow(false);openTick=TickPatch.Timer;Log.Message("TRADE_PROBE CLOSE_REOPEN phase="+phase);}}
  if(probeStart>=0&&TickPatch.Timer-probeStart>=nextCheckpoint){Log.Message("TRADE_PROBE CHECKPOINT ticks="+(TickPatch.Timer-probeStart)+" phase="+phase+" players="+Mp.session.players.Count+" desynced="+Mp.session.desynced);nextCheckpoint+=1000;}
  if(regression&&phase==targets.Length&&probeStart>=0&&TickPatch.Timer-probeStart>=10000||!regression&&executedAt>=0&&TickPatch.Timer-executedAt>=90){Require(File.Exists(Path.Combine(root,peer+".transfers")),"accept did not transfer");done=true;File.WriteAllText(Path.Combine(root,peer+".complete"),regression?"PASS":"DIAGNOSTIC");Log.Message("TRADE_PROBE COMPLETE phases="+phase+" ticks="+(TickPatch.Timer-probeStart)+" desynced="+Mp.session.desynced);}
  Require(Time.realtimeSinceStartup-began<1800,"timeout");
 }catch(Exception e){Fail(e);}}
}
