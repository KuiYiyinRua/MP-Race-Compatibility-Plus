using System;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class YaoYao_Compat
{
	private const string NS = "MiliraExpansionYaoYao";

	private const string NS_AB = "MiliraExpansionYaoYao.Abilities";

	private const string NS_PATCH = "MiliraExpansionYaoYao.HarmonyPatches.Patches";

	static YaoYao_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		if (!MP.enabled || !ModsConfig.IsActive("ZuoYao.MiliraYaoYao"))
		{
			return;
		}
		try
		{
			PatchAll();
		}
		catch (Exception arg)
		{
			Log.Error($"[YaoYao_Compat] init failed: {arg}");
		}
	}

	private static void PatchAll()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected O, but got Unknown
		Harmony harmony = new Harmony("YaoYao.Compat");
		int num = 0;
		if (PatchSkipOnClient(harmony, "MiliraExpansionYaoYao.Abilities.HediffComp_ProjectileShield", "ReflectIncomingProjectiles"))
		{
			num++;
		}
		if (PatchSkipOnClient(harmony, "MiliraExpansionYaoYao.HediffComp_PainPleasureHealing", "PerformActiveHealing"))
		{
			num++;
		}
		if (PatchRandWrap(harmony, "MiliraExpansionYaoYao.HarmonyPatches.Patches.TendUtility_DoTend_Patch", "Postfix"))
		{
			num++;
		}
		if (PatchRandWrap(harmony, "MiliraExpansionYaoYao.HarmonyPatches.Patches.Pawn_HealthTracker_AddHediff_Patch", "Prefix"))
		{
			num++;
		}
		if (PatchRandWrap(harmony, "MiliraExpansionYaoYao.HarmonyPatches.Patches.Recipe_Surgery_ApplyOnPawn_Patch", "Postfix"))
		{
			num++;
		}
		if (PatchRandWrap(harmony, "MiliraExpansionYaoYao.HarmonyPatches.Patches.HediffComp_GetsPermanent_PreFinalizeInjury_Patch", "Prefix"))
		{
			num++;
		}
		if (PatchRandWrap(harmony, "MiliraExpansionYaoYao.HarmonyPatches.Patches.RecordsUtility_Notify_BillDone_Patch", "Postfix"))
		{
			num++;
		}
		Log.Message($"[YaoYao_Compat] patched {num} methods");
	}

	private static bool PatchSkipOnClient(Harmony harmony, string typeName, string methodName)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Expected O, but got Unknown
		Type type = ResolveType(typeName);
		if (type == null)
		{
			return false;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, methodName, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(YaoYao_Compat), "Prefix_SkipOnClient", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	public static bool Prefix_SkipOnClient()
	{
		if (MP.IsInMultiplayer && !MP.IsHosting)
		{
			return false;
		}
		return true;
	}

	private static bool PatchRandWrap(Harmony harmony, string typeName, string methodName)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Expected O, but got Unknown
		//IL_0065: Expected O, but got Unknown
		Type type = ResolveType(typeName);
		if (type == null)
		{
			return false;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, methodName, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	private static Type ResolveType(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveTypeSilent(fullName);
	}
}
