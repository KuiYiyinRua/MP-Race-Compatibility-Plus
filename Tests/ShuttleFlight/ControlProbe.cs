using System;
using Multiplayer.API;
using Multiplayer.Client;
using Verse;
public static class ControlProbe
{
 public static int Maps=>GenCommandLine.TryGetCommandLineArg("opcontrolmaps",out var value)?int.Parse(value):0;
 static ISyncMethod finish;static bool sent;
 public static void Install(){finish=MP.RegisterSyncMethod(typeof(ControlProbe),nameof(Finish));}
 public static void Finish(){if(Find.Maps.Count!=Maps||Multiplayer.Client.Multiplayer.session.desynced)throw new Exception("control end invariant");Log.Message("ODYSSEY_CONTROL COMPLETE maps="+Maps+" async=False");OdysseyProbe.Complete();}
 public static void Update(){if(UIRefuelProbe.Update())return;if(Maps>1&&FlightProbe.Update())return;if(LoadingProbe.Update())return;if(AcceptanceProbe.Update())return;if(GenCommandLine.CommandLineArgPassed("opclient")&&!sent)sent=finish.DoSync(null);}
}
