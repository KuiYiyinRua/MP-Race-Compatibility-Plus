using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Runtime.CompilerServices;using HarmonyLib;using Verse;using RimWorld;using Multiplayer.API;using Meow.SpecialPawnIdeologyCompatibility;
namespace Multiplayer.API {public static class MP {public static bool enabled=false;public static bool IsInMultiplayer;}}
namespace Verse {
 public class StaticConstructorOnStartup:Attribute{}
 public static class ModsConfig {public static bool IdeologyActive=true;public static bool IsActive(string s)=>false;}
 public static class Log {public static void Message(string s){} public static void Error(string s)=>throw new Exception(s);}
 public class Pawn {public int thingIDNumber;public Faction Faction;public Ideo Ideo;public bool Dead;public bool ShouldHaveIdeo=true;public bool special=true;}
}
namespace RimWorld {
 public class Ideo {public string name;}
 public class FactionDef {public bool isPlayer;}
 public class Faction {public FactionDef def=new();public Ideos ideos=new();public static Faction current;public static Faction OfPlayer=>current;}
 public class Ideos {public Ideo PrimaryIdeo;}
 public static class PawnsFinder {
  public static List<Pawn> all=new();
  public static List<Pawn> AllMapsCaravansAndTravellingTransporters_Alive=>all;
  public static List<Pawn> AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction=>all.Where(p=>p.Faction==Faction.OfPlayer).ToList();
 }
}
// Fixtures reproduce the installed control-flow boundaries. Actual installed IL
// local types and target call counts are checked separately against the DLLs.
class MonitorFixture {
 public static int changes;
 [MethodImpl(MethodImplOptions.NoInlining)] public static void RunSpecialPawnCheck(){
  List<Pawn> list=PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;Ideo target=null;
  if(ModsConfig.IdeologyActive && Faction.OfPlayer?.ideos!=null)target=Faction.OfPlayer.ideos.PrimaryIdeo;
  for(int i=0;i<list.Count;i++){Pawn p=list[i];if(p!=null&&p.special&&target!=null&&p.Ideo!=null&&p.Ideo!=target){p.Ideo=target;changes++;}}
 }
}
class ConversionFixture {
 public class Pending {public Pawn pawn;public int tick;}
 public List<Pending> pending=new();public List<int> completed=new();public List<Pawn> world=new();
 [MethodImpl(MethodImplOptions.NoInlining)] public void RegisterPlayerSpecialPawns(int now){
  List<Pawn> list=PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
  for(int i=0;i<list.Count;i++)RegisterIfNeeded(list[i],now);
  foreach(Pawn p in world)if(p?.Faction==Faction.OfPlayer)RegisterIfNeeded(p,now);
 }
 [MethodImpl(MethodImplOptions.NoInlining)] public void RegisterIfNeeded(Pawn p,int now){
  if(!IsEligibleSpecialPawn(p)||completed.Contains(p.thingIDNumber))return;
  Ideo target=Faction.OfPlayer?.ideos?.PrimaryIdeo;
  if(target==null)pending.RemoveAll(e=>e.pawn==p);
  else if(p.Ideo==target){completed.Add(p.thingIDNumber);pending.RemoveAll(e=>e.pawn==p);}
  else if(!pending.Any(e=>e.pawn==p))pending.Add(new Pending{pawn=p,tick=now+30000});
 }
 [MethodImpl(MethodImplOptions.NoInlining)] public void ProcessPendingConversions(int now){
  for(int i=pending.Count-1;i>=0;i--){Pending entry=pending[i];Pawn p=entry?.pawn;
   if(p==null||p.Dead||!p.special)pending.RemoveAt(i);
   else if(p.Faction!=Faction.OfPlayer){completed.Remove(p.thingIDNumber);pending.RemoveAt(i);}
   else if(!IsEligibleSpecialPawn(p))pending.RemoveAt(i);
   else if(now>=entry.tick){Ideo target=Faction.OfPlayer?.ideos?.PrimaryIdeo;if(target!=null&&p.ShouldHaveIdeo){if(p.Ideo!=target)p.Ideo=target;completed.Add(p.thingIDNumber);pending.RemoveAt(i);}}
  }
 }
 [MethodImpl(MethodImplOptions.NoInlining)] public static bool IsEligibleSpecialPawn(Pawn p)=>p!=null&&p.Faction==Faction.OfPlayer&&!p.Dead&&p.ShouldHaveIdeo&&p.special;
}
class Program {
 static int checks;static void Check(bool b,string s){if(!b)throw new Exception(s);checks++;Console.WriteLine("PASS "+s);}
 static Faction F(string name,bool player=true)=>new(){def=new(){isPlayer=player},ideos=new(){PrimaryIdeo=new(){name=name}}};
 static string Snapshot()=>string.Join(";",PawnsFinder.all.OrderBy(p=>p.thingIDNumber).Select(p=>p.thingIDNumber+":"+p.Ideo?.name));
 static void Main(){
  var a=F("A");var b=F("B");var npc=F("NPC",false);var original=new Ideo{name="old"};
  var pa=new Pawn{thingIDNumber=2,Faction=a,Ideo=original};var pb=new Pawn{thingIDNumber=1,Faction=b,Ideo=original};var pn=new Pawn{thingIDNumber=3,Faction=npc,Ideo=original};
  PawnsFinder.all=new(){pa,pb,pn};MP.IsInMultiplayer=true;
  Faction.current=a;MonitorFixture.RunSpecialPawnCheck();string oldA=Snapshot();foreach(var p in PawnsFinder.all)p.Ideo=original;
  Faction.current=b;MonitorFixture.RunSpecialPawnCheck();Check(oldA!=Snapshot(),"unfixed monitor differs by local faction");
  var h=new Harmony("test.ideology");h.Patch(AccessTools.Method(typeof(MonitorFixture),"RunSpecialPawnCheck"),transpiler:new HarmonyMethod(typeof(SpecialPawnIdeology),"MonitorTranspiler"));
  foreach(string method in new[]{"RegisterPlayerSpecialPawns","RegisterIfNeeded","ProcessPendingConversions","IsEligibleSpecialPawn"})h.Patch(AccessTools.Method(typeof(ConversionFixture),method),transpiler:new HarmonyMethod(typeof(SpecialPawnIdeology),"ConversionTranspiler"));
  h.Patch(AccessTools.Method(typeof(ConversionFixture),"IsEligibleSpecialPawn"),postfix:new HarmonyMethod(typeof(SpecialPawnIdeology),"EligiblePostfix"));
  string expected=null;
  foreach(var local in new[]{a,b,npc}){
   Faction.current=local;foreach(var p in PawnsFinder.all)p.Ideo=original;MonitorFixture.changes=0;MonitorFixture.RunSpecialPawnCheck();
   Check(pa.Ideo==a.ideos.PrimaryIdeo&&pb.Ideo==b.ideos.PrimaryIdeo&&pn.Ideo==original,"monitor preserves pawn owners and NPC culture");
   expected??=Snapshot();Check(expected==Snapshot(),"monitor equal across local factions");MonitorFixture.RunSpecialPawnCheck();Check(MonitorFixture.changes==2,"monitor does not repeat conversions");
   foreach(var p in PawnsFinder.all)p.Ideo=original;
   var f=new ConversionFixture();f.RegisterPlayerSpecialPawns(600);Check(f.pending.Count==2,"register both player factions");
   f.ProcessPendingConversions(30599);Check(f.pending.Count==2&&pa.Ideo==original,"preserve conversion delay");
   f.ProcessPendingConversions(30600);Check(f.completed.Count==2&&f.pending.Count==0&&pa.Ideo==a.ideos.PrimaryIdeo&&pb.Ideo==b.ideos.PrimaryIdeo,"pending conversions target each owner");
   f.RegisterPlayerSpecialPawns(31200);Check(f.pending.Count==0,"completed conversions remain completed");
  }
  Check(!ConversionFixture.IsEligibleSpecialPawn(new Pawn()),"factionless pawn excluded");
  MP.IsInMultiplayer=false;Faction.current=a;foreach(var p in PawnsFinder.all)p.Ideo=original;MonitorFixture.RunSpecialPawnCheck();Check(PawnsFinder.all.All(p=>p.Ideo==a.ideos.PrimaryIdeo),"single-player original semantics retained");
  Check(ReferenceEquals(SpecialPawnIdeology.StablePawns(PawnsFinder.all),PawnsFinder.all),"single-player list identity retained");
  Console.WriteLine("PASS_ALL checks="+checks);
 }
}
