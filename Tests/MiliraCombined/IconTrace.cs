using System;
using HarmonyLib;
using Verse;

internal static class MiliraIconTrace
{
    private static bool installed;
    internal static void Init(Harmony harmony)
    {
        if (!GenCommandLine.CommandLineArgPassed("miliraicontrace") || installed) return;
        var method = AccessTools.DeclaredMethod(typeof(BuildableDef), "ResolveIcon");
        if (method == null) throw new MissingMethodException("BuildableDef.ResolveIcon");
        harmony.Patch(method, finalizer: new HarmonyMethod(typeof(MiliraIconTrace), nameof(FinalizeIcon)));
        installed = true;
        Log.Message("MILIRA_ICON_TRACE installed BuildableDef.ResolveIcon finalizer");
    }

    private static Exception FinalizeIcon(Exception __exception, BuildableDef __instance)
    {
        if (__exception != null)
            Log.Error("MILIRA_ICON_TRACE_EXCEPTION defName=" + (__instance?.defName ?? "<null>") +
                " type=" + (__instance?.GetType().FullName ?? "<null>") +
                " mod=" + (__instance?.modContentPack?.Name ?? "<null>") +
                " exception=" + __exception.GetType().FullName);
        return __exception;
    }
}

// Mod constructors run during play-data loading, before ThingDef.PostLoad. The
// later StaticConstructorOnStartup probe is too late to identify that failure.
public sealed class MiliraEarlyIconTraceMod : Mod
{
    public MiliraEarlyIconTraceMod(ModContentPack content) : base(content)
    {
        if (GenCommandLine.CommandLineArgPassed("miliraicontrace"))
            MiliraIconTrace.Init(new Harmony("local.meow.milira.icontrace.early"));
    }
}
