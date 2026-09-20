using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
[assembly: InternalsVisibleTo("Meow.RavenTestHarness")]
namespace MP_MeowOnlineShop
{
 [StaticConstructorOnStartup]
 public static class RavenCompatibilityBootstrap
 {
  static RavenCompatibilityBootstrap()
  {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("raven")) return;
   Patch_RavenIndustrialActions.Apply(new Harmony("meow.raven.compatibility"));
   if (ModsConfig.IsActive("ZuoYao.RavenRace")) Log.Message("[RAVEN-COMPAT] core MVID=" + typeof(RavenCompatibilityBootstrap).Assembly.ManifestModule.ModuleVersionId);
  }
 }
}
