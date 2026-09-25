using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meow.TurretCombatSleep
{
    internal static class RavenAccessorCache
    {
        private static readonly AccessTools.FieldRef<Building_TurretGun, int> Warmup =
            AccessTools.FieldRefAccess<Building_TurretGun, int>("burstWarmupTicksLeft");

        internal static void Install(Harmony harmony)
        {
            var type = AccessTools.TypeByName("RavenRace.Features.DefenseHub.Harmony.Patch_DefenseHub_UniversalTurret");
            if (type == null) return;
            // All assemblies are loaded before these callbacks. This also avoids
            // colliding with the standalone module if its callback runs after ours.
            if (AccessTools.TypeByName("Meow.LatestSavePerformance.LatestSavePerformanceMod") != null)
            {
                Log.Message("[Meow.TurretCombatSleep] Raven accessor delegated to standalone LatestSavePerformance module");
                return;
            }
            try
            {
                var target = AccessTools.DeclaredMethod(type, "Postfix_TryStartShootSomething", new[] { typeof(Building_TurretGun) });
                if (target == null || !target.IsStatic || target.ReturnType != typeof(void))
                    throw new MissingMethodException(type.FullName, "Postfix_TryStartShootSomething");
                // Older optional accessor-only module may already own the exact rewrite.
                if (Harmony.GetPatchInfo(target)?.Transpilers.Any(p => p.owner == "meow.latestsaveperformance.raventurretaccessor") == true)
                    return;
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(RavenAccessorCache), nameof(Rewrite)));
                Log.Message("[Meow.TurretCombatSleep] Raven warmup accessor cached (values remain live)");
            }
            catch (Exception e) { Log.Error("[Meow.TurretCombatSleep] REQUIRED_TARGET_FAILED Raven accessor: " + e); }
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int matches = 0;
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Call || !(code[i].operand is MethodInfo method) ||
                    method.DeclaringType != typeof(AccessTools) || method.Name != "FieldRefAccess" || !method.IsGenericMethod ||
                    !method.GetGenericArguments().SequenceEqual(new[] { typeof(Building_TurretGun), typeof(int) }) ||
                    !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string) })) continue;
                if (code[i - 1].opcode != OpCodes.Ldstr || !Equals(code[i - 1].operand, "burstWarmupTicksLeft"))
                    throw new InvalidOperationException("Unexpected Raven field accessor input");
                code[i].operand = AccessTools.Method(typeof(RavenAccessorCache), nameof(GetWarmup));
                matches++;
            }
            if (matches != 1) throw new InvalidOperationException("Expected one Raven warmup factory, found " + matches);
            return code;
        }

        private static AccessTools.FieldRef<Building_TurretGun, int> GetWarmup(string fieldName) => Warmup;
    }
}
