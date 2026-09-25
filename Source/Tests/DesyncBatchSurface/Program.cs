using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

class Program
{
    static int count;
    static void Check(bool valid,string label){if(!valid)throw new Exception("FAIL "+label);Console.WriteLine("PASS "+label);count++;}
    static List<CodeInstruction> IL(MethodBase m)=>PatchProcessor.GetOriginalInstructions(m).ToList();
    static bool Calls(List<CodeInstruction> code,string type,string name)=>code.Any(i=>i.operand is MethodBase m&&m.DeclaringType.FullName==type&&m.Name==name);
    static void Main(string[] args)
    {
        if(args.Length!=2)throw new ArgumentException("game root, candidate DLL");
        string game=Path.GetFullPath(args[0]), candidatePath=Path.GetFullPath(args[1]);
        string mods=Path.Combine(game,"Mods");
        var folders=new[]{Path.Combine(game,"RimWorldWin64_Data/Managed"),Path.Combine(mods,"Multiplayer/1.6/Assemblies"),
            Path.Combine(mods,"Multiplayer/1.6/AssembliesCustom"),Path.Combine(mods,"1612312286/1.6/Assemblies"),Path.GetDirectoryName(candidatePath)};
        AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{string n=new AssemblyName(e.Name).Name+".dll";string p=folders.Select(f=>Path.Combine(f,n)).FirstOrDefault(File.Exists);return p==null?null:Assembly.LoadFrom(p);};
        var rw=Assembly.LoadFrom(Path.Combine(folders[0],"Assembly-CSharp.dll"));
        var niv=Assembly.LoadFrom(Path.Combine(mods,"3624805128/1.6/Assemblies/Nivarian_Race.dll"));
        var al=Assembly.LoadFrom(Path.Combine(mods,"3665997350/1.6/Assemblies/AriandelLibrary.dll"));
        var elite=Assembly.LoadFrom(Path.Combine(mods,"3492954974/1.6/Assemblies/RimwoldEliteRaidProject.dll"));
        var nine=Assembly.LoadFrom(Path.Combine(mods,"1612312286/1.6/Assemblies/IncidentWorker_Ninetail.dll"));
        var mp=Assembly.LoadFrom(Path.Combine(folders[2],"Multiplayer.dll"));
        var candidate=Assembly.LoadFrom(candidatePath);
        foreach(var asm in new[]{rw,niv,al,elite,nine,mp,candidate})Console.WriteLine("MVID "+asm.GetName().Name+" "+asm.ManifestModule.ModuleVersionId);
        Type Patch(string name)=>candidate.GetType("Meow.DesyncBatchCompatibility."+name,true);
        var tree=niv.GetType("Nivarian_Race.Code.Comps.ThingComps.Comp_FruitTree",true);
        var renderer=niv.GetType("Nivarian_Race.Code.Comps.ThingComps.Comp_PlantRenderer",true);
        var rendererProps=niv.GetType("Nivarian_Race.Code.Comps.ThingComps.CompProperties_PlantRenderer",true);
        var maturity=AccessTools.PropertyGetter(tree,"IsMature");
        Check(maturity.ReturnType==typeof(bool)&&Calls(IL(maturity),renderer.FullName,"get_IsMature"),"original fruit maturity depends on renderer cache");
        Check(Calls(IL(AccessTools.Method(renderer,"UpdateRenderCache")),"Verse.UnityData","get_IsInMainThread"),"render cache update gated by main thread");
        Check(AccessTools.Field(rendererProps,"matureGrowth").FieldType==typeof(float),"actual configured maturity threshold");
        var fruitTick=IL(AccessTools.Method(tree,"CompTick"));
        Check(Calls(fruitTick,tree.FullName,"get_IsMature")&&Calls(fruitTick,tree.FullName,"RandomScatterOffset")&&Calls(fruitTick,tree.FullName,"SpawnFruit"),"fruit maturity controls random draws and actual spawning");
        Check(Calls(IL(AccessTools.Method(tree,"PostExposeData")),"Verse.Scribe_Collections","Look"),"fruit slots already serialized; no speculative replacement");
        foreach(var name in new[]{"Visual_Lightning_Red","Visual_Lightning_Gold","Visual_Lightning_Purple"})
        {
            var target=AccessTools.DeclaredMethod(al.GetType("AriandelLibrary."+name,true),"ThrowLightningGlow");
            Check(target.ReturnType==typeof(void)&&string.Join(",",target.GetParameters().Select(p=>p.ParameterType.FullName))=="UnityEngine.Vector3,Verse.Map,System.Single","exact lightning signature "+name);
            var code=IL(target);
            Check(Calls(code,"Verse.GenView","ShouldSpawnMotesAt")&&code.Count(i=>i.operand is MethodInfo m&&m.DeclaringType.FullName=="Verse.Rand")==5,"visibility gates five random draws "+name);
            Check(!code.Any(i=>i.operand is MethodInfo m&&(m.Name=="TakeDamage"||m.Name=="MakeThing"||m.Name=="GetNextID")),"lightning scope has no damage or Thing IDs "+name);
        }
        var skin=rw.GetType("RimWorld.Pawn_StoryTracker",true);
        Check(AccessTools.Field(skin,"skinColorBase").FieldType.FullName.Contains("Nullable")&&AccessTools.Field(skin,"pawn").FieldType.FullName=="Verse.Pawn","skin injected fields");
        var skinCode=IL(AccessTools.PropertyGetter(skin,"SkinColorBase"));
        Check(Calls(skinCode,"RimWorld.PawnSkinColors","RandomSkinColorGene")&&Calls(skinCode,"RimWorld.Pawn_GeneTracker","AddGene"),"original skin getter mutates genes as well as RNG");
        var skinPatch=IL(AccessTools.Method(Patch("LazySimulationCaches"),"SkinColor"));
        Check(!Calls(skinPatch,"RimWorld.Pawn_GeneTracker","AddGene")&&!skinPatch.Any(i=>i.opcode==OpCodes.Stfld),"candidate skin fallback has no persistent writes");
        foreach(var name in new[]{"SkinColor","FallbackSkinGene"})
            Check(!IL(AccessTools.Method(Patch("LazySimulationCaches"),name)).Any(i=>i.operand is MethodInfo m&&(m.DeclaringType.FullName=="Verse.Rand"||m.DeclaringType.FullName=="RimWorld.PawnSkinColors")),"skin visual worker never touches global RNG or lazy list "+name);
        var hediff=rw.GetType("Verse.Hediff",true);
        Check(AccessTools.Field(hediff,"abilities").FieldType==AccessTools.PropertyGetter(hediff,"AllAbilitiesForReading").ReturnType,"ability cache field matches getter");
        Check(Calls(IL(AccessTools.PropertyGetter(hediff,"AllAbilitiesForReading")),"RimWorld.AbilityUtility","MakeAbility"),"actual lazy hediff getter creates abilities");
        Check(Calls(IL(AccessTools.Method(rw.GetType("RimWorld.Ability",true),"Initialize")),"RimWorld.UniqueIDsManager","GetNextAbilityID"),"actual ability initialization allocates shared ID");
        var power=elite.GetType("EliteRaid.CR_Powerup",true);
        foreach(var name in new[]{"tickingMap","executingCmdMap"})
        {
            var field=AccessTools.Field(mp.GetType("Multiplayer.Client.AsyncTimeComp",true),name);
            Check(field!=null&&field.IsStatic&&field.FieldType==rw.GetType("Verse.Map",true),"MP shared simulation map "+name);
        }
        var expose=AccessTools.Method(Patch("EliteState"),"Expose");
        foreach(var p in expose.GetParameters())Check(AccessTools.Field(power,p.Name.Substring(3)).FieldType.MakeByRefType()==p.ParameterType,"elite injected state "+p.Name);
        Check(!IL(expose).Any(i=>i.operand is string key && key=="meowEliteGameTick"),"elite load-reconstruction sentinel is not serialized");
        var transpiler=AccessTools.Method(Patch("EliteState"),"Context");
        foreach(string name in new[]{"PostMake","Tick"})
        {
            var original=AccessTools.DeclaredMethod(power,name,Type.EmptyTypes);var code=IL(original);
            var rewritten=((IEnumerable<CodeInstruction>)transpiler.Invoke(null,new object[]{code,original})).ToList();
            Check(!Calls(rewritten,"RimWorld.Faction","get_OfPlayer")&&Calls(rewritten,Patch("EliteState").FullName,"TargetFaction"),"exact elite faction rewrite "+name);
            Check(!Calls(rewritten,"RimWorld.Faction","get_IsPlayer"),"elite no contextual IsPlayer "+name);
            if(name=="Tick")Check(Calls(rewritten,power.FullName,"RemoveThis")&&Calls(rewritten,power.FullName,"UpdatePawnInfo"),"elite reward and update calls retained");
        }
        var emptyCode=new List<CodeInstruction>();bool rejected=false;
        try{((IEnumerable<CodeInstruction>)transpiler.Invoke(null,new object[]{emptyCode,AccessTools.Method(power,"Tick")})).ToList();}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"elite changed IL fails closed");
        // Check labels are moved to the inserted ldarg.0, not the consuming call.
        var ilgen=new DynamicMethod("label",typeof(void),Type.EmptyTypes).GetILGenerator();var label=ilgen.DefineLabel();
        var getter=AccessTools.PropertyGetter(rw.GetType("RimWorld.Faction",true),"OfPlayer");
        var call=new CodeInstruction(OpCodes.Call,getter);call.labels.Add(label);
        var labelled=((IEnumerable<CodeInstruction>)transpiler.Invoke(null,new object[]{new[]{call},AccessTools.Method(power,"PostMake")})).ToList();
        Check(labelled[0].opcode==OpCodes.Ldarg_0&&labelled[0].labels.Contains(label)&&labelled[1].labels.Count==0,"elite branch target preserves argument stack");
        var persistent=mp.GetType("Multiplayer.Client.PersistentDialog",true);
        Check(Calls(IL(AccessTools.Method(persistent,"Click")),"Verse.DiaOption","Activate"),"MP already replays dialog choice; no duplicate sync added");
        Check(nine.GetType("Ninetail.ChoiceLetter_AcceptKyulen",true).GetMethods(AccessTools.allDeclared).Any(m=>m.Name=="<get_Choices>b__5_0"),"historical Kyulen callback matches installed binary");
        Check(!candidate.GetReferencedAssemblies().Any(a=>a.Name=="Multiplayer"),"candidate avoids early internal MP type loading");
        Console.WriteLine("INSTALLED_SURFACE_PASS checks="+count+"; no game methods or game processes executed");
    }
}
