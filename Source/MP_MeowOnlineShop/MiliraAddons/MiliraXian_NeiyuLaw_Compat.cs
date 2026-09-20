using System;
using System.Collections;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class MiliraXian_NeiyuLaw_Compat
{
	private static readonly Harmony harmony;

	private static bool isInitialized;

	static MiliraXian_NeiyuLaw_Compat()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("MiliraXian.NeiyuLaw.Compat");
		if (!MP.enabled)
		{
			Log.Message("[MiliraXian_NeiyuLaw_Compat] MP not enabled, skipping");
			return;
		}
		if (!ModsConfig.IsActive("HeChuanRiver.MiliraXian.NeiyuLaw"))
		{
			Log.Message("[MiliraXian_NeiyuLaw_Compat] Mod not installed, skipping");
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"MiliraXian_NeiyuLaw_Compat: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (isInitialized)
		{
			return;
		}
		isInitialized = true;
		Type type = AccessTools.TypeByName("MiliraXian.Characters.Neiyu.NeiyuCombatMapComponent");
		if (type != null)
		{
			MethodInfo methodInfo = AccessTools.Method(type, "ScheduleThunderMarks", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				MP.RegisterSyncMethod(methodInfo, (SyncType[])null);
			}
			MethodInfo methodInfo2 = AccessTools.Method(type, "ScheduleArrowBarrage", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				MP.RegisterSyncMethod(methodInfo2, (SyncType[])null);
			}
		}
		ClearVisualDicts();
		LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
		Log.Message("MiliraXian_NeiyuLaw_Compat: OK");
	}

	private static void LatePatch()
	{
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_015b: Expected O, but got Unknown
		//IL_015b: Expected O, but got Unknown
		Type type = AccessTools.TypeByName("MiliraXian.Characters.HediffComp_PawnSpecialResource");
		if (type != null)
		{
			TryLambda(type, "CompGetGizmos", 0);
		}
		Type type2 = AccessTools.TypeByName("MiliraXian.Characters.Neiyu.Comp_ModeSwitchWeapon");
		if (type2 != null)
		{
			TryLambda(type2, "CompGetGizmosExtra", 0);
		}
		TryLambda(typeof(Pawn), "GetGizmos", 0);
		string[] array = new string[8] { "MiliraXian.Characters.Neiyu.CompAbilityEffect_NeiyuSwordSkyfall", "MiliraXian.Characters.Neiyu.CompAbilityEffect_NeiyuSwordExecution", "MiliraXian.Characters.Neiyu.CompAbilityEffect_NeiyuArrowBarrage", "MiliraXian.Characters.Neiyu.CompAbilityEffect_NeiyuThunderSigil", "MiliraXian.Characters.Neiyu.CompAbilityEffect_NeiyuFlowerBless", "MiliraXian.Characters.Neiyu.CompAbilityEffect_NeiyuFlowerToxinField", "MiliraXian.Characters.Neiyu.CompAbilityEffect_NeiyuWarpFeather", "MiliraXian.Characters.Zhaoli.CompAbilityEffect_ZhaoliDeathField" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			Type type3 = AccessTools.TypeByName(text);
			if (type3 != null)
			{
				TryLambda(type3, "GetGizmos", 0);
			}
		}
		Type type4 = AccessTools.TypeByName("MiliraXian.Characters.Neiyu.NeiyuCombatMapComponent");
		if (type4 != null)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type4, "MapComponentTick", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRandFinalizer", (Type[])null));
			}
		}
	}

	private static void TryLambda(Type t, string m, int o)
	{
		try
		{
			MP.RegisterSyncMethodLambda(t, m, o, (Type[])null, (ParentMethodType)0);
		}
		catch
		{
		}
	}

	private static void ClearVisualDicts()
	{
		Type type = AccessTools.TypeByName("MiliraXian.Characters.Neiyu.Patch_MXNL_MeleeSlashGhost_TryCastShot");
		if (type != null)
		{
			ClearDict(type, "LastFxTickByPawn");
			ClearDict(type, "NextCycleIndexByPawn");
		}
		Type type2 = AccessTools.TypeByName("MiliraXian.Characters.Neiyu.NeiyuSkyfallVisualTracker");
		if (type2 != null)
		{
			ClearDict(type2, "states");
		}
	}

	private static void ClearDict(Type t, string f)
	{
		(AccessTools.Field(t, f)?.GetValue(null) as IDictionary)?.Clear();
	}
}
