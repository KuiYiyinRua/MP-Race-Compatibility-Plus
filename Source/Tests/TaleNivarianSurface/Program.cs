using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;
class Program {
 static int checks;
 static void Check(bool b,string name){if(!b)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static int Main(string[] args){
 var game=Path.GetFullPath(args[0]);var candidatePath=Path.GetFullPath(args[1]);
 var dirs=new[]{Path.Combine(game,"RimWorldWin64_Data/Managed"),Path.Combine(game,"Mods/Multiplayer/1.6/Assemblies"),Path.Combine(game,"Mods/Multiplayer/1.6/AssembliesCustom"),Path.Combine(game,"Mods/3624805128/1.6/Assemblies"),Path.Combine(game,"Mods/3624805128/LumiParticle1.6/Assemblies"),Path.Combine(game,"Mods/3477405110/1.6/Assemblies"),Path.Combine(game,"Mods/3256974620/1.6/Assemblies"),Path.GetDirectoryName(candidatePath)};
 dirs=dirs.Concat(Directory.GetDirectories(Path.Combine(game,"Mods")).Select(d=>Path.Combine(d,"1.6/Assemblies")).Where(d=>File.Exists(Path.Combine(d,"RJW.dll"))||File.Exists(Path.Combine(d,"Privacy-Please.dll")))).ToArray();
 AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{var name=new AssemblyName(e.Name).Name+".dll";var p=dirs.Select(d=>Path.Combine(d,name)).FirstOrDefault(File.Exists);return p==null?null:Assembly.LoadFrom(p);};
 var rw=Assembly.LoadFrom(Path.Combine(dirs[0],"Assembly-CSharp.dll"));
 var niv=Assembly.LoadFrom(Path.Combine(dirs[3],"Nivarian_Race.dll"));
 var royalty=Assembly.LoadFrom(Path.Combine(dirs[3],"Nivarian_Race_Royalty.dll"));
 var tale=Assembly.LoadFrom(Path.Combine(dirs[5],"TheTaleofMilira.dll"));
 var cand=Assembly.LoadFrom(candidatePath);
 foreach(var asm in new[]{rw,niv,royalty,tale,cand})Console.WriteLine(asm.GetName().Name+" MVID="+asm.ManifestModule.ModuleVersionId);
 var fireworks=niv.GetType("Nivarian.NivarianGameCondition.GameCondition_OrbitalFireworks",true);
 var pulse=AccessTools.DeclaredMethod(fireworks,"ApplyOutdoorColonyMoodPulse",Type.EmptyTypes);
 Check(pulse!=null&&pulse.ReturnType==typeof(void),"fireworks actual pulse target");
 var roster=AccessTools.PropertyGetter(rw.GetType("Verse.MapPawns",true),"FreeColonistsAndPrisonersSpawned");
 var pulseIL=PatchProcessor.GetOriginalInstructions(pulse).ToList();
 Check(pulseIL.Count(i=>i.Calls(roster))==1,"fireworks exactly one local-faction roster read");
 var fwPatch=cand.GetType("Meow.TaleNivarianCompatibility.NivarianFireworks",true);
 var fwRewrite=AccessTools.Method(fwPatch,"ReplaceRecipients");
 var beforeOperands=pulseIL.Select(i=>i.operand).ToArray();
 var fireworksRewritten=((IEnumerable<CodeInstruction>)fwRewrite.Invoke(null,new object[]{pulseIL})).ToList();
 Check(fireworksRewritten.Count==beforeOperands.Length&&fireworksRewritten.Where((i,n)=>!Equals(i.operand,beforeOperands[n])).Count()==1,"fireworks only recipient call changes; pulse/roof/memory logic retained");
 Check(!fireworksRewritten.Any(i=>i.Calls(roster))&&fireworksRewritten.Count(i=>i.Calls(AccessTools.Method(fwPatch,"Recipients")))==1,"fireworks contextual roster replaced");
 var progress=niv.GetType("Nivarian_Race.Code.Progress.ProgressManager",true);var progressNode=niv.GetType("Nivarian_Race.Code.Defs.ProgressNodeDef",true);
 Check(progress.IsSubclassOf(rw.GetType("Verse.GameComponent",true))&&progressNode.IsSubclassOf(rw.GetType("Verse.Def",true)),"progress native GameComponent/Def serialization types");
 foreach(var name in new[]{"DebugForceUnlock","DebugForceLock"}){var m=AccessTools.DeclaredMethod(progress,name,new[]{progressNode});Check(m!=null&&!m.IsStatic&&m.ReturnType==typeof(void),"progress debug target "+name);}
 var patch=cand.GetType("Meow.TaleNivarianCompatibility.NivarianSimulationRandom",true);
 var replace=AccessTools.Method(patch,"Replace");
 var targets=new[]{new[]{"Nivarian.NivarianDrones.NivarianStraftingAttackerDrone","CalcEntryPoint"},new[]{"Nivarian.NivarianDrones.NivarianStraftingAttackerDrone","CalcRepositionPoint"},new[]{"Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_MovingBase","OrbitTarget"},new[]{"Nivarian_Race.Code.NivarianThing.ProgrammableMoverThing","CreateParabolicArc"},new[]{"Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret","CompTick"},new[]{"Nivarian.ReflectiveShield","LaunchReturnShot"}};
 foreach(var pair in targets){var type=niv.GetType(pair[0])??royalty.GetType(pair[0],true);var m=AccessTools.DeclaredMethod(type,pair[1]);Check(m!=null,"target "+pair[0]+"."+pair[1]);var orig=PatchProcessor.GetOriginalInstructions(m).ToList();var old=orig.Count(i=>(i.operand as MethodInfo)?.DeclaringType?.FullName=="UnityEngine.Random");var changed=((IEnumerable<CodeInstruction>)replace.Invoke(null,new object[]{orig,m})).ToList();Check(old>0 && !changed.Any(i=>(i.operand as MethodInfo)?.DeclaringType?.FullName=="UnityEngine.Random"),"all Unity calls replaced "+m.Name+" count="+old);}
 var privacy=Assembly.LoadFrom(dirs.Select(d=>Path.Combine(d,"Privacy-Please.dll")).First(File.Exists));
 Console.WriteLine("Privacy MVID="+privacy.ManifestModule.ModuleVersionId);
 foreach(var pair in new[]{new[]{"PrivacyUtility","PrivacyCheckForPawn"},new[]{"SexInteractionUtility","GetReactionsToSexAct"},new[]{"HarmonyPatch_JobDriver_Sex_setup_ticks","Postfix"}}){var m=AccessTools.DeclaredMethod(privacy.GetType("Privacy_Please."+pair[0],true),pair[1]);var code=PatchProcessor.GetOriginalInstructions(m).ToList();var rewrittenPrivacy=((IEnumerable<CodeInstruction>)replace.Invoke(null,new object[]{code,m})).ToList();Check(!rewrittenPrivacy.Any(i=>(i.operand as MethodInfo)?.DeclaringType?.FullName=="UnityEngine.Random"),"Privacy Unity replaced "+m.Name);}
 var aid=cand.GetType("Meow.TaleNivarianCompatibility.NivarianAidEvents",true);var cooldown=AccessTools.DeclaredMethod(niv.GetType("Nivarian_Race.Code.Incidents.DefExtension_NivarianAidIncident",true),"GetRandomCooldownTicks");var instructions=PatchProcessor.GetOriginalInstructions(cooldown).ToList();var rewritten=((IEnumerable<CodeInstruction>)AccessTools.Method(aid,"CooldownRandom").Invoke(null,new object[]{instructions})).ToList();Check(rewritten.Count(i=>(i.operand as MethodInfo)?.DeclaringType==aid)==1,"aid cooldown exact replacement");
 foreach(var pair in new[]{new[]{"CompTransformableWeapon","Transform"},new[]{"Comp_FlyActivator","SwitchMode"},new[]{"ThingComp_ExpCanister","StartAbsorption"}})Check(AccessTools.DeclaredMethod(niv.GetType("Nivarian_Race.Code.Comps.ThingComps."+pair[0],true),pair[1])!=null,"UI executor "+pair[1]);
 var capture=AccessTools.Method(cand.GetType("Meow.TaleNivarianCompatibility.Tale",true),"CapturedCaravan");int captures=0;int factionCalls=0;
 foreach(var t in tale.GetTypes()){
 var path=(FieldInfo[])capture.Invoke(null,new object[]{t,0});
 foreach(var m in t.GetMethods(AccessTools.allDeclared).Where(m=>!m.IsStatic&&m.ReturnType==typeof(void)&&m.GetParameters().Length==0&&m.Name.StartsWith("<Outcome_"))){
 var calls=PatchProcessor.GetOriginalInstructions(m).Select(i=>i.operand).OfType<MethodInfo>().ToArray();
 if(calls.Any(c=>c.DeclaringType.FullName=="RimWorld.Faction"&&c.Name=="get_OfPlayer"||c.DeclaringType.FullName=="RimWorld.PawnGroupMakerUtility"&&c.Name=="GeneratePawns"||c.DeclaringType.FullName=="Verse.PawnGenerator"&&c.Name=="GeneratePawn")){Check(path!=null,"caravan capture "+t.FullName+"."+m.Name);captures++;factionCalls+=calls.Count(c=>c.Name=="get_OfPlayer");}}
 }
 Check(captures>=40,"Tale covered callbacks="+captures+" faction reads="+factionCalls);
 var core=Assembly.LoadFrom(Path.Combine(game,"Mods/MP-meow-online-shop/1.6/Assemblies/MP_MeowOnlineShop.dll"));
 var coreValue=AccessTools.DeclaredMethod(core.GetType("MP_MeowOnlineShop.Patch_RjwP1",true),"GetDeterministicRandomValue");
 var privacySetup=AccessTools.DeclaredMethod(privacy.GetType("Privacy_Please.HarmonyPatch_JobDriver_Sex_setup_ticks",true),"Postfix");
 var stacked=PatchProcessor.GetOriginalInstructions(privacySetup).Select(i=>new CodeInstruction(i)).ToList();
 foreach(var i in stacked)if((i.operand as MethodInfo)?.DeclaringType?.FullName=="UnityEngine.Random")i.operand=coreValue;
 var preserved=((IEnumerable<CodeInstruction>)replace.Invoke(null,new object[]{stacked,privacySetup})).ToList();
 Check(preserved.Count(i=>Equals(i.operand,coreValue))==1,"existing core Privacy replacement preserved");
 var ui=cand.GetType("Meow.TaleNivarianCompatibility.NivarianUiDelta",true);
 foreach(var name in new[]{"ThingComp_ExpCanister","Comp_FlyActivator"}){
 var m=AccessTools.DeclaredMethod(niv.GetType("Nivarian_Race.Code.Comps.ThingComps."+name,true),"PostSpawnSetup");
 var generator=new DynamicMethod("test",typeof(void),Type.EmptyTypes).GetILGenerator();
 var code=PatchProcessor.GetOriginalInstructions(m,generator);
 var output=((IEnumerable<CodeInstruction>)AccessTools.Method(ui,"PreserveLoadedSetting").Invoke(null,new object[]{code,m,generator})).ToList();
 Check(output.Count(i=>(i.operand as MethodInfo)?.Name=="KeepLoadedSetting")==1,"single respawn default assignment guarded "+name);
 }
 var controls=cand.GetType("Meow.TaleNivarianCompatibility.NivarianControlSettings",true);
 var names=(string[])AccessTools.Field(controls,"Names").GetValue(null);
 var metrics=niv.GetType("Nivarian.GameComp_NivarianNiraMetrics",true);
 foreach(var name in names)Check(AccessTools.Field(metrics,name)!=null,"buffered Nira scalar "+name);
 var draw=AccessTools.DeclaredMethod(niv.GetType("Nivarian_Race.Code.UI.UplinkNiraMetricsTabPanel",true),"DrawSettings");
 Check(draw!=null&&draw.GetParameters()[3].ParameterType==metrics,"real Nira UI watch target argument");
 foreach(var name in new[]{"ToggleBatteryTarget","SelectAllBatteries","ClearBatteryTargets"})Check(AccessTools.DeclaredMethod(niv.GetType("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianEnergyTower",true),name)!=null,"energy battery UI executor "+name);
 Check(Enum.GetName(niv.GetType("Nivarian_Race.Code.Comps.ThingComps.NivarianFlyState",true),1)=="Walking","actual fly enum named state");
 Console.WriteLine("SURFACE_PASS checks="+checks+"; static actual-binary/transpiler validation only");return 0;
 }
}


