using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianPlantMaturity
    {
        static MethodInfo rendererMature;
        static FieldInfo matureGrowth;
        internal static void Apply(Harmony harmony)
        {
            const string ns = "Nivarian_Race.Code.Comps.ThingComps.";
            var renderer = AccessTools.TypeByName(ns + "Comp_PlantRenderer") ?? throw new TypeLoadException("Nivarian plant renderer");
            rendererMature = AccessTools.PropertyGetter(renderer, "IsMature") ?? throw new MissingMethodException(renderer.FullName, "get_IsMature");
            matureGrowth = AccessTools.Field(AccessTools.TypeByName(ns + "CompProperties_PlantRenderer"), "matureGrowth") ?? throw new MissingFieldException("matureGrowth");
            var bloom = AccessTools.TypeByName(ns + "Comp_MoonBloom") ?? throw new TypeLoadException("Nivarian moon bloom");
            // This custom god-mode gizmo writes Plant.Growth directly; it does not
            // pass through Multiplayer's vanilla developer-menu synchronization.
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(bloom, "<CompGetGizmosExtra>b__14_0", Type.EmptyTypes)
                ?? throw new MissingMethodException(bloom.FullName, "mature gizmo callback")).SetDebugOnly();
            harmony.Patch(AccessTools.DeclaredMethod(bloom, "UpdateMaturitySignal", Type.EmptyTypes)
                ?? throw new MissingMethodException(bloom.FullName, "UpdateMaturitySignal"),
                transpiler: new HarmonyMethod(typeof(NivarianPlantMaturity), nameof(ReplaceRead)));
            Log.Message("[TaleNivarianCompat] Moon-bloom maturity signals use actual plant growth, independent of rendering cache.");
        }
        static bool IsMature(ThingComp renderer)
        {
            if (!MP.IsInMultiplayer) return (bool)rendererMature.Invoke(renderer, null);
            return renderer.parent is Plant plant && plant.Growth >= (float)matureGrowth.GetValue(renderer.props);
        }
        static IEnumerable<CodeInstruction> ReplaceRead(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(rendererMature))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianPlantMaturity), nameof(IsMature));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Moon-bloom renderer maturity read count changed: " + count);
        }
    }
}
