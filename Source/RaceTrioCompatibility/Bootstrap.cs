using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        const string Building = "Nivarian_Race.Code.Comps.BuildingComps.";
        public static int Resolved { get; private set; }
        public static int Missing { get; private set; }

        static Bootstrap()
        {
            if (!MP.enabled) return;
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("visual")) FacialHealthBoundary.Apply(new Harmony("meow.trio.facial-health-thread"));
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("rjw")) BrothelBedPrices.Apply(new Harmony("meow.trio.brothel-bed-prices"));
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("nivarian")) NivarianExpansions.Apply(new Harmony("meow.trio.nivarian-expansions"));
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("nivarian") && ModsConfig.IsActive("keeptpa.NivarianRace"))
            {
                Recruitment.Apply(new Harmony("meow.trio.recruitment"));
                NivarianUi.Apply(new Harmony("meow.trio.nivarian-ui"));
                NivarianSimulationRandom.Apply(new Harmony("meow.trio.nivarian-simulation-random"));
                Mothership.Apply(new Harmony("meow.trio.mothership"));
                JoinDecisions.Apply(new Harmony("meow.trio.join-decisions"));
                NivarianAidEvents.Apply(new Harmony("meow.trio.nivarian-aid-events"));
                NivarianSelectionBoost.Apply(new Harmony("meow.trio.nivarian-selection-boost"));
                Register(Building + "CompUplinkResearch", "StartNewProject", typeof(ResearchProjectDef));
                Register(Building + "CompUplinkResearch", "CancelCurrentProject");
                Register(Building + "Comp_NivarianPowerNetworkIO", "SetTargetPowerOutput", typeof(float));
                Register(Building + "Comp_NivarianWirelessPowerAdapter", "SetWirelessPower", typeof(bool));
                Register("Nivarian.NivarianDrones.NivarianDroneHubComp", "ToggleEnable");
                Register("Nivarian.ThingCom_DroneHub", "ToggleEnable");
                Register("Nivarian_Race.Code.Comps.ThingComps.CompEnergyWeaponBattery", "TogglePower");
                Register("Nivarian_Race.Code.Comps.ThingComps.CompTransformableWeapon", "Transform");
                Register("Nivarian_Race.Code.Comps.ThingComps.Comp_FlyActivator", "SwitchMode");
                Register("Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister", "StartAbsorption", typeof(Pawn));
                Register(Building + "Comp_NivarianEnergyTower", "ToggleBatteryTarget", typeof(Thing));
                Register(Building + "Comp_NivarianEnergyTower", "SelectAllBatteries");
                Register(Building + "Comp_NivarianEnergyTower", "ClearBatteryTargets");
                Register("Nivarian_Race.Code.MechModuleSystem.Nira_MechUnitCommon", "set_GlowColor", typeof(UnityEngine.Color));
                Register("Nivarian_Race.Code.MechModuleSystem.Nira_MechUnitCommon", "set_RainbowGlow", typeof(bool));
                Register("Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret", "set_CurTarget", typeof(LocalTargetInfo));
                Register("Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret", "ClearTarget");
                Register("Nivarian_Race.Code.Comps.ThingComps.ColdEssenceDrawUtility", "SetAllDrawOnWeapon", typeof(ThingWithComps), typeof(bool));
                Type draftFlag = AccessTools.TypeByName("Nivarian_Race.Code.GameComponent.NivarianDraftAutoToggle");
                if (draftFlag == null) throw new TypeLoadException("Required Nivarian draft flag missing");
                Register("Nivarian_Race.Code.GameComponent.GameComp_NivarianDraftSettings", "SetFlag", typeof(Pawn), draftFlag, typeof(bool));
                Register("Nivarian_Race.Code.GameComponent.GameComp_NivarianDraftSettings", "ApplySetting", typeof(Pawn));
                Register(Building + "CompSelfBuilding", "DropAllContents", typeof(Map));
                Register(Building + "Comp_NivarianBillDoerCompBase", "AddQueue", typeof(RecipeDef), typeof(int));
                Register(Building + "Comp_NivarianBillDoerCompBase", "MoveQueue", typeof(int), typeof(int));
                Register(Building + "Comp_NivarianBillDoerCompBase", "RemoveQueue", typeof(int));
                Register(Building + "CompNiraControlCenter", "StartBuild");
                Register(Building + "CompNiraControlCenter", "CancelNiraBuild");
                Register(Building + "CompNiraControlCenter", "CancelCurrentModuleInstallation");
                Register(Building + "CompNiraControlCenter", "CancelCurrentModuleUninstallation");
                Register(Building + "CompNiraControlCenter", "RemoveQueuedModuleInstallation", typeof(int));
                Register(Building + "CompNiraControlCenter", "RemoveQueuedModuleUninstallation", typeof(int));
                Register("Nivarian.GameComp_NivarianMainStory", "SendSignal");
                Register("Nivarian.GameComp_NivarianMainStory", "SendQuest");
                Register("Nivarian.GameComp_NivarianMainStory", "SetClipReadIndex", typeof(string), typeof(int));
                Register("Nivarian.GameComp_NivarianNiraMetrics", "RollHoloDie", typeof(int));
                Register("Nivarian.GameComp_NivarianNiraMetrics", "TriggerHoloDieOption", typeof(int), typeof(int));
                Register("Nivarian.GameComp_NivarianNiraMetrics", "SkipHoloDie", typeof(int));
                foreach(var setting in new[]{"EvaluationIntervalDays","ActivePeriodDays","DormantPeriodDays"})
                    Register("Nivarian.GameComp_NivarianNiraMetrics", "set_"+setting, typeof(int));
                foreach(var setting in new[]{"ActiveEventFrequency","BaseEventFrequency","RaidThreatMultiplier","RapidEventRate"})
                    Register("Nivarian.GameComp_NivarianNiraMetrics", "set_"+setting, typeof(float));
                Register("Nivarian_Race.Code.Helper.RebirthMonumentEffectUtility", "RefreshMechBackdoorOnAllMaps");
                if (ModsConfig.RoyaltyActive) Register("Nivarian.Hediff_PsionicAttunement", "SetAbilityTree", typeof(string));
                Type module = AccessTools.TypeByName("Nivarian_Race.Code.MechModuleSystem.ModuleDef");
                if (module != null)
                {
                    Register(Building + "CompNiraControlCenter", "EnqueueModuleInstallation", typeof(Pawn), module);
                    Register(Building + "CompNiraControlCenter", "EnqueueModuleUninstallation", typeof(Pawn), module);
                }
                else throw new TypeLoadException("Required Nivarian ModuleDef missing");
                Type shuttleModule = AccessTools.TypeByName("Nivarian_Race.Code.ShuttleUpgradeSystem.ShuttleUpgradeDef");
                if (shuttleModule == null) throw new TypeLoadException("Required Nivarian ShuttleUpgradeDef missing");
                const string shuttle = "Nivarian_Race.Code.ShuttleUpgradeSystem.Comp_ShuttleUpgrade";
                Register(shuttle, "QueueInstall", shuttleModule);
                Register(shuttle, "QueueUninstall", shuttleModule);
                Register(shuttle, "RemoveQueuedInstall", shuttleModule);
                Register(shuttle, "RemoveQueuedUninstall", shuttleModule);
                Register(shuttle, "CancelCurrentInstall");
                Register(shuttle, "CancelCurrentUninstall");
            }
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("races") && ModsConfig.IsActive("ASEL.MonolynRace"))
            {
                MonolynUi.Apply(new Harmony("meow.trio.monolyn-ui"));
                Determinism.Monolyn(new Harmony("meow.trio.monolyn-random"));
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
            if (MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("races") && ModsConfig.IsActive("hatena.VoiceroidAsAnimal"))
            {
                Register("VoiceroidAsAnimal.CompVAAKotodama", "<CompGetGizmosExtra>b__3_1");
                Register("VoiceroidAsAnimal.CompVAAKotodama", "set_AdditionalMechBandwidthValue", typeof(int));
                Register("VoiceroidAsAnimal.CompVAANineTail", "<CompGetGizmosExtra>b__8_2", typeof(LocalTargetInfo));
                Register("VoiceroidAsAnimal.CompVAANineTail", "<CompGetGizmosExtra>b__8_3");
                Register("VoiceroidAsAnimal.CompVAANineTail", "<CompGetGizmosExtra>b__8_4");
                Register("VoiceroidAsAnimal.CompVAAVerbSkill", "<CompGetGizmosExtra>b__24_0", typeof(LocalTargetInfo));
                Register("VoiceroidAsAnimal.CompVAAItakoSkill", "<CompGetGizmosExtra>b__8_3");
                VoiceroidUi.Apply(new Harmony("meow.trio.vaa-ui"));
                Determinism.Voiceroid(new Harmony("meow.trio.vaa-random"));
            }
            Log.Message("[RaceTrioCompat] resolved=" + Resolved + " missing=" + Missing
                + " MVID=" + typeof(Bootstrap).Assembly.ManifestModule.ModuleVersionId);
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
                Log.Error("[RaceTrioCompat] Required target failed " + typeName + "." + methodName + ": " + e);
            }
        }
    }
}
