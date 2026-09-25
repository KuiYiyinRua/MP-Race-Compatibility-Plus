using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meow.LatestSavePerformance
{
    public sealed class LatestSavePerformanceMod : Mod
    {
        public LatestSavePerformanceMod(ModContentPack content) : base(content)
        {
            if (!ModsConfig.IsActive("rwmt.Multiplayer") || !ModsConfig.IsActive("ZuoYao.RavenRace")) return;
            LongEventHandler.ExecuteWhenFinished(RavenTurretAccessor.Install);
        }
    }

    // Only the field accessor is cached, never the field value or a game object.
    // The original Raven postfix retains all branches, ordering and writes.
    public static class RavenTurretAccessor
    {
        private const string TargetType = "RavenRace.Features.DefenseHub.Harmony.Patch_DefenseHub_UniversalTurret";
        private static readonly AccessTools.FieldRef<Building_TurretGun, int> Warmup =
            AccessTools.FieldRefAccess<Building_TurretGun, int>("burstWarmupTicksLeft");

        // Test-only drivers can select the original factory for identical-DLL A/B.
        // Normal play always uses the cached accessor. Both read current state.
        public static bool UseCachedAccessor = true;
        public static long CachedCalls;
        public static long FactoryCalls;

        public static void Install()
        {
            try
            {
                var target = AccessTools.DeclaredMethod(AccessTools.TypeByName(TargetType),
                    "Postfix_TryStartShootSomething", new[] { typeof(Building_TurretGun) });
                if (target == null || !target.IsStatic || target.ReturnType != typeof(void))
                    throw new MissingMethodException(TargetType, "Postfix_TryStartShootSomething");
                new Harmony("meow.latestsaveperformance.raventurretaccessor").Patch(target,
                    transpiler: new HarmonyMethod(typeof(RavenTurretAccessor), nameof(Rewrite)));
                Log.Message("[Meow.LatestSavePerformance] READY Raven warmup accessor; version=1.0.0 mvid=" +
                    typeof(RavenTurretAccessor).Assembly.ManifestModule.ModuleVersionId);
            }
            catch (Exception e)
            {
                Log.Error("[Meow.LatestSavePerformance] REQUIRED_TARGET_FAILED " + e);
            }
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        {
            var code = source.ToList();
            var replacement = AccessTools.DeclaredMethod(typeof(RavenTurretAccessor), nameof(GetAccessor));
            int count = 0;
            for (int i = 1; i < code.Count; i++)
            {
                var method = code[i].operand as MethodInfo;
                if (code[i].opcode != OpCodes.Call || method == null || method.DeclaringType != typeof(AccessTools) ||
                    method.Name != "FieldRefAccess" || !method.IsGenericMethod ||
                    !method.GetGenericArguments().SequenceEqual(new[] { typeof(Building_TurretGun), typeof(int) }) ||
                    !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string) })) continue;
                if (code[i - 1].opcode != OpCodes.Ldstr || !Equals(code[i - 1].operand, "burstWarmupTicksLeft"))
                    throw new InvalidOperationException("Unexpected Raven field accessor input; original method retained");
                code[i].operand = replacement;
                count++;
            }
            if (count != 1) throw new InvalidOperationException("Expected one Raven warmup field factory, found " + count);
            return code;
        }

        public static AccessTools.FieldRef<Building_TurretGun, int> GetAccessor(string fieldName)
        {
            if (UseCachedAccessor)
            {
                CachedCalls++;
                return Warmup;
            }
            FactoryCalls++;
            return AccessTools.FieldRefAccess<Building_TurretGun, int>(fieldName);
        }
    }
}
