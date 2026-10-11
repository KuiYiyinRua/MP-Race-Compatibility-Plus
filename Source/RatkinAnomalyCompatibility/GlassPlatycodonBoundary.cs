using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RatkinCompatibility
{
    // Graphic/GetInspectString call State. The native getter sends a letter
    // and writes cachedState, so viewing the flower used to mutate a peer's save.
    internal static class GlassPlatycodonBoundary
    {
        static FieldInfo counter, maximum, cached;
        static MethodInfo stateGetter;
        [ThreadStatic] static bool committing;

        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("RatkinAnomaly.GlassPlatycodon")
                ?? throw new TypeLoadException("RatkinAnomaly.GlassPlatycodon");
            counter = AccessTools.DeclaredField(type, "Counter");
            maximum = AccessTools.DeclaredField(type, "MaxCounter");
            cached = AccessTools.DeclaredField(type, "cachedState");
            stateGetter = AccessTools.PropertyGetter(type, "State");
            var tick = AccessTools.DeclaredMethod(type, "Tick", Type.EmptyTypes);
            var incident = AccessTools.TypeByName("RatkinAnomaly.IncidentWorker_GlassPlatycodonAppear");
            var choose = AccessTools.DeclaredMethod(incident, "FindOwnerAndLover");
            if (counter?.FieldType != typeof(int) || maximum?.FieldType != typeof(int) ||
                !maximum.IsStatic || cached == null || !cached.FieldType.IsEnum ||
                Enum.GetUnderlyingType(cached.FieldType) != typeof(int) ||
                stateGetter?.ReturnType != cached.FieldType || tick == null || choose == null)
                throw new MissingMemberException("GlassPlatycodon state/tick/incident signature changed");
            var values = Enum.GetNames(cached.FieldType);
            if (!values.SequenceEqual(new[] { "Fresh", "Wizened", "Withered" }))
                throw new InvalidOperationException("GlassPlatycodon state enum changed");
            harmony.Patch(stateGetter, transpiler: new HarmonyMethod(typeof(GlassPlatycodonBoundary), nameof(ReadOnlyGetter)));
            harmony.Patch(tick, postfix: new HarmonyMethod(typeof(GlassPlatycodonBoundary), nameof(CommitAfterTick)));
            harmony.Patch(choose, transpiler: new HarmonyMethod(typeof(GlassPlatycodonBoundary), nameof(OrderCandidates)));
            // Installed 1.6 DLL: these instance lambdas respectively change
            // Counter, FailCounter, Bonus and dev. The log/read lambdas stay local.
            for (int i = 1; i <= 4; i++)
            {
                var action = AccessTools.DeclaredMethod(type, "<GetGizmos>b__34_" + i, Type.EmptyTypes);
                if (action == null || action.IsStatic || action.ReturnType != typeof(void))
                    throw new MissingMethodException(type.FullName, "GlassPlatycodon DEV action " + i);
                MP.RegisterSyncMethod(action).SetDebugOnly();
            }
            Log.Message("[RatkinMP] GlassPlatycodon: read-only state queries; native transitions on shared ticks; stable lover selection.");
        }

        static bool ReadOnly() => MP.IsInMultiplayer && !committing;
        internal static int VisibleState(object flower)
        {
            float age = (int)counter.GetValue(flower) / (float)(int)maximum.GetValue(null);
            return age > 0.7f ? 2 : age > 0.4f ? 1 : Convert.ToInt32(cached.GetValue(flower));
        }
        static IEnumerable<CodeInstruction> ReadOnlyGetter(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var body = instructions.ToList();
            if (body.Count == 0) throw new InvalidOperationException("Empty GlassPlatycodon State getter");
            var original = generator.DefineLabel();
            body[0].labels.Add(original);
            // Enum's verified underlying type is Int32. Keep the complete native
            // getter for single-player and the deterministic transition executor.
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GlassPlatycodonBoundary), nameof(ReadOnly)));
            yield return new CodeInstruction(OpCodes.Brfalse, original);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GlassPlatycodonBoundary), nameof(VisibleState)));
            yield return new CodeInstruction(OpCodes.Ret);
            foreach (var instruction in body) yield return instruction;
        }
        static void CommitAfterTick(Thing __instance)
        {
            if (!MP.IsInMultiplayer || MP.InInterface || __instance.Destroyed) return;
            bool previous = committing;
            committing = true;
            try { stateGetter.Invoke(__instance, null); }
            finally { committing = previous; }
        }
        static IEnumerable<CodeInstruction> OrderCandidates(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.IsGenericMethod &&
                    method.DeclaringType == typeof(GenCollection) && method.Name == "RandomElement" &&
                    method.GetGenericArguments().SequenceEqual(new[] { typeof(Pawn) }) && method.GetParameters().Length == 1)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(GlassPlatycodonBoundary), nameof(ChooseOwner));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one GlassPlatycodon owner draw, found " + count);
        }
        internal static Pawn ChooseOwner(IEnumerable<Pawn> source) =>
            (MP.IsInMultiplayer ? source.OrderBy(p => p.thingIDNumber) : source).RandomElement();
    }
}
