using System;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class MilianModification_RandIsolation
{
	private const string NS = "MilianModification.";

	private static readonly Harmony harmony;

	static MilianModification_RandIsolation()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("Ancot.MilianModification.RandIsolation");
		if (!MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"MilianModification_RandIsolation: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		PatchDetRand("MilianModification.Recipe_MilianComponentResearch:Notify_IterationCompleted");
		PatchDetRand("MilianModification.MilianComponentResearchUtility:UnlockRandomComponentRecipeUnknown");
		Log.Message("MilianModification_RandIsolation: OK");
	}

	private static void PatchDetRand(string typeColonMethod)
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
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRandFinalizer", (Type[])null));
			}
		}
		catch
		{
		}
	}
}
