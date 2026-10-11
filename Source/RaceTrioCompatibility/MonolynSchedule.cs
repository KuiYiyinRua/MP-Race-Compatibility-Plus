using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynSchedule
    {
        sealed class Capture { internal bool Present, Loaded; }
        static readonly ConditionalWeakTable<ThingComp, Capture> captures = new ConditionalWeakTable<ThingComp, Capture>();
        static FieldInfo allowed;
        static MethodInfo recalculate;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.CompScheduleNormal") ?? throw new TypeLoadException("ASEL.CompScheduleNormal");
            allowed = AccessTools.DeclaredField(type, "intAllowed");
            if (allowed?.FieldType != typeof(bool)) throw new MissingFieldException(type.FullName, "intAllowed");
            recalculate = AccessTools.DeclaredMethod(type, "RecalculateAllowed", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "RecalculateAllowed");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingComp), nameof(ThingComp.PostExposeData), Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(MonolynSchedule), nameof(Save)));
            harmony.Patch(AccessTools.DeclaredMethod(type, "PostSpawnSetup", new[] { typeof(bool) }),
                transpiler: new HarmonyMethod(typeof(MonolynSchedule), nameof(Spawn)));
            Log.Message("[MonolynSchedule] resolved=native scheduled-light allowed phase and cold spawn boundary; missing=0");
        }
        static void Save(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer || !allowed.DeclaringType.IsInstanceOfType(__instance)) return;
            var capture = captures.GetValue(__instance, _ => new Capture());
            if (Scribe.mode == LoadSaveMode.Saving) capture.Present = true;
            Scribe_Values.Look(ref capture.Present, "meowSchedulePhasePresent", false, true);
            if (Scribe.mode == LoadSaveMode.LoadingVars) capture.Loaded = capture.Present;
            if (!capture.Present) return;
            bool value = (bool)allowed.GetValue(__instance);
            Scribe_Values.Look(ref value, "meowScheduleAllowed", false, true);
            allowed.SetValue(__instance, value);
        }
        static IEnumerable<CodeInstruction> Spawn(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(recalculate))
                {
                    var flag = new CodeInstruction(OpCodes.Ldarg_1);
                    flag.labels.AddRange(code.labels); flag.blocks.AddRange(code.blocks);
                    code.labels.Clear(); code.blocks.Clear();
                    yield return flag;
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(MonolynSchedule), nameof(SpawnRecalculate));
                    count++;
                }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Expected one native schedule spawn recalculation; found " + count);
        }
        static void SpawnRecalculate(ThingComp owner, bool respawningAfterLoad)
        {
            // Tick only refreshes Allowed every 1000 hash ticks. Recalculating
            // at a cold spawn changes the remaining phase around dawn/dusk.
            bool retained = MP.IsInMultiplayer && respawningAfterLoad && captures.TryGetValue(owner, out var capture) && capture.Loaded;
            try { if (!retained) recalculate.Invoke(owner, null); }
            finally { captures.Remove(owner); }
        }
    }
}
