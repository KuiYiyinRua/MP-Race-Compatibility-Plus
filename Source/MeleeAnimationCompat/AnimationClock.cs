using System;
using System.Collections.Generic;
using System.Linq;
using AM;
using AM.Events;
using AM.Processing;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    // Registered renderers capture pawns and emit damage/jobs. Their clock belongs to
    // the owning map, independent of drawing and the local player's current map.
    [HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.MapComponentTick))]
    internal static class AnimationClock
    {
        [ThreadStatic] internal static bool Advancing;
        private static readonly Action AddPending = AccessTools.MethodDelegate<Action>(
            AccessTools.Method(typeof(AnimRenderer), "AddFromPostLoad"));
        internal static readonly Action<AnimRenderer> DestroyRenderer = AccessTools.MethodDelegate<Action<AnimRenderer>>(
            AccessTools.Method(typeof(AnimRenderer), "DestroyNew"));

        private static void Postfix(AnimationManager __instance)
        {
            if (!Bootstrap.Active) return;
            Advancing = true;
            try
            {
                AddPending();
                var renderers = AnimRenderer.ActiveRenderers
                    .Where(r => r.Map == __instance.map && r.PawnCount > 0)
                    .OrderBy(r => r.Pawns.FirstOrDefault()?.thingIDNumber ?? -1).ToArray();
                foreach (var renderer in renderers)
                {
                    if (renderer.IsDestroyed) continue;
                    renderer.Tick();
                    if (!renderer.IsDestroyed)
                        renderer.Seek(null, 1f / 60f, ev => EventHelper.Handle(ev, renderer));
                }
                // Cleanup can start a successor animation, so process only this map.
                foreach (var renderer in AnimRenderer.ActiveRenderers
                    .Where(r => r.Map == __instance.map && r.IsDestroyed)
                    .OrderBy(r => r.Pawns.FirstOrDefault()?.thingIDNumber ?? -1).ToArray())
                    DestroyRenderer(renderer);
            }
            finally { Advancing = false; }
        }
    }

    [HarmonyPatch(typeof(AnimationManager),nameof(AnimationManager.MapRemoved))]
    internal static class RemovedMapAnimations
    {
        private static readonly Action<AnimRenderer,bool> Interrupt=AccessTools.MethodDelegate<Action<AnimRenderer,bool>>(
            AccessTools.PropertySetter(typeof(AnimRenderer),nameof(AnimRenderer.WasInterrupted)));
        private static void Prefix(AnimationManager __instance)
        {
            if(!Bootstrap.Active) return;
            foreach(var renderer in AnimRenderer.ActiveRenderers.Where(r=>r.Map==__instance.map).ToArray())
            {
                Interrupt(renderer,true);
                renderer.OnEndAction=null; // Removed maps must not spawn successor animations.
                renderer.Destroy();
                AnimationClock.DestroyRenderer(renderer);
            }
        }
    }

    [HarmonyPatch(typeof(AnimRenderer), nameof(AnimRenderer.TickAll))]
    internal static class StopWorldAnimationTick
    {
        private static bool Prefix() => !Bootstrap.Active;
    }

    [HarmonyPatch(typeof(AnimRenderer), nameof(AnimRenderer.DrawAll))]
    internal static class RenderWithoutSimulation
    {
        private static void Prefix(ref Action<AnimRenderer, EventBase> onEvent)
        {
            if (Bootstrap.Active) onEvent = (renderer, ev) => EventHelper.Handle(ev, renderer);
        }
    }

    [HarmonyPatch(typeof(AnimRenderer), nameof(AnimRenderer.Draw))]
    internal static class DrawClockBoundary
    {
        private static void Prefix(AnimRenderer __instance, ref Action<AnimRenderer, EventBase> eventOutput, out bool __state)
        {
            __state = Bootstrap.Active;
            if (!__state) return;
            Rand.PushState();
            if (__instance.PawnCount > 0) eventOutput = null;
        }
        private static void Finalizer(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(AnimationManager), "SeekMultithreaded")]
    internal static class StopRenderSeek
    {
        private static bool Prefix() => !Bootstrap.Active;
    }

    [HarmonyPatch(typeof(AnimRenderer), nameof(AnimRenderer.RemoveDestroyed))]
    internal static class CleanupOnMapTick
    {
        private static bool Prefix() => !Bootstrap.Active || AnimationClock.Advancing;
    }

    [HarmonyPatch(typeof(AnimRenderer), "AddFromPostLoad")]
    internal static class LoadBeforeSimulation
    {
        private static readonly Action<AnimRenderer> Apply = AccessTools.MethodDelegate<Action<AnimRenderer>>(
            AccessTools.Method(typeof(AnimRenderer), "ApplyPostLoad"));
        private static bool Prefix()
        {
            if (!Bootstrap.Active) return true;
            if (!AnimationClock.Advancing) return false;
            foreach (var renderer in AnimRenderer.PostLoadPendingAnimators) Apply(renderer);
            foreach (var renderer in AnimRenderer.PostLoadPendingAnimators
                .OrderBy(r => r.Map?.uniqueID ?? -1).ThenBy(r => r.Pawns.FirstOrDefault()?.thingIDNumber ?? -1))
                renderer.Register();
            AnimRenderer.PostLoadPendingAnimators.Clear();
            return false;
        }
    }

    [HarmonyPatch(typeof(MapPawnProcessor), nameof(MapPawnProcessor.MakeProcessingSlices))]
    internal static class OrderedAutoExecution
    {
        private static bool Prefix(int todo, ref IEnumerable<IntRange> __result)
        {
            if (!Bootstrap.Active) return true;
            __result = todo > 0 ? new[] { new IntRange(0, todo - 1) } : Array.Empty<IntRange>();
            return false;
        }
    }
}
