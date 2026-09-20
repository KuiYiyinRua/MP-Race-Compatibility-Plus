using System;
using HarmonyLib;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianLifespanState
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.Comp_NivarianLifespanTicking")
                ?? throw new TypeLoadException("Nivarian lifespan component");
            var expose = AccessTools.DeclaredMethod(type, "PostExposeData", Type.EmptyTypes)
                ?? throw new MissingMethodException(type.FullName, "PostExposeData");
            harmony.Patch(expose, prefix: new HarmonyMethod(typeof(NivarianLifespanState), nameof(BeforeExpose)));
            Log.Message("[TaleNivarianCompat] Nivarian lifespan load-phase guard installed.");
        }

        static bool BeforeExpose()
        {
            // Native legacy migration uses local defaults when Look is a no-op.
            // Repeating it during cross-reference resolution/PostLoadInit resets the
            // restored lifetime. Keep the existing XML keys and LoadingVars migration.
            return Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.LoadingVars;
        }
    }
}
