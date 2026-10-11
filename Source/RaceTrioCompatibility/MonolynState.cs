using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynState
    {
        static FieldInfo hallucination, consumeAll, delay;
        static FieldInfo insufficientLight, lightConsumption;

        internal static void Apply(Harmony harmony)
        {
            var lightDamage = AccessTools.TypeByName("ASEL.HediffComp_LightDamage") ?? throw new TypeLoadException("ASEL.HediffComp_LightDamage");
            harmony.Patch(AccessTools.DeclaredMethod(lightDamage, "ApplyLightDamage", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(MonolynState), nameof(LightDamageCanTick)),
                transpiler: new HarmonyMethod(typeof(MonolynState), nameof(LightDamageMap)));
            var monitor = AccessTools.TypeByName("ASEL.CompComputationMonitor") ?? throw new TypeLoadException("ASEL.CompComputationMonitor");
            var meteor = AccessTools.DeclaredMethod(monitor, "MeteorGuidance", Type.EmptyTypes) ?? throw new MissingMethodException(monitor.FullName, "MeteorGuidance");
            harmony.Patch(meteor, transpiler: new HarmonyMethod(typeof(MonolynState), nameof(MeteorMap)));
            var mental = AccessTools.TypeByName("ASEL.CallFromBeyond") ?? throw new TypeLoadException("ASEL.CallFromBeyond");
            hallucination = RequiredField(mental, "hallucination", typeof(bool));
            consumeAll = RequiredField(mental, "ConsumeAll", typeof(bool));
            delay = RequiredField(mental, "delay", typeof(int));
            harmony.Patch(AccessTools.DeclaredMethod(mental, "ExposeData", Type.EmptyTypes) ?? throw new MissingMethodException(mental.FullName, "ExposeData"),
                postfix: new HarmonyMethod(typeof(MonolynState), nameof(MentalSave)));
            var baptism = AccessTools.TypeByName("ASEL.Building_BaptismalFont") ?? throw new TypeLoadException("ASEL.Building_BaptismalFont");
            insufficientLight = RequiredField(baptism, "insufficientLight", typeof(bool));
            lightConsumption = RequiredField(baptism, "LightConsumption", typeof(int));
            harmony.Patch(AccessTools.DeclaredMethod(baptism, "ExposeData", Type.EmptyTypes) ?? throw new MissingMethodException(baptism.FullName, "ExposeData"),
                postfix: new HarmonyMethod(typeof(MonolynState), nameof(BaptismSave)));
            Log.Message("[MonolynState] resolved=meteor owner map, phone mental flags/timer, baptism pause/rate; missing=0");
        }

        static bool LightDamageCanTick(HediffComp __instance) => !MP.IsInMultiplayer || __instance.Pawn?.Spawned == true;
        static Map LightDamageOwnerMap(HediffComp owner) => MP.IsInMultiplayer ? owner.Pawn.Map : Find.CurrentMap;
        static IEnumerable<CodeInstruction> LightDamageMap(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(getter)) { yield return instruction; continue; }
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels);
                load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MonolynState), nameof(LightDamageOwnerMap)));
                count++;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected native discharge current-map reads: " + count);
        }

        static IEnumerable<CodeInstruction> MeteorMap(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var getter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap));
            int count = code.Count(i => i.Calls(getter));
            if (count != 1) throw new InvalidOperationException("Expected exactly one MeteorGuidance CurrentMap access, got " + count);
            foreach (var instruction in code)
            {
                if (!instruction.Calls(getter)) { yield return instruction; continue; }
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels);
                load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MonolynState), nameof(OwnerMap)));
            }
        }

        static Map OwnerMap(ThingComp owner) => owner.parent.Map;

        static void MentalSave(object __instance)
        {
            bool currentHallucination = (bool)hallucination.GetValue(__instance);
            bool currentConsumeAll = (bool)consumeAll.GetValue(__instance);
            int currentDelay = (int)delay.GetValue(__instance);
            Scribe_Values.Look(ref currentHallucination, "meowPhoneHallucination", false);
            Scribe_Values.Look(ref currentConsumeAll, "meowPhoneConsumeAll", false);
            Scribe_Values.Look(ref currentDelay, "meowPhoneMentalDelay", 0);
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            hallucination.SetValue(__instance, currentHallucination);
            consumeAll.SetValue(__instance, currentConsumeAll);
            delay.SetValue(__instance, currentDelay);
        }

        static FieldInfo RequiredField(Type type, string name, Type fieldType)
        {
            var field = AccessTools.Field(type, name);
            if (field == null || field.FieldType != fieldType) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        static void BaptismSave(object __instance)
        {
            bool paused = (bool)insufficientLight.GetValue(__instance);
            int rate = (int)lightConsumption.GetValue(__instance);
            Scribe_Values.Look(ref paused, "meowBaptismInsufficientLight");
            Scribe_Values.Look(ref rate, "meowBaptismLightConsumption", 20);
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            insufficientLight.SetValue(__instance, paused);
            lightConsumption.SetValue(__instance, rate);
        }
    }
}
