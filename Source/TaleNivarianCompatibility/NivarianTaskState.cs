using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianTaskState
    {
        static FieldInfo direction;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.ThingTask.CircleTask")
                ?? throw new TypeLoadException("Nivarian.ThingTask.CircleTask");
            direction = AccessTools.DeclaredField(type, "_counterClockWise")
                ?? throw new MissingFieldException(type.FullName, "_counterClockWise");
            harmony.Patch(AccessTools.DeclaredMethod(type, "ExposeData", Type.EmptyTypes)
                ?? throw new MissingMethodException(type.FullName, "ExposeData"),
                postfix: new HarmonyMethod(typeof(NivarianTaskState), nameof(ExposeDirection)));
            Log.Message("[TaleNivarianCompat] Drone circle task direction persistence installed.");
        }

        static void ExposeDirection(object __instance)
        {
            bool value = (bool)direction.GetValue(__instance);
            // Legacy saves contain no direction; retain the native deserialization default.
            Scribe_Values.Look(ref value, "meowCircleCounterClockWise", false);
            direction.SetValue(__instance, value);
        }
    }
}
