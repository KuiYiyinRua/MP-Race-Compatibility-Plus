using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350LoadStateMp
    {
        private static bool applied;
        private static Type identityType;
        private static FieldInfo identityTicks, overflowOrientation;
        private static ConstructorInfo overflowConstructor;
        internal static bool HasOverflowRandomPatch => overflowConstructor != null;
        internal static bool OwnsRandomConstructor(MethodBase method) => Equals(method, overflowConstructor);
        private static readonly Dictionary<string, FieldInfo> moonFields = new Dictionary<string, FieldInfo>();
        private static readonly ConstructorInfo randomConstructor = typeof(Random).GetConstructor(Type.EmptyTypes);
        private static readonly MethodInfo randomNext = AccessTools.DeclaredMethod(typeof(Random), "Next", new[] { typeof(int), typeof(int) });

        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled) return;
            applied = true;
            Package("ariandel.ariandellibrary", () =>
            {
                identityType = RequiredType("AriandelLibrary.HediffComp_SpecialIdentity");
                if (identityType.BaseType != typeof(HediffComp) || AccessTools.DeclaredMethod(identityType, "CompExposeData") != null)
                    throw new InvalidOperationException("Identity serializer shape changed");
                identityTicks = Field(identityType, "ticksPassed", typeof(int));
                harmony.Patch(Method(typeof(HediffComp), "CompExposeData", Type.EmptyTypes),
                    postfix: new HarmonyMethod(typeof(Patch_Light350LoadStateMp), nameof(IdentityExpose)));
            });
            Package("rkk.ratknights.core", () =>
            {
                var type = RequiredType("RatkinKnights.RKK_Race_WhiteMoon");
                if (type.BaseType != typeof(Pawn)) throw new InvalidOperationException("WhiteMoon pawn base changed");
                foreach (var key in new[] { "tickSkillsCoolDown", "targetUpdateTick" }) moonFields[key] = Field(type, key, typeof(int));
                foreach (var key in new[] { "inited", "isDead" }) moonFields[key] = Field(type, key, typeof(bool));
                var expose = Method(type, "ExposeData", Type.EmptyTypes);
                var body = PatchProcessor.GetOriginalInstructions(expose);
                if (body.Any(i => i.opcode == OpCodes.Ldstr && moonFields.ContainsKey((string)i.operand)))
                    throw new InvalidOperationException("WhiteMoon pawn already serializes a target field");
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_Light350LoadStateMp), nameof(MoonExpose)));
            });
            Package("vegapnk.cumpilation", () =>
            {
                var type = RequiredType("Cumpilation.Cumflation.JobDriver_OverflowingCumflation");
                if (type.BaseType != typeof(JobDriver)) throw new InvalidOperationException("Overflow driver base changed");
                overflowOrientation = Field(type, "orientation", typeof(IntVec3));
                Field(type, "random", typeof(Random));
                var ctor = AccessTools.DeclaredConstructor(type, Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, ".ctor");
                var nearby = Method(type, "GetRandomNearbyPosition", new[] { typeof(int), typeof(int) });
                var direction = Method(type, "GetRandomOrientation", Type.EmptyTypes);
                RequireOperand(PatchProcessor.GetOriginalInstructions(ctor), randomConstructor);
                RequireOperand(PatchProcessor.GetOriginalInstructions(nearby), randomNext);
                // Run before the older assembly-wide unseeded-Random replacement.
                // This driver no longer uses its private Random during MP simulation;
                // constructing it must not consume Verse Rand during snapshot loading.
                harmony.Patch(ctor, transpiler: new HarmonyMethod(typeof(Patch_Light350LoadStateMp), nameof(OverflowConstructor)) { priority = Priority.First });
                harmony.Patch(nearby, transpiler: new HarmonyMethod(typeof(Patch_Light350LoadStateMp), nameof(OverflowNext)));
                harmony.Patch(direction, prefix: new HarmonyMethod(typeof(Patch_Light350LoadStateMp), nameof(PreserveLoadedOrientation)));
                overflowConstructor = ctor;
            });
        }
        private static void Package(string id, Action install)
        {
            if (!ModsConfig.IsActive(id)) return;
            try { install(); Log.Message("[MP-MeowOnlineShop][Light350-B4] " + id + ": load-state targets installed."); }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B4] REQUIRED TARGET FAILED " + id + ": " + e); }
        }
        private static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        private static MethodInfo Method(Type type, string name, Type[] args) => AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = AccessTools.DeclaredField(type, name);
            if (field == null || field.IsStatic || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static void IdentityExpose(HediffComp __instance)
        {
            if (__instance.GetType() != identityType) return;
            int ticks = (int)identityTicks.GetValue(__instance);
            Scribe_Values.Look(ref ticks, "meowMpIdentityTicksPassed", 0);
            identityTicks.SetValue(__instance, ticks);
        }
        private static void MoonExpose(Pawn __instance)
        {
            foreach (var key in new[] { "tickSkillsCoolDown", "targetUpdateTick" })
            {
                int value = (int)moonFields[key].GetValue(__instance);
                Scribe_Values.Look(ref value, "meowMpMoonPawn_" + key, 0);
                moonFields[key].SetValue(__instance, value);
            }
            foreach (var key in new[] { "inited", "isDead" })
            {
                bool value = (bool)moonFields[key].GetValue(__instance);
                Scribe_Values.Look(ref value, "meowMpMoonPawn_" + key, false);
                moonFields[key].SetValue(__instance, value);
            }
        }
        private static void RequireOperand(IEnumerable<CodeInstruction> body, MemberInfo operand)
        {
            if (body.Count(i => Equals(i.operand, operand)) != 1) throw new InvalidOperationException("Expected one exact call to " + operand);
        }
        private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, MemberInfo from, string to)
        {
            var body = instructions.ToList();
            RequireOperand(body, from);
            foreach (var instruction in body)
                if (Equals(instruction.operand, from))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Light350LoadStateMp), to);
                }
            return body;
        }
        private static IEnumerable<CodeInstruction> OverflowConstructor(IEnumerable<CodeInstruction> instructions) => Replace(instructions, randomConstructor, nameof(NewPrivateRandom));
        private static IEnumerable<CodeInstruction> OverflowNext(IEnumerable<CodeInstruction> instructions) => Replace(instructions, randomNext, nameof(NextDistance));
        private static Random NewPrivateRandom() => new Random();
        private static int NextDistance(Random random, int minimum, int maximum)
        {
            if (!MP.IsInMultiplayer) return random.Next(minimum, maximum);
            if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
            return Rand.Range(minimum, maximum);
        }
        private static bool PreserveLoadedOrientation(object __instance, ref IntVec3 __result)
        {
            if (!MP.IsInMultiplayer || Scribe.mode != LoadSaveMode.PostLoadInit) return true;
            __result = (IntVec3)overflowOrientation.GetValue(__instance);
            return false;
        }
    }
}
