using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350GenesToilLoadMp
    {
        private static bool installed;
        private static MethodInfo addHediff;
        internal static void Apply(Harmony harmony)
        {
            if (installed || !MP.enabled || !ModsConfig.IsActive("vegapnk.rjw.genes")) return;
            try
            {
                Type type = AccessTools.TypeByName("RJW_Genes.JobDriver_SexOnSpot") ?? throw new TypeLoadException("RJW_Genes.JobDriver_SexOnSpot");
                MethodInfo factory = AccessTools.DeclaredMethod(type, "MakeNewToils", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "MakeNewToils");
                Type iterator = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType ?? throw new InvalidOperationException("Genes toil iterator metadata missing");
                MethodInfo moveNext = AccessTools.DeclaredMethod(iterator, "MoveNext", Type.EmptyTypes) ?? throw new MissingMethodException(iterator.FullName, "MoveNext");
                addHediff = AccessTools.DeclaredMethod(typeof(Pawn_HealthTracker), "AddHediff", new[] { typeof(HediffDef), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) });
                if (addHediff == null || PatchProcessor.GetOriginalInstructions(moveNext).Count(i => Equals(i.operand, addHediff)) != 1)
                    throw new InvalidOperationException("Expected one Genes toil AddHediff call");
                harmony.Patch(moveNext, transpiler: new HarmonyMethod(typeof(Patch_Light350GenesToilLoadMp), nameof(ReplaceAdd)));
                installed = true;
                Log.Message("[MP-MeowOnlineShop][Light350-B12] Genes: SexOnSpot toil rebuild no longer reapplies submitting hediff.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][Light350-B12] REQUIRED TARGET FAILED Genes toil load: " + e);
            }
        }

        private static IEnumerable<CodeInstruction> ReplaceAdd(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> body = instructions.ToList();
            if (body.Count(i => Equals(i.operand, addHediff)) != 1) throw new InvalidOperationException("Genes AddHediff IL changed");
            foreach (CodeInstruction instruction in body)
            {
                if (Equals(instruction.operand, addHediff))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Light350GenesToilLoadMp), nameof(AddDuringNewJob));
                }
                yield return instruction;
            }
        }

        private static Hediff AddDuringNewJob(Pawn_HealthTracker health, HediffDef def, BodyPartRecord part, DamageInfo? damage, DamageWorker.DamageResult result)
        {
            // The exact original call's return value is discarded. During load,
            // keep the existing hediff and its remaining duration untouched.
            if (MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.PostLoadInit) return null;
            return health.AddHediff(def, part, damage, result);
        }
    }
}
