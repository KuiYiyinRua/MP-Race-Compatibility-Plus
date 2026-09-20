using System;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;
public sealed class OdysseySoakState:GameComponent
{
 public bool active,flight;public int start,cycles;
 public OdysseySoakState(Game game){}
 public override void ExposeData(){Scribe_Values.Look(ref active,"soakActive");Scribe_Values.Look(ref flight,"soakFlight");Scribe_Values.Look(ref start,"soakStart");Scribe_Values.Look(ref cycles,"soakCycles");}
}
public static class SoakProbe
{
 static OdysseySoakState S=>Current.Game.GetComponent<OdysseySoakState>();
 public static int Duration=>GenCommandLine.TryGetCommandLineArg("opsoak",out var value)?int.Parse(value):0;
 public static bool Active=>S.active;
 static ISyncMethod startFlight,finishFlight,finish;static bool sent,speeds;
 public static void Install(){startFlight=MP.RegisterSyncMethod(typeof(SoakProbe),nameof(StartFlight));finishFlight=MP.RegisterSyncMethod(typeof(SoakProbe),nameof(FinishFlight));finish=MP.RegisterSyncMethod(typeof(SoakProbe),nameof(Finish));}
 public static void Begin(){S.active=true;S.start=TickPatch.Timer;Current.Game.GetComponent<OdysseySnapshotState>().active=false;Log.Message("ODYSSEY_SOAK BEGIN duration="+Duration+" sharedTick="+S.start);}
 public static void StartFlight(){if(S.flight)throw new Exception("duplicate soak flight");S.flight=true;if(Find.Maps.Count==1)AcceptanceProbe.Restart();else FlightProbe.Restart();sent=false;}
 public static void FinishFlight(){S.flight=false;S.cycles++;sent=false;Log.Message("ODYSSEY_SOAK CYCLE ordinal="+S.cycles+" sharedTick="+TickPatch.Timer);}
 public static void Finish(){
  if(TickPatch.Timer-S.start<Duration||S.flight||S.cycles!=(Duration-1)/12000||Mp.session.desynced)throw new Exception("soak end invariant");
  Log.Message("ODYSSEY_SOAK COMPLETE elapsed="+(TickPatch.Timer-S.start)+" cycles="+S.cycles+" maps="+string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState))+" world="+Mp.AsyncWorldTime.worldTicks+":"+Mp.AsyncWorldTime.randState);
  S.active=false;OdysseyProbe.FinishRun();
 }
 public static void Update(){
  bool client=GenCommandLine.CommandLineArgPassed("opclient");
  if(!client&&!speeds){speeds=true;foreach(var map in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)(map==Find.Maps.OrderBy(m=>m.uniqueID).First()?TimeSpeed.Fast:TimeSpeed.Normal));}
  if(S.flight){if(!(Find.Maps.Count==1?AcceptanceProbe.Update():FlightProbe.Update())&&client&&!sent)sent=finishFlight.DoSync(null);return;}
  if(!client||sent)return;
  if(TickPatch.Timer-S.start>=Duration){sent=finish.DoSync(null);return;}
  if(S.cycles<(Duration-1)/12000&&TickPatch.Timer-S.start>=(S.cycles+1)*12000)sent=startFlight.DoSync(null);
 }
}
