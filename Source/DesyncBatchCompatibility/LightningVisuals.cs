using System;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class LightningVisuals
    {
        internal static void Apply(Harmony harmony)
        {
            // All three exact implementations contain ShouldSpawnMotesAt then five Rand draws.
            // Do not enclose LightingSpear.Impact: it also applies real damage.
            foreach (var name in new[] { "AriandelLibrary.Visual_Lightning_Red", "AriandelLibrary.Visual_Lightning_Gold", "AriandelLibrary.Visual_Lightning_Purple" })
            {
                var target = Bootstrap.Method(Bootstrap.Type(name), "ThrowLightningGlow", typeof(Vector3), typeof(Map), typeof(float));
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(LightningVisuals), nameof(Before)),
                    finalizer: new HarmonyMethod(typeof(LightningVisuals), nameof(After)));
            }
            // Desync-148/149: damage text has a separate visibility/saturation gate.
            // Scope only the visual emitter, never Apply/Impact or actual injuries.
            var damageText = Bootstrap.Method(Bootstrap.Type("AriandelLibrary.DamageWorker_AddInjury_NoDamageFactor"),
                "ThrowDamageMote", typeof(Vector3), typeof(Map), typeof(string), typeof(Color));
            harmony.Patch(damageText, prefix: new HarmonyMethod(typeof(LightningVisuals), nameof(Before)),
                finalizer: new HarmonyMethod(typeof(LightningVisuals), nameof(After)));
        }
        internal static void ApplySnowball(Harmony harmony)
        {
            // Desync-151..153: ThrowObjectAt only creates a Fleck; duration, joy,
            // movement and interaction state remain in the original job/toils.
            var target = Bootstrap.Method(Bootstrap.Type("RomanceOnTheRim.JobDriver_SnowballFight"),
                "ThrowObjectAt", typeof(Pawn), typeof(IntVec3), typeof(FleckDef));
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(LightningVisuals), nameof(Before)),
                finalizer: new HarmonyMethod(typeof(LightningVisuals), nameof(After)));
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
