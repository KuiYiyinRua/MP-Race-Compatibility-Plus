using System;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMiliraSariel_Compat
{
	private const string NS = "PLAMiliraSariel.";

	private static readonly Harmony harmony;

	private static bool isInitialized;

	static PLAMiliraSariel_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("PLAMiliraSariel.Compat");
		if (!MP.enabled)
		{
			Log.Message("[PLAMiliraSariel_Compat] MP not enabled, skipping");
			return;
		}
		if (!ModsConfig.IsActive("Ariandel.MiliraImperium"))
		{
			Log.Message("[PLAMiliraSariel_Compat] Mod not installed, skipping");
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"PLAMiliraSariel_Compat: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
			RegisterHediffSyncFields();
			RegisterFlyerSyncFields();
			RegisterCommandSyncMethods();
			Log.Message("PLAMiliraSariel_Compat: OK");
		}
	}

	private static void RegisterHediffSyncFields()
	{
		Type type = Resolve("PLAMiliraSariel.HediffWithComps_HardenedShield");
		if (!(type == null))
		{
			TryRegField(type, "shieldCount");
			TryRegField(type, "maxShieldCount");
			TryRegField(type, "buffed");
			TryRegField(type, "lastTriggerTick");
		}
	}

	private static void RegisterFlyerSyncFields()
	{
		Type type = Resolve("PLAMiliraSariel.Flyer_Wave");
		if (type == null)
		{
			return;
		}
		TryRegField(type, "stoppingPower");
		TryRegField(type, "damageDefOverride");
		FieldInfo fieldInfo = AccessTools.DeclaredField(type, "extraDamages");
		if (!(fieldInfo != null))
		{
			return;
		}
		try
		{
			MP.RegisterSyncField(fieldInfo);
		}
		catch
		{
		}
	}

	private static void RegisterCommandSyncMethods()
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		Type type = Resolve("PLAMiliraSariel.Command_SarielAbility");
		if (type == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(type, "ProcessInput", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return;
		}
		try
		{
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMiliraSariel_Compat), "CommandProcessInput_Prefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		}
		catch (Exception ex)
		{
			Log.Warning("[PLAMiliraSariel_Compat] Command_SarielAbility ProcessInput patch failed: " + ex.Message);
		}
	}

	private static bool CommandProcessInput_Prefix(Command __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return true;
		}
		return true;
	}

	private static void LatePatch()
	{
		RegisterAbilityEffects();
	}

	private static void RegisterAbilityEffects()
	{
		string[] array = new string[3] { "CompAbilityEffect_Alarak_Wave", "CompAbilityEffect_Sariel_Mind_Blast", "CompAbilityEffect_Sariel_Shield" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			Type type = Resolve("PLAMiliraSariel." + text);
			if (!(type == null))
			{
				MethodInfo m = AccessTools.DeclaredMethod(type, "Apply", new Type[2]
				{
					typeof(LocalTargetInfo),
					typeof(LocalTargetInfo)
				}, (Type[])null);
				TryReg(m);
			}
		}
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(fullName, "PLAMiliraSariel_Compat");
	}

	private static void TryReg(MethodInfo m)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void TryRegField(Type type, string field)
	{
		FieldInfo fieldInfo = AccessTools.DeclaredField(type, field);
		if (fieldInfo != null)
		{
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncField(fieldInfo);
		}
	}

	private static void LogOnce(string msg)
	{
		Log.Message(msg);
	}
}
