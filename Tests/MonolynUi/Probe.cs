using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;
using UnityEngine;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class MonolynUiState : GameComponent
{
    public MonolynUiState(Game g) { }
    public override void GameComponentUpdate() => MonolynUiProbe.Update();
}

[StaticConstructorOnStartup]
public static class MonolynUiProbe
{
    static readonly bool enabled = GenCommandLine.CommandLineArgPassed("monolynuiprobe");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("monolynclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static bool hosted, ready, sent, failed, done, dispatched, verifying;
    static int finish = -1, checkpoint, nextTick, stage, verified = -1;
    static string clickLabel;
    static ISyncMethod prepare, verify;
    static readonly List<Thing> targets = new List<Thing>();
    static Type producer, consumer, tabType;

    static MonolynUiProbe()
    {
        if (!enabled) return;
        prepare = MP.RegisterSyncMethod(typeof(MonolynUiProbe), nameof(Prepare));
        verify = MP.RegisterSyncMethod(typeof(MonolynUiProbe), nameof(Verify));
        producer = AccessTools.TypeByName("ASEL.Building_MonolynProducer");
        consumer = AccessTools.TypeByName("ASEL.MonolynConsumer");
        tabType = AccessTools.TypeByName("ASEL.ITab_MonolynRecipeList");
        var h = new Harmony("local.meow.monolynuiprobe");
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(MonolynUiProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(UIRoot_Play), "UIRootOnGUI"), postfix: new HarmonyMethod(typeof(MonolynUiProbe), nameof(OnGUI)));
        h.Patch(AccessTools.Method(typeof(Widgets), "ButtonText", new[]{typeof(Rect),typeof(string),typeof(bool),typeof(bool),typeof(bool),typeof(TextAnchor?)}), prefix: new HarmonyMethod(typeof(MonolynUiProbe), nameof(Button)));
        h.Patch(AccessTools.Method(typeof(RimWorld.Planet.WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(MonolynUiProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(MonolynUiProbe), nameof(Unseed)));
        var a = AccessTools.TypeByName("Meow.RaceTrioCompatibility.MonolynUi").Assembly;
        Log.Message("MONOLYN_UI RUN label=" + Path.GetFileName(root) + " hash=" + File.ReadAllText(Path.Combine(root,"candidate.sha256")));
        Log.Message("MONOLYN_UI START peer=" + Peer + " version=" + a.GetName().Version + " MVID=" + a.ManifestModule.ModuleVersionId);
    }
    static void Seed() => Rand.PushState(260929);
    static void Unseed() => Rand.PopState();
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Fail(Exception e) { failed=true; File.WriteAllText(Path.Combine(root,Peer+".failed"),e.ToString()); Log.Error("MONOLYN_UI FAILED " + e); }
    static string Snapshot() => Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" + string.Join(";",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID+":"+m.AsyncTime().mapTicks+":"+m.AsyncTime().randState));
    static int Option(int s) => new[]{2,3,1}[s%3];
    static bool Button(string label, ref bool __result)
    {
        if (clickLabel == null || label != clickLabel) return true;
        clickLabel=null; __result=true; return false;
    }
    public static void Prepare()
    {
        var map=Find.Maps[0];
        var defs=new[]{DefDatabase<ThingDef>.GetNamed("InformationFabricator"),DefDatabase<ThingDef>.GetNamed("InformationFabricator"),DefDatabase<ThingDef>.AllDefs.First(d=>d.thingClass==AccessTools.TypeByName("ASEL.Building_GravityPillar"))};
        foreach(var def in defs)
        {
            var pos=GenRadial.RadialCellsAround(map.Center,30,true).First(c=>c.InBounds(map)&&c.Standable(map)&&GenAdj.OccupiedRect(c,Rot4.North,def.size).All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetEdifice(map)==null));
            var t=ThingMaker.MakeThing(def); t.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(t,pos,map); targets.Add(t);
        }
        finish=TickPatch.Timer+10000; nextTick=TickPatch.Timer+100; checkpoint=TickPatch.Timer+2000;
        Log.Message("MONOLYN_UI PREPARED tick="+TickPatch.Timer+" target="+finish+" ids="+string.Join(",",targets.Select(t=>t.thingIDNumber)));
    }
    public static void Verify(int s)
    {
        if(s<9)
        {
            var t=targets[s%2]; var actual=(int)AccessTools.Field(producer,"selectedOption").GetValue(t);
            Check(actual==Option(s),"mode did not synchronize stage="+s+" got="+actual);
            Log.Message("MONOLYN_UI ASSERT stage="+s+" id="+t.thingIDNumber+" option="+actual);
        }
        else
        {
            var actual=(float)AccessTools.Field(consumer,"sliderValue").GetValue(targets[2]);
            Check(actual==(s-7)*3f,"slider did not synchronize");
            Log.Message("MONOLYN_UI ASSERT stage="+s+" id="+targets[2].thingIDNumber+" radius="+actual);
        }
        verified=s;
    }
    static FloatMenu OpenMenu(object tab)
    {
        clickLabel=(string)AccessTools.Property(producer,"productionOption").GetValue(targets[stage%2]);
        AccessTools.Method(tabType,"FillTab").Invoke(tab,null);
        Check(clickLabel==null,"production mode button not reached");
        return Find.WindowStack.Windows.OfType<FloatMenu>().Last();
    }
    public static void OnGUI()
    {
        if(!enabled||!client||failed||done||finish<0||stage>=12||dispatched||TickPatch.Timer<nextTick||!MP.InInterface||TickPatch.Frozen||Event.current.type!=EventType.Repaint)return;
        try
        {
            dispatched=true;
            if(stage<9)
            {
                var target=targets[stage%2]; Current.Game.CurrentMap=target.Map; Find.Selector.ClearSelection(); Find.Selector.Select(target,false,false);
                var tab=Activator.CreateInstance(tabType);
                int before=(int)AccessTools.Field(producer,"selectedOption").GetValue(target);
                var menu=OpenMenu(tab); menu.Close(false);
                Check((int)AccessTools.Field(producer,"selectedOption").GetValue(target)==before,"cancel changed simulation");
                menu=OpenMenu(tab);
                var options=(List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu),"options").GetValue(menu);
                Check(options.Count==3,"wrong native option count");
                options[Option(stage)-1].Chosen(false,menu);
                Check((int)AccessTools.Field(producer,"selectedOption").GetValue(target)==before,"local value not rolled back before command");
                menu.Close(false);
            }
            else
            {
                var command=targets[2].GetGizmos().First(g=>g.GetType().FullName=="ASEL.Command_SetFloatValue");
                var setter=(Delegate)AccessTools.Field(command.GetType(),"valueSetter").GetValue(command);
                float before=(float)AccessTools.Field(consumer,"sliderValue").GetValue(targets[2]);
                setter.DynamicInvoke(targets[2],(stage-7)*3f);
                Check((float)AccessTools.Field(consumer,"sliderValue").GetValue(targets[2])==before,"slider local rollback failed");
            }
            nextTick=TickPatch.Timer+100;
            Log.Message("MONOLYN_UI DISPATCH stage="+stage+" tick="+TickPatch.Timer);
        }
        catch(Exception e){Fail(e);}
        finally{clickLabel=null;}
    }
    public static void Update()
    {
        if(!enabled)return;
        if(File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}
        if(failed||done||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
        try
        {
            if(!client&&!hosted&&!MP.IsInMultiplayer)
            {
                hosted=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Mp.username="MnHost";
                Check(HostWindow.HostProgrammatically(new ServerSettings{gameName="MonolynUI",direct=true,directAddress="127.0.0.1:31039",lan=false,steam=false,multifaction=false,asyncTime=false,syncConfigs=true,pauseOnJoin=false,autoJoinPoint=0,autosaveInterval=0,desyncTraces=true}),"host rejected");return;
            }
            if(!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            Check(!Mp.session.desynced,"desync");
            if(!client&&!ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
            if(client&&!ready){if(!TickPatch.Frozen)return;ready=true;File.WriteAllText(Path.Combine(root,"client.ready"),Snapshot());Log.Message("MONOLYN_UI CLIENT_LOADED_FROZEN "+Snapshot());}
            if(Mp.session.players.Count!=2||!File.Exists(Path.Combine(root,"client.ready")))return;
            if(!client&&!ready)
            {
                Check(Snapshot()==File.ReadAllText(Path.Combine(root,"client.ready")),"BASELINE_LOAD_DRIFT");ready=true;Log.Message("MONOLYN_UI HOST_FROZEN "+Snapshot());
                Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);
                foreach(var map in Find.Maps)Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)TimeSpeed.Superfast);
            }
            if(TickPatch.Frozen)return;
            if(finish<0){if(client&&!sent)sent=prepare.DoSync(null);return;}
            Check(Mp.session.players.Count==2,"peer lost");
            if(client&&dispatched&&TickPatch.Timer>=nextTick&&!verifying)verifying=verify.DoSync(null,stage);
            if(client&&dispatched&&verified==stage){stage++;dispatched=false;verifying=false;nextTick=TickPatch.Timer+50;}
            if(TickPatch.Timer>=checkpoint){Log.Message("MONOLYN_UI CHECKPOINT tick="+TickPatch.Timer+" verified="+verified+" players=2 desynced=False");checkpoint+=2000;}
            if(TickPatch.Timer>=finish){Check(verified==11,"not all actions completed");done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"PASS "+TickPatch.Timer);Log.Message("MONOLYN_UI COMPLETE tick="+TickPatch.Timer+" desynced=False");}
        }
        catch(Exception e){Fail(e);}
    }
}
