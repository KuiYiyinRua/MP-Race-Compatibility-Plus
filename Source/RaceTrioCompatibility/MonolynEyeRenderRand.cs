using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynEyeRenderRand
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.AnimationWorker_EyeMovement") ?? throw new TypeLoadException("ASEL.AnimationWorker_EyeMovement");
            var data = type.GetNestedType("EyeMovementData", System.Reflection.BindingFlags.NonPublic)
                ?? throw new TypeLoadException("ASEL.AnimationWorker_EyeMovement/EyeMovementData");
            var method = AccessTools.DeclaredMethod(type, "CheckAndMove", new[] { data, typeof(Pawn) })
                ?? throw new MissingMethodException(type.FullName, "CheckAndMove");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(MonolynEyeRenderRand), nameof(Begin)),
                finalizer: new HarmonyMethod(typeof(MonolynEyeRenderRand), nameof(End)));
            Log.Message("[MonolynEyeRenderRand] resolved=native eye-render movement, isolated visual randomness; missing=0");
        }
        static void Begin(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }
        static Exception End(Exception __exception, bool __state)
        {
            if (__state) Rand.PopState();
            return __exception;
        }
    }
}
