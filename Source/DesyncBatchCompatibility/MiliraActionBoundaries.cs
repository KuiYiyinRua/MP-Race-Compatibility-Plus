using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class MiliraActionBoundaries
    {
        private sealed class Curve
        {
            internal FieldInfo once, offset;
            internal MethodInfo randomize;
        }
        private static readonly Dictionary<Type, Curve> curves = new Dictionary<Type, Curve>();
        private static Type unscribedPlane;
        private static readonly Dictionary<string, FieldInfo> planeFields = new Dictionary<string, FieldInfo>();
        internal sealed class DrawState
        {
            internal FieldInfo once, offset;
            internal object oldOnce, oldOffset;
        }

        internal static void ApplyAirstrike(Harmony harmony)
        {
            var plane = Bootstrap.Type("PLAMilira.PLAMilira_Plane");
            harmony.Patch(AccessTools.DeclaredPropertyGetter(plane, "UpdateRateTicks") ??
                throw new MissingMethodException(plane.FullName, "UpdateRateTicks"),
                prefix: new HarmonyMethod(typeof(ExpandableProjectileRate), "Rate"));
            foreach (var name in new[] { "Map_Sweep_Plane", "Map_Sweep_Plane_Strafing",
                "Projectile_TrackingBullet", "Projectile_TrackingBulletNormal", "Projectile_Plane", "Projectile_WASP" })
            {
                var type = Bootstrap.Type("PLAMilira." + name);
                var curve = new Curve {
                    once = AccessTools.DeclaredField(type, "OnceFlag"),
                    offset = AccessTools.DeclaredField(type, "RandNew"),
                    randomize = Bootstrap.Method(type, "RandFactor")
                };
                if (curve.once?.FieldType != typeof(bool) || curve.offset?.FieldType != typeof(Vector3))
                    throw new MissingFieldException(type.FullName, "curve state");
                curves.Add(type, curve);
                var launch = AccessTools.GetDeclaredMethods(type).Single(m => m.Name == "Launch");
                harmony.Patch(launch, postfix: Patch(nameof(InitializeCurve)));
                harmony.Patch(Bootstrap.Method(type, "DrawAt", typeof(Vector3), typeof(bool)),
                    prefix: Patch(nameof(BeforeDraw)), finalizer: Patch(nameof(AfterDraw)));
            }
            // Cached mote lifetime/visibility is local; it must not change the
            // stream used by DoSweepStrike in the same simulation tick.
            harmony.Patch(Bootstrap.Method(Bootstrap.Type("PLAMilira.PLAmiliraUtility"), "ThrowText",
                typeof(Vector3), typeof(Map), typeof(string), typeof(Color), typeof(float)),
                prefix: new HarmonyMethod(typeof(LightningVisuals), "Before"),
                finalizer: new HarmonyMethod(typeof(LightningVisuals), "After"));
            harmony.Patch(Bootstrap.Method(Bootstrap.Type("PLAMilira.MapSweepStrike"), "SpawnSetup", typeof(Map), typeof(bool)),
                transpiler: Patch(nameof(SoundSelection)));
            // Unlike the other five native curve types, Projectile_Plane does
            // not save any of its trajectory state. A joining client must not
            // initialize a new curve for a projectile already in flight.
            unscribedPlane = Bootstrap.Type("PLAMilira.Projectile_Plane");
            foreach (var pair in new[] {
                new KeyValuePair<string, Type>("OnceFlag", typeof(bool)),
                new KeyValuePair<string, Type>("RandNew", typeof(Vector3)),
                new KeyValuePair<string, Type>("Tickcount", typeof(int)),
                new KeyValuePair<string, Type>("Lastposition", typeof(Vector3)),
                new KeyValuePair<string, Type>("offsetSet", typeof(bool)),
                new KeyValuePair<string, Type>("pawnOffset", typeof(Vector3)),
                new KeyValuePair<string, Type>("RandRangeX", typeof(float)),
                new KeyValuePair<string, Type>("RandRangeZ", typeof(float)),
                new KeyValuePair<string, Type>("trackingEnabled", typeof(bool)) })
                planeFields.Add(pair.Key, Bootstrap.Field(unscribedPlane, pair.Key, pair.Value));
            if (AccessTools.DeclaredMethod(unscribedPlane, "ExposeData") != null)
                throw new InvalidOperationException("Projectile_Plane now has native serialization; review compatibility");
            harmony.Patch(Bootstrap.Method(typeof(Projectile_Explosive), "ExposeData"), postfix: Patch(nameof(ExposePlane)));
        }

        private static IEnumerable<CodeInstruction> SoundSelection(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (var instruction in instructions) {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(GenCollection) &&
                    method.Name == "RandomElement" && method.IsGenericMethod &&
                    method.GetGenericArguments().SequenceEqual(new[] { typeof(SoundDef) })) {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(MiliraActionBoundaries), nameof(RandomSound));
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Harrier sound selection IL changed");
        }
        private static SoundDef RandomSound(IEnumerable<SoundDef> sounds)
        {
            if (!MP.IsInMultiplayer) return sounds.RandomElement();
            Rand.PushState();
            try { return sounds.RandomElement(); }
            finally { Rand.PopState(); }
        }
        private static void SavePlaneField<T>(object plane, string name, T defaultValue = default(T))
        {
            var field = planeFields[name];
            T value = (T)field.GetValue(plane);
            Scribe_Values.Look(ref value, "meowPlane_" + name, defaultValue);
            field.SetValue(plane, value);
        }
        private static void ExposePlane(object __instance)
        {
            if (__instance.GetType() != unscribedPlane) return;
            SavePlaneField<bool>(__instance, "OnceFlag");
            SavePlaneField<Vector3>(__instance, "RandNew");
            SavePlaneField<int>(__instance, "Tickcount");
            SavePlaneField<Vector3>(__instance, "Lastposition");
            SavePlaneField<bool>(__instance, "offsetSet");
            SavePlaneField<Vector3>(__instance, "pawnOffset");
            SavePlaneField(__instance, "RandRangeX", 20f);
            SavePlaneField(__instance, "RandRangeZ", 20f);
            SavePlaneField<bool>(__instance, "trackingEnabled");
        }

        private static Curve GetCurve(object obj)
        {
            for (var type = obj.GetType(); type != null; type = type.BaseType)
                if (curves.TryGetValue(type, out var curve)) return curve;
            throw new InvalidOperationException("Unknown airstrike curve type");
        }

        private static void InitializeCurve(object __instance)
        {
            if (!MP.IsInMultiplayer) return;
            var curve = GetCurve(__instance);
            if (!(bool)curve.once.GetValue(__instance)) curve.randomize.Invoke(__instance, null);
        }

        private static void BeforeDraw(object __instance, out DrawState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer) return;
            var curve = GetCurve(__instance);
            // Also protect a projectile restored from an older save before its
            // first tick. Drawing may sample a preview, never commit trajectory.
            __state = new DrawState { once = curve.once, offset = curve.offset,
                oldOnce = curve.once.GetValue(__instance), oldOffset = curve.offset.GetValue(__instance) };
            Rand.PushState();
        }

        private static void AfterDraw(object __instance, DrawState __state)
        {
            if (__state == null) return;
            try {
                __state.once.SetValue(__instance, __state.oldOnce);
                __state.offset.SetValue(__instance, __state.oldOffset);
            } finally { Rand.PopState(); }
        }

        private static Type hairType;
        private static FieldInfo number, hairColor;
        private static MethodInfo changeGraphic;
        internal static void ApplyHair(Harmony harmony)
        {
            hairType = Bootstrap.Type("Milira.CompMilianHairSwitch");
            number = AccessTools.Field(hairType, "num");
            hairColor = AccessTools.Field(hairType, "colorOverride");
            changeGraphic = Bootstrap.Method(hairType, "ChangeGraphic", typeof(int));
            if (number?.FieldType != typeof(int) || hairColor?.FieldType != typeof(Color))
                throw new MissingFieldException(hairType.FullName, "hair state");
            MP.RegisterSyncMethod(typeof(MiliraActionBoundaries), nameof(SetHairStyle));
            MP.RegisterSyncMethod(typeof(MiliraActionBoundaries), nameof(SetHairColor));
            harmony.Patch(Bootstrap.Method(Bootstrap.Type("Milira.Dialog_MilianHairStyleConfig"),
                "DrawScrollHairSwitch", typeof(Pawn), typeof(Rect)), transpiler: Patch(nameof(HairSelection)));
            // Resolve the actual instance callback by its verified signature;
            // the window-opening callback has no Color parameter.
            var callback = AccessTools.GetDeclaredMethods(hairType).Single(m =>
                m.Name.StartsWith("<CompGetGizmosExtra>") && m.ReturnType == typeof(void) &&
                m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Color) }));
            harmony.Patch(callback, prefix: Patch(nameof(QueueColor)));
        }

        internal static IEnumerable<CodeInstruction> HairSelection(IEnumerable<CodeInstruction> instructions)
        {
            int calls = 0, writes = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(changeGraphic)) {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(MiliraActionBoundaries), nameof(SelectHair));
                    calls++;
                } else if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, number)) {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(MiliraActionBoundaries), nameof(LocalHairNumber));
                    writes++;
                }
                yield return instruction;
            }
            if (calls != 1 || writes != 1) throw new InvalidOperationException("Milian hair UI IL changed");
        }

        private static void SelectHair(ThingComp comp, int index)
        {
            if (MP.IsInMultiplayer) SetHairStyle(comp.parent as Pawn, index);
            else changeGraphic.Invoke(comp, new object[] { index });
        }
        private static void LocalHairNumber(ThingComp comp, int index)
        {
            if (!MP.IsInMultiplayer) number.SetValue(comp, index);
        }
        private static ThingComp Hair(Pawn pawn) => pawn?.AllComps.FirstOrDefault(c => hairType.IsInstanceOfType(c));
        public static void SetHairStyle(Pawn pawn, int index)
        {
            var comp = Hair(pawn);
            if (comp == null || pawn.Destroyed || index < 0) return;
            var paths = AccessTools.Property(hairType, "frontHairPaths").GetValue(comp) as System.Collections.IList;
            if (paths == null || index >= paths.Count) return;
            number.SetValue(comp, index);
            changeGraphic.Invoke(comp, new object[] { index });
            Dirty(pawn);
        }
        private static bool QueueColor(ThingComp __instance, Color __0)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            SetHairColor(__instance.parent as Pawn, __0);
            return false;
        }
        public static void SetHairColor(Pawn pawn, Color color)
        {
            var comp = Hair(pawn);
            if (comp == null || pawn.Destroyed) return;
            hairColor.SetValue(comp, color);
            Dirty(pawn);
        }
        private static void Dirty(Pawn pawn)
        {
            pawn.Drawer?.renderer?.renderTree?.SetDirty();
            PortraitsCache.SetDirty(pawn);
        }
        private static HarmonyMethod Patch(string name) => new HarmonyMethod(typeof(MiliraActionBoundaries), name);
    }
}
