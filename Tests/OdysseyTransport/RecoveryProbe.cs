using System;
using System.IO;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;
public sealed class OdysseyRecoveryState:GameComponent
{
 public bool active,finished,actionsDone;public int cycle,phase,start;
 public OdysseyRecoveryState(Game game){}
 public override void ExposeData(){Scribe_Values.Look(ref active,"orActive");Scribe_Values.Look(ref finished,"orFinished");Scribe_Values.Look(ref actionsDone,"orActionsDone");Scribe_Values.Look(ref cycle,"orCycle");Scribe_Values.Look(ref phase,"orPhase");Scribe_Values.Look(ref start,"orStart");}
}
public static class RecoveryProbe
{
 static OdysseyRecoveryState S=>Current.Game.GetComponent<OdysseyRecoveryState>();
 static string Root=>Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
 static bool Client=>GenCommandLine.CommandLineArgPassed("opclient");
 public static int Cycles=>GenCommandLine.TryGetCommandLineArg("oprecovery",out var value)?int.Parse(value):0;
 public static bool Active=>S.active;
 public static bool Finished=>S.finished;
 static OdysseySnapshotState Snapshot=>Current.Game.GetComponent<OdysseySnapshotState>();
 static ISyncMethod beginCycle,finishActions,endCycle;static bool sent;static int hostPhase=-1,hostStep,advanceStart,loads;
 public static void Install(){beginCycle=MP.RegisterSyncMethod(typeof(RecoveryProbe),nameof(BeginCycle));finishActions=MP.RegisterSyncMethod(typeof(RecoveryProbe),nameof(FinishActions));endCycle=MP.RegisterSyncMethod(typeof(RecoveryProbe),nameof(EndCycle));}
 public static void Begin(){S.active=true;S.phase=0;S.cycle=0;Current.Game.GetComponent<OdysseySnapshotState>().active=false;}
 static string Receipt()=>"shared="+TickPatch.Timer+";"+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.ParentFaction.loadID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState+":"+m.AsyncTime().DesiredTimeSpeed+":"+m.AsyncTime().TimeToTickThrough))+";world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState+";cycle="+S.cycle+";phase="+S.phase+";pending="+SnapshotProbe.Pending+";pawn="+Snapshot.pawn?.thingIDNumber+":"+Snapshot.pawn?.Spawned+";job="+Snapshot.jobId+";loads="+Snapshot.loads+";dropRecorded="+Snapshot.dropRecorded;
 static string BoundaryReceipt(){if(SnapshotProbe.Pending!=1||Snapshot.pawn.Spawned||!Snapshot.shuttle.TransporterComp.innerContainer.Contains(Snapshot.pawn)||Snapshot.shuttle.ShuttleComp.shipParent.curJob?.loadID!=Snapshot.jobId)throw new Exception("recovery pending native job boundary");return Receipt();}
 static void Write(string file,string value){file=Path.Combine(Root,file);if(File.Exists(file))return;File.WriteAllText(file+".tmp",value);File.Move(file+".tmp",file);}
 public static void BeginCycle(int cycle){if(S.phase!=0||cycle!=S.cycle+1)throw new Exception("recovery cycle order");S.cycle=cycle;S.phase=1;S.start=TickPatch.Timer;S.actionsDone=false;sent=false;FlightProbe.Restart();Log.Message("ODYSSEY_RECOVERY START cycle="+cycle+" shared="+S.start);}
 public static void FinishActions(){S.actionsDone=true;sent=false;}
 public static void EndCycle(){
  if(!S.actionsDone||TickPatch.Timer-S.start<300||Mp.session.desynced||!Snapshot.dropRecorded||!Snapshot.pawn.Spawned||SnapshotProbe.Pending!=0||Snapshot.loads<=Snapshot.beforeLoad)throw new Exception("recovery cycle incomplete");
  Log.Message("ODYSSEY_RECOVERY COMPLETE cycle="+S.cycle+" elapsed="+(TickPatch.Timer-S.start)+" "+Receipt());
  Write("recovery.done."+(Client?"client":"host")+S.cycle,Receipt());Snapshot.active=false;S.phase=2;sent=false;
  if(S.cycle==Cycles){S.active=false;S.finished=true;if(SoakProbe.Duration>0)SoakProbe.Begin();else OdysseyProbe.FinishRun();}
 }
 public static void Update(){
  int desired=0;var file=Path.Combine(Root,"recovery.phase");if(File.Exists(file)&&!int.TryParse(File.ReadAllText(file),out desired))return;
  if(S.phase==1){
   if(!S.actionsDone){if(!FlightProbe.Update()&&Client&&!sent)sent=finishActions.DoSync(null);return;}
   if(Client&&!sent&&TickPatch.Timer-S.start>=300)sent=endCycle.DoSync(null);return;
  }
  if(S.phase==2){
   if(!Client){if(!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}if(TickPatch.Frozen)Write("recovery.frozen"+S.cycle,Receipt());}
   if(desired<=S.cycle)return;
   // Advance with only the host connected, then create a genuine server joinpoint.
   S.phase=0;
  }
  if(!Client){
   if(hostPhase!=desired){hostPhase=desired;hostStep=0;advanceStart=TickPatch.Timer;if(desired>0)Mp.Client.Send(new ClientFreezePacket(false));}
   var map=Find.Maps.OrderBy(m=>m.uniqueID).First();
   if(hostStep==0&&(desired==0||TickPatch.Timer-advanceStart>=300)){
    if(Snapshot.active){loads=Snapshot.loads;SnapshotProbe.RequestJoinPoint();hostStep=3;}
    else{Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)TimeSpeed.Paused);hostStep=1;}return;
   }
   if(hostStep==1&&map.AsyncTime().Paused){SnapshotProbe.Prepare();hostStep=2;return;}
   if(hostStep==2&&Snapshot.active){loads=Snapshot.loads;SnapshotProbe.RequestJoinPoint();hostStep=3;return;}
   if(hostStep==3&&Snapshot.loads>loads)hostStep=4;
   if(hostStep==4){if(!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}if(TickPatch.Frozen){Write("recovery.host"+desired,BoundaryReceipt());hostStep=5;}}
   if(File.Exists(Path.Combine(Root,"recovery.resume"+desired))&&TickPatch.serverFrozen)Mp.Client.Send(new ClientFreezePacket(false));
   return;
  }
  if(TickPatch.Frozen)Write("recovery.client"+desired,BoundaryReceipt());
  if(desired==0||!File.Exists(Path.Combine(Root,"recovery.resume"+desired))||TickPatch.Frozen)return;
  var owner=Find.Maps.OrderBy(m=>m.uniqueID).First().ParentFaction;
  if(MP.RealPlayerFaction!=owner){Mp.Client.Send(new ClientSetFactionPacket(Mp.session.playerId,owner.loadID));return;}
  if(!sent){Mp.Client.SendCommand(CommandType.MapTimeSpeed,Find.Maps.OrderBy(m=>m.uniqueID).First().uniqueID,(byte)TimeSpeed.Normal);sent=beginCycle.DoSync(null,desired);}
 }
}
