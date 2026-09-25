using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class LoggedBoundaryFailures
    {
        internal static void ApplyBallz(Harmony harmony)
        {
            var target = Bootstrap.Method(Bootstrap.Type("Ballz.Patch_PreventGonadDestruction"),
                "Prefix", typeof(Hediff_MissingPart), typeof(DamageInfo?));
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(LoggedBoundaryFailures), nameof(SafeAnimalName)));
        }

        internal static IEnumerable<CodeInstruction> SafeAnimalName(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var name = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Name));
            var shortName = AccessTools.PropertyGetter(typeof(Name), nameof(Name.ToStringShort));
            int count = 0;
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (!code[i].Calls(name) || !code[i + 1].Calls(shortName)) continue;
                // Preserve the stack and branch/exception metadata of both slots.
                code[i].opcode = OpCodes.Call;
                code[i].operand = AccessTools.Method(typeof(LoggedBoundaryFailures), nameof(AnimalLabel));
                code[i + 1].opcode = OpCodes.Nop;
                code[i + 1].operand = null;
                count++;
            }
            if (count != 1) throw new InvalidOperationException("Ballz animal name chain count: " + count);
            return code;
        }

        internal static string AnimalLabel(Pawn pawn) => pawn.Name?.ToStringShort ?? pawn.LabelShort;

        internal static void ApplyRaven(Harmony harmony)
        {
            var type = Bootstrap.Type("RavenRace.Features.UnityEffects.MapComponent_RavenUnityEffectUpdater");
            harmony.Patch(Bootstrap.Method(type, "Get", typeof(Map)),
                prefix: new HarmonyMethod(typeof(LoggedBoundaryFailures), nameof(LiveEffectMap)));
        }

        internal static bool LiveEffectMap(Map __0)
        {
            // Static trail registries can reference a cleared old map after
            // rejoin. GetComponent dereferences components; callers accept null.
            // Skipping this getter returns null; no gameplay exceptions are caught.
            return !MP.enabled || __0 == null || __0.components != null;
        }
    }
}
