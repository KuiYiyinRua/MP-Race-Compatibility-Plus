using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianDroneOwnership
    {
        sealed class Binding { internal object Hub; }
        static readonly ConditionalWeakTable<object, Binding> restored = new ConditionalWeakTable<object, Binding>();
        static FieldInfo owner, drones;

        internal static void Apply(Harmony harmony)
        {
            var drone = AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneBase") ?? throw new TypeLoadException("NivarianDroneBase");
            var hub = AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneHubComp") ?? throw new TypeLoadException("NivarianDroneHubComp");
            owner = AccessTools.Field(drone, "_owningNivarianHub") ?? throw new MissingFieldException(drone.FullName, "_owningNivarianHub");
            drones = AccessTools.Field(hub, "_spawnedDrones") ?? throw new MissingFieldException(hub.FullName, "_spawnedDrones");
            harmony.Patch(AccessTools.DeclaredMethod(drone, "ExposeData"), postfix: new HarmonyMethod(typeof(NivarianDroneOwnership), nameof(ExposeDrone)));
            harmony.Patch(AccessTools.DeclaredMethod(hub, "PostExposeData"), postfix: new HarmonyMethod(typeof(NivarianDroneOwnership), nameof(ExposeHub)));
            Log.Message("[TaleNivarianCompat] drone ownership restored from saved hub membership.");
        }

        // Native drone ExposeData loses its local holder reference between loading phases.
        // Hub membership is already serialized and also distinguishes multiple hubs on one thing.
        static void ExposeHub(object __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || !(drones.GetValue(__instance) is IEnumerable members)) return;
            foreach (var drone in members)
            {
                if (drone == null) continue;
                restored.GetValue(drone, key => new Binding()).Hub = __instance;
                owner.SetValue(drone, __instance);
            }
        }

        static void ExposeDrone(object __instance)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars) restored.Remove(__instance);
            // Either object can receive PostLoadInit first. Reapply if the hub ran first;
            // otherwise ExposeHub will install the binding later in the same load pass.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && restored.TryGetValue(__instance, out var binding))
                owner.SetValue(__instance, binding.Hub);
        }
    }
}
