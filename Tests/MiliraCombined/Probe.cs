using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class MiliraActionsState : GameComponent
{
    public MiliraActionsState(Game game) { }
    public override void GameComponentUpdate() => MiliraActionsProbe.Update();
}

[StaticConstructorOnStartup]
public static class MiliraActionsProbe
{
    static bool Enabled => GenCommandLine.CommandLineArgPassed("miliraactionsprobe");
    internal static bool TraceDiagnostic => GenCommandLine.CommandLineArgPassed("miliratracediagnostic");
    internal static bool TraceHistory => GenCommandLine.CommandLineArgPassed("miliratracehistory");
    internal static bool MultiMap => GenCommandLine.CommandLineArgPassed("miliramultimap");
    internal static bool AsyncTime => GenCommandLine.CommandLineArgPassed("miliraasynctime");
    internal static bool ReplayCold3 => GenCommandLine.CommandLineArgPassed("milirareplaycold3");
    internal static bool NaglfarOnly => GenCommandLine.CommandLineArgPassed("miliranaglfaronly");
    internal static bool FunnelLinesOnly => GenCommandLine.CommandLineArgPassed("milirafunnellinesonly");
    internal static bool ResearchUiOnly => GenCommandLine.CommandLineArgPassed("miliraresearchuionly");
    internal static bool BackupBatteryOnly => GenCommandLine.CommandLineArgPassed("milirabackupbatteryonly");
    internal static bool LowFrequency => GenCommandLine.CommandLineArgPassed("miliralowfrequency");
    internal static bool ResearchLong => GenCommandLine.CommandLineArgPassed("miliraresearchlong");
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static bool hosted, ready, failed, done, prepared, sent, called;
    static float next, deadline;
    static int start, round, nextReceipt, smokeStart=-1;
    static bool loaded, clicked, actionsComplete;
    static bool delegateGateChecked;
    static float nextManualStageLog;
    static string Address=>"127.0.0.1:"+(Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-miliraport="))?.Substring(12)??(MiliraFeatures.Enabled?"30998":"30997"));
    static Window summonWindow;
    static readonly bool replay = GenCommandLine.CommandLineArgPassed("milirareplay");
    static Pawn negotiator;
    static Building_CommsConsole console;
    static ISyncMethod prepare;
    static Type supportType, gameType;
    static object Support => AccessTools.Property(gameType, "Support").GetValue(AccessTools.Property(gameType, "Instance").GetValue(null));
    static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
    static string Snapshot() => Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" +
        string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState));
    static void Seed() => Rand.PushState(260926);
    static void Unseed() => Rand.PopState();
    static MiliraActionsProbe()
    {
        if (!Enabled) return;
        if (TraceDiagnostic) { Mp.settings.includeReplayInDesync = true; Mp.settings.desyncTracesRadius = 150; Log.Message("MILIRA_ACTIONS TRACE_DIAGNOSTIC mode=" + Mp.settings.desyncTracingMode + " radius=150 replay=True postcoldTicks=10000 historyIDs=" + (TraceHistory ? "included" : "excluded")); }
        prepare = MP.RegisterSyncMethod(typeof(MiliraActionsProbe), nameof(Prepare));
        supportType = AccessTools.TypeByName("MiliraImperium.MiliraImperiumSupportManager");
        gameType = AccessTools.TypeByName("MiliraImperium.MiliraImperium_GameComponent");
        var h = new Harmony("local.meow.milira.actions.probe");
        MiliraFeatures.Init(h); MiliraCombined.Init(h); NaglfarWorldProbe.Init(h); AbilityWorldCastProbe.Init(h); FunnelLinesProbe.Init(h); HandbookProbe.Init(h); ResearchProbe.Init(); HistoryIdsProbe.Init(); ResearchUiProbe.Init(h); BackupBatteryProbe.Init(h); AbilitySelfCastProbe.Init(); InstallationJobProbe.Init(); UninstallationJobProbe.Init(); ComponentDataJobProbe.Init(h); BooleanControlsProbe.Init(h); DefenseProbe.Init(h); LowFrequencyProbe.Init(h); WingsControlsProbe.Init(); ResearchSoakProbe.Init(); TechnologyProbe.Init(h); RoyalPermitProbe.Init(h); CooldownProbe.Init(); TransactionProbe.Init(h); ColdProbe.Init(h); StudentColdProbe.Init(h); MermeisterColdProbe.Init(); IncidentTrace.Init(h); MiliraIconTrace.Init(h);
        h.Patch(AccessTools.Method(AccessTools.TypeByName("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window"),"DrawContactButton"), prefix:new HarmonyMethod(typeof(MiliraActionsProbe),nameof(ClickSummon)));
        h.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(MiliraActionsProbe), nameof(Update)));
        h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(MiliraActionsProbe), nameof(Seed)), finalizer: new HarmonyMethod(typeof(MiliraActionsProbe), nameof(Unseed)));
        Log.Message("MILIRA_ACTIONS START peer=" + Peer + " candidate=" + File.ReadAllText(Path.Combine(root,"candidate.sha256")));
    }
    static bool ClickSummon(int column, int row, bool enabled, ref bool __result)
    {
        if(!client || summonWindow==null || clicked || !prepared || Event.current.type!=EventType.Repaint) return true;
        __result=column==1 && row==round%4 && enabled;
        if(__result){clicked=true;Log.Message("MILIRA_ACTIONS NATIVE_BUTTON round="+round+" map="+negotiator.Map.uniqueID+" interface="+MP.InInterface);}
        return false;
    }
    static void HostReplay()
    {
        Mp.username="MiliraHost";
        var settings=new ServerSettings{gameName="MiliraActions",direct=true,directAddress=Address,lan=false,steam=false,multifaction=false,asyncTime=AsyncTime,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=TraceDiagnostic};
        Require((bool)AccessTools.Method(typeof(HostWindow),"TryStartLocalServer").Invoke(null,new object[]{settings}),"host preinit");
        HostUtil.HostServer(settings,true);hosted=true;
    }
    static void PrepareSecondMap()
    {
        if(!MultiMap)return;
        if(replay)
        {
            Require(Find.Maps.Count==2 && Find.Maps.All(m=>m.IsPlayerHome),"two-player-map replay baseline required");
            Log.Message("MILIRA_ACTIONS SECOND_MAP_REPLAY maps="+string.Join(",",Find.Maps.OrderBy(m=>m.uniqueID).Select(m=>m.uniqueID))+" async="+AsyncTime);
            return;
        }
        Require(Find.Maps.Count==1,"second-map setup expected one initial map");
        var first=Find.CurrentMap;
        var tile=TileFinder.RandomStartingTile();
        for(int attempt=0;attempt<30 && Find.WorldObjects.AllWorldObjects.Any(w=>w.Tile==tile);attempt++)tile=TileFinder.RandomStartingTile();
        Require(!Find.WorldObjects.AllWorldObjects.Any(w=>w.Tile==tile),"no unoccupied second settlement tile");
        var settlement=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        settlement.Tile=tile;settlement.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(settlement);
        var second=MapGenerator.GenerateMap(new IntVec3(100,1,100),settlement,first.generatorDef);
        var colonist=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true));
        GenSpawn.Spawn(colonist,CellFinder.RandomClosewalkCellNear(second.Center,second,10),second);
        Current.Game.CurrentMap=first;
        Require(Find.Maps.Count==2 && first.IsPlayerHome && second.IsPlayerHome,"second home map not ready");
        Log.Message("MILIRA_ACTIONS SECOND_MAP first="+first.uniqueID+" second="+second.uniqueID+" tile="+tile+" colonist="+colonist.ThingID+" async="+AsyncTime);
    }
    public static void Prepare(int n)
    {
        round = n; called = sent = clicked = false;
        if(summonWindow!=null){summonWindow.Close(false);summonWindow=null;}
        foreach(var t in Mp.WorldComp.trading.ToArray()) Mp.WorldComp.RemoveTradeSession(t);
        Find.WindowStack.TryRemove(typeof(TradingWindow), false);
        var map = Find.Maps.First(m=>m.IsPlayerHome);
        foreach(var ship in map.passingShipManager.passingShips.ToArray()) map.passingShipManager.RemoveShip(ship);
        if (negotiator == null)
        {
            negotiator = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn:true, validatorPostGear:p => !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled));
            var cell = CellFinder.RandomClosewalkCellNear(map.Center,map,10);
            GenSpawn.Spawn(negotiator,cell,map);
            console = (Building_CommsConsole)GenSpawn.Spawn(ThingDefOf.CommsConsole,cell+new IntVec3(3,0,0),map);
            console.SetFaction(Faction.OfPlayer);
            var battery = (Building_Battery)GenSpawn.Spawn(ThingDef.Named("Battery"),cell+new IntVec3(5,0,0),map);
            battery.SetFaction(Faction.OfPlayer); battery.GetComp<CompPowerBattery>().AddEnergy(600);
            GenSpawn.Spawn(ThingDef.Named("PowerConduit"),console.Position,map).SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(ThingDef.Named("PowerConduit"),cell+new IntVec3(4,0,0),map).SetFaction(Faction.OfPlayer);
            map.powerNetManager.UpdatePowerNetsAndConnections_First();
        }
        var faction = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("Milira_Imperium"));
        Require(faction != null,"missing Imperium faction");
        AccessTools.Field(supportType,"imperiumSupport").SetValue(Support, 100);
        start=TickPatch.Timer; nextReceipt=start; deadline=Time.realtimeSinceStartup+120; prepared=true;
        if(smokeStart<0)smokeStart=start;
        Log.Message("MILIRA_ACTIONS PREPARE round="+n+" pawn="+negotiator.thingIDNumber+" console="+console.thingIDNumber);
    }
    public static void Update()
    {
        if(!Enabled)return;
        RuntimeHeartbeat.Update();
        IncidentTrace.Update();
        if(ColdProbe.QuitRequested || File.Exists(Path.Combine(root,"quit"))){Application.Quit();return;}
        if(HandbookProbe.Manual && Time.realtimeSinceStartup >= nextManualStageLog)
        {
            nextManualStageLog = Time.realtimeSinceStartup + 20;
            Log.Message("HANDBOOK MANUAL_STAGE program=" + Current.ProgramState + " longEvent=" + LongEventHandler.AnyEventNowOrWaiting +
                " next=" + next + " now=" + Time.realtimeSinceStartup + " mp=" + MP.IsInMultiplayer +
                " clientState=" + Mp.Client?.State + " simulating=" + TickPatch.Simulating +
                " serverFrozen=" + TickPatch.serverFrozen + " coldActive=" + ColdProbe.Active);
        }
        if(replay&&!client&&!loaded&&!LongEventHandler.AnyEventNowOrWaiting){loaded=true;Replay.LoadReplay(new FileInfo(Path.Combine(root,"input.zip")),true,HostReplay);return;}
        if(failed||done||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||Time.realtimeSinceStartup<next)return;
        next=Time.realtimeSinceStartup+0.1f;
        try
        {
            if(!client&&!hosted&&!MP.IsInMultiplayer)
            {
                hosted=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Mp.username="MiliraHost";
                PrepareSecondMap();
                Require(HostWindow.HostProgrammatically(new ServerSettings{gameName="MiliraActions",direct=true,directAddress=Address,lan=false,steam=false,multifaction=false,asyncTime=AsyncTime,syncConfigs=false,pauseOnJoin=false,pauseOnLetter=PauseOnLetter.Never,autoJoinPoint=0,autosaveInterval=0,desyncTraces=TraceDiagnostic}),"host rejected");return;
            }
            if(!MP.IsInMultiplayer||Mp.Client.State!=ConnectionStateEnum.ClientPlaying||TickPatch.Simulating)return;
            Require(!Mp.session.desynced,"desync");
            if (MiliraFeatures.Enabled && !delegateGateChecked)
            {
                var gate = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.DelegateSerialization"), "CheckMethodAllowed", new[] { typeof(MethodInfo) });
                foreach (var name in new[] { "PLAMilira_DialogCallbacks+SavedLink", "PLAMilira_DialogCallbacks+SavedAction", "PLAMilira_ReadingCallbacks+SavedNext" })
                {
                    var type = AccessTools.TypeByName("MP_MeowOnlineShop.MiliraAddonCompat." + name);
                    Require(type != null, "persistent callback type absent: " + name);
                    var method = AccessTools.DeclaredMethod(type, "Invoke", Type.EmptyTypes);
                    Require(Equals(gate.Invoke(null, new object[] { method }), method), "persistent callback gate: " + name);
                }
                delegateGateChecked = true;
                Log.Message("MILIRA_ACTIONS exact persistent delegate gate passed3");
            }
            if(ColdProbe.Active){ColdProbe.Update();return;}
            if(HandbookProbe.Manual){HandbookProbe.ManualUpdate();return;}
            if(!client&&!ready&&!TickPatch.serverFrozen){Mp.Client.Send(new ClientFreezePacket(true));return;}
            if(client&&!ready){if(!TickPatch.Frozen)return;ready=true;File.WriteAllText(Path.Combine(root,"client.ready"),Snapshot());}
            if(Mp.session.players.Count!=2||!File.Exists(Path.Combine(root,"client.ready")))return;
            if(!client&&!ready){Require(Snapshot()==File.ReadAllText(Path.Combine(root,"client.ready")),"BASELINE_LOAD_DRIFT");Require(!MultiMap||Find.Maps.Count==2,"multi-map baseline map count");Require(Mp.GameComp.asyncTime==AsyncTime,"MP async setting mismatch");Log.Message("MILIRA_ACTIONS BASELINE maps="+Find.Maps.Count+" async="+Mp.GameComp.asyncTime+" state="+Snapshot());ready=true;ColdProbe.ExportReplay("baseline");Mp.Client.Send(new ClientFreezePacket(false));Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)TimeSpeed.Superfast);foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)(AsyncTime && map!=Find.Maps.OrderBy(m=>m.uniqueID).First()?TimeSpeed.Fast:TimeSpeed.Superfast));}
            if(TickPatch.Frozen)return;
            if (GenCommandLine.CommandLineArgPassed("studentcoldonly")) { ColdProbe.Start(); return; }
            if(FunnelLinesOnly)
            {
                if(!FunnelLinesProbe.Update())return;
                if(smokeStart<0){smokeStart=TickPatch.Timer;Log.Message("FUNNEL_LINES SOAK_START world="+smokeStart);}
                if(TickPatch.Timer-smokeStart>=10000)
                {
                    done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"funnel line casts=6;worldTicks="+(TickPatch.Timer-smokeStart)+";desynced=False");
                    Log.Message("FUNNEL_LINES SOAK_PASS casts=6 ticks="+(TickPatch.Timer-smokeStart));
                }
                return;
            }
            if(NaglfarOnly)
            {
                if(!(NaglfarWorldProbe.Update() && AbilityWorldCastProbe.Update()))return;
                if(smokeStart<0){smokeStart=TickPatch.Timer;Log.Message("NAGLFAR_WORLD SOAK_START world="+smokeStart);}
                if(TickPatch.Timer-smokeStart>=10000)
                {
                    done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"naglfar rounds=3;worldTicks="+(TickPatch.Timer-smokeStart)+";desynced=False");
                    Log.Message("NAGLFAR_WORLD SOAK_PASS rounds=3 ticks="+(TickPatch.Timer-smokeStart));
                }
                return;
            }
            if(ResearchUiOnly)
            {
                if(GenCommandLine.CommandLineArgPassed("miliraturretdiagnostic")){if(!DefenseProbe.Update())return;}
                else if(!(HandbookProbe.Update() && ResearchProbe.Update() && HistoryIdsProbe.Update() && ResearchUiProbe.Update() && BackupBatteryProbe.Update() && AbilitySelfCastProbe.Update() && InstallationJobProbe.Update() && UninstallationJobProbe.Update() && ComponentDataJobProbe.Update() && (!LowFrequency || (LowFrequencyProbe.Update() && WingsControlsProbe.Update())) && (!GenCommandLine.CommandLineArgPassed("miliradefense") || DefenseProbe.Update()) && (!GenCommandLine.CommandLineArgPassed("milirabooleans") || BooleanControlsProbe.Update())))return;
                if(ResearchLong && !ResearchSoakProbe.Update())return;
                if(smokeStart<0){smokeStart=TickPatch.Timer;Log.Message("RESEARCH_UI SOAK_START world="+smokeStart);}
                if(ResearchLong || TickPatch.Timer-smokeStart>=10000)
                {
                    done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"research UI/actions;worldTicks="+(ResearchLong ? ResearchSoakProbe.WorldElapsed : TickPatch.Timer-smokeStart)+";desynced=False");
                    Log.Message("RESEARCH_UI SOAK_PASS ticks="+(ResearchLong ? ResearchSoakProbe.WorldElapsed : TickPatch.Timer-smokeStart));
                }
                return;
            }
            if(BackupBatteryOnly)
            {
                if(!BackupBatteryProbe.Update())return;
                if(smokeStart<0){smokeStart=TickPatch.Timer;Log.Message("BACKUP_BATTERY SOAK_START world="+smokeStart);}
                if(TickPatch.Timer-smokeStart>=1000)
                {
                    done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"backup battery;worldTicks="+(TickPatch.Timer-smokeStart)+";desynced=False");
                    Log.Message("BACKUP_BATTERY SOAK_PASS ticks="+(TickPatch.Timer-smokeStart));
                }
                return;
            }
            if(MiliraFeatures.Enabled){if(HandbookProbe.Update() && ResearchProbe.Update() && ((TraceDiagnostic && !TraceHistory) || HistoryIdsProbe.Update()) && ResearchUiProbe.Update() && TechnologyProbe.Update() && CooldownProbe.Update() && RoyalPermitProbe.Update() && TransactionProbe.Update() && (!GenCommandLine.CommandLineArgPassed("milirabooleans") || BooleanControlsProbe.Update()) && (!GenCommandLine.CommandLineArgPassed("miliradefense") || DefenseProbe.Update()) && MiliraCombined.Update() && FunnelLinesProbe.Update() && (!MultiMap || NaglfarWorldProbe.Update())) MiliraFeatures.Update();return;}
            if(actionsComplete)
            {
                if(TickPatch.Timer-smokeStart>=10000){done=true;File.WriteAllText(Path.Combine(root,Peer+".complete"),"desynced=False ticks="+(TickPatch.Timer-smokeStart));Log.Message("MILIRA_ACTIONS COMPLETE desynced=False ticks="+(TickPatch.Timer-smokeStart));}
                else if(TickPatch.Timer>=nextReceipt){nextReceipt+=1000;Log.Message("MILIRA_ACTIONS SOAK tick="+TickPatch.Timer+" elapsed="+(TickPatch.Timer-smokeStart)+" players="+Mp.session.players.Count+" maps="+Find.Maps.Count+" desynced="+Mp.session.desynced);}
                return;
            }
            if(!prepared){if(client&&!sent)sent=prepare.DoSync(null,0);return;}
            var m=negotiator.Map;
            if(client&&!sent && TickPatch.Timer-start>60)
            {
                sent=true;
                summonWindow=(Window)Activator.CreateInstance(AccessTools.TypeByName("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window"),negotiator);
                Find.WindowStack.Add(summonWindow);
                Log.Message("MILIRA_ACTIONS OPEN_NATIVE_CONSOLE round="+round);
            }
            var trader=m.passingShipManager.passingShips.FirstOrDefault(s=>s.GetType().FullName=="MiliraImperium.MI_TradeShip");
            if(TickPatch.Timer>=nextReceipt){nextReceipt+=120;Log.Message("MILIRA_ACTIONS STATE tick="+TickPatch.Timer+" ships="+m.passingShipManager.passingShips.Count+" support="+AccessTools.Field(supportType,"imperiumSupport").GetValue(Support)+" job="+negotiator.CurJob?.def.defName+" target="+negotiator.CurJob?.commTarget+" sessions="+Mp.WorldComp.trading.Count);}
            Require(Time.realtimeSinceStartup<deadline || round>=11,"action timeout");
            if(trader!=null && TickPatch.Timer-start>240)
            {
                Require((int)AccessTools.Field(supportType,"imperiumSupport").GetValue(Support)==97,"support not deducted exactly once");
                var receipt=trader.GetUniqueLoadID()+":"+string.Join(";",((ITrader)trader).Goods.OrderBy(t=>t.thingIDNumber).Select(t=>t.ThingID+":"+t.def.defName+":"+t.stackCount+":"+t.Stuff?.defName));
                var path=Path.Combine(root,Peer+".assert"+round);
                if(!File.Exists(path)){File.WriteAllText(path,receipt);Log.Message("MILIRA_ACTIONS ASSERT round="+round+" "+receipt);}
                if(File.Exists(Path.Combine(root,"host.assert"+round))&&File.Exists(Path.Combine(root,"client.assert"+round)))
                {
                    Require(File.ReadAllText(Path.Combine(root,"host.assert"+round))==File.ReadAllText(Path.Combine(root,"client.assert"+round)),"state mismatch");
                    if(round>=11){actionsComplete=true;}
                    else if(client){prepare.DoSync(null,round+1);next=Time.realtimeSinceStartup+1;}
                }
            }
        }
        catch(Exception e){failed=true;Log.Error("MILIRA_ACTIONS FAILED "+e);File.WriteAllText(Path.Combine(root,Peer+".failed"),e.ToString());}
    }
}

