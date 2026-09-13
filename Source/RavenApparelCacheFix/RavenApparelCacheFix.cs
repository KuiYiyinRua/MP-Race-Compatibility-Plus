using System;
using System.Threading;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop.RavenApparelCache
{
    // Loaded by the existing RavenCompatibility conditional load folder.
    [StaticConstructorOnStartup]
    public static class RavenApparelCacheFix
    {
        private static readonly object Gate = new object();

        static RavenApparelCacheFix()
        {
            if (GenCommandLine.CommandLineArgPassed("meowdisableravenapparelcache"))
            {
                Log.Message("[MP-MeowOnlineShop][RavenApparelCache] Disabled by startup argument.");
                return;
            }
            var type = AccessTools.TypeByName("RavenRace.Core.Harmony.Patch_ApparelScaleFix");
            if (type == null) return;
            var method = AccessTools.DeclaredMethod(type, "IsPackOrBelt", new[] { typeof(ThingDef) });
            if (method == null || method.ReturnType != typeof(bool) || !method.IsStatic)
            {
                Log.Warning("[MP-MeowOnlineShop][RavenApparelCache] Expected IsPackOrBelt signature unavailable; guard not applied.");
                return;
            }
            new Harmony("meow.raven.apparelcachefix").Patch(method,
                prefix: new HarmonyMethod(typeof(RavenApparelCacheFix), nameof(Enter)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(RavenApparelCacheFix), nameof(Leave)) { priority = Priority.Last });
            Log.Message("[MP-MeowOnlineShop][RavenApparelCache] Active; original apparel cache access serialized. Target MVID=" + method.Module.ModuleVersionId);
        }

        // Preserve the original cache lifetime, calculation and scale result.
        // The finalizer releases on success or exception, without suppressing it.
        private static void Enter(ref bool __state) { Monitor.Enter(Gate, ref __state); }
        private static void Leave(bool __state) { if (__state) Monitor.Exit(Gate); }
    }
}
