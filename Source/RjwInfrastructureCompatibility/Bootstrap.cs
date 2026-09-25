using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RjwInfrastructure
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            if (!MP.enabled) return;
            var harmony=new Harmony("meow.rjw.infrastructure");
            MP_MeowOnlineShop.Patch_RjwAnimationSoundRandIsolation.Apply(harmony);
            DeterministicRandomUtility.Apply(harmony);
            ComponentInitialization.Apply(harmony);
            CalendarFactionContext.Apply(harmony);
            EventMapContext.Apply(harmony);
            var assembly = typeof(Bootstrap).Assembly;
            Log.Message("[Meow.RjwInfrastructure] version=" + assembly.GetName().Version +
                " mvid=" + assembly.ManifestModule.ModuleVersionId);
        }
    }
}
