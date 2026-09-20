using System;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_StrikeRandFix
{
	private static readonly Harmony harmony;

	private static bool isInitialized;

	static PLAMilira_StrikeRandFix()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("PLAMilira.StrikeRandFix");
		if (!MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMilira_StrikeRandFix] init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
		}
	}

	private static void LatePatch()
	{
		//IL_01bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Expected O, but got Unknown
		//IL_01dc: Expected O, but got Unknown
		try
		{
			string[] array = new string[23]
			{
				"PLAMilira.Orbital_HE_Barrage", "PLAMilira.Orbital_Airburst_Strike", "PLAMilira.Orbital_Cluster_Barrage", "PLAMilira.Orbital_Flare_Barrage", "PLAMilira.Orbital_Gatling_Barrage", "PLAMilira.Orbital_Laser", "PLAMilira.Orbital_Railcannon_Strike", "PLAMilira.Orbital_Support_Strike", "PLAMilira.BombardmentTest", "PLAMilira.MapSweepStrike",
				"PLAMilira.Map_Sweep_Plane", "PLAMilira.Map_Sweep_Plane_Strafing", "PLAMilira.Projectile_Missile_Mother", "PLAMilira.Projectile_Naglfar_Left_Missile", "PLAMilira.Projectile_SpeedUP_TrackingBullet", "PLAMilira.Projectile_WASP", "PLAMilira.Projectile_Sin", "PLAMilira.Projectile_TrackingBullet", "PLAMilira.Projectile_TrackingBulletNormal", "PLAMilira.Projectile_Plane",
				"PLAMilira.Projectile_Explosive_Wave", "PLAMilira.Projectile_Naglfar_OrbitalCannon", "PLAMilira.New_New_Solar_Bombardment"
			};
			foreach (string typeName in array)
			{
				global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandTick(harmony, typeName);
			}
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandConditionTick(harmony, "PLAMilira.GameCondition_HarrierSupport_Enemy");
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandCompTick(harmony, "PLAMilira.CompEnemyHarrierTower");
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandStatic(harmony, "PLAMilira.Patch_IncidentWorker_Raid_Harrier", "Postfix");
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandStatic(harmony, "PLAMilira.JobGiver_AICallAirSupprot", "GetTarget");
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandStatic(harmony, "PLAMilira.PLAMilira_MilianPawnGenerator_Patch", "Postfix");
			Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly("PLAMilira.CompAbilityEffect_Call_Airsupport", (string)null);
			if (typeInAnyAssembly != null)
			{
				MethodInfo methodInfo = AccessTools.Method(typeInAnyAssembly, "Apply", new Type[2]
				{
					typeof(LocalTargetInfo),
					typeof(LocalTargetInfo)
				}, (Type[])null);
				if (methodInfo != null)
				{
					harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
			Log.Message("[PLAMilira_StrikeRandFix] all patches installed");
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMilira_StrikeRandFix] LatePatch failed: {arg}");
		}
	}
}
