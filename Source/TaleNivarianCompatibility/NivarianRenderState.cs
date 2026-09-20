using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianRenderState
    {
        static FieldInfo[] turretFloats, droneFloats;
        static FieldInfo swayTicks, swayActive, visualOffset, turretId;
        public struct PositionState { public bool Restore; public int Tick; public Vector3 Position; }
        public struct RotationState { public bool Restore; public float Rotation; }
        static FieldInfo Required(Type t, string n) => AccessTools.Field(t, n) ?? throw new MissingFieldException(t.FullName, n);
        internal static void Apply(Harmony harmony)
        {
            var turret = AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret") ?? throw new TypeLoadException("Nivarian turret");
            var drone = AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneBase") ?? throw new TypeLoadException("Nivarian drone");
            turretFloats = Array.ConvertAll(new[] { "_curRotation", "_targetRotation", "_curRecoilDistance" }, n => Required(turret, n));
            droneFloats = Array.ConvertAll(new[] { "_cachedRotation", "_windSwayPhase", "_swayPhaseX", "_swayPhaseY", "_swayPhaseZ" }, n => Required(drone, n));
            swayTicks = Required(drone, "_swayLocalTicks"); swayActive = Required(drone, "_windSwayActive"); visualOffset = Required(drone, "_visualOffsets");
            turretId = Required(AccessTools.Property(turret, "Props").PropertyType, "turretID");
            Required(turret, "_cachedRenderingPos"); Required(turret, "_cachedRenderingPosTick");
            Required(turret, "_lastAttackedTarget"); Required(turret, "_lastAttackTargetTick");
            harmony.Patch(AccessTools.PropertyGetter(turret, "GetRenderingPos"), prefix: Hook(nameof(BeforePosition)), finalizer: Hook(nameof(AfterPosition)));
            harmony.Patch(AccessTools.PropertyGetter(drone, "DrawRotation"), prefix: Hook(nameof(BeforeRotation)), finalizer: Hook(nameof(AfterRotation)));
            harmony.Patch(AccessTools.DeclaredMethod(turret, "PostExposeData"), postfix: Hook(nameof(ExposeTurret)));
            harmony.Patch(AccessTools.DeclaredMethod(drone, "ExposeData"), postfix: Hook(nameof(ExposeDrone)));
            Log.Message("[TaleNivarianCompat] turret/drone drawing caches isolated; rotation/recoil/sway save state installed.");
        }
        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(NivarianRenderState), name);

        // Drawing gets its normal position but cannot overwrite the cache used by simulation.
        // Harmony injects direct field references: no reflection, allocation, or collection scan here.
        static void BeforePosition(ref Vector3 ____cachedRenderingPos, ref int ____cachedRenderingPosTick, out PositionState __state)
        {
            __state = default;
            if (!MP.IsInMultiplayer || !MP.InInterface) return;
            __state = new PositionState { Restore = true, Position = ____cachedRenderingPos, Tick = ____cachedRenderingPosTick };
            ____cachedRenderingPosTick = -1;
        }
        static void AfterPosition(ref Vector3 ____cachedRenderingPos, ref int ____cachedRenderingPosTick, PositionState __state)
        {
            if (!__state.Restore) return;
            ____cachedRenderingPos = __state.Position; ____cachedRenderingPosTick = __state.Tick;
        }
        static void BeforeRotation(float ____cachedRotation, out RotationState __state)
        {
            __state = new RotationState { Restore = MP.IsInMultiplayer && MP.InInterface, Rotation = ____cachedRotation };
        }
        static void AfterRotation(ref float ____cachedRotation, RotationState __state)
        {
            if (__state.Restore) ____cachedRotation = __state.Rotation;
        }
        static void ExposeFloats(object instance, FieldInfo[] fields, string prefix)
        {
            foreach (var field in fields)
            {
                float value = (float)field.GetValue(instance);
                Scribe_Values.Look(ref value, prefix + field.Name, 0f);
                if (Scribe.mode == LoadSaveMode.LoadingVars) field.SetValue(instance, value);
            }
        }
        static void ExposeTurret(object __instance, ref int ____cachedRenderingPosTick,
            ref LocalTargetInfo ____lastAttackedTarget, ref int ____lastAttackTargetTick)
        {
            // Multiple turrets share their parent's Scribe node, as in the original implementation.
            string id = (string)turretId.GetValue(((ThingComp)__instance).props) ?? "anon_";
            ExposeFloats(__instance, turretFloats, "meowNivarianTurret_" + id);
            Scribe_TargetInfo.Look(ref ____lastAttackedTarget, "meowNivarianTurret_" + id + "lastAttackedTarget");
            Scribe_Values.Look(ref ____lastAttackTargetTick, "meowNivarianTurret_" + id + "lastAttackTargetTick");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) ____cachedRenderingPosTick = -1;
        }
        static void ExposeDrone(object __instance)
        {
            ExposeFloats(__instance, droneFloats, "meowNivarianDrone");
            int ticks = (int)swayTicks.GetValue(__instance); bool active = (bool)swayActive.GetValue(__instance);
            Vector3 offset = (Vector3)visualOffset.GetValue(__instance);
            Scribe_Values.Look(ref ticks, "meowNivarianSwayTicks");
            Scribe_Values.Look(ref active, "meowNivarianSwayActive");
            Scribe_Values.Look(ref offset, "meowNivarianVisualOffset");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                swayTicks.SetValue(__instance, ticks); swayActive.SetValue(__instance, active); visualOffset.SetValue(__instance, offset);
            }
        }
    }
}
