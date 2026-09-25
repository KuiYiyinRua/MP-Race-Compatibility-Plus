using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        public static bool Ready;
        static Bootstrap()
        {
            if (!MP.enabled) return;
            var core = AccessTools.TypeByName("MP_MeowOnlineShop.MpMeowOnlineShopBootstrap");
            if (core != null) RuntimeHelpers.RunClassConstructor(core.TypeHandle);
            var trio = AccessTools.TypeByName("Meow.RaceTrioCompatibility.Bootstrap");
            if (trio != null) RuntimeHelpers.RunClassConstructor(trio.TypeHandle);
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        static void Install()
        {
            var harmony = new Harmony("meow.tale-nivarian.multiplayer");
            try
            {
                if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual")) FireVisualRandom.Apply(harmony);
                if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual")) WoundCacheRandom.Apply(harmony);
                if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("transport")) SweepPlanePath.Apply(harmony);
                if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("multifaction")) DateNotifierContextOrder.Apply(harmony);
                if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("nivarian") && ModsConfig.IsActive("keeptpa.NivarianRace"))
                {
                    NivarianRenderState.Apply(harmony);
                    NivarianFireworks.Apply(harmony);
                    NivarianRemainingActions.Apply(harmony);
                    NivarianSelfBuilding.Apply(harmony);
                    NivarianPlacementPayload.Apply(harmony);
                    NivarianWindowDebugTools.Apply(harmony);
                    NivarianWindowQuestButtons.Apply(harmony);
                    NivarianDroneDeveloperActions.Apply(harmony);
                    NivarianModuleRefuel.Apply(harmony);
                    NivarianResearchCache.Apply(harmony);
                    NivarianWirelessPower.Apply(harmony);
                    NivarianArchive.Apply(harmony);
                    NivarianMultiblock.Apply(harmony);
                    NivarianComponentCaches.Apply(harmony);
                    NivarianMonumentOwnership.Apply(harmony);
                    NivarianBillOverrides.Apply();
                    NivarianIcyCoreSettings.Apply(harmony);
                    NivarianGameplaySwitches.Apply(harmony);
                    NivarianVerdantSettings.Apply(harmony);
                    NivarianPsionicCap.Apply(harmony);
                    NivarianMothershipVisuals.Apply(harmony);
                    NivarianOptionalVisualRandom.Apply(harmony);
                    NivarianPlantMaturity.Apply(harmony);
                    NivarianShieldState.Apply(harmony);
                    NivarianReturnProjectile.Apply(harmony);
                    NivarianRecruitmentPayment.Apply(harmony);
                    NivarianLifespanState.Apply(harmony);
                    NivarianCasterState.Apply(harmony);
                    NivarianTaskState.Apply(harmony);
                    NivarianDroneOwnership.Apply(harmony);
                    NivarianDroneSearchState.Apply(harmony);
                    NivarianDroneWorkState.Apply(harmony);
                    NivarianPodTickRegistration.Apply(harmony);
                    NivarianProgressOwnership.Apply(harmony);
                    NivarianUplinkProgressSignal.Apply(harmony);
                    NivarianMetricsRefresh.Apply(harmony);
                    NivarianMetricInputs.Apply(harmony);
                    NivarianCurrencyCollectors.Apply(harmony);
                    NivarianLentColonists.Apply(harmony);


                    var existingTrio=AccessTools.TypeByName("Meow.RaceTrioCompatibility.Bootstrap");
                    if(existingTrio==null || existingTrio.Assembly.GetName().Version<=new Version(1,1,0,0))
                    {
                        NivarianUiDelta.Apply(harmony);
                        NivarianControlSettings.Apply(harmony);
                        NivarianJoinContexts.Apply(harmony);
                    }
                    var orbit = AccessTools.DeclaredMethod(AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_MovingBase"), "OrbitTarget");
                    if (Harmony.GetPatchInfo(orbit)?.Owners.Contains("meow.trio.nivarian-simulation-random") != true)
                        NivarianSimulationRandom.Apply(harmony);
                    var accept = AccessTools.DeclaredMethod(AccessTools.TypeByName("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianAid"), "ExecuteAccept");
                    if (Harmony.GetPatchInfo(accept)?.Owners.Contains("meow.trio.nivarian-aid-events") != true)
                        NivarianAidEvents.Apply(harmony);
                    RegisterIfOldTrio("Nivarian_Race.Code.Comps.ThingComps.CompTransformableWeapon", "Transform", Type.EmptyTypes);
                    RegisterIfOldTrio("Nivarian_Race.Code.Comps.ThingComps.Comp_FlyActivator", "SwitchMode", Type.EmptyTypes);
                    RegisterIfOldTrio("Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister", "StartAbsorption", new[] { typeof(Pawn) });
                }
                if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira") && ModsConfig.IsActive("Pakerwot.MiliraEventandStortExpandTheTaleofMilira")) { Tale.Apply(harmony); TaleArrival.Apply(harmony); TaleSupplyDialog.Apply(harmony); }
                if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("rjw") && ModsConfig.IsActive("abscon.privacy.please")) Privacy.Apply(harmony);
                Ready = true;
                Log.Message("[TaleNivarianCompat] 1.1.3 READY MVID=" + typeof(Bootstrap).Module.ModuleVersionId);
            }
            catch (Exception e)
            {
                Ready = false;
                Log.Error("[TaleNivarianCompat] REQUIRED_TARGET_FAILED: " + e);
            }
        }

        static void RegisterIfOldTrio(string typeName, string method, Type[] args)
        {
            var trio = AccessTools.TypeByName("Meow.RaceTrioCompatibility.Bootstrap");
            if (trio != null && trio.Assembly.GetName().Version > new Version(1, 1, 0, 0)) return;
            var target = AccessTools.DeclaredMethod(AccessTools.TypeByName(typeName), method, args)
                ?? throw new MissingMethodException(typeName, method);
            MP.RegisterSyncMethod(target);
        }
    }
}









