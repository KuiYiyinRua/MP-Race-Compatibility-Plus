using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class HostConfigNetworkState : GameComponent
{
    public HostConfigNetworkState(Game g) { }
    public override void GameComponentUpdate() => HostConfigNetworkProbe.Update();
}
[StaticConstructorOnStartup]
public static class HostConfigNetworkProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("hostconfignetworkprobe");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("cfgclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static bool hosted, ready, sent, failed, done;
    static int finish = -1, checkpoint;
    static ISyncMethod prepare;
    static Type module;
    static HostConfigNetworkProbe()
    {
        if (!enabled) return;
        prepare = MP.RegisterSyncMethod(typeof(HostConfigNetworkProbe), nameof(Prepare));
        module = AccessTools.TypeByName("Meow.TurretCombatSleep.TurretCombatSleep");
        var h = new Harmony("local.meow.hostconfignetwork");
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(HostConfigNetworkProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(RimWorld.Planet.WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(HostConfigNetworkProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(HostConfigNetworkProbe), nameof(Unseed)));
        Log.Message("CFG_NETWORK RUN label=" + Path.GetFileName(root) + " hashes=" + File.ReadAllText(Path.Combine(root, "candidate.sha256")));
        Log.Message("CFG_NETWORK START peer=" + Peer + " turretMvid=" + module.Module.ModuleVersionId + " coreMvid=" + AccessTools.TypeByName("MP_MeowOnlineShop.Patch_MpConfigHotSync").Module.ModuleVersionId);
    }
    static void Seed() => Rand.PushState(260929);
    static void Unseed() => Rand.PopState();
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static bool Active => (bool)AccessTools.Property(module, "Active").GetValue(null);
    static string Snapshot() => Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" +
        string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState));
    public static void Prepare(int end)
    {
        Check(Active, "authoritative runtime switch is off");
        var map = Find.Maps[0];
        for (int i=0;i<3;i++)
        {
            var pos = GenRadial.RadialCellsAround(map.Center, 15, true).First(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null);
            var t = (Building_TurretGun)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Turret_MiniTurret"));
            t.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(t, pos, map);
            if (i == 0)
            {
                // Quick-test maps can contain dormant hostile targets. Prepare
                // the quiet-map scenario inside the synchronized fixture only.
                var threats = map.listerThings.AllThings.Where(x => x is IAttackTarget && x != t && x.HostileTo(t)).OrderBy(x => x.thingIDNumber).ToArray();
                foreach (var threat in threats)
                {
                    Log.Message("CFG_NETWORK FIXTURE_DESPAWN " + threat.def.defName + ":" + threat.thingIDNumber);
                    threat.DeSpawn();
                }
            }
            AccessTools.Field(typeof(Building_TurretGun), "burstCooldownTicksLeft").SetValue(t, 0);
            AccessTools.Field(typeof(Building_TurretGun), "burstWarmupTicksLeft").SetValue(t, 0);
            var eq = t.gun?.TryGetComp<CompEquippable>();
            Log.Message("CFG_NETWORK SLEEP_INPUT active=" + Active + " spawned=" + t.Spawned + " faction=" + t.Faction?.IsPlayer +
                " forced=" + t.ForcedTarget + " current=" + t.CurrentTarget +
                " activated=" + AccessTools.Field(typeof(Building_TurretGun), "burstActivated").GetValue(t) +
                " progress=" + (AccessTools.Field(typeof(Building_TurretGun), "progressBarEffecter").GetValue(t) != null) +
                " quiet=" + AccessTools.Method(module, "MapIsQuiet").Invoke(null,new object[]{t}) +
                " verbs=" + (eq == null ? "null" : string.Join(",",eq.AllVerbs.Select(v => v.state + "/" + ((System.Collections.ICollection)AccessTools.Field(typeof(Verb), "maintainedEffecters").GetValue(v)).Count))));
            Check((bool)AccessTools.Method(module, "ShouldSleep").Invoke(null,new object[]{t}), "idle turret not sleeping");
            Log.Message("CFG_NETWORK TURRET id=" + t.thingIDNumber + " cell=" + pos + " sleep=True");
        }
        Desync325Assertions.Exercise(map); finish = Math.Max(end, TickPatch.Timer + 10000); checkpoint = TickPatch.Timer + 2000;
        Log.Message("CFG_NETWORK PREPARED target=" + finish + " tick=" + TickPatch.Timer);
    }
    public static void Update()
    {
        if (!enabled) return;
        if (File.Exists(Path.Combine(root,"quit"))) { Application.Quit(); return; }
        if (failed || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            if (!client && !hosted && !MP.IsInMultiplayer)
            {
                hosted=true; Find.TickManager.CurTimeSpeed=TimeSpeed.Paused; Mp.username="CfgHost";
                Check(HostWindow.HostProgrammatically(new ServerSettings {gameName="HostConfig",direct=true,directAddress="127.0.0.1:31035",lan=false,steam=false,multifaction=false,asyncTime=false,syncConfigs=true,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=false}),"host rejected"); return;
            }
            if (!MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
            Check(!Mp.session.desynced,"desync"); Check(Active,"runtime authority mismatch"); Desync325Assertions.ReadInInterface();
            if (!client && !ready && !TickPatch.serverFrozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
            if (client && !ready)
            {
                if (!TickPatch.Frozen) return;
                ready=true; File.WriteAllText(Path.Combine(root,"client.ready"),Snapshot());
                Log.Message("CFG_NETWORK CLIENT_LOADED_FROZEN " + Snapshot());
            }
            if (Mp.session.players.Count != 2 || !File.Exists(Path.Combine(root,"client.ready"))) return;
            if (!client && !ready)
            {
                Check(Snapshot()==File.ReadAllText(Path.Combine(root,"client.ready")),"BASELINE_LOAD_DRIFT");
                Log.Message("CFG_NETWORK HOST_FROZEN " + Snapshot()); ready=true;
                Mp.Client.Send(new ClientFreezePacket(false));
                Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);
                foreach(var map in Find.Maps) Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)TimeSpeed.Superfast);
            }
            if(TickPatch.Frozen)return;
            if(finish<0){if(client&&!sent)sent=prepare.DoSync(null,TickPatch.Timer+10000);return;}
            Check(Mp.session.players.Count==2,"peer lost");
            if(TickPatch.Timer>=checkpoint){Log.Message("CFG_NETWORK CHECKPOINT tick="+TickPatch.Timer+" target="+finish+" players=2 desynced=False");checkpoint+=2000;}
            if(TickPatch.Timer>=finish){done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"PASS "+TickPatch.Timer);Log.Message("CFG_NETWORK COMPLETE tick="+TickPatch.Timer+" desynced=False");}
        }
        catch(Exception e){failed=true;File.WriteAllText(Path.Combine(root,Peer+".failed"),e.ToString());Log.Error("CFG_NETWORK FAILED "+e);}
    }
}
