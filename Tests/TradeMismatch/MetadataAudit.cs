using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

class MetadataAudit {
 static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots){foreach(var t in roots){yield return t;foreach(var child in Types(t.NestedTypes))yield return child;}}
 static string Operand(object value)=>value is Instruction i?"IL_"+i.Offset:value is Instruction[] a?string.Join(",",a.Select(x=>x.Offset)):value is MemberReference m?m.FullName:value?.ToString()??"";
 static Dictionary<string,string> Read(string path){using var asm=AssemblyDefinition.ReadAssembly(path);var result=new Dictionary<string,string>();foreach(var t in Types(asm.MainModule.Types)){
  if(t.FullName.StartsWith("MP_MeowOnlineShop.Patch_TradeExecutionSnapshot",StringComparison.Ordinal))continue;
  result.Add("type:"+t.FullName,t.Attributes+"|"+t.BaseType?.FullName+"|"+string.Join(";",t.Fields.Select(f=>f.FullName+":"+f.Attributes+":"+f.Constant)));
  foreach(var m in t.Methods){var body=m.Attributes+"|"+m.ImplAttributes;if(m.HasBody)body+="|"+m.Body.InitLocals+"|"+string.Join(";",m.Body.Variables.Select(v=>v.VariableType.FullName))+"|"+string.Join(";",m.Body.Instructions.Select(i=>i.OpCode+":"+Operand(i.Operand)))+"|"+string.Join(";",m.Body.ExceptionHandlers.Select(h=>h.HandlerType+":"+h.TryStart?.Offset+":"+h.TryEnd?.Offset+":"+h.HandlerStart?.Offset+":"+h.HandlerEnd?.Offset+":"+h.CatchType?.FullName));result.Add(m.FullName,body);}
 }return result;}
 static int Main(string[] args){var before=Read(args[0]);var after=Read(args[1]);var changes=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var a)||!after.TryGetValue(k,out var b)||a!=b).ToArray();var output=JsonSerializer.Serialize(new{scope="All core types and method bodies outside Patch_TradeExecutionSnapshot; assembly version attributes intentionally excluded",comparedEntries=before.Count,changedOutsideTarget=changes},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(args[2],output);Console.WriteLine("entries="+before.Count+" changes="+changes.Length);foreach(var key in changes.Take(3)){Console.WriteLine(key);Console.WriteLine("BEFORE "+(before.TryGetValue(key,out var x)?x:"missing"));Console.WriteLine("AFTER "+(after.TryGetValue(key,out var y)?y:"missing"));}return changes.Length==0?0:1;}
}
