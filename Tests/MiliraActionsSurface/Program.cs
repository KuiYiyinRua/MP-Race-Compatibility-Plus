using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;
class Program
{
 static int count;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 static MethodInfo M(Type t,string name)=>AccessTools.DeclaredMethod(t,name);
 static List<CodeInstruction> IL(MethodBase m)=>PatchProcessor.GetOriginalInstructions(m).ToList();
 static bool Calls(IEnumerable<CodeInstruction> il,string name)=>il.Any(i=>i.operand is MethodBase m&&m.Name==name);
 static int Main(string[] a){try{Run(a);return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(string[] a)
 {
  var game=Path.GetFullPath(a[0]);var candidate=Path.GetFullPath(a[1]);var mods=Path.Combine(game,"Mods");
  var dirs=new[]{Path.Combine(game,"RimWorldWin64_Data/Managed"),Path.Combine(mods,"Multiplayer/1.6/Assemblies"),Path.Combine(mods,"MP-meow-online-shop/1.6/Assemblies"),Path.Combine(mods,"2988801276/1.6/Assemblies")};
  AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{var name=new AssemblyName(e.Name).Name+".dll";var p=dirs.Select(d=>Path.Combine(d,name)).FirstOrDefault(File.Exists);return p==null?null:Assembly.LoadFrom(p);};
  var pla=Assembly.LoadFrom(Path.Combine(mods,"3626456960/1.6/Assemblies/PLAMilira.dll"));
  var milira=Assembly.LoadFrom(Path.Combine(mods,"3256974620/1.6/Assemblies/Milira.dll"));
  var core=Assembly.LoadFrom(Path.Combine(candidate,"Core/MP_MeowOnlineShop.dll"));
  var batch=Assembly.LoadFrom(Path.Combine(candidate,"Batch/Meow.DesyncBatchCompatibility.dll"));
  var patch=batch.GetType("Meow.DesyncBatchCompatibility.MiliraActionBoundaries",true);
  foreach(var name in new[]{"Map_Sweep_Plane","Map_Sweep_Plane_Strafing","Projectile_TrackingBullet","Projectile_TrackingBulletNormal","Projectile_Plane","Projectile_WASP"})
  {
   var t=pla.GetType("PLAMilira."+name,true);
   Check(M(t,"Launch")!=null&&M(t,"DrawAt")!=null&&M(t,"RandFactor")!=null,name+" exact hooks");
   Check(Calls(IL(M(t,"DrawAt")),"BPos")&&Calls(IL(M(t,"BPos")),"RandFactor"),name+" rendering reaches simulation random cache");
   Check(IL(M(t,"RandFactor")).Any(i=>i.opcode==OpCodes.Stfld&&((FieldInfo)i.operand).Name=="OnceFlag"),name+" writes cached trajectory flag");
  }
  var rate=IL(AccessTools.DeclaredPropertyGetter(pla.GetType("PLAMilira.PLAMilira_Plane"),"UpdateRateTicks"));
  Check(Calls(rate,"get_CurrentMap")&&Calls(rate,"InViewOf"),"old plane rate depends on local view");
  Check(Calls(IL(M(patch,"InitializeCurve")),"Invoke"),"curve initialized at launch");
  Check(Calls(IL(M(patch,"AfterDraw")),"SetValue")&&Calls(IL(M(patch,"AfterDraw")),"PopState"),"draw rolls back curve and RNG");
  var h=milira.GetType("Milira.CompMilianHairSwitch",true);
  AccessTools.Field(patch,"number").SetValue(null,AccessTools.Field(h,"num"));
  AccessTools.Field(patch,"changeGraphic").SetValue(null,M(h,"ChangeGraphic"));
  var ui=IL(M(milira.GetType("Milira.Dialog_MilianHairStyleConfig",true),"DrawScrollHairSwitch"));
  var rewritten=((IEnumerable<CodeInstruction>)M(patch,"HairSelection").Invoke(null,new object[]{ui})).ToList();
  Check(Calls(rewritten,"SelectHair")&&Calls(rewritten,"LocalHairNumber")&&!Calls(rewritten,"ChangeGraphic"),"native hair selection routes through command");
  Check(!rewritten.Any(i=>i.opcode==OpCodes.Stfld&&i.operand is FieldInfo f&&f.Name=="num"),"no unsynchronized style index write");
  Check(AccessTools.GetDeclaredMethods(h).Count(m=>m.Name.StartsWith("<CompGetGizmosExtra>")&&m.GetParameters().Select(p=>p.ParameterType.Name).SequenceEqual(new[]{"Color"}))==1,"exact color callback resolves once");
  bool rejected=false;try{((IEnumerable<CodeInstruction>)M(patch,"HairSelection").Invoke(null,new object[]{new CodeInstruction[0]})).ToList();}catch{rejected=true;}
  Check(rejected,"changed native UI fails closed");
  var sweep=pla.GetType("PLAMilira.MapSweepStrike",true);
  var soundRewritten=((IEnumerable<CodeInstruction>)M(patch,"SoundSelection").Invoke(null,new object[]{IL(M(sweep,"SpawnSetup"))})).ToList();
  Check(Calls(soundRewritten,"RandomSound")&&!soundRewritten.Any(i=>i.operand is MethodInfo m&&m.Name=="RandomElement"),"native sound random selection is isolated");
  Check(M(pla.GetType("PLAMilira.Projectile_Plane"),"ExposeData")==null && Calls(IL(M(patch,"ExposePlane")),"SavePlaneField"),"previously unscribed plane has trajectory persistence hook");
  var carrier=core.GetType("MP_MeowOnlineShop.MiliraAddonCompat.MiliraConsulMechCarrier_Compat",true);
  var storage=IL(M(carrier,"RegisterStoragePreset"));
  Check(storage.Any(i=>i.opcode==OpCodes.Ldstr&&Equals(i.operand,"AncotLibrary.CompThingCarrier_Custom")) &&
   !storage.Where((i,j)=>i.opcode==OpCodes.Stsfld&&i.operand is FieldInfo f&&f.Name=="_consulType").Any(i=>storage[storage.IndexOf(i)-1].opcode==OpCodes.Ldarg_0),"storage gate uses base carrier instead of Consul argument");
  var harrier=core.GetTypes().Single(t=>t.Name=="PLAMilira_Compat");
  var strike=M(harrier,"SyncedHarrierStrike");
  Check(strike.GetParameters().Select(p=>p.ParameterType.Name).SequenceEqual(new[]{"Thing","Map","String","IntVec3","Int32","Int32"}),"Harrier command serializes instigator then authoritative target map");
  Check(!Calls(IL(strike),"get_CurrentMap")&&Calls(IL(strike),"LaunchSilver"),"Harrier execution uses explicit map and pays silver");
  Console.WriteLine("OFFLINE_SURFACE_PASS checks="+count);
 }
}
