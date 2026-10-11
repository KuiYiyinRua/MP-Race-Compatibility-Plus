using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    // Native TickTimer restores elapsed but resets start to the load tick. Its
    // next TickIntervalDelta then discards all elapsed time from before loading.
    internal static class TickTimerSnapshot
    {
        sealed class Saved { public Saved() { } internal int Start = int.MinValue; }
        static readonly ConditionalWeakTable<TickTimer, Saved> saved = new ConditionalWeakTable<TickTimer, Saved>();
        internal static void Apply(Harmony harmony) => harmony.Patch(
            Bootstrap.Method(typeof(TickTimer), nameof(TickTimer.ExposeData)),
            prefix: new HarmonyMethod(typeof(TickTimerSnapshot), nameof(Before)),
            postfix: new HarmonyMethod(typeof(TickTimerSnapshot), nameof(After)));
        static void Before(TickTimer __instance, int ___start)
        {
            if (!MP.IsInMultiplayer) return;
            var state = saved.GetOrCreateValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving) state.Start = ___start;
            Scribe_Values.Look(ref state.Start, "mpMeowTimerStart", int.MinValue);
        }
        static void After(TickTimer __instance, ref int ___start, int ___elapsed)
        {
            if (!MP.IsInMultiplayer || Scribe.mode != LoadSaveMode.PostLoadInit) return;
            var state = saved.GetOrCreateValue(__instance);
            if (state.Start != int.MinValue) ___start = state.Start;
            // Old saves lack an absolute start. Preserve their recorded elapsed
            // rather than resetting the timer; the unsaved sub-interval is unknown.
            else if (!__instance.Finished) ___start = GenTicks.TicksGame - ___elapsed;
            saved.Remove(__instance);
        }
    }

    // UI reads and printer ticks share an unsaved, 120-tick active-bill cache.
    // Re-evaluate the native predicate each time in MP, preserving its ordering.
    internal static class NivarianBillQuery
    {
        static FieldInfo cached, expires;
        internal static void Apply(Harmony harmony)
        {
            var type = Bootstrap.Type("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianBillDoerCompBase");
            cached = AccessTools.DeclaredField(type, "_cachedActiveBill") ?? throw new MissingFieldException(type.FullName, "_cachedActiveBill");
            expires = Bootstrap.Field(type, "_cacheExpireTick", typeof(int));
            harmony.Patch(Bootstrap.Method(type, "GetActiveBill"),
                prefix: new HarmonyMethod(typeof(NivarianBillQuery), nameof(Before)));
        }
        static void Before(object __instance)
        {
            if (!MP.IsInMultiplayer) return;
            cached.SetValue(__instance, null);
            expires.SetValue(__instance, -1);
        }
    }
}
