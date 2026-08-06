using System;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMiliraSariel_RandIsolation
{
	private enum DetRandType
	{
		Thing,
		ThingComp,
		Tick
	}

	private const string NS = "PLAMiliraSariel.";

	private static readonly Harmony harmony;

	static PLAMiliraSariel_RandIsolation()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("PLAMiliraSariel.RandIsolation");
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
			Log.Error($"PLAMiliraSariel_RandIsolation: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		PatchDetRand("PLAMiliraSariel.HediffComp_ShieldControl", "CompPostTickInterval", DetRandType.ThingComp);
		PatchDetRand("PLAMiliraSariel.HediffWithComps_HardenedShield", "Notify_Pawn_PreApplyDamage", DetRandType.Tick);
		PatchDetRand("PLAMiliraSariel.Verb_MeleeAttackDamage_AOE", "TryCastShot", DetRandType.Tick);
		PatchDetRand("PLAMiliraSariel.Projectile_Alarak_Soul", "HealRandomInjury", DetRandType.Thing);
		PatchDetRand("PLAMiliraSariel.Flyer_Wave", "Launch", new Type[5]
		{
			typeof(Thing),
			typeof(LocalTargetInfo),
			typeof(LocalTargetInfo),
			typeof(bool),
			typeof(Thing)
		}, DetRandType.Thing);
		PatchDetRand("PLAMiliraSariel.Flyer_Wave", "Launch", new Type[7]
		{
			typeof(Thing),
			typeof(Vector3),
			typeof(LocalTargetInfo),
			typeof(LocalTargetInfo),
			typeof(bool),
			typeof(Thing),
			typeof(ThingDef)
		}, DetRandType.Thing);
		PatchPushPopRand("PLAMiliraSariel.Flyer_Wave:TickInterval");
		PatchPushPopRand("PLAMiliraSariel.Projectile_Alarak_Soul:RandFactor");
		Log.Message("PLAMiliraSariel_RandIsolation: OK");
	}

	private static void PatchDetRand(string typeColonMethod, string methodName, DetRandType randType)
	{
		PatchDetRand(typeColonMethod, methodName, null, randType);
	}

	private static void PatchDetRand(string typeName, string methodName, Type[] argTypes, DetRandType randType)
	{
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Expected O, but got Unknown
		//IL_00e3: Expected O, but got Unknown
		Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(typeName, (string)null);
		if (typeInAnyAssembly == null)
		{
			return;
		}
		MethodInfo methodInfo = ((argTypes != null) ? AccessTools.Method(typeInAnyAssembly, methodName, argTypes, (Type[])null) : AccessTools.Method(typeInAnyAssembly, methodName, (Type[])null, (Type[])null));
		if (methodInfo == null)
		{
			return;
		}
		try
		{
			MethodInfo methodInfo2 = null;
			switch (randType)
			{
			case DetRandType.Thing:
				methodInfo2 = typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility).GetMethod("PushDetRand_Thing");
				break;
			case DetRandType.ThingComp:
				methodInfo2 = typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility).GetMethod("PushDetRand_ThingComp");
				break;
			case DetRandType.Tick:
				methodInfo2 = typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility).GetMethod("PushDetRand_Tick");
				break;
			}
			if (!(methodInfo2 == null))
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(methodInfo2), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		catch
		{
		}
	}

	private static void PatchPushPopRand(string typeColonMethod, Type[] argTypes = null)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Expected O, but got Unknown
		//IL_005c: Expected O, but got Unknown
		try
		{
			MethodInfo methodInfo = ((argTypes != null) ? AccessTools.Method(typeColonMethod, argTypes, (Type[])null) : AccessTools.Method(typeColonMethod, (Type[])null, (Type[])null));
			if (!(methodInfo == null))
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushRand", (Type[])null), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		catch
		{
		}
	}
}
