using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RjwInfrastructure
{
    // Only the numeric utility's entropy source changes. Its callers, arithmetic
    // and parameters remain intact. Single-player keeps the original System.Random.
    internal static class DeterministicRandomUtility
    {
        internal static void Apply(Harmony harmony)
        {
            var type=AccessTools.TypeByName("RJWSexperience.Utility");
            if(type==null)return;
            var method=AccessTools.DeclaredMethod(type,"RandGaussianLike",new[]{typeof(float),typeof(float),typeof(int)});
            if(method==null||!method.IsStatic||method.ReturnType!=typeof(float))
                throw new MissingMethodException("INFRA REQUIRED_TARGET_FAILURE: numeric RNG utility signature changed");
            harmony.Patch(method,transpiler:new HarmonyMethod(typeof(DeterministicRandomUtility),nameof(Transpile)));
            Log.Message("[Meow.RjwInfrastructure] Numeric random utility now uses simulation Rand in multiplayer.");
        }
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var list=instructions.ToList();
            var target=AccessTools.Method(typeof(Random),nameof(Random.NextDouble),Type.EmptyTypes);
            var calls=list.Where(i=>i.Calls(target)).ToList();
            if(calls.Count!=1)throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: numeric RNG utility call shape changed");
            calls[0].opcode=System.Reflection.Emit.OpCodes.Call;
            calls[0].operand=AccessTools.Method(typeof(DeterministicRandomUtility),nameof(NextDouble));
            return list;
        }
        internal static double NextDouble(Random original) => MP.IsInMultiplayer ? (double)Rand.Value : original.NextDouble();
    }
}
