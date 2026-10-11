using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynRoots
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.PhoneBooth") ?? throw new TypeLoadException("ASEL.PhoneBooth");
            foreach (var name in new[] { "ExposeData", "SpawnSetup", "Tick" })
            {
                var method = AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
                string patch = name == "ExposeData" ? nameof(Save) : name == "SpawnSetup" ? nameof(Spawn) : nameof(Tick);
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(MonolynRoots), patch));
            }
            Log.Message("[MonolynRoots] resolved=live root references, native spawn context, stable damage order; missing=0");
        }
        static IEnumerable<CodeInstruction> Save(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Scribe_Collections) && method.Name == "Look" &&
                    method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(ThingWithComps) }))
                { instruction.operand = AccessTools.Method(typeof(MonolynRoots), nameof(Roots)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one phone roots save boundary, found " + count);
        }
        static void Roots(ref List<ThingWithComps> roots, string nativeKey, LookMode nativeMode, object[] arguments)
        {
            // Native rootTargets is a cache of spawned objects, not their owner.
            // A separate key also prevents legacy deep nodes loading duplicate Things.
            // SpawnSetup/UpdateRoots rebuild the cache when this key is absent.
            Scribe_Collections.Look(ref roots, "meowPhoneRoots", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && roots == null) roots = new List<ThingWithComps>();
        }
        static IEnumerable<CodeInstruction> Spawn(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(LongEventHandler) && method.Name == "ExecuteWhenFinished")
                { instruction.operand = AccessTools.Method(typeof(MonolynRoots), nameof(UpdateRoots)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one phone root spawn event, found " + count);
        }
        static void UpdateRoots(Action action)
        {
            if (MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.Inactive) action();
            else LongEventHandler.ExecuteWhenFinished(action);
        }
        static IEnumerable<CodeInstruction> Tick(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Enumerable) && method.Name == "ToList" &&
                    method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(ThingWithComps) }))
                { instruction.operand = AccessTools.Method(typeof(MonolynRoots), nameof(DamageOrder)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one phone root damage traversal, found " + count);
        }
        static List<ThingWithComps> DamageOrder(IEnumerable<ThingWithComps> roots) => roots.OrderBy(t => t.thingIDNumber).ToList();
    }
}
