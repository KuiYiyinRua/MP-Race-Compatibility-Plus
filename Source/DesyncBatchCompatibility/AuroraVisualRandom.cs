using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class AuroraVisualRandom
    {
        internal static void Apply(Harmony harmony)
        {
            var method = Bootstrap.Method(typeof(GameCondition_Aurora), "GetNewColorIndex");
            if (method.IsStatic || method.ReturnType != typeof(int) || method.GetParameters().Length != 0)
                throw new InvalidOperationException("Aurora color method signature changed");
            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(AuroraVisualRandom), nameof(Before)),
                finalizer: new HarmonyMethod(typeof(AuroraVisualRandom), nameof(After)));
        }

        // The chosen index is used only for sky/overlay colors. A color
        // transition can occur on one peer without advancing map simulation RNG.
        internal static void Before(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }

        internal static void After(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }
}
