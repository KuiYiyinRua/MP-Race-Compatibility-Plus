using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class WoundCacheRandom
    {
        internal static void Apply(Harmony harmony)
        {
            var target = AccessTools.DeclaredMethod(typeof(PawnWoundDrawer), "WriteCache")
                ?? throw new MissingMethodException("PawnWoundDrawer.WriteCache");
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(WoundCacheRandom), nameof(Begin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(WoundCacheRandom), nameof(End)) { priority = Priority.Last });
            // Portrait rendering can also initialize other cold render caches. In-game
            // lovers' motes request portraits from a simulation toil, outside UI scopes.
            var portrait = AccessTools.DeclaredMethod(typeof(PortraitsCache), nameof(PortraitsCache.Get))
                ?? throw new MissingMethodException("PortraitsCache.Get");
            harmony.Patch(portrait,
                prefix: new HarmonyMethod(typeof(WoundCacheRandom), nameof(BeginPortrait)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(WoundCacheRandom), nameof(End)) { priority = Priority.Last });
            Log.Message("[TaleNivarianCompat] wound cache anchor randomness isolated, including portraits requested during simulation.");
        }

        // Vanilla chooses a wound anchor before entering its per-wound random scope.
        // A warm portrait cache skips that draw; a cold cache must not advance map Rand.
        static void Begin(Pawn ___pawn, out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState(Gen.HashCombineInt(0x574F554E, ___pawn.thingIDNumber));
        }
        static void End(bool __state)
        {
            if (__state) Rand.PopState();
        }
        static void BeginPortrait(Pawn __0, out bool __state)
        {
            __state = MP.IsInMultiplayer && __0 != null;
            if (__state) Rand.PushState(Gen.HashCombineInt(0x504F5254, __0.thingIDNumber));
        }
    }
}
