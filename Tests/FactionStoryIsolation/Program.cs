using System;
using System.Collections.Generic;
using System.Reflection;
using Meow.FactionStoryIsolation;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using MC=Multiplayer.Client.Multiplayer;

internal static class Program
{
    private sealed class WorldTarget:IIncidentTarget {}
    public sealed class Preferences { public bool enableFactionStoryRoutingIsolation; }
    public static Preferences Settings { get; }=new Preferences();
    private static Map fallback;
    private static int selections, passed;
    private static bool SelectMap(Slate slate,out Map map) { selections++; map=fallback; return map!=null; }
    private static void Assert(bool ok,string name) { if(!ok) throw new Exception(name); passed++; Console.WriteLine("PASS "+name); }
    private static Map Home(Faction faction)=>new Map {Parent=new MapParent {Faction=faction}};
    private static int Main()
    {
        MP.enabled=false; // Do not install Harmony into the offline stand-ins.
        Bootstrap.Ready=true;
        var a=new Faction {loadID=10,def=new FactionDef {isPlayer=true}};
        var b=new Faction {loadID=20,def=new FactionDef {isPlayer=true}};
        var npc=new Faction {loadID=30};
        Faction.OfPlayer=a;
        var a1=Home(a); var a2=Home(a); var b1=Home(b); var n=Home(npc);
        var ca=new Caravan {Faction=a}; var cb=new Caravan {Faction=b}; var world=new WorldTarget();
        var state=new IsolationSession(null) {RoutingEnabled=true};
        Current.Game=new Game {Component=state}; MC.GameComp=new Multiplayer.Client.Comp {multifaction=true};
        MP.IsInMultiplayer=true; MC.Ticking=true;
        var list=new List<IIncidentTarget>{b1,a2,cb,world,a1,ca,n,Home(null)};
        RoutingPatches.FilterTargets(list);
        Assert(list.Count==4 && list[0]==a2 && list[1]==world && list[2]==a1 && list[3]==ca,"A keeps both homes and caravan in original order; world remains stage-1 compatible");
        Faction.OfPlayer=b; list=new List<IIncidentTarget>{a1,b1,ca,cb}; RoutingPatches.FilterTargets(list);
        Assert(list.Count==2 && list[0]==b1 && list[1]==cb,"B uses simulation faction rather than first map");
        Faction.OfPlayer=a;
        bool result=true; Assert(!RoutingPatches.CheckMap(b1,ref result) && !result,"foreign slate map rejected before vanilla checks");
        result=false; Assert(RoutingPatches.CheckMap(a1,ref result) && !result,"owned map still requires vanilla acceptance");
        foreach(var mode in new[]{"off","singleplayer","singlefaction","interface"})
        {
            state.RoutingEnabled=mode!="off"; MP.IsInMultiplayer=mode!="singleplayer";
            MC.GameComp.multifaction=mode!="singlefaction"; MC.Ticking=mode!="interface";
            list=new List<IIncidentTarget>{b1}; RoutingPatches.FilterTargets(list);
            result=true; Assert(list.Count==1 && RoutingPatches.CheckMap(b1,ref result),mode+" preserves original behavior");
        }
        MC.Ticking=false; MC.ExecutingCmds=true; state.RoutingEnabled=true; MP.IsInMultiplayer=true; MC.GameComp.multifaction=true;
        list=new List<IIncidentTarget>{b1}; RoutingPatches.FilterTargets(list); Assert(list.Count==0,"command replay applies routing without map tick");
        var node=new QuestNode_GetMap(); QuestGen.slate=new Slate(); QuestGen.slate.Set("map",b1);
        RoutingPatches.TryFindMap=typeof(Program).GetMethod(nameof(SelectMap),BindingFlags.Static|BindingFlags.NonPublic);
        fallback=a2; RoutingPatches.PrepareMap(node);
        Assert(QuestGen.slate.TryGet<Map>("map",out var selected) && selected==a2 && selections==1,"foreign slate map replaced using one vanilla selection");
        RoutingPatches.PrepareMap(node); Assert(selections==1,"valid slate map not randomly reselected");
        QuestGen.slate.Set("map",b1); fallback=null; bool aborted=false;
        try { RoutingPatches.PrepareMap(node); } catch(InvalidOperationException) { aborted=true; }
        Assert(aborted,"missing legal replacement aborts before RunInt can retain foreign target");
        IsolationSession.SettingsProperty=typeof(Program).GetProperty(nameof(Settings));
        IsolationSession.PreferenceField=typeof(Preferences).GetField(nameof(Preferences.enableFactionStoryRoutingIsolation));
        Settings.enableFactionStoryRoutingIsolation=true; IsolationSession.CaptureHostPreferences(false);
        Assert(state.RoutingEnabled,"host preference captured at room creation");
        Settings.enableFactionStoryRoutingIsolation=false;
        Assert(state.RoutingEnabled,"local preference edits cannot change running simulation");
        Scribe_Values.Loading=false; state.ExposeData();
        var client=new IsolationSession(null); Scribe_Values.Loading=true; client.ExposeData();
        Assert(client.RoutingEnabled,"join/load restores saved rule despite local preference mismatch (scribe model)");
        IsolationSession.CaptureHostPreferences(true); Assert(!state.RoutingEnabled,"rehosting permits disabling at next snapshot boundary");
        Scribe_Values.Values.Clear(); client.ExposeData(); Assert(!client.RoutingEnabled,"legacy save missing field defaults off");
        try { passed += AcceptanceTests.Run(); }
        catch(Exception error) { for(var e=error;e!=null;e=e.InnerException) Console.WriteLine(e.GetType().FullName+": "+e.Message); return 1; }
        Console.WriteLine("PASS offline assertions="+passed+"; real Harmony on game stand-ins, not a Unity or multiplayer runtime test"); return 0;
    }
}
