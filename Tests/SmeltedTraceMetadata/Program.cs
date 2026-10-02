using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {foreach(var t in types){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    static string Body(MethodDefinition m)
    {
        if(!m.HasBody)return "no-body";
        string Operand(object x)=>x switch
        {
            Instruction i=>"@"+m.Body.Instructions.IndexOf(i),
            Instruction[] a=>string.Join(",",a.Select(i=>"@"+m.Body.Instructions.IndexOf(i))),
            _=>x?.ToString()??""
        };
        return string.Join(";",m.Body.Variables.Select(v=>v.VariableType.FullName))+"|"+
            string.Join(";",m.Body.Instructions.Select(i=>i.OpCode+" "+Operand(i.Operand)))+"|"+
            string.Join(";",m.Body.ExceptionHandlers.Select(h=>h.HandlerType+":"+Operand(h.TryStart)+":"+Operand(h.TryEnd)+":"+Operand(h.HandlerStart)+":"+Operand(h.HandlerEnd)+":"+h.CatchType));
    }
    static int Main(string[] args)
    {
        using var old=AssemblyDefinition.ReadAssembly(args[0]);using var candidate=AssemblyDefinition.ReadAssembly(args[1]);using var native=AssemblyDefinition.ReadAssembly(args[2]);
        var oldMethods=Types(old.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Body);
        var nextMethods=Types(candidate.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Body);
        var changed=oldMethods.Where(x=>!nextMethods.TryGetValue(x.Key,out var n)||n!=x.Value).Select(x=>x.Key).ToArray();
        var added=nextMethods.Keys.Except(oldMethods.Keys).ToArray();
        Check(changed.Length==1&&changed[0].Contains("Patch_SmeltedLoongMp::Apply"),"Unexpected existing method changes count="+changed.Length+" first="+string.Join("\n",changed.Take(5)));
        Check(added.Length>=3&&added.All(x=>x.Contains("Patch_SmeltedLoongMp::")||x.Contains("MilianGenerationMapContext")),"Unexpected new methods");
        var type=Types(candidate.MainModule.Types).Single(t=>t.FullName=="MP_MeowOnlineShop.Patch_SmeltedLoongMp");
        var prefix=type.Methods.Single(m=>m.Name=="SmartTracePrefix");var finish=type.Methods.Single(m=>m.Name=="SmartTraceFinalizer");
        Check(prefix.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Verse.Rand"&&m.Name=="PushState")==1,"Missing stable seed scope");
        Check(finish.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Verse.Rand"&&m.Name=="PopState")==1,"Missing exception-safe restore");
        Check(!prefix.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_IsExecutingSyncCommand"),"Sync context exemption");
        var bullet=native.MainModule.Types.Single(t=>t.FullName=="SmeltedLoong.Bullet_SmartTrace");
        var init=bullet.Methods.Single(m=>m.Name=="InitRandOffset");
        Check(init.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Verse.Rand"&&m.Name=="Range")==2,"Native random boundary changed");
        Check(!bullet.Methods.Any(m=>m.Name=="ExposeData"),"Native cache persistence assumption changed");
        var references=bullet.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is MethodReference mr&&mr.Name=="BPos")).Select(m=>m.Name).OrderBy(x=>x).ToArray();
        Check(references.SequenceEqual(new[]{"DrawAt","Tick"}),"Curve used outside verified cosmetic call sites");
        Check(native.MainModule.Mvid.ToString()=="f68209ed-999f-47f1-9ffe-544fa98f2101","Desync native MVID mismatch");
        var result=new{status="PASS",unchangedMethods=oldMethods.Count-changed.Length,changed,added,candidateVersion=candidate.Name.Version.ToString(),candidateMvid=candidate.MainModule.Mvid.ToString(),nativeMvid=native.MainModule.Mvid.ToString(),candidateSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))};
        File.WriteAllText(args[3],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result));return 0;
    }
}
