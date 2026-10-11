using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynVerbs
    {
        sealed class Fields { internal FieldInfo Path, PathCells, HitCells, Pillars; internal MethodInfo SavePillars; }
        static readonly Dictionary<Type, Fields> fields = new Dictionary<Type, Fields>();
        static FieldInfo pillarLight, lanternLight;
        static FieldInfo alternatingSide;
        static MethodInfo lightCost, lanternStorage;
        static class EmptyMotes<T> { internal static readonly Dictionary<T, MoteDualAttached> Value = new Dictionary<T, MoteDualAttached>(); }
        internal static void Apply(Harmony harmony)
        {
            var alternating = AccessTools.TypeByName("ASEL.Verb_ShootLB_CustomOffset") ?? throw new TypeLoadException("ASEL.Verb_ShootLB_CustomOffset");
            alternatingSide = AccessTools.DeclaredField(alternating, "lastSideLeft");
            if (alternatingSide?.FieldType != typeof(bool)) throw new MissingFieldException(alternating.FullName, "lastSideLeft");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Verb), nameof(Verb.ExposeData)),
                postfix: new HarmonyMethod(typeof(MonolynVerbs), nameof(SaveAlternatingSide)));
            foreach (var name in new[] { "ASEL.Verb_ShootBeamL", "ASEL.Verb_ShootBeamLP" })
            {
                var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
                var path = AccessTools.DeclaredField(type, "path");
                var pathCells = AccessTools.DeclaredField(type, "pathCells");
                var hitCells = AccessTools.DeclaredField(type, "hitCells");
                if (path?.FieldType != typeof(List<Vector3>) || pathCells?.FieldType != typeof(HashSet<IntVec3>) || hitCells?.FieldType != typeof(HashSet<IntVec3>))
                    throw new InvalidOperationException("Native beam path/hit field signature changed: " + name);
                var state = new Fields { Path = path, PathCells = pathCells, HitCells = hitCells };
                fields.Add(type, state);
                var expose = AccessTools.DeclaredMethod(type, "ExposeData", Type.EmptyTypes) ?? throw new MissingMethodException(name, "ExposeData");
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(MonolynVerbs), nameof(Save)));
                var bursting = AccessTools.DeclaredMethod(type, "BurstingTick", Type.EmptyTypes) ?? throw new MissingMethodException(name, "BurstingTick");
                harmony.Patch(bursting, prefix: new HarmonyMethod(typeof(MonolynVerbs), nameof(VisualBegin)),
                    finalizer: new HarmonyMethod(typeof(MonolynVerbs), nameof(VisualEnd)));
                if (name == "ASEL.Verb_ShootBeamLP")
                {
                    var pillar = AccessTools.TypeByName("ASEL.PillarOfSalt") ?? throw new TypeLoadException("ASEL.PillarOfSalt");
                    state.Pillars = AccessTools.DeclaredField(type, "pillar");
                    if (state.Pillars?.FieldType != typeof(List<>).MakeGenericType(pillar)) throw new MissingFieldException(name, "pillar");
                    state.SavePillars = AccessTools.Method(typeof(MonolynVerbs), nameof(SavePillars)).MakeGenericMethod(pillar);
                    pillarLight = AccessTools.DeclaredField(pillar, "LightStorage");
                    var lantern = AccessTools.TypeByName("ASEL.CompLightStorageLantern");
                    lanternLight = AccessTools.DeclaredField(lantern, "currentLight");
                    lightCost = AccessTools.DeclaredPropertyGetter(type, "LightCost");
                    lanternStorage = AccessTools.DeclaredPropertyGetter(type, "storage");
                    if (pillarLight?.FieldType != typeof(float) || lanternLight?.FieldType != typeof(float) || lightCost?.ReturnType != typeof(float) || lanternStorage == null)
                        throw new InvalidOperationException("Native staff beam energy signatures changed");
                    harmony.Patch(bursting, prefix: new HarmonyMethod(typeof(MonolynVerbs), nameof(PillarCharge)),
                        transpiler: new HarmonyMethod(typeof(MonolynVerbs), nameof(StaffBurst)));
                    harmony.Patch(AccessTools.DeclaredMethod(type, "ApplyDamage") ?? throw new MissingMethodException(name, "ApplyDamage"),
                        transpiler: new HarmonyMethod(typeof(MonolynVerbs), nameof(StaffDamage)));
                }
            }
            Log.Message("[MonolynVerbs] resolved=2 active native beam path/hit sets, captured staff pillars, visual Rand and mote-independent pillar energy; missing=0");
        }
        static void SaveAlternatingSide(Verb __instance)
        {
            if (!MP.IsInMultiplayer || !alternatingSide.DeclaringType.IsInstanceOfType(__instance)) return;
            bool value = (bool)alternatingSide.GetValue(__instance);
            Scribe_Values.Look(ref value, "meowAlternatingBarrelLeft", false, true);
            alternatingSide.SetValue(__instance, value);
        }
        static void VisualBegin(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }
        static Exception VisualEnd(Exception __exception, bool __state)
        {
            if (__state) Rand.PopState();
            return __exception;
        }
        static void SavePillars<T>(object owner, FieldInfo field) where T : Thing
        {
            var pillars = (List<T>)field.GetValue(owner);
            Scribe_Collections.Look(ref pillars, "meowBeamPillars", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pillars == null) pillars = new List<T>();
            field.SetValue(owner, pillars);
        }
        static void PillarCharge(Verb __instance)
        {
            if (!MP.IsInMultiplayer) return;
            var storage = lanternStorage.Invoke(__instance, null);
            if (storage == null) return;
            float cost = (float)lightCost.Invoke(__instance, null);
            if ((float)lanternLight.GetValue(storage) < cost) return;
            // Native damage is in TryCastShot/ApplyDamage. BurstingTick's only simulation
            // use of its cosmetic dictionaries is this deterministic light cost.
            var caster = (Thing)AccessTools.Field(typeof(Verb), "caster").GetValue(__instance);
            foreach (var pillar in ((IEnumerable)fields[__instance.GetType()].Pillars.GetValue(__instance)).Cast<Thing>())
                if (pillar != null && !pillar.Destroyed && pillar.Spawned && pillar.Map == caster.Map)
                    pillarLight.SetValue(pillar, (float)pillarLight.GetValue(pillar) - cost * 2f);
        }
        static Dictionary<T, MoteDualAttached> WithoutVisualCharge<T>(Dictionary<T, MoteDualAttached> motes) => MP.IsInMultiplayer ? EmptyMotes<T>.Value : motes;
        static IEnumerable<CodeInstruction> VisualCharge(IEnumerable<CodeInstruction> instructions)
        {
            int reads = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == "pillarMotes")
                {
                    if (reads++ == 0)
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MonolynVerbs), nameof(WithoutVisualCharge)).MakeGenericMethod(field.FieldType.GetGenericArguments()[0]));
                }
            }
            if (reads != 4) throw new InvalidOperationException("Expected four native staff mote reads, found " + reads);
        }
        static int PillarCount(Verb verb) => MP.IsInMultiplayer ? ((ICollection)fields[verb.GetType()].Pillars.GetValue(verb)).Count :
            ((IDictionary)AccessTools.Field(verb.GetType(), "pillarMotes").GetValue(verb)).Count;
        static IEnumerable<CodeInstruction> StaffBurst(IEnumerable<CodeInstruction> instructions) => CountPillars(VisualCharge(instructions), 2);
        static IEnumerable<CodeInstruction> StaffDamage(IEnumerable<CodeInstruction> instructions) => CountPillars(instructions, 4);
        static IEnumerable<CodeInstruction> CountPillars(IEnumerable<CodeInstruction> instructions, int expected)
        {
            var body = instructions.ToList();
            int count = 0;
            for (int i = 0; i < body.Count; i++)
            {
                var instruction = body[i];
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == "pillarMotes" &&
                    i + 1 < body.Count && body[i + 1].operand is MethodInfo method && method.Name == "get_Count" &&
                    method.DeclaringType == field.FieldType)
                {
                    if (body[i + 1].labels.Count != 0 || body[i + 1].blocks.Count != 0)
                        throw new InvalidOperationException("Unexpected control-flow boundary inside native staff pillar count");
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(MonolynVerbs), nameof(PillarCount));
                    i++; count++;
                }
                yield return instruction;
            }
            if (count != expected) throw new InvalidOperationException("Native staff pillar count shape changed: " + count + "/" + expected);
        }
        static void Save(Verb __instance)
        {
            if (!fields.TryGetValue(__instance.GetType(), out var state)) return;
            var pathCells = (HashSet<IntVec3>)state.PathCells.GetValue(__instance);
            var hitCells = (HashSet<IntVec3>)state.HitCells.GetValue(__instance);
            Scribe_Collections.Look(ref pathCells, "meowBeamPathCells", LookMode.Value);
            Scribe_Collections.Look(ref hitCells, "meowBeamHitCells", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Legacy native saves have path points but no sets. Recreate the
                // path using the exact native conversion, without invoking its
                // random path generation. Old hit history cannot be recovered.
                if (pathCells == null)
                    pathCells = new HashSet<IntVec3>(((List<Vector3>)state.Path.GetValue(__instance) ?? new List<Vector3>()).Select(p => p.ToIntVec3()));
                if (hitCells == null) hitCells = new HashSet<IntVec3>();
            }
            state.PathCells.SetValue(__instance, pathCells);
            state.HitCells.SetValue(__instance, hitCells);
            state.SavePillars?.Invoke(null, new object[] { __instance, state.Pillars });
        }
    }
}
