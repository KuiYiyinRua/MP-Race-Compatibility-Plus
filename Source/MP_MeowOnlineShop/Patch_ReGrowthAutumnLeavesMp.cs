using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_ReGrowthAutumnLeavesMp
    {
        private static FieldInfo renderedFallFactor;

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || !ModsConfig.IsActive("regrowth.botr.core")) return;
            try
            {
                Type spawner = AccessTools.TypeByName("ReGrowthCore.CompAutumnLeavesSpawner");
                MethodInfo target = AccessTools.DeclaredMethod(spawner, "CheckShouldSpawn", Type.EmptyTypes);
                renderedFallFactor = AccessTools.Field(
                    AccessTools.TypeByName("ReGrowthCore.PlantFallColors_GetFallColorFactor_Patch"), "fallColorFactor");
                if (target == null || renderedFallFactor == null || renderedFallFactor.FieldType != typeof(float) ||
                    !renderedFallFactor.IsStatic || !typeof(ThingComp).IsAssignableFrom(spawner))
                    throw new InvalidOperationException("ReGrowth autumn leaf spawner signature changed");
                if (PatchProcessor.GetOriginalInstructions(target).Count(IsRenderedFactorRead) != 1)
                    throw new InvalidOperationException("Expected exactly one ReGrowth rendered fall-factor read");
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Patch_ReGrowthAutumnLeavesMp), nameof(UseTreeMapSeason)));
                Log.Message("[MP-MeowOnlineShop][ReGrowth] Autumn leaves use their tree's map latitude and date in multiplayer; rendered-map cache no longer controls spawning.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][ReGrowth] REQUIRED TARGET FAILED autumn leaves: " + e);
            }
        }

        private static bool IsRenderedFactorRead(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, renderedFallFactor);

        private static IEnumerable<CodeInstruction> UseTreeMapSeason(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> body = instructions.ToList();
            if (body.Count(IsRenderedFactorRead) != 1)
                throw new InvalidOperationException("ReGrowth autumn leaf IL changed after patch ordering");
            foreach (CodeInstruction instruction in body)
            {
                if (!IsRenderedFactorRead(instruction)) { yield return instruction; continue; }
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.DeclaredMethod(typeof(Patch_ReGrowthAutumnLeavesMp), nameof(FallFactorForTree)));
            }
        }

        private static float FallFactorForTree(ThingComp comp, float renderedFactor)
        {
            Map map = comp.parent.Map;
            if (!MP.IsInMultiplayer || map == null) return renderedFactor;
            // AsyncTimeComp establishes this map's clock before its plants tick.
            // The original renderer uses the same vanilla latitude/date formula,
            // but its process-wide last result depends on local camera/render timing.
            return PlantFallColors.GetFallColorFactor(Find.WorldGrid.LongLatOf(map.Tile).y,
                GenLocalDate.DayOfYear(map));
        }
    }
}
