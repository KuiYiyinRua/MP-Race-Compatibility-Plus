using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.TurretCombatSleep
{
    // Optional integration: no assembly dependency on DraconicMilitary or Lumi.
    internal static class NivarianTurretSleep
    {
        private const string Namespace = "NivarianRace.DraconicMilitary.";
        private static Type validatorType, conePropsType;
        private static Func<Building_TurretGun, int> lastWarmup, processedShot;
        private static Func<Building_TurretGun, float> recoil, facing;
        private static Func<Building_TurretGun, ThingComp> holder;
        private static Func<VerbProperties, float> halfAngle;
        private static Func<ThingComp, object> animator;
        private static Func<object, GameObject> gameObject;
        private static readonly AccessTools.FieldRef<Building_TurretGun, int> Warmup =
            AccessTools.FieldRefAccess<Building_TurretGun, int>("burstWarmupTicksLeft");
        private static readonly AccessTools.FieldRef<Verb, int> Shot =
            AccessTools.FieldRefAccess<Verb, int>("lastShotTick");
        private sealed class ConeState
        {
            internal bool Valid;
            internal float Angle, Facing, Half;
            internal VerbProperties Props;
        }
        private sealed class RotationState
        {
            internal bool Valid;
            internal float Angle;
            internal Quaternion Rotation;
        }
        private static readonly ConditionalWeakTable<Building_TurretGun, ConeState> Cones = new ConditionalWeakTable<Building_TurretGun, ConeState>();
        private static readonly ConditionalWeakTable<ThingComp, RotationState> Rotations = new ConditionalWeakTable<ThingComp, RotationState>();

        internal static void Install(Harmony harmony)
        {
            validatorType = AccessTools.TypeByName(Namespace + "Building_TurretWithValidator");
            if (validatorType == null) return;
            InstallPart("validator", () =>
            {
                lastWarmup = TurretCombatSleep.FieldGetter<Building_TurretGun, int>(validatorType, "lastBurstWarmupTicksLeft");
                holder = TurretCombatSleep.FieldGetter<Building_TurretGun, ThingComp>(validatorType, "_animatorHolder");
                PatchTail(harmony, validatorType);
            });
            InstallPart("cryo-recoil", () =>
            {
                var type = AccessTools.TypeByName(Namespace + "Building_TurretCryoMortarRecoil") ?? throw new TypeLoadException("CryoMortarRecoil");
                processedShot = TurretCombatSleep.FieldGetter<Building_TurretGun, int>(type, "lastProcessedShotTick");
                recoil = TurretCombatSleep.FieldGetter<Building_TurretGun, float>(type, "currentRecoilDistance");
                var reader = Required(type, "ReadLastShotTick");
                EnsureUnpatched(reader);
                harmony.Patch(reader, prefix: new HarmonyMethod(typeof(NivarianTurretSleep), nameof(ReadShot)));
                PatchTail(harmony, type);
            });
            InstallPart("winter-idle-cone", () =>
            {
                var type = AccessTools.TypeByName(Namespace + "Building_TurretWinter") ?? throw new TypeLoadException("Winter");
                conePropsType = AccessTools.TypeByName("Nivarian_Race.Code.Verbs.VerbProperties_ConeSweepContinuousBeam") ?? throw new TypeLoadException("ConeSweep properties");
                facing = TurretCombatSleep.FieldGetter<Building_TurretGun, float>(type, "coneFacingDegrees");
                halfAngle = TurretCombatSleep.FieldGetter<VerbProperties, float>(conePropsType, "coneHalfAngle");
                var method = Required(type, "ConstrainIdleTopRotationToCone");
                EnsureUnpatched(method);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NivarianTurretSleep), nameof(ConstrainCone)),
                    postfix: new HarmonyMethod(typeof(NivarianTurretSleep), nameof(ConeCompleted)));
            });
            InstallPart("animator-rotation", () =>
            {
                var type = AccessTools.TypeByName("LumiParticle.Comps.BuildingComps.Comp_LumiAnimatorHolder") ?? throw new TypeLoadException("Lumi holder");
                var itemType = AccessTools.TypeByName("LumiParticle.Items.LumiAnimatorItem") ?? throw new TypeLoadException("Lumi item");
                animator = TurretCombatSleep.FieldGetter<ThingComp, object>(type, "_animator");
                gameObject = TurretCombatSleep.FieldGetter<object, GameObject>(itemType, "_gameObject");
                var method = AccessTools.DeclaredMethod(type, "SetRotation", new[] { typeof(float) }) ?? throw new MissingMethodException("SetRotation");
                EnsureUnpatched(method);
                EnsureUnpatched(AccessTools.Method(itemType, "SetRotation", new[] { typeof(float) }));
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NivarianTurretSleep), nameof(RotateAnimator)));
            });
        }
        private static void InstallPart(string name, Action install)
        {
            try { install(); Log.Message("[Meow.TurretCombatSleep] NIVARIAN_SLEEP " + name); }
            catch (Exception e) { Log.Warning("[Meow.TurretCombatSleep] NIVARIAN_FALLBACK " + name + ": " + e.Message); }
        }
        private static MethodInfo Required(Type type, string name)
            => AccessTools.DeclaredMethod(type, name, Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, name);
        private static void EnsureUnpatched(MethodBase method)
        {
            if (method == null) throw new MissingMethodException();
            var info = Harmony.GetPatchInfo(method);
            if (info != null && info.Owners.Any(o => o != TurretCombatSleep.HarmonyId))
                throw new InvalidOperationException("Other hooks retained: " + method);
        }
        private static bool Active(Building_TurretGun turret)
            => TurretCombatSleep.Active && turret.Spawned && turret.Faction != null && turret.Faction.IsPlayer;
        private static int ShotTick(Building_TurretGun turret)
        {
            var verb = turret.AttackVerb;
            return verb == null ? int.MinValue : Shot(verb);
        }
        private static bool ReadShot(Building_TurretGun __instance, ref int __result)
        {
            if (!Active(__instance)) return true;
            __result = ShotTick(__instance);
            return false;
        }
        private static bool SkipValidatorTail(Building_TurretGun turret)
            => Active(turret) && Warmup(turret) == 0 && lastWarmup(turret) == 0 && holder(turret) == null;
        private static bool SkipRecoilTail(Building_TurretGun turret)
            => Active(turret) && recoil(turret) <= 0f && ShotTick(turret) <= processedShot(turret);
        private static void PatchTail(Harmony harmony, Type type)
        {
            var method = Required(type, "Tick");
            EnsureUnpatched(method);
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NivarianTurretSleep), nameof(Tail)));
        }
        private static IEnumerable<CodeInstruction> Tail(IEnumerable<CodeInstruction> input, ILGenerator generator, MethodBase __originalMethod)
        {
            var code = input.ToList();
            if (code.Any(c => c.blocks.Count != 0)) throw new InvalidOperationException("Unexpected exception regions");
            var baseMethod = Required(__originalMethod.DeclaringType.BaseType, "Tick");
            int first = code.FindIndex(c => c.opcode != OpCodes.Nop);
            if (first < 0 || first + 2 >= code.Count || code[first].opcode != OpCodes.Ldarg_0 || !code[first + 1].Calls(baseMethod))
                throw new InvalidOperationException("Unexpected base Tick prologue");
            var awake = generator.DefineLabel();
            code[first + 2].labels.Add(awake);
            code.InsertRange(first + 2, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NivarianTurretSleep),
                    __originalMethod.DeclaringType == validatorType ? nameof(SkipValidatorTail) : nameof(SkipRecoilTail))),
                new CodeInstruction(OpCodes.Brfalse, awake), new CodeInstruction(OpCodes.Ret)
            });
            return code;
        }
        // Memoize only this pure angle clamp's INPUTS, not threat or combat state.
        // A cold cache executes the same idempotent clamp; peer cache warmth is irrelevant.
        private static bool ConstrainCone(Building_TurretGun __instance, out ConeState __state)
        {
            __state = null;
            if (!Active(__instance) || __instance.CurrentTarget.IsValid || Warmup(__instance) > 0) return true;
            var props = __instance.AttackVerb?.verbProps;
            if (props == null || !conePropsType.IsInstanceOfType(props)) return true;
            var state = Cones.GetValue(__instance, _ => new ConeState());
            float angle = __instance.Top.CurRotation, center = facing(__instance), half = halfAngle(props);
            if (state.Valid && state.Props == props && state.Angle == angle && state.Facing == center && state.Half == half) return false;
            state.Valid = false; state.Props = props; state.Angle = angle; state.Facing = center; state.Half = half;
            __state = state;
            return true;
        }
        private static void ConeCompleted(Building_TurretGun __instance, ConeState __state)
        {
            // Cache only a proven fixed point, never an input that changed the angle.
            if (__state != null) __state.Valid = __state.Angle == __instance.Top.CurRotation;
        }
        private static bool RotateAnimator(ThingComp __instance, float __0)
        {
            if (!(__instance.parent is Building_TurretGun turret) || !validatorType.IsInstanceOfType(turret) || !Active(turret)) return true;
            var item = animator(__instance);
            if (item == null) return false;
            var obj = gameObject(item);
            if (obj == null) return false;
            var state = Rotations.GetValue(__instance, _ => new RotationState());
            if (!state.Valid || state.Angle != __0)
            {
                state.Valid = true; state.Angle = __0; state.Rotation = Quaternion.Euler(0f, __0, 0f);
            }
            // Read actual rotation: animations, pooled objects and other writers stay authoritative.
            return !obj.transform.rotation.Equals(state.Rotation);
        }
    }
}
