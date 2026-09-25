using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class MugirlMilkingVisuals
    {
        internal static void Apply(Harmony harmony)
        {
            var type = Bootstrap.Type("Mugirl.MugirlMilkingAnimation");
            // Start initializes an unsaved visual pulse timer; Tick emits only
            // flecks and updates that timer. Both draw from Verse.Rand inside
            // a pawn job, so each peer can otherwise advance its map stream a
            // different number of times when its local visual state differs.
            foreach (var name in new[] { "Start", "Tick" })
            {
                var method = Bootstrap.Method(type, name, typeof(Pawn), typeof(Pawn));
                if (!method.IsStatic || method.ReturnType != typeof(void))
                    throw new InvalidOperationException("Mugirl visual method signature changed: " + name);
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(MugirlMilkingVisuals), nameof(Before)),
                    finalizer: new HarmonyMethod(typeof(MugirlMilkingVisuals), nameof(After)));
            }
        }

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
