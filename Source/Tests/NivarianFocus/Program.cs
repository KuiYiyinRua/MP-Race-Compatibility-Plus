using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meow.NivarianFocusCompatibility;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.API
{
    public struct SyncType { public Type type; public bool contextMap; public SyncType(Type t) { type=t; contextMap=false; } }
    public interface ISyncMethod { bool DoSync(object target, params object[] args); }
    public sealed class Player { public int Id; public string Username; }
    public static class MP
    {
        public static bool enabled, IsInMultiplayer, InInterface=true;
        public static Faction RealPlayerFaction;
        public static string PlayerName="test";
        public static List<Player> Players=new List<Player>{new Player{Id=1,Username="test"}};
        public static IEnumerable<Player> GetPlayers()=>Players;
        public static ISyncMethod RegisterSyncMethod(Type t,string name,SyncType[] args)=>null;
    }
}
namespace RimWorld { public class Faction { public static Faction OfPlayer; } }
namespace Verse
{
    public class StaticConstructorOnStartup:Attribute { }
    public static class ModsConfig { public static bool IsActive(string name)=>false; }
    public static class Log { public static void Message(string s){} public static void Error(string s)=>throw new Exception(s); }
    public interface IExposable { void ExposeData(); }
    public enum LookMode { Reference, Deep }
    public enum LoadSaveMode { Inactive, Saving, LoadingVars, PostLoadInit }
    public static class Scribe { public static LoadSaveMode mode; public static Dictionary<string,object> data=new Dictionary<string,object>(); }
    public static class Scribe_Values { public static void Look(ref int x,string key) { if(Scribe.mode==LoadSaveMode.Saving) Scribe.data[key]=x; else if(Scribe.mode==LoadSaveMode.LoadingVars)x=(int)Scribe.data[key]; } }
    public static class Scribe_References { public static void Look<T>(ref T x,string key) { if(Scribe.mode==LoadSaveMode.Saving)Scribe.data[key]=x; else if(Scribe.mode==LoadSaveMode.LoadingVars)x=(T)Scribe.data[key]; } }
    public static class Scribe_Collections { public static void Look<T>(ref List<T> x,string key,LookMode mode) { if(Scribe.mode==LoadSaveMode.Saving)Scribe.data[key]=x?.ToList(); else if(Scribe.mode==LoadSaveMode.LoadingVars)x=((List<T>)Scribe.data[key])?.ToList(); } }
    public class Pawn { public int thingIDNumber; public bool Dead, Spawned=true, Nivarian=true; public Map Map; public int boosts; }
    public class Map { public Faction ParentFaction; public FocusState state; public T GetComponent<T>() where T:MapComponent => (T)(object)(state??(state=new FocusState(this))); }
    public class MapComponent { public Map map; public MapComponent(Map m){map=m;} public virtual void ExposeData(){} }
    public class Game { }
    public class GameComponent { public virtual void GameComponentUpdate(){} }
    public class Selector { public List<Pawn> values=new List<Pawn>(); public List<Pawn> SelectedPawns=>values; }
    public static class Find { public static List<Map> Maps=new List<Map>(); public static Map CurrentMap; public static Selector Selector=new Selector(); }
}

