using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

internal static class SpeechWatch
{
    static readonly PropertyInfo state = AccessTools.Property(typeof(Rand), "StateCompressed");
    static int calls;
    internal static void Install(Harmony h)
    {
        var type = AccessTools.TypeByName("NivarianRace.DraconicMilitary.EngineeringDroneThing");
        var method = AccessTools.DeclaredMethod(type, "TickThrowWords");
        h.Patch(method, prefix: new HarmonyMethod(typeof(SpeechWatch), nameof(Before)) {priority=Priority.First},
            finalizer: new HarmonyMethod(typeof(SpeechWatch), nameof(After)) {priority=Priority.Last});
        Log.Message("DRACO_REPLAY SPEECH_TARGET MVID=" + type.Module.ModuleVersionId);
    }
    static void Before(out ulong __state) { __state=(ulong)state.GetValue(null); }
    static void After(ulong __state)
    {
        if (!MP.IsInMultiplayer) return;
        if (++calls <= 3) Log.Message("DRACO_REPLAY NATIVE_SPEECH before="+__state+" after="+state.GetValue(null));
        if (__state!=(ulong)state.GetValue(null)) throw new InvalidOperationException("SPEECH_SHARED_RAND_LEAK");
    }
    internal static void AssertReached()
    {
        if(calls<3) throw new InvalidOperationException("Native engineer speech not exercised");
        Log.Message("DRACO_REPLAY SPEECH_CHECK calls="+calls+" rand=preserved");
    }
}
