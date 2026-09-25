using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.PerformanceCompatibility
{
    // Mod constructors run before StaticConstructorOnStartup. Esmolas chooses
    // its installed patches and modifies StatDefs during that later phase.
    public sealed class PerformanceCompatibilityMod : Mod
    {
        internal static readonly Harmony Patcher = new Harmony("meow.performancecompatibility");

        public PerformanceCompatibilityMod(ModContentPack content) : base(content)
        {
            if (!ModsConfig.IsActive("rwmt.Multiplayer")) return;
            EsmolasCompatibility.InstallEarly();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    BuildingIndex.Install();
                    KingfisherCompatibility.Install();
                    EsmolasCompatibility.InstallLate();
                    Log.Message("[Meow.Performance] READY version=1.0.0 mvid=" +
                        typeof(PerformanceCompatibilityMod).Assembly.ManifestModule.ModuleVersionId);
                }
                catch (Exception e)
                {
                    Log.Error("[Meow.Performance] REQUIRED_TARGET_FAILED " + e);
                }
            });
        }

        internal static MethodInfo Require(string type, string method, Type[] args = null)
        {
            var t = AccessTools.TypeByName(type);
            var result = t == null ? null : AccessTools.DeclaredMethod(t, method, args);
            return result ?? throw new MissingMethodException(type, method);
        }

        internal static void Prefix(MethodInfo target, Type patch, string method)
        {
            Patcher.Patch(target, prefix: new HarmonyMethod(patch, method) { priority = Priority.First });
        }
    }
}
