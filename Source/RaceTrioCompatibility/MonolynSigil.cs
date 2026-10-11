using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynSigil
    {
        sealed class Capture { internal int ResurrectionStart, WarmupStart; internal bool OriginsSaved; }
        static readonly ConditionalWeakTable<Hediff, Capture> captures = new ConditionalWeakTable<Hediff, Capture>();
        static FieldInfo resurrection, warmup, resurrecting, start;
        static MethodInfo resurrect;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.Hediff_UrielSigil") ?? throw new TypeLoadException("ASEL.Hediff_UrielSigil");
            resurrection = AccessTools.DeclaredField(type, "resurrectTimer");
            warmup = AccessTools.DeclaredField(type, "warmupTimer");
            resurrecting = AccessTools.DeclaredField(type, "resurrecting");
            start = AccessTools.DeclaredField(typeof(TickTimer), "start");
            resurrect = AccessTools.DeclaredMethod(type, "Resurrect", Type.EmptyTypes);
            if (resurrection?.FieldType != typeof(TickTimer) || warmup?.FieldType != typeof(TickTimer) ||
                resurrecting?.FieldType != typeof(bool) || start?.FieldType != typeof(int) || resurrect?.ReturnType != typeof(void))
                throw new InvalidOperationException("Native Uriel timer signatures changed");
            harmony.Patch(AccessTools.DeclaredMethod(type, "ExposeData"), postfix: new HarmonyMethod(typeof(MonolynSigil), nameof(Save)));
            Log.Message("[MonolynSigil] resolved=2 owner timer origins and native pending resurrection callback; missing=0");
        }
        static void Save(Hediff __instance)
        {
            if (!MP.IsInMultiplayer) return;
            var state = captures.GetValue(__instance, _ => new Capture());
            var timer = (TickTimer)resurrection.GetValue(__instance);
            var warmupTimer = (TickTimer)warmup.GetValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                state.ResurrectionStart = (int)start.GetValue(timer);
                state.WarmupStart = (int)start.GetValue(warmupTimer);
                state.OriginsSaved = true;
            }
            Scribe_Values.Look(ref state.OriginsSaved, "meowUrielTimerOriginsSaved");
            Scribe_Values.Look(ref state.ResurrectionStart, "meowUrielResurrectionStart");
            Scribe_Values.Look(ref state.WarmupStart, "meowUrielWarmupStart");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Deep TickTimer objects initialize before their containing
                // hediff. Native TickTimer resets start to the loading clock and
                // omits OnFinish; retain the actual owner-map timer checkpoint.
                if (state.OriginsSaved)
                {
                    start.SetValue(timer, state.ResurrectionStart);
                    start.SetValue(warmupTimer, state.WarmupStart);
                }
                if (!timer.Finished && (bool)resurrecting.GetValue(__instance))
                    timer.OnFinish = (Action)resurrect.CreateDelegate(typeof(Action), __instance);
            }
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.PostLoadInit) captures.Remove(__instance);
        }
    }
}
