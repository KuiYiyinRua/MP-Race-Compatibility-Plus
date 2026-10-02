using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        public static bool Ready;
        static Bootstrap()
        {
            if (MP.enabled) LongEventHandler.ExecuteWhenFinished(Install);
        }

        static void Install()
        {
            bool ok = true;
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira") &&
                !ModsConfig.IsActive("usamiseika.fixmod.miliramultiplayer"))
            {
                if (ModsConfig.IsActive("sleepycot.wingsofdemocracy"))
                    ok &= InstallGroup("airstrike-simulation", MiliraActionBoundaries.ApplyAirstrike);
                if (ModsConfig.IsActive("Ancot.MiliraRace"))
                    ok &= InstallGroup("milian-hair-actions", MiliraActionBoundaries.ApplyHair);
            }
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira") && ModsConfig.IsActive("Ariandel.MiliraImperium") &&
                !ModsConfig.IsActive("usamiseika.fixmod.miliramultiplayer"))
            {
                ok &= InstallGroup("imperium-donation", ImperiumDonation.Apply);
                ok &= InstallGroup("imperium-console-actions", ImperiumConsoleActions.Apply);
            }
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("core") && ModsConfig.IsActive("Chezhou.ChezhouLib.lib"))
                ok &= InstallGroup("flight-simulation-boundary", FlightSimulationBoundary.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("rjw") && ModsConfig.IsActive("TeheeItsMe525.RJWGenderOrgansMod"))
                ok &= InstallGroup("ballz-animal-name", LoggedBoundaryFailures.ApplyBallz);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual") && ModsConfig.IsActive("ZuoYao.RavenRace"))
                ok &= InstallGroup("raven-disposed-effect-map", LoggedBoundaryFailures.ApplyRaven);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("core") && ModsConfig.IsActive("OskarPotocki.VanillaFactionsExpanded.Core"))
                ok &= InstallGroup("expandable-projectile-rate", ExpandableProjectileRate.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual") && ModsConfig.IsActive("Chezhou.ChezhouLib.lib"))
                ok &= InstallGroup("flight-fleck-random", LightningVisuals.ApplyFlight);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual") && ModsConfig.IsActive("Ariandel.AriandelLibrary"))
                ok &= InstallGroup("ariandel-local-music-random", AriandelMusicRandom.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("core")) ok &= InstallGroup("lazy-caches", LazySimulationCaches.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("core")) ok &= InstallGroup("world-component-clock", WorldComponentClock.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual") &&
                (ModsConfig.IsActive("telardo.RomanceOnTheRim.chillkill190.pe") || ModsConfig.IsActive("telardo.RomanceOnTheRim")))
                ok &= InstallGroup("snowball-visual", LightningVisuals.ApplySnowball);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("races") && ModsConfig.IsActive("HAR.MugirlRace"))
                ok &= InstallGroup("mugirl-dismount", MugirlDismount.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual") && ModsConfig.IsActive("HAR.MugirlRace"))
                ok &= InstallGroup("mugirl-milking-visual-random", MugirlMilkingVisuals.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual"))
                ok &= InstallGroup("aurora-visual-random", AuroraVisualRandom.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("nivarian") && ModsConfig.IsActive("keeptpa.NivarianRace"))
                ok &= InstallGroup("nivarian-transient-trails", NivarianTrailBoundary.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira") && ModsConfig.IsActive("Ancot.MiliraRace") &&
                !ModsConfig.IsActive("usamiseika.fixmod.miliramultiplayer"))
                ok &= InstallGroup("milian-apparel-def-isolation", MilianApparelBoundary.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("nivarian") && ModsConfig.IsActive("keeptpa.NivarianRace"))
                ok &= InstallGroup("fruit-tree", FruitTree.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira") && ModsConfig.IsActive("Ariandel.AriandelLibrary"))
                ok &= InstallGroup("lightning", LightningVisuals.Apply);
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("multifaction") && ModsConfig.IsActive("trigger.eliteRaid"))
                ok &= InstallGroup("elite-state", EliteState.Apply);
            Ready = ok;
            if (ok) Log.Message("[DesyncBatchCompat] " + typeof(Bootstrap).Assembly.GetName().Version + " READY MVID=" + typeof(Bootstrap).Module.ModuleVersionId);
        }

        static bool InstallGroup(string name, Action<Harmony> install)
        {
            var harmony = new Harmony("meow.desync94-145." + name);
            try
            {
                install(harmony);
                Log.Message("[DesyncBatchCompat] installed " + name);
                return true;
            }
            catch (Exception e)
            {
                // Never advertise a half-installed group as ready; other independent fixes can still load.
                harmony.UnpatchAll(harmony.Id);
                Log.Error("[DesyncBatchCompat] REQUIRED_TARGET_FAILED " + name + ": " + e);
                return false;
            }
        }

        internal static Type Type(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        internal static MethodInfo Method(Type type, string name, params Type[] args) =>
            AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        internal static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = AccessTools.Field(type, name);
            if (field == null || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
