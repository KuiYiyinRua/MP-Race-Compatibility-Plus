using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    // YaOpt 1.1.4 assumes Kingfisher's ThingRequestGroup is local 6. In
    // Kingfisher 0.7.4 it is local 4; local 6 is YaOpt's newly declared indexer.
    // Repair the emitted argument, preserving YaOpt's paired Add/Remove index.
    internal static class Patch_YaOptKingfisherRemove
    {
        internal static MethodInfo Target;

        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("YaOpt.Patches.Verse_ListerThings_Remove");
            var kingfisher = AccessTools.TypeByName("Kingfisher.Features.ListerThingsRewrite");
            if (type == null || kingfisher == null) return;
            Target = AccessTools.Method(kingfisher, "Remove", new[] { typeof(ListerThings), typeof(Thing) });
            var transpiler = AccessTools.Method(type, "Transpiler");
            if (Target == null || transpiler == null)
                throw new InvalidOperationException("YaOpt/Kingfisher Remove boundary not found");
            // PurePatcher 1.8 copies the replacement body into Verse. Patching
            // the annotated source then repairs a method the game never calls.
            var entry = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Remove));
            if (PatchProcessor.GetOriginalInstructions(entry).Any(i =>
                i.operand is MethodInfo m && m.DeclaringType == kingfisher && m.Name == "RemoveFromTail"))
                Target = entry;
            ResolveGroupLocal(Target);
            harmony.Patch(AccessTools.Method(type, "TargetMethod"),
                postfix: new HarmonyMethod(typeof(Patch_YaOptKingfisherRemove), nameof(ResolveTarget)));
            harmony.Patch(transpiler, postfix: new HarmonyMethod(typeof(Patch_YaOptKingfisherRemove), nameof(Repair)));
            // The normal late YaOpt installer invokes this repaired transpiler.
            // If it already attempted installation, rebuild the registered wrapper.
            var registered = Harmony.GetPatchInfo(Target)?.Transpilers.FirstOrDefault(p => p.PatchMethod == transpiler);
            if (registered != null)
            {
                harmony.Unpatch(Target, transpiler);
                new Harmony(registered.owner).Patch(Target, transpiler: new HarmonyMethod(transpiler));
            }
            Log.Message("[MP-MeowOnlineShop] YaOpt/Kingfisher Remove compatibility active: target=" + Target.DeclaringType.FullName +
                "; ThingRequestGroup local=" + ResolveGroupLocal(Target) + ".");
        }

        private static void ResolveTarget(ref MethodBase __result) { __result = Target; }

        internal static int ResolveGroupLocal(MethodBase target)
        {
            var locals = target.GetMethodBody()?.LocalVariables
                .Where(l => l.LocalType == typeof(ThingRequestGroup)).ToList();
            if (locals == null || locals.Count != 1)
                throw new InvalidOperationException("Expected one Kingfisher ThingRequestGroup local");
            return locals[0].LocalIndex;
        }

        private static void Repair(ref IEnumerable<CodeInstruction> __result)
        {
            __result = RepairInstructions(__result, ResolveGroupLocal(Target));
        }

        internal static IEnumerable<CodeInstruction> RepairInstructions(IEnumerable<CodeInstruction> source, int groupLocal)
        {
            var instructions = source.ToList();
            int calls = 0;
            for (int i = 1; i < instructions.Count; i++)
            {
                if (!(instructions[i].operand is MethodInfo method) ||
                    method.DeclaringType?.FullName != "YaOpt.Helpers.ListerThingsHelper" ||
                    method.Name != "RemoveFromThingList") continue;
                calls++;
                // First call removes the by-def entry (constant group 82).
                // Second removes each group entry and must load the enum local.
                if (calls != 2) continue;
                var argument = instructions[i - 1];
                if (!argument.IsLdloc())
                    throw new InvalidOperationException("Unexpected YaOpt group argument; refusing ambiguous rewrite");
                var load = CodeInstruction.LoadLocal(groupLocal);
                argument.opcode = load.opcode;
                argument.operand = load.operand;
            }
            if (calls != 2)
                throw new InvalidOperationException("Expected two YaOpt indexed Thing-list removal calls");
            return instructions;
        }
    }
}
