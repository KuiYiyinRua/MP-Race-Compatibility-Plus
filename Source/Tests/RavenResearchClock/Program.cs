using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
class Program {
 static int count;
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
 static MethodInfo M(Type t,string n)=>AccessTools.DeclaredMethod(t,n);
 static List<CodeInstruction> IL(MethodBase m)=>PatchProcessor.GetOriginalInstructions(m).ToList();
 static void Main(string[] args){try{Run(args);}catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}}
 static void Run(string[] a){
 string game=Path.GetFullPath(a[0]),candidate=Path.GetFullPath(a[1]);
 string[] dirs={game+"/RimWorldWin64_Data/Managed",game+"/Mods/Multiplayer/1.6/Assemblies",game+"/Mods/Multiplayer/1.6/AssembliesCustom",game+"/Mods/3781005562/Assemblies",game+"/Mods/MP-meow-online-shop/1.6/Assemblies"};
 AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{var f=dirs.Select(d=>Path.Combine(d,new AssemblyName(e.Name).Name+".dll")).FirstOrDefault(File.Exists);return f==null?null:Assembly.LoadFrom(f);};
 var rw=Assembly.LoadFrom(dirs[0]+"/Assembly-CSharp.dll");var raven=Assembly.LoadFrom(dirs[3]+"/ZuoYao_RavenRace.dll");var patch=Assembly.LoadFrom(candidate);
 var system=raven.GetType("RavenRace.Features.CentralHub.GameComponent_RavenCentralHubSystem",true);var fix=patch.GetType("MP_MeowOnlineShop.RavenResearchClock",true);
 var tick=M(system,"GameComponentTick");var code=IL(tick);var getter=AccessTools.PropertyGetter(rw.GetType("Verse.TickManager"),"TicksGame");
 Check(code.Count(i=>i.Calls(getter))==1,"installed native research tick reads one ambient clock");
 var rewritten=((IEnumerable<CodeInstruction>)M(fix,"UseResearchClock").Invoke(null,new object[]{code})).ToList();
 Check(!rewritten.Any(i=>i.Calls(getter))&&rewritten.Count(i=>i.Calls(M(fix,"ResearchTick")))==1,"transpiler replaces only clock read");
 Check(rewritten.Any(i=>i.operand is MethodBase m&&m.Name=="TickResearch"),"native cost/prerequisite execution retained");
 var managerType=rw.GetType("Verse.TickManager");var gameType=rw.GetType("Verse.Game");var currentType=rw.GetType("Verse.Current");
 var manager=FormatterServices.GetUninitializedObject(managerType);var g=FormatterServices.GetUninitializedObject(gameType);
 AccessTools.Field(gameType,"tickManager").SetValue(g,manager);AccessTools.PropertySetter(currentType,"Game").Invoke(null,new[]{g});
 M(managerType,"DebugSetTicksGame").Invoke(manager,new object[]{2579274});
 var original=FormatterServices.GetUninitializedObject(system);var last=AccessTools.Field(system,"lastResearchTick");last.SetValue(original,3571544);
 tick.Invoke(original,null);Check((int)last.GetValue(original)==3571544,"actual native method stalls at Autosave-4 clock (2579274 < 3571544)");
 var recover=M(fix,"RecoverFutureTick");object[] future={3571544};recover.Invoke(null,future);Check((int)future[0]==-1,"candidate rebases future marker");
 foreach(int valid in new[]{-1,2579214,2579274}){object[] values={valid};recover.Invoke(null,values);Check((int)values[0]==valid,"preserve valid marker "+valid);}
 Check((int)M(fix,"ResearchTick").Invoke(null,new[]{manager})==2579274,"offline clock remains vanilla");
 var clock=IL(M(fix,"ResearchTick"));Check(clock.Any(i=>i.operand is FieldInfo f&&f.Name=="worldTicks")&&clock.Any(i=>i.opcode==OpCodes.Add),"MP clock uses world tick with in-dispatch increment");
 Check(IL(M(patch.GetType("MP_MeowOnlineShop.RavenCentralResearch"),"Apply")).Any(i=>i.Calls(M(fix,"Apply"))),"candidate integration installs scheduler repair");
 Console.WriteLine("COMPLETE checks="+count);
 }
}
