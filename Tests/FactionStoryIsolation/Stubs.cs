// Offline model only. The production project separately compiles against the
// installed game/MP DLLs. These stubs cannot establish runtime Harmony compatibility.
using System;
using System.Collections.Generic;
using System.Reflection;
namespace Verse
{
    public class StaticConstructorOnStartup:Attribute {}
    public class GameComponent { public virtual void ExposeData() {} }
    public class Game { public object Component; public T GetComponent<T>() where T:class=>Component as T; }
    public static class Current { public static Game Game; }
    public static class Log { public static void Message(string s) {} public static void Warning(string s) {} public static void Error(string s)=>throw new Exception(s); }
    public class Pawn { public RimWorld.Faction Faction; }
    public static class Messages { public static void Message(string text,object def,bool historical) {} }
    public static class Scribe_Values
    {
        public static Dictionary<string,bool> Values=new Dictionary<string,bool>(); public static bool Loading;
        public static Dictionary<string,int> IntValues=new Dictionary<string,int>();
        public static void Look(ref int value,string key,int defaultValue) { if(Loading) value=IntValues.TryGetValue(key,out var v)?v:defaultValue; else IntValues[key]=value; }
        public static void Look(ref bool value,string key,bool defaultValue) { if(Loading) value=Values.TryGetValue(key,out var v)?v:defaultValue; else Values[key]=value; }
    }
    public class Map:RimWorld.IIncidentTarget { public RimWorld.Planet.MapParent Parent; }
}
namespace RimWorld
{
    public interface IIncidentTarget {}
    public class FactionDef { public bool isPlayer; }
    public class Faction { public static Faction OfPlayer; public int loadID; public FactionDef def=new FactionDef(); }
    public class Storyteller { public List<IIncidentTarget> AllIncidentTargets=>null; }
    public static class MessageTypeDefOf { public static object RejectInput; }
    public class Quest
    {
        public int id, SideEffects; public bool Accepted, ThrowOnAccept, Hidden; public bool MatchesTab=true;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Accept(Verse.Pawn by) { if(ThrowOnAccept) throw new InvalidOperationException("fixture failure"); if(!Accepted) { SideEffects++; Accepted=true; } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void SetInitiallyAccepted() { SideEffects++; Accepted=true; }
    }
}
namespace RimWorld.Planet
{
    public class MapParent { public RimWorld.Faction Faction; }
    public class Caravan:RimWorld.IIncidentTarget { public RimWorld.Faction Faction; }
}
namespace RimWorld.QuestGen
{
    public class Slate
    {
        readonly Dictionary<string,object> values=new Dictionary<string,object>();
        public bool TryGet<T>(string key,out T result) { if(values.TryGetValue(key,out var v) && v is T t) { result=t; return true; } result=default(T); return false; }
        public void Set<T>(string key,T value)=>values[key]=value;
    }
    public class SlateRef<T> { public T Value; public T GetValue(Slate slate)=>Value; }
    public class QuestNode_GetMap { public SlateRef<string> storeAs=new SlateRef<string>{Value="map"}; }
    public static class QuestGen { public static Slate slate; }
}
namespace Multiplayer.API { public static class MP { public static bool enabled; public static bool IsInMultiplayer,InInterface; } }
namespace Multiplayer.Client
{
    public class Comp { public bool multifaction; }
    public class WorldComp { public RimWorld.Faction spectatorFaction; }
    public static class Multiplayer { public static Comp GameComp; public static bool Ticking,ExecutingCmds; public static RimWorld.Faction RealPlayerFaction; public static WorldComp WorldComp; }
}
