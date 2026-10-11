using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynEffects
    {
        static FieldInfo towerGraphic;
        static FieldInfo crucibleWorking, crucibleInsufficient;
        static MethodInfo crucibleMaterials;
        static readonly Graphic emptyTowerGraphic = new Graphic_Single { color = UnityEngine.Color.white };
        internal static void Apply(Harmony harmony)
        {
            var crucible = AccessTools.TypeByName("ASEL.Building_Crucible") ?? throw new TypeLoadException("ASEL.Building_Crucible");
            crucibleWorking = AccessTools.Field(crucible, "working");
            crucibleInsufficient = AccessTools.Field(crucible, "insufficientLight");
            crucibleMaterials = AccessTools.Method(crucible, "HasSufficientMaterials", Type.EmptyTypes);
            if (crucibleWorking?.FieldType != typeof(bool) || crucibleInsufficient?.FieldType != typeof(bool) || crucibleMaterials?.ReturnType != typeof(bool))
                throw new InvalidOperationException("Native crucible work signatures changed");
            harmony.Patch(AccessTools.DeclaredMethod(crucible, "Tick"), transpiler: new HarmonyMethod(typeof(MonolynEffects), nameof(CrucibleHeat)));
            var brokenTower = AccessTools.TypeByName("ASEL.TowerOfLightBroken") ?? throw new TypeLoadException("ASEL.TowerOfLightBroken");
            towerGraphic = AccessTools.DeclaredField(brokenTower, "PlantGraphic");
            if (towerGraphic?.FieldType != typeof(Graphic)) throw new MissingFieldException(brokenTower.FullName, "PlantGraphic");
            harmony.Patch(AccessTools.DeclaredMethod(brokenTower, "Tick"), transpiler: new HarmonyMethod(typeof(MonolynEffects), nameof(TowerGraphicRead)));
            var type = AccessTools.TypeByName("ASEL.CompForgeArray") ?? throw new TypeLoadException("ASEL.CompForgeArray");
            var tick = AccessTools.DeclaredMethod(type, "CompTick", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "CompTick");
            harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(MonolynEffects), nameof(EffectChoice)));
            var expose = AccessTools.DeclaredMethod(type, "PostExposeData", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "PostExposeData");
            harmony.Patch(expose, postfix: new HarmonyMethod(typeof(MonolynEffects), nameof(CacheState)));
            var sight = AccessTools.TypeByName("ASEL.CompSightSign") ?? throw new TypeLoadException("ASEL.CompSightSign");
            // Native PostDraw lazily calls this method, which creates a cosmetic
            // effect at stage 2. Its random use must not depend on drawing a pawn.
            var graphic = AccessTools.DeclaredMethod(sight, "initializeGraphic", Type.EmptyTypes) ?? throw new MissingMethodException(sight.FullName, "initializeGraphic");
            harmony.Patch(graphic, prefix: new HarmonyMethod(typeof(MonolynEffects), nameof(GraphicBegin)),
                finalizer: new HarmonyMethod(typeof(MonolynEffects), nameof(GraphicEnd)));
            Log.Message("[MonolynEffects] resolved=forge cosmetic choice/spawn random isolation, cold cache state; missing=0");
        }

        static IEnumerable<CodeInstruction> CrucibleHeat(IEnumerable<CodeInstruction> source)
        {
            int anchors = 0, pushes = 0;
            foreach (var code in source)
            {
                // The first sound-cache read is inside the native workStarted
                // branch, after base.Tick and its insufficient-light reset.
                // Keep heat at that point, independent of local audio lifetime.
                if (anchors == 0 && code.opcode == System.Reflection.Emit.OpCodes.Ldfld &&
                    code.operand is FieldInfo field && field.DeclaringType.FullName == "ASEL.Building_Crucible" && field.Name == "sustainerWorking")
                {
                    var duplicate = new CodeInstruction(System.Reflection.Emit.OpCodes.Dup);
                    duplicate.labels.AddRange(code.labels);
                    code.labels.Clear();
                    yield return duplicate;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(MonolynEffects), nameof(WorkHeat)));
                    anchors++;
                }
                if (code.operand is MethodInfo method && method.DeclaringType == typeof(GenTemperature) && method.Name == "PushHeat" &&
                    method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(IntVec3), typeof(Map), typeof(float) }))
                {
                    code.operand = AccessTools.Method(typeof(MonolynEffects), nameof(AudioBranchHeat));
                    pushes++;
                }
                yield return code;
            }
            if (anchors != 1 || pushes != 1) throw new InvalidOperationException("Unexpected native crucible heat boundaries: " + anchors + "/" + pushes);
        }
        static void WorkHeat(Thing owner)
        {
            if (MP.IsInMultiplayer && (bool)crucibleWorking.GetValue(owner) && !(bool)crucibleInsufficient.GetValue(owner) &&
                (bool)crucibleMaterials.Invoke(owner, null) && owner.IsHashIntervalTick(60))
                GenTemperature.PushHeat(owner.PositionHeld, owner.MapHeld, 160f);
        }
        static bool AudioBranchHeat(IntVec3 cell, Map map, float energy)
        {
            return !MP.IsInMultiplayer && GenTemperature.PushHeat(cell, map, energy);
        }

        static IEnumerable<CodeInstruction> TowerGraphicRead(IEnumerable<CodeInstruction> source)
        {
            int count = 0;
            foreach (var code in source)
            {
                if (code.opcode == System.Reflection.Emit.OpCodes.Ldfld && code.operand as FieldInfo == towerGraphic)
                {
                    code.opcode = System.Reflection.Emit.OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(MonolynEffects), nameof(TowerAlphaGraphic));
                    count++;
                }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected native broken tower graphic reads: " + count);
        }
        static Graphic TowerAlphaGraphic(Thing owner)
        {
            var graphic = (Graphic)towerGraphic.GetValue(owner);
            // Tick writes alpha immediately before replacing this graphic. The
            // preceding DrawAt may never have run on a hidden/rejoined map.
            // A local placeholder makes that obsolete alpha write safe; the
            // native replacement and its saved bt/tf/efb progression remain.
            return graphic ?? (MP.IsInMultiplayer ? emptyTowerGraphic : null);
        }

        static void GraphicBegin(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }
        static Exception GraphicEnd(Exception __exception, bool __state)
        {
            if (__state) Rand.PopState();
            return __exception;
        }

        static IEnumerable<CodeInstruction> EffectChoice(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0, spawns = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(GenCollection) &&
                    method.Name == "RandomElement" && method.IsGenericMethod &&
                    method.GetGenericArguments().SequenceEqual(new[] { typeof(EffecterDef) }) &&
                    method.GetParameters().Length == 1)
                {
                    instruction.operand = AccessTools.Method(typeof(MonolynEffects), nameof(ChooseEffect));
                    count++;
                }
                else if (instruction.operand is MethodInfo spawn && spawn.DeclaringType == typeof(EffecterDef) &&
                    spawn.Name == "SpawnAttached" && spawn.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Thing), typeof(Map), typeof(float) }))
                {
                    instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(MonolynEffects), nameof(SpawnEffect));
                    spawns++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Forge expected one cosmetic EffecterDef choice, found " + count);
            if (spawns != 2) throw new InvalidOperationException("Forge expected two cosmetic effect spawns, found " + spawns);
        }

        static EffecterDef ChooseEffect(IEnumerable<EffecterDef> options)
        {
            if (!MP.IsInMultiplayer) return options.RandomElement();
            // The camera-dependent branch must not advance shared gameplay Rand.
            Rand.PushState();
            try { return options.RandomElement(); }
            finally { Rand.PopState(); }
        }
        static Effecter SpawnEffect(EffecterDef def, Thing parent, Map map, float scale)
        {
            if (!MP.IsInMultiplayer) return def.SpawnAttached(parent, map, scale);
            Rand.PushState();
            try { return def.SpawnAttached(parent, map, scale); }
            finally { Rand.PopState(); }
        }
        static void CacheState(ThingComp __instance)
        {
            var initialized = AccessTools.Field(__instance.GetType(), "init");
            bool value = (bool)initialized.GetValue(__instance);
            Scribe_Values.Look(ref value, "meowForgeInitialized");
            initialized.SetValue(__instance, value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                AccessTools.Field(__instance.GetType(), "refuelable").SetValue(__instance,
                    value ? __instance.parent.GetComp<RimWorld.CompRefuelable>() : null);
        }
    }
}
