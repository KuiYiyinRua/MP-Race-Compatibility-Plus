using System;
using Multiplayer.Client;
using RimWorld.Planet;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;

namespace Meow.ParallelRenderCompatibility
{
    // Rendering only. A worker must never see another worker's pawn or clear
    // its marker. Harmony's __state also restores nested/throwing draw scopes.
    internal static class RenderContext
    {
        [ThreadStatic] private static Pawn calculating;
        internal static void Enter(Pawn ___pawn, out Pawn __state)
        {
            __state=calculating;
            calculating=___pawn;
        }
        internal static void Exit(Pawn __state) { calculating=__state; }
        internal static void Rate(ref float __result)
        {
            var pawn=calculating;
            if(pawn==null || Mp.Client==null || WorldRendererUtility.WorldSelected)return;
            var map=pawn.Map ?? Find.CurrentMap;
            var time=map?.AsyncTime();
            if(time==null)return;
            var speed=Mp.IsReplay?TickPatch.replayTimeSpeed:time.DesiredTimeSpeed;
            __result=TickPatch.Simulating?6:time.ActualRateMultiplier(speed);
        }
    }
}
