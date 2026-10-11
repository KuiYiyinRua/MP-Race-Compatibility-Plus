using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using UnityEngine;

namespace Meow.TaleNivarianCompatibility
{
    // Installed 20260922151508: intercept executors, leaving windows and targeters local.
    internal static class NivarianUpdateActions
    {
        const string Buildings = "Nivarian_Race.Code.Comps.BuildingComps.";
        static Type node, input;
        static PropertyInfo service, enabled;
        static ISyncMethod setToggle;
        static Type animal;
        static MethodInfo clearOrders;
        static FieldInfo animalGizmoComp, radiusWindowParent;
        static ISyncField inputRadius, guardRadius;
        static ISyncMethod cancel;
        static FieldInfo timestampOffset, guardExpires;

        internal static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        internal static MethodInfo RequiredMethod(Type type, string name, params Type[] args) =>
            AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);

        internal static void Apply(Harmony harmony)
        {
            node = RequiredType(Buildings + "CompCannonNode");
            input = RequiredType(Buildings + "CompCannonInput");
            service = AccessTools.Property(node, "AllowContainerService") ?? throw new MissingMemberException(node.FullName, "AllowContainerService");
            enabled = AccessTools.Property(input, "Enabled") ?? throw new MissingMemberException(input.FullName, "Enabled");
            setToggle = MP.RegisterSyncMethod(typeof(NivarianUpdateActions), nameof(SetToggle));
            foreach (var type in new[] { node, input })
                harmony.Patch(RequiredMethod(type, "CompGetGizmosExtra"),
                    postfix: new HarmonyMethod(typeof(NivarianUpdateActions), nameof(Gizmos)));
            RequiredMethod(input, "set_ScanRadius", typeof(float));
            inputRadius = MP.RegisterSyncField(input, "scanRadius").SetBufferChanges();
            var radiusWindow = RequiredType("Nivarian_Race.Code.UI.Dialog_CannonRadiusSettings");
            radiusWindowParent = AccessTools.Field(radiusWindow, "parent") ?? throw new MissingFieldException(radiusWindow.FullName, "parent");
            harmony.Patch(RequiredMethod(radiusWindow, "DoWindowContents", typeof(Rect)),
                prefix: new HarmonyMethod(typeof(NivarianUpdateActions), nameof(BeginInputRadius)),
                finalizer: new HarmonyMethod(typeof(NivarianUpdateActions), nameof(EndWatch)));
            MP.RegisterSyncMethod(RequiredMethod(node, "DropAll", typeof(IntVec3), typeof(Map))).SetDebugOnly();

            animal = RequiredType("Nivarian_Race.Code.Comps.ThingComps.Comp_AnimalOrders");
            RequiredMethod(animal, "SetGuardRadius", typeof(float));
            guardRadius = MP.RegisterSyncField(animal, "guardRadius").SetBufferChanges();
            var animalGizmo = RequiredType("Nivarian_Race.Code.UI.Gizmo_AnimalGuard");
            animalGizmoComp = AccessTools.Field(animalGizmo, "comp") ?? throw new MissingFieldException(animalGizmo.FullName, "comp");
            harmony.Patch(RequiredMethod(animalGizmo, "DrawExtra", typeof(Rect)),
                prefix: new HarmonyMethod(typeof(NivarianUpdateActions), nameof(BeginGuardRadius)),
                finalizer: new HarmonyMethod(typeof(NivarianUpdateActions), nameof(EndWatch)));
            MP.RegisterSyncMethod(RequiredMethod(animal, "SetGuard", typeof(IntVec3))).SetContext(SyncContext.QueueOrder_Down);
            // Vanilla already synchronizes the ordered melee job; this method does not write mod state.
            clearOrders = RequiredMethod(animal, "ClearOrders", typeof(bool));
            cancel = MP.RegisterSyncMethod(typeof(NivarianUpdateActions), nameof(CancelOrders));
            harmony.Patch(RequiredMethod(animal, "CompGetGizmosExtra"),
                postfix: new HarmonyMethod(typeof(NivarianUpdateActions), nameof(AnimalGizmos)));
            timestampOffset = AccessTools.Field(RequiredType("Multiplayer.Client.Patches.TimestampFixer"), "currentOffset")
                ?? throw new MissingFieldException("MP timestamp offset");
            guardExpires = AccessTools.Field(animal, "guardExpiresTick") ?? throw new MissingFieldException(animal.FullName, "guardExpiresTick");
            harmony.Patch(RequiredMethod(animal, "PostExposeData"),
                prefix: new HarmonyMethod(typeof(NivarianUpdateActions), nameof(TransferGuardClock)));
            Log.Message("[TaleNivarianCompat] update actions: cannon toggles/radius/eject and animal guard/radius/cancel resolved.");
        }

