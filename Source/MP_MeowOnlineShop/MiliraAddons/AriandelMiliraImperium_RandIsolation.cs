using System;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class AriandelMiliraImperium_RandIsolation
{
	private const string NS = "MiliraImperium.";

	private static readonly Harmony harmony;

	static AriandelMiliraImperium_RandIsolation()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("Ariandel.MiliraImperium.RandIsolation");
		if (!MP.enabled || !ModsConfig.IsActive("Ariandel.MiliraImperium"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"AriandelMiliraImperium_RandIsolation: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		PatchDetRand("MiliraImperium.Orbital_VoidWarship_Strike", "SpawnSetup");
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandTick(harmony, "MiliraImperium.ApocalypseBombardment");
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.WrapDetRandTick(harmony, "MiliraImperium.OrbitalSlicer");
		PatchDetRand("MiliraImperium.AuraEffectWorker", "Apply");
		PatchDetRand("MiliraImperium.HediffComp_HaloSwitch", "CompPostTick");
		PatchDetRand("MiliraImperium.Verb_ShootSustainedWithOffset", "TryCastShot");
		PatchDetRand("MiliraImperium.MiliraImperiumPawnFlyer_MichaSlice", "LandingEffects");
		PatchDetRand("MiliraImperium.Projectile_TrackingBullet_Orbital_Explosive", "Explode");
		PatchDetRand("MiliraImperium.Projectile_TrackingBullet_Orbital", "Impact");
		PatchDetRand("MiliraImperium.Projectile_TrackingBulletCrit", "CheckCrit");
		PatchDetRand("MiliraImperium.Projectile_TrackingBulletCrit", "Impact");
		PatchDetRand("MiliraImperium.MI_PointDefnseProjectile", "ImpactSomething");
		PatchDetRand("MiliraImperium.Projectile_ParallelLaser", "Explode");
		PatchPushPopRand("MiliraImperium.Orbital_VoidWarship_Strike:Tick");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_CallWarship:CallWarShip");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_OrbitalStrike:SpawnFlyOverShadow");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_StunBlast:OrderForceTarget");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_EMPStorm:OrderForceTarget");
		PatchDetRand("MiliraImperium.OrbitalStrikeShip", "SpawnSetup");
		PatchDetRand("MiliraImperium.Orbital_Precise_Strike", "SpawnSetup");
		PatchDetRand("MiliraImperium.MI_TradeShip", "SpawnShipShadow");
		PatchPushPopRand("MiliraImperium.Orbital_Precise_Strike:SpawnFlyOverShadow");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_DropSkyFaller:OrderForceTarget");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_DropSkyFaller:CallSkyFaller");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_DropSkyFaller_TargetLine:OrderForceTarget");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_DropSkyFaller_TargetLine:CallSkyFaller");
		PatchPushPopRand("MiliraImperium.RoyalTitlePermitWorker_Slicing:OrderForceTarget");
		PatchPushPopRand("MiliraImperium.Projectile_Missile_Fox3:Tick");
		PatchPushPopRand("MiliraImperium.Projectile_Missile_Fox3:TickInterval");
		PatchPushPopRand("MiliraImperium.Projectile_Missile_Fox3:FindNextTarget");
		PatchPushPopRand("MiliraImperium.Projectile_Missile_StarLight:Tick");
		PatchPushPopRand("MiliraImperium.Projectile_Missile_StarLight:TickInterval");
		PatchPushPopRand("MiliraImperium.Projectile_Missile_StarLight:BPos");
		PatchPushPopRand("MiliraImperium.Projectile_Missile_StarLight:FindNextTarget");
		PatchPushPopRand("MiliraImperium.JobDriver_EatingMichaHalo:ApplyAfterEatingEffects");
		PatchPushPopRand("MiliraImperium.MiliraImperium_Utility_Milian:GetImperiumReplacementWeaponDef");
		PatchPushPopRand("MiliraImperium.MiliraImperium_Utility_Milian:GetPirateReplacementWeaponDef");
		PatchPushPopRand("MiliraImperium.MiliraImperium_Utility_Milian:ReplaceWeapon_Pirate");
		PatchPushPopRand("MiliraImperium.MiliraImperium_Utility_Milian:AddSolarShieldBeltII_Pirate");
		PatchPushPopRand("MiliraImperium.PawnsArrivalModeWorker_EdgeWarp_MiliraPirate:Arrive");
		PatchPushPopRand("MiliraImperium.PawnsArrivalModeWorker_EdgeWarp_MiliraPirate:ResolveNormalExtraPawnKinds");
		PatchPushPopRand("MiliraImperium.PawnsArrivalModeWorker_EdgeWarp_MiliraPirate:ResolveHardOrExtremeExtraPawnKinds");
		PatchPushPopRand("MiliraImperium.PawnsArrivalModeWorker_EdgeWarp_MiliraPirate:GenerateExtraPawns");
		PatchPushPopRand("MiliraImperium.MiliraImperium_PawnGenerator_Patch:Postfix");
		PatchPushPopRand("MiliraImperium.FactionDialogFor_Patch:GetWeightedRandom");
		PatchPushPopRand("MiliraImperium.Comp_DeflectorShield:Break");
		PatchPushPopRand("MiliraImperium.Comp_DeflectorShield:AbsorbedDamage");
		PatchPushPopRand("MiliraImperium.CompAerocraftFleckExhaust:CompTick");
		PatchPushPopRand("MiliraImperium.CompAnimatedDraw_Projectile_Inregular:CompTick");
		Log.Message("AriandelMiliraImperium_RandIsolation: OK");
	}

	private static void PatchDetRand(string typeName, string methodName)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Expected O, but got Unknown
		//IL_0066: Expected O, but got Unknown
		Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(typeName, (string)null);
		if (typeInAnyAssembly == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(typeInAnyAssembly, methodName, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return;
		}
		try
		{
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRandFinalizer", (Type[])null));
		}
		catch
		{
		}
	}

	private static void PatchPushPopRand(string typeColonMethod)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_004f: Expected O, but got Unknown
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeColonMethod, (Type[])null, (Type[])null);
			if (!(methodInfo == null))
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopRandFinalizer", (Type[])null));
			}
		}
		catch
		{
		}
	}
}
