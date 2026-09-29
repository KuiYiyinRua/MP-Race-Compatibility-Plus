using System;
using System.Collections.Generic;
using System.Diagnostics;
using Meow.FactionDiplomacy;
using Verse;

namespace Verse
{
    public class Game { public GameComponent component; public int lookups; public T GetComponent<T>() where T:GameComponent { lookups++; return component as T; } }
    public static class Current { public static Game Game; }
    public class GameComponent { public virtual void ExposeData(){} public virtual void LoadedGame(){} }
    public enum LoadSaveMode { PostLoadInit }
    public enum LookMode { Deep }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Values { public static void Look(ref bool value,string key){} }
    public static class Scribe_Collections { public static void Look<T>(ref List<T> list,string key,LookMode mode){} }
}
namespace Meow.FactionDiplomacy
{
    public class FactionGoodwillRecoveryRecord { public int timer; }
    public class MiliraDiplomacyRecord { public bool friend; }
    internal static class Patch_MiliraMultifactionRelations { public static int restores; public static void RestoreDefinitions(){ restores++; } }
}
static class Program
{
    static int checks;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    static int Main()
    {
        try
        {
            Check(FactionDiplomacyState.CurrentState()==null,"no game");
            var a=Current.Game=new Game();
            Check(FactionDiplomacyState.CurrentState()==null,"construction miss");
            var state=new FactionDiplomacyState(a);a.component=state;
            Check(ReferenceEquals(FactionDiplomacyState.CurrentState(),state),"miss not cached");
            int lookups=a.lookups;
            var timer=new FactionGoodwillRecoveryRecord{timer=2999999};state.recovery.Add(timer);
            state.records.Add(new MiliraDiplomacyRecord());
            var clock=Stopwatch.StartNew();
            for(int i=0;i<1000000;i++) if(!ReferenceEquals(FactionDiplomacyState.CurrentState(),state))throw new Exception("wrong identity");
            Console.WriteLine("million warm queries ms="+clock.Elapsed.TotalMilliseconds);
            Check(a.lookups==lookups,"warm lookup avoids component scan");
            Check(timer.timer==2999999,"lookup does not advance goodwill recovery");
            state.records[0].friend=true;state.recovery=new List<FactionGoodwillRecoveryRecord>();
            Check(FactionDiplomacyState.CurrentState().records[0].friend && FactionDiplomacyState.CurrentState().recovery.Count==0,"live mutations and replaced lists visible");
            var b=Current.Game=new Game();var other=new FactionDiplomacyState(b);b.component=other;
            Check(ReferenceEquals(FactionDiplomacyState.CurrentState(),other),"different game isolated");
            Current.Game=a;
            Check(ReferenceEquals(FactionDiplomacyState.CurrentState(),state),"MP game switch restores original identity");
            var loaded=new FactionDiplomacyState(a){initialized=true};a.component=loaded;loaded.LoadedGame();
            Check(ReferenceEquals(FactionDiplomacyState.CurrentState(),loaded),"same game component replacement on load");
            Check(Patch_MiliraMultifactionRelations.restores==1,"existing definition restore preserved");
            Current.Game=null;Check(FactionDiplomacyState.CurrentState()==null,"unloaded game not retained as current");
            Console.WriteLine("PASS checks="+checks);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
