using System;
using System.IO;
using System.Linq;
using System.Collections;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;
public static class ColdProbe
{
 public static void Install(Harmony harmony){harmony.Patch(AccessTools.Method(typeof(AsyncTimeComp),nameof(AsyncTimeComp.ExecuteCmd)),postfix:new HarmonyMethod(typeof(ColdProbe),nameof(AfterMapCommand)));harmony.Patch(AccessTools.Method(typeof(ClientSyncOpinion),nameof(ClientSyncOpinion.CheckForDesync)),postfix:new HarmonyMethod(typeof(ColdProbe),nameof(AfterOpinion)));}
 static string Opinion(ClientSyncOpinion o){var parts=new System.Collections.Generic.List<string>();foreach(object item in (IEnumerable)AccessTools.Field(o.GetType(),"mapStates").GetValue(o)){parts.Add(AccessTools.Field(item.GetType(),"mapId").GetValue(item)+":"+((ICollection)AccessTools.Field(item.GetType(),"randomStates").GetValue(item)).Count);}return o.isLocalClientsOpinion+"@"+o.startTick+" maps="+string.Join(";",parts)+" world="+o.worldRandomStates.Count+" cmds="+o.commandRandomStates.Count;}
 static void AfterOpinion(ClientSyncOpinion __instance,ClientSyncOpinion other,string __result){if(__result!=null)Log.Message("ODYSSEY_TRACE OPINION "+__result+" first="+Opinion(__instance)+" other="+Opinion(other)+" live="+string.Join(";",Find.Maps.Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().DesiredTimeSpeed+":"+m.AsyncTime().Paused)));}
 static void AfterMapCommand(AsyncTimeComp __instance,ScheduledCommand cmd){if(Enabled&&cmd.type==CommandType.MapTimeSpeed)Log.Message("ODYSSEY_TRACE MAP_COMMAND shared="+TickPatch.Timer+" map="+__instance.map.uniqueID+" speed="+__instance.DesiredTimeSpeed+" paused="+__instance.Paused+" faction="+cmd.factionId);}
 public static bool Enabled=>GenCommandLine.CommandLineArgPassed("opcold");
 public static bool Rejoining=>GenCommandLine.CommandLineArgPassed("oprejoin");
 static string Root=>Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
 static OdysseySnapshotState S=>Current.Game.GetComponent<OdysseySnapshotState>();
 static int phase=-1,step,start,loads;
 static void SaveReceipt(string file){if(File.Exists(file))return;File.WriteAllText(file+".tmp",Receipt());File.Move(file+".tmp",file);}
 static string Receipt(){
  var t=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_TransportShipUnloadMp");var pending=(ICollection)AccessTools.Property(t,"PendingUnloadOperations").GetValue(null);
  if(pending.Count!=1||S.pawn.Spawned||!S.shuttle.TransporterComp.innerContainer.Contains(S.pawn))throw new Exception("cold queue boundary: pending="+pending.Count+" spawned="+S.pawn.Spawned+" container="+S.shuttle.TransporterComp.innerContainer.Contains(S.pawn)+" mapTick="+S.shuttle.Map.AsyncTime().mapTicks+" speed="+S.shuttle.Map.AsyncTime().DesiredTimeSpeed+" loads="+S.loads);
  return "tick="+TickPatch.Timer+";"+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState+":"+m.AsyncTime().DesiredTimeSpeed+":"+m.AsyncTime().Paused+":"+m.AsyncTime().TimeToTickThrough))+";world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";pawn="+S.pawn.thingIDNumber+";pending="+pending.Count+";loads="+S.loads;
 }
 public static bool Update(){
  if(!Enabled)return false;
  int desired=0;if(File.Exists(Path.Combine(Root,"cold.phase"))&&!int.TryParse(File.ReadAllText(Path.Combine(Root,"cold.phase")),out desired))return true;
  if(desired==4){if(!GenCommandLine.CommandLineArgPassed("opclient")&&TickPatch.serverFrozen)Mp.Client.Send(new ClientFreezePacket(false));return false;}
  bool client=GenCommandLine.CommandLineArgPassed("opclient");
  if(client){if(TickPatch.Frozen){string file=Path.Combine(Root,"cold.client"+desired);SaveReceipt(file);}return true;}
  if(phase!=desired){phase=desired;step=desired==0?2:0;start=TickPatch.Timer;if(desired>0)Mp.Client.Send(new ClientFreezePacket(false));}
  if(step==0&&TickPatch.Timer-start>=300){loads=S.loads;SnapshotProbe.RequestJoinPoint();step=1;return true;}
  if(step==1&&S.loads>loads)step=2;
  if(step==2){if(!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return true;}if(TickPatch.Frozen){SaveReceipt(Path.Combine(Root,"cold.host"+phase));step=3;}}
  return true;
 }
}
