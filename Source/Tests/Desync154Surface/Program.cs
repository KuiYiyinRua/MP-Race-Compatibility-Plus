using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
class Program
{
 static int checks;
 static void Check(bool b,string s) { if(!b) throw new Exception(s); Console.WriteLine("PASS "+s); checks++; }
 static MethodInfo M(Type t,string n)=>AccessTools.DeclaredMethod(t,n);
 static List<CodeInstruction> IL(MethodBase m)=>PatchProcessor.GetOriginalInstructions(m).ToList();
 static bool Calls(IEnumerable<CodeInstruction> c,string n)=>c.Any(i=>i.operand is MethodBase m && m.Name==n);
 static void Main(string[] a) {try {Run(a);} catch(Exception e) {for(;e!=null;e=e.InnerException) Console.WriteLine(e.GetType().Name+": "+e.Message); Environment.ExitCode=1;}}
 static void Run(string[] a)
 {
  var root=Path.GetFullPath(a[0]); var mods=Path.Combine(root,"Mods"); var evidence=Path.GetFullPath(a[1]);
  var dirs=new[]{Path.Combine(root,"RimWorldWin64_Data/Managed"),Path.Combine(mods,"Multiplayer/1.6/Assemblies"),Path.Combine(mods,"Multiplayer/1.6/AssembliesCustom"),Path.Combine(mods,"MP-meow-online-shop/1.6/Assemblies"),Path.Combine(mods,"3595247479/1.6/Assemblies"),Path.Combine(mods,"2023507013/1.6/Assemblies")};
  AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{var n=new AssemblyName(e.Name).Name+".dll";var p=dirs.Select(d=>Path.Combine(d,n)).FirstOrDefault(File.Exists);return p==null?null:Assembly.LoadFrom(p);};
  var game=Assembly.LoadFrom(Path.Combine(dirs[0],"Assembly-CSharp.dll"));
  var candidate=Assembly.LoadFrom(Path.Combine(evidence,"Candidate/DesyncBatch/Meow.DesyncBatchCompatibility.dll"));
  var trio=Assembly.LoadFrom(Path.Combine(evidence,"Candidate/RaceTrio/Meow.RaceTrioCompatibility.dll"));
  var che=Assembly.LoadFrom(Path.Combine(dirs[4],"ChezhouLib.dll"));
  var vef=Assembly.LoadFrom(Path.Combine(dirs[5],"VEF.dll"));
  foreach(var x in new[]{game,candidate,trio,che,vef})Console.WriteLine("MVID "+x.GetName().Name+" "+x.ManifestModule.ModuleVersionId);
  Check(game.ManifestModule.ModuleVersionId==new Guid("239ae808-e7f5-427a-a802-2d09e89ee1ed"),"exact logged game binary");
  Check(che.ManifestModule.ModuleVersionId==new Guid("20849622-b827-41ad-b435-dd6785fc97bc"),"exact logged flight binary");
  var fly=che.GetType("ChezhouLib.ClThingComp.ThingComp_RaceFly",true);
  var emitter=M(fly,"SpawnConfiguredFleck"); var code=IL(emitter);
  Check(emitter.GetParameters().Single().ParameterType.FullName=="ChezhouLib.ClThingComp.FlightEffectEntry","exact fleck argument");
  Check(Calls(code,"GetEffectLocation") && Calls(code,"ShouldSpawnMotesAt") && Calls(code,"RandomizeValue"),"random position and conditional visual RNG");
  Check(!code.Any(i=>i.operand is MethodBase m && new[]{"AddHediff","TakeDamage","StartJob","TryBeginMovingTakeOff"}.Contains(m.Name)),"fleck scope excludes flight gameplay");
  var visual=candidate.GetType("Meow.DesyncBatchCompatibility.LightningVisuals",true);
  Check(IL(M(visual,"ApplyFlight")).Any(i=>Equals(i.operand,"SpawnConfiguredFleck")),"patch resolves actual emitter");
  var proj=vef.GetType("VEF.Weapons.ExpandableProjectile",true);
  Check(Calls(IL(AccessTools.PropertyGetter(proj,"UpdateRateTicks")),"get_CurrentMap"),"native projectile scheduling depends on local viewed map");
  var rate=candidate.GetType("Meow.DesyncBatchCompatibility.ExpandableProjectileRate",true);
  Check(Calls(IL(M(rate,"Rate")),"get_IsInMultiplayer"),"fixed rate limited to multiplayer");
  var rateArgs=new object[]{99};Check((bool)M(rate,"Rate").Invoke(null,rateArgs) && (int)rateArgs[0]==99,"single player preserves original scheduling");
  var focus=IL(M(trio.GetType("Meow.RaceTrioCompatibility.NivarianSelectionBoost",true),"Apply"));
  var guard=focus.FindIndex(i=>Equals(i.operand,"Meow.NivarianFocusCompatibility.Focus"));
  var registration=focus.FindIndex(i=>i.operand is MethodBase m && m.Name=="RegisterSyncMethod");
  Check(guard>=0 && guard<registration,"standalone owner checked before sync registration/transpiler");
  var clock=candidate.GetType("Meow.DesyncBatchCompatibility.WorldComponentClock",true);
  foreach(var target in new[]{"RimWorld.Planet.WorldPawns|WorldPawnsTick","RimWorld.FactionManager|FactionManagerTick"})
  {var p=target.Split('|');Check(M(game.GetType(p[0],true),p[1]).GetParameters().Length==0,"exact world target "+p[1]);}
  Check(Calls(IL(M(clock,"BeforeManager")),"DebugSetTicksGame") && Calls(IL(M(clock,"AfterManager")),"DebugSetTicksGame"),"world clock enter and finalizer restore");
  Check(!Calls(IL(M(clock,"BeforeManager")),"PushState"),"world clock does not hide simulation RNG");
  var fire=game.GetType("RimWorld.Fire",true);
  Check(Calls(IL(M(fire,"SpawnSmokeParticles")),"get_Value"),"fire smoke consumes random outside FleckMaker");
  var tale=Assembly.LoadFrom(Path.Combine(dirs[3],"Meow.TaleNivarianCompatibility.dll"));
  Check(tale.GetType("Meow.TaleNivarianCompatibility.FireVisualRandom",true)!=null,"existing deployed fire repair present; startup blocker repaired in Trio");
  Console.WriteLine("ALL "+checks+" OFFLINE CHECKS PASSED; no game launched");
 }
}
