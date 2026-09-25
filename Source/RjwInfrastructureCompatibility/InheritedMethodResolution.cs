using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Prepatcher;

namespace Meow.RjwInfrastructure
{
    public static class InheritedMethodResolution
    {
        [FreePatchAll]
        public static bool CanonicalizeScenarioTargets(ModuleDefinition module)
        {
            if (module.Assembly.Name.Name != "MP_MeowOnlineShop") return false;
            var type=module.GetType("MP_MeowOnlineShop.Patch_MultifactionScenarioContext");
            if (type==null) return false;
            var apply=type.Methods.Single(m=>m.Name=="Apply");
            if(apply.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.FullName==typeof(InheritedMethodResolution).FullName)) return false;
            var calls=apply.Body.Instructions.Where(i=>i.Operand is MethodReference m && m.Name=="Add" && m.DeclaringType.FullName=="System.Collections.Generic.HashSet`1<System.Reflection.MethodInfo>").ToList();
            if(calls.Count!=1) throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: scenario target set shape changed");
            apply.Body.GetILProcessor().InsertBefore(calls[0],Instruction.Create(OpCodes.Call,module.ImportReference(typeof(InheritedMethodResolution).GetMethod(nameof(CanonicalMethod)))));
            Console.WriteLine("[Meow.RjwInfrastructure] Inherited scenario patch methods canonicalized to their declaring type.");
            return true;
        }
        public static MethodInfo CanonicalMethod(MethodInfo method)
        {
            if(method==null)throw new ArgumentNullException(nameof(method));
            return method.DeclaringType.GetMethod(method.Name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly,
                null,method.GetParameters().Select(p=>p.ParameterType).ToArray(),null)
                ?? throw new MissingMethodException(method.DeclaringType.FullName,method.Name);
        }
    }
}