// The test target reproduces the installed tick's Selector.SelectedPawns read.
class TickFixture:MapComponent
{
    public TickFixture(Map map):base(map){}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Tick()
    {
        foreach(var pawn in Find.Selector.SelectedPawns) if(pawn.Map==map && pawn.Nivarian) pawn.boosts++;
    }
}
class Guards { public static bool IsReplay=>false; public static bool Simulating=>false; }
class Sender:ISyncMethod
{
    public int Calls; public bool Accept;
    public bool DoSync(object target,params object[] args){Calls++;return Accept;}
}
class Program
{
    static int count;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);count++;Console.WriteLine("PASS "+name);}
    static int Main()
    {
        try
        {
            var a=new Faction();var b=new Faction();Faction.OfPlayer=a;
            var map=new Map{ParentFaction=a};var other=new Map{ParentFaction=b};Find.Maps.AddRange(new[]{map,other});Find.CurrentMap=map;
            var p=new Pawn{thingIDNumber=2,Map=map};var q=new Pawn{thingIDNumber=1,Map=map};var foreign=new Pawn{thingIDNumber=3,Map=other};
            var fixture=new TickFixture(map);
            Find.Selector.values=new List<Pawn>{p};fixture.Tick();Find.Selector.values.Clear();fixture.Tick();
            Check(p.boosts==1,"unfixed tick follows local UI selection");p.boosts=0;
            MP.IsInMultiplayer=true;Focus.IsNivarian=x=>x.Nivarian;
            new Harmony("test.focus").Patch(AccessTools.Method(typeof(TickFixture),"Tick"),transpiler:new HarmonyMethod(typeof(Focus),nameof(Focus.ReplaceSelection)));
            Find.Selector.values.Add(p);fixture.Tick();Check(p.boosts==0,"patched tick ignores unsent local selection");
            Focus.SetFocus(map,1,new List<Pawn>{p,p,q,foreign,null});
            Check(map.state.Records[0].Pawns.SequenceEqual(new[]{q,p}),"filter map, deduplicate, sort durable IDs");
            Find.Selector.values.Clear();fixture.Tick();Check(p.boosts==1 && q.boosts==1,"shared selection applies with empty local UI");
            Focus.SetFocus(map,2,new List<Pawn>{p});fixture.Tick();Check(p.boosts==2 && q.boosts==2,"two players do not double the ramp");
            Focus.SetFocus(map,1,new List<Pawn>());fixture.Tick();Check(p.boosts==3 && q.boosts==2,"deselect removes only issuing player's focus");
            Focus.SetFocus(map,2,new List<Pawn>());fixture.Tick();Check(p.boosts==3,"last deselect stops ramp");
            Faction.OfPlayer=b;Focus.SetFocus(map,1,new List<Pawn>{p});Check(map.state.Records.Count==0,"foreign faction command rejected");Faction.OfPlayer=a;
            Focus.SetFocus(map,1,new List<Pawn>{p});p.Dead=true;fixture.Tick();Check(p.boosts==3,"dead pawn excluded");p.Dead=false;
            p.Map=other;fixture.Tick();Check(p.boosts==3,"pawn moving between maps excluded");p.Map=map;
            Focus.SetFocus(map,1,new List<Pawn>{p});int before=p.boosts;for(int i=0;i<181;i++)fixture.Tick();
            Check(p.boosts-before==180 && map.state.Records.Count==0,"disconnected input expires after exactly 180 map ticks");
            Focus.SetFocus(map,1,new List<Pawn>{p});map.ParentFaction=b;fixture.Tick();Check(map.state.Records.Count==0,"map faction changes clear old focus");map.ParentFaction=a;
            Focus.SetFocus(map,1,new List<Pawn>{p});var record=map.state.Records[0];record.TicksLeft=17;
            Scribe.mode=LoadSaveMode.Saving;record.ExposeData();var restored=new FocusRecord();Scribe.mode=LoadSaveMode.LoadingVars;restored.ExposeData();
            Check(restored.PlayerId==1 && restored.TicksLeft==17 && restored.Faction==a && restored.Pawns.Single()==p,"record writes IDs, expiry, faction and references");
            map.state.Clock=511;Scribe.mode=LoadSaveMode.Saving;map.state.ExposeData();var restoredState=new FocusState(map);Scribe.mode=LoadSaveMode.LoadingVars;restoredState.ExposeData();
            Check(restoredState.Clock==511 && restoredState.Records.Count==1,"map state writes clock and focus records");
            restoredState.Records=null;Scribe.mode=LoadSaveMode.PostLoadInit;restoredState.ExposeData();Check(restoredState.Records!=null,"old saves initialize absent state");Scribe.mode=LoadSaveMode.Inactive;
            MP.IsInMultiplayer=false;Find.Selector.values=new List<Pawn>{q};before=q.boosts;fixture.Tick();Check(q.boosts==before+1,"single-player retains original selection behavior");MP.IsInMultiplayer=true;
            bool rejected=false;try{Focus.ReplaceSelection(new[]{new CodeInstruction(OpCodes.Ret)}).ToList();}catch(InvalidOperationException){rejected=true;}Check(rejected,"unknown target IL fails closed");
            typeof(Focus).GetField("replay",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,typeof(Guards).GetProperty("IsReplay"));
            typeof(Focus).GetField("simulating",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,typeof(Guards).GetProperty("Simulating"));
            var sender=new Sender();Focus.UpdateFocus=sender;MP.RealPlayerFaction=a;Find.Selector.values=new List<Pawn>{p};
            var input=new FocusInput(new Game());input.GameComponentUpdate();input.GameComponentUpdate();Check(sender.Calls==2,"rejected DoSync is retried");
            sender.Accept=true;input.GameComponentUpdate();input.GameComponentUpdate();Check(sender.Calls==3,"accepted input is not resent each frame");
            Find.CurrentMap=other;Find.Selector.values.Clear();input.GameComponentUpdate();Check(sender.Calls==4,"map switch dispatches deselection to old map");
            Console.WriteLine("PASS_ALL checks="+count+"; fixtures, not a real MP runtime");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
