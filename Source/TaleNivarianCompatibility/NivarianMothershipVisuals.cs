using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianMothershipVisuals
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian_Race.Code.NivarianMapComponent.MapComp_MothershipProjection")
                ?? throw new TypeLoadException("Nivarian mothership projection");
            var request = AccessTools.TypeByName("Nivarian_Race.Code.Defs.NivarianMotherShipRequestDef")
                ?? throw new TypeLoadException("Nivarian mothership request");
            var start = AccessTools.DeclaredMethod(type, "StartProjection", new[] { request })
                ?? throw new MissingMethodException(type.FullName, "StartProjection");
            // The local shadow preference controls this call. Its random edge selection must
            // never advance the gameplay stream on only the peer displaying the shadow.
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(NivarianMothershipVisuals), nameof(Begin)),
                finalizer: new HarmonyMethod(typeof(NivarianMothershipVisuals), nameof(End)));
            Log.Message("[TaleNivarianCompat] Mothership shadow randomness isolated from gameplay; local display preference preserved.");
        }
        static void Begin(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }
        static void End(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }
}