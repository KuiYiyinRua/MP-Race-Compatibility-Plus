using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    // Exact binary evidence: Light350 F02/F06/F07/F08/F18.
    internal static class Patch_Light350StateMp
    {
        private static bool applied;
        private static FieldInfo lighthouseNextTick, spearCount;
        private static Type randomSpearType;
        private static readonly Dictionary<MethodBase, Replacement> replacements = new Dictionary<MethodBase, Replacement>();

        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled) return;
            applied = true;
            Package("kokodayo.mlgreturn", () => Replace(harmony, "MLG.Comp_SubExlosion", "PostDestroy",
                new[] { typeof(DestroyMode), typeof(Map) }, AccessTools.PropertyGetter(typeof(UnityEngine.Random), "insideUnitSphere"), nameof(InsideUnitSphere), 1));
            Package("vamv.maruracemod", () =>
            {
                var type = RequiredType("MaruLighthouse.Comp_active");
                lighthouseNextTick = RequiredInt(type, "NextTick");
                var expose = RequiredMethod(type, "PostExposeData", Type.EmptyTypes);
                Replace(harmony, "MaruLighthouse.Comp_CountDown", "CompTick", Type.EmptyTypes,
                    AccessTools.Method(typeof(UnityEngine.Random), "Range", new[] { typeof(int), typeof(int) }), nameof(RangeInt), 1);
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_Light350StateMp), nameof(LighthouseExpose)));
            });
            Package("rkk.ratknights.core", () =>
            {
                randomSpearType = RequiredType("RatkinKnights.RKK_WhiteMoonSpearProjectile_Random");
                spearCount = RequiredInt(randomSpearType, "spearsLaunched");
                var parent = RequiredType("RatkinKnights.BaseWhiteMoonSpearProjectile");
                if (randomSpearType.BaseType != parent) throw new InvalidOperationException("Random spear base changed");
                var expose = RequiredMethod(parent, "ExposeData", Type.EmptyTypes);
                Replace(harmony, randomSpearType.FullName, "GetRandomPositionAround", new[] { typeof(IntVec3), typeof(int) },
                    AccessTools.Method(typeof(UnityEngine.Random), "Range", new[] { typeof(int), typeof(int) }), nameof(RangeInt), 2);
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_Light350StateMp), nameof(SpearExpose)));
            });
        }

        private static void Package(string package, Action install)
        {
            if (!ModsConfig.IsActive(package)) return;
            try { install(); Log.Message("[MP-MeowOnlineShop][Light350-B2] " + package + ": installed verified random/state targets."); }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B2] REQUIRED TARGET FAILED " + package + ": " + e); }
        }

        private static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        private static FieldInfo RequiredInt(Type type, string name)
        {
            var field = AccessTools.DeclaredField(type, name);
            if (field == null || field.FieldType != typeof(int) || field.IsStatic) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static MethodInfo RequiredMethod(Type type, string name, Type[] args) =>
            AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);

        private static void Replace(Harmony harmony, string typeName, string methodName, Type[] args, MethodInfo from, string toName, int count)
        {
            var target = RequiredMethod(RequiredType(typeName), methodName, args);
            var to = AccessTools.DeclaredMethod(typeof(Patch_Light350StateMp), toName);
            if (from == null || to == null || from.ReturnType != to.ReturnType ||
                !from.GetParameters().Select(p => p.ParameterType).SequenceEqual(to.GetParameters().Select(p => p.ParameterType)))
                throw new InvalidOperationException("Random replacement signature mismatch");
            var body = PatchProcessor.GetOriginalInstructions(target);
            if (body.Count(i => i.Calls(from)) != count) throw new InvalidOperationException("Unexpected random call count: " + target);
            replacements[target] = new Replacement { From = from, To = to, Count = count };
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Patch_Light350StateMp), nameof(Transpile)));
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var replacement = replacements[__originalMethod];
            var result = instructions.ToList();
            int count = 0;
            foreach (var instruction in result)
                if (instruction.Calls(replacement.From))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement.To;
                    count++;
                }
            if (count != replacement.Count) throw new InvalidOperationException("Random transpiler coverage changed: " + __originalMethod);
            return result;
        }
        private sealed class Replacement { internal MethodInfo From, To; internal int Count; }

        private static int RangeInt(int minInclusive, int maxExclusive) =>
            MP.IsInMultiplayer ? Rand.Range(minInclusive, maxExclusive) : UnityEngine.Random.Range(minInclusive, maxExclusive);

        private static Vector3 InsideUnitSphere()
        {
            if (!MP.IsInMultiplayer) return UnityEngine.Random.insideUnitSphere;
            // Uniform rejection sampling retains the volume distribution, including Y.
            // Use the active simulation stream; do not restore it after real outcomes.
            Vector3 point;
            do { point = new Vector3(Rand.Range(-1f, 1f), Rand.Range(-1f, 1f), Rand.Range(-1f, 1f)); }
            while (point.sqrMagnitude > 1f);
            return point;
        }

        private static void LighthouseExpose(object __instance)
        {
            int next = (int)lighthouseNextTick.GetValue(__instance);
            Scribe_Values.Look(ref next, "meowMpLight350NextTick", 0);
            lighthouseNextTick.SetValue(__instance, next);
        }
        private static void SpearExpose(object __instance)
        {
            if (!randomSpearType.IsInstanceOfType(__instance)) return;
            int count = (int)spearCount.GetValue(__instance);
            // Old snapshots have no recoverable exact count (out-of-bounds spawns
            // need not increment it). Default zero preserves their prior behavior.
            Scribe_Values.Look(ref count, "meowMpLight350SpearsLaunched", 0);
            spearCount.SetValue(__instance, count);
        }
    }
}