        static void TransferGuardClock(object __instance)
        {
            if (!MP.IsInMultiplayer || Scribe.mode != LoadSaveMode.Saving || !(timestampOffset.GetValue(null) is int offset)) return;
            int expires = (int)guardExpires.GetValue(__instance);
            if (expires > 0) guardExpires.SetValue(__instance, expires + offset);
        }

        static void BeginInputRadius(object __instance, out bool __state)
        {
            var parent = radiusWindowParent.GetValue(__instance) as ThingWithComps;
            var comp = parent?.AllComps.FirstOrDefault(c => input.IsInstanceOfType(c));
            __state = MP.IsInMultiplayer && MP.InInterface && comp != null;
            if (__state) { MP.WatchBegin(); inputRadius.Watch(comp); }
        }

        static void BeginGuardRadius(object __instance, out bool __state)
        {
            __state = MP.IsInMultiplayer && MP.InInterface;
            if (__state) { MP.WatchBegin(); guardRadius.Watch(animalGizmoComp.GetValue(__instance)); }
        }

        static Exception EndWatch(Exception __exception, bool __state)
        {
            if (__state) MP.WatchEnd();
            return __exception;
        }

        static void AnimalGizmos(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (MP.IsInMultiplayer && MP.InInterface) __result = WrapAnimal(__instance, __result);
        }

        static IEnumerable<Gizmo> WrapAnimal(ThingComp comp, IEnumerable<Gizmo> gizmos)
        {
            foreach (var gizmo in gizmos)
            {
                if (gizmo is Command_Action action && action.action != null
                    && IsCancel(action.action.Method))
                    action.action = () => cancel.DoSync(null, comp);
                yield return gizmo;
            }
        }
        static readonly Dictionary<MethodInfo, bool> cancelCallbacks = new Dictionary<MethodInfo, bool>();
        static bool IsCancel(MethodInfo callback)
        {
            if (!cancelCallbacks.TryGetValue(callback, out bool result))
                cancelCallbacks[callback] = result = PatchProcessor.GetOriginalInstructions(callback).Any(i => i.Calls(clearOrders));
            return result;
        }

        static void CancelOrders(ThingComp comp)
        {
            if (comp?.parent is Pawn pawn && !pawn.Destroyed && animal.IsInstanceOfType(comp))
            {
                pawn.jobs?.ClearQueuedJobs(true);
                clearOrders.Invoke(comp, new object[] { true });
            }
        }

        static void Gizmos(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (MP.IsInMultiplayer && MP.InInterface) __result = Wrap(__instance, __result);
        }

        static IEnumerable<Gizmo> Wrap(ThingComp comp, IEnumerable<Gizmo> gizmos)
        {
            // The first native toggle is the gameplay setting; the second node toggle is local diagnostics.
            bool first = true;
            foreach (var gizmo in gizmos)
            {
                if (first && gizmo is Command_Toggle toggle)
                {
                    first = false;
                    var property = comp.GetType() == node ? service : enabled;
                    toggle.toggleAction = () => { bool next = !(bool)property.GetValue(comp); bool sent = setToggle.DoSync(null, comp, next); if (GenCommandLine.CommandLineArgPassed("nivarianupdateprobe")) Log.Message("NU_PATCH toggle send=" + sent + " interface=" + MP.InInterface + " thing=" + comp.parent.thingIDNumber + " next=" + next); };
                }
                yield return gizmo;
            }
        }

        static void SetToggle(ThingComp comp, bool value)
        {
            if (GenCommandLine.CommandLineArgPassed("nivarianupdateprobe")) Log.Message("NU_PATCH toggle execute thing=" + comp?.parent?.thingIDNumber + " spawned=" + comp?.parent?.Spawned + " type=" + comp?.GetType().FullName + " value=" + value);
            if (comp?.parent == null || comp.parent.Destroyed || !comp.parent.Spawned) return;
            if (comp.GetType() == node) service.SetValue(comp, value);
            else if (comp.GetType() == input) enabled.SetValue(comp, value);
        }
    }
}


