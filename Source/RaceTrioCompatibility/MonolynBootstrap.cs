using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    // Separate startup owner: failure in an unrelated race must not skip Codex sync.
    [StaticConstructorOnStartup]
    internal static class MonolynBootstrap
    {
        static int Resolved, Missing;
        static MonolynBootstrap()
        {
            if (!MP.enabled) return;
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("races") && ModsConfig.IsActive("ASEL.MonolynRace"))
            {
                MonolynProduction.Apply();
                MonolynUi.Apply(new Harmony("meow.trio.monolyn-ui"));
                Determinism.Monolyn(new Harmony("meow.trio.monolyn-random"));
                MonolynActions.Apply(new Harmony("meow.trio.monolyn-actions"));
                MonolynCodex.Apply(new Harmony("meow.trio.monolyn-codex"));
                MonolynState.Apply(new Harmony("meow.trio.monolyn-state"));
                MonolynSight.Apply(new Harmony("meow.trio.monolyn-sight"));
                MonolynEffects.Apply(new Harmony("meow.trio.monolyn-effects"));
                MonolynDelayed.Apply(new Harmony("meow.trio.monolyn-delayed"));
                MonolynCoverage.Apply(new Harmony("meow.trio.monolyn-coverage"));
                MonolynOrdering.Apply(new Harmony("meow.trio.monolyn-ordering"));
                MonolynSettings.Apply(new Harmony("meow.trio.monolyn-settings"));
                MonolynRetainedRoots.Apply(new Harmony("meow.trio.monolyn-retained-roots"));
                MonolynLanternCanonical.Initialize();
                MonolynFlareCache.Apply(new Harmony("meow.trio.monolyn-flare-cache"));
                MonolynSwapTarget.Apply(new Harmony("meow.trio.monolyn-swap-target"));
                MonolynEyeRenderRand.Apply(new Harmony("meow.trio.monolyn-eye-render-rand"));
                MonolynSchedule.Apply(new Harmony("meow.trio.monolyn-schedule"));
                MonolynRoots.Apply(new Harmony("meow.trio.monolyn-roots"));
                MonolynProjectiles.Apply(new Harmony("meow.trio.monolyn-projectiles"));
                MonolynProgress.Apply(new Harmony("meow.trio.monolyn-progress"));
                MonolynAbilityBoundary.Apply(new Harmony("meow.trio.monolyn-ability-boundary"));
                MonolynVerbs.Apply(new Harmony("meow.trio.monolyn-verbs"));
                MonolynStages.Apply(new Harmony("meow.trio.monolyn-stages"));
                MonolynLanternCache.Apply(new Harmony("meow.trio.monolyn-lantern-cache"));
                // Replay the entire activation callback, including PowerOn/PowerOff.
                Register("ASEL.MonolynConsumer", "<GetGizmos>b__20_1");
                Register("ASEL.AstralBeacon", "<GetGizmos>b__11_1");
                Register("ASEL.Building_TowerOfLight", "<GetGizmos>b__15_2");
                Register("ASEL.CompForgeArray", "<CompGetGizmosExtra>b__10_1");
                Register("ASEL.Comp_MNC", "<CompGetGizmosExtra>b__9_1");
                Register("ASEL.CompAbilityEffect_DistributorBeam", "<CompGetGizmosExtra>b__8_1");
                Register("ASEL.HediffComp_AutoUseThermalSlash", "<CompGetGizmos>b__4_1");
                Register("ASEL.Building_GravityPillar", "<GetGizmos>b__12_0");
                Register("ASEL.Building_MNGravCannon", "<GetGizmos>b__8_0");
                Register("ASEL.Extractor", "<GetGizmos>b__15_1");
                Register("ASEL.Extractor", "EjectContents");
            }
            Log.Message("[MonolynBootstrap] resolved=" + Resolved + " missing=" + Missing
                + " MVID=" + typeof(MonolynBootstrap).Assembly.ManifestModule.ModuleVersionId);
        }

        static void Register(string typeName, string methodName, params Type[] parameters)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                MethodInfo method = type == null ? null : AccessTools.DeclaredMethod(type, methodName, parameters);
                if (method == null) throw new MissingMethodException(typeName, methodName);
                MP.RegisterSyncMethod(method, null);
                Resolved++;
            }
            catch (Exception e)
            {
                Missing++;
                Log.Error("[MonolynBootstrap] Required target failed " + typeName + "." + methodName + ": " + e);
            }
        }
    }
}
