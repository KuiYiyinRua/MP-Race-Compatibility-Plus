using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MP_MeowOnlineShop;
using Verse;

class Program
{
    static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string game = Path.GetFullPath(Path.Combine(root, "../.."));
        var paths = new[] { Path.Combine(game, "RimWorldWin64_Data/Managed"),
            Path.Combine(root,"../3718308218/1.6/Assemblies"), Path.Combine(root,"../3711543433/Assemblies/net48"),
            Path.Combine(root,"../3755504957/Assemblies/net48") };
        AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string path = paths.Select(p=>Path.Combine(p,name)).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try { Run(paths); return 0; } catch(Exception e) { Console.WriteLine(e); return 1; }
    }

    static void Check(bool ok, string message) { if(!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static void Run(string[] paths)
    {
        var ya = Assembly.LoadFrom(Path.Combine(paths[1], "YaOpt.dll"));
        var king = Assembly.LoadFrom(Path.Combine(paths[2], "Kingfisher.dll"));
        var global = ya.GetType("YaOpt.YaOptGlobal", true);
        ((IDictionary)AccessTools.Field(global,"_modLookup").GetValue(null))["Vortex.Kingfisher"] = true;
        var target = AccessTools.Method(king.GetType("Kingfisher.Features.ListerThingsRewrite"), "Remove");
        Patch_YaOptKingfisherRemove.Target = target;
        int local = Patch_YaOptKingfisherRemove.ResolveGroupLocal(target);
        Check(local == 4, "installed Kingfisher group local is 4");
        var transpiler = AccessTools.Method(ya.GetType("YaOpt.Patches.Verse_ListerThings_Remove"), "Transpiler");
        var dm = new DynamicMethod("probe", typeof(void), new[] { typeof(ListerThings), typeof(Thing) });
        var gen = dm.GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(target, gen);
        var broken = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[]{original,gen})).ToList();
        int index = broken.FindLastIndex(i => (i.operand as MethodInfo)?.Name == "RemoveFromThingList");
        Check(broken[index-1].IsLdloc() && Convert.ToInt32(broken[index-1].operand) == 6, "actual YaOpt emits invalid local 6");
        var label = gen.DefineLabel(); broken[index-1].labels.Add(label);
        var fixedIl = Patch_YaOptKingfisherRemove.RepairInstructions(broken,local).ToList();
        Check(fixedIl[index-1].IsLdloc() && Convert.ToInt32(fixedIl[index-1].operand) == 4, "repair emits group local 4");
        Check(fixedIl[index-1].labels.Contains(label), "labels preserved");
        Check(fixedIl.Count == broken.Count, "instruction count preserved");
        bool rejected=false;
        try { Patch_YaOptKingfisherRemove.RepairInstructions(new List<CodeInstruction>(),local).ToList(); } catch(InvalidOperationException){rejected=true;}
        Check(rejected,"unknown call shape rejected");
        var h = new Harmony("test.yaopt.group-local");
        bool failedBefore = false;
        try { h.Patch(target,transpiler:new HarmonyMethod(transpiler)); }
        catch(Exception e) { failedBefore = true; Console.WriteLine("EXPECTED ORIGINAL FAILURE " + e.GetType().Name); }
        h.Unpatch(target,transpiler);
        Console.WriteLine("INFO desktop CLR rejects original wrapper=" + failedBefore + "; Mono startup validation remains required");
        h.Patch(transpiler,postfix:new HarmonyMethod(typeof(Patch_YaOptKingfisherRemove),"Repair"));
        var actual = h.Patch(target,transpiler:new HarmonyMethod(transpiler));
        Check(actual != null, "actual installed removal wrapper compiles with repaired transpiler");
        h.UnpatchAll(h.Id);
    }
}
