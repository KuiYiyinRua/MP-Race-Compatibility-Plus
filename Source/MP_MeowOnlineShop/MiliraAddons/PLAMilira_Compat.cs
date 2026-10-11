using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_Compat
{
	private static bool isInitialized;

	private static readonly Harmony harmony;

	static PLAMilira_Compat()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("PLAMilira.Compat");
		if (!MP.enabled)
		{
			Log.Message("[PLAMilira_Compat] MP not enabled, skipping");
			return;
		}
		if (!ModsConfig.IsActive("sleepycot.wingsofdemocracy"))
		{
			Log.Message("[PLAMilira_Compat] Mod not installed, skipping");
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMilira_Compat] init failed - {arg}");
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

	private static void TryReg(MethodInfo m)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void RegField(Type type, string field)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncField(type, field);
	}

	private static void LatePatch()
	{
		//IL_037e: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Expected O, but got Unknown
		//IL_0478: Unknown result type (might be due to invalid IL or missing references)
		//IL_0485: Expected O, but got Unknown
		//IL_0404: Unknown result type (might be due to invalid IL or missing references)
		//IL_0411: Expected O, but got Unknown
		string text = "PLAMilira.";
		Type type = Resolve(text + "Dialog_Secret_ArrowWindow");
		string[] array = new string[1] { "PLAMilira.PortableSecretConsole:GenerateSecretCode" };
		string[] array2 = array;
		foreach (string m in array2)
		{
			WrapRand(m);
		}
		TryRegExt("PLAMilira.PLAmiliraUtility", "AddNewQueuedIncident", typeof(IncidentDef), typeof(int), typeof(IncidentParms), typeof(int));
		TryRegExt("PLAMilira.PLAmiliraUtility", "TrySpawnQuest", typeof(QuestScriptDef), typeof(Map), typeof(bool), typeof(bool));
		TryRegExt("PLAMilira.PLAmiliraUtility", "TryFireIncidentNow", typeof(IncidentDef), typeof(IncidentParms), typeof(bool));
		Type type2 = Resolve("PLAMilira.PLAMilira_Church_Faction_Handler");
		Type type3 = Resolve("PLAMilira.PLAMilira_GameComponent");
		Type type4 = Resolve("PLAMilira.PLAMilira_Faction_Handler");
		if (type2 != null)
		{
			RegField(type2, "tecPoint");
		}
		if (type3 != null)
		{
			RegField(type3, "isUnlocking");
		}
		if (type4 != null)
		{
			RegField(type4, "reinforcements");
		}
		TryReg(typeof(PLAMilira_Compat).GetMethod("SyncedAdjustTECPoints", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
		{
			typeof(int),
			typeof(bool)
		}, null));
		TryReg(typeof(PLAMilira_Compat).GetMethod("SyncedAdjustReinforcements", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
		{
			typeof(int),
			typeof(bool)
		}, null));
		TryReg(typeof(PLAMilira_Compat).GetMethod("SyncedUnlockingTec", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
		{
			typeof(int),
			typeof(string)
		}, null));
		if (type2 != null)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type2, "AdjustTECPoints", new Type[2]
			{
				typeof(int),
				typeof(bool)
			}, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_Compat), "Prefix_AdjustTECPoints", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		if (type3 != null)
		{
			Type type5 = Resolve("PLAMilira.PLAMiliraFactionTecDef");
			if (type5 != null)
			{
				MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type3, "UnlockingTec", new Type[2]
				{
					typeof(int),
					type5
				}, (Type[])null);
				if (methodInfo2 != null)
				{
					harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(PLAMilira_Compat), "Prefix_UnlockingTec", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
		}
		if (type4 != null)
		{
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(type4, "AdjustReinforcements", new Type[2]
			{
				typeof(int),
				typeof(bool)
			}, (Type[])null);
			if (methodInfo3 != null)
			{
				harmony.Patch((MethodBase)methodInfo3, new HarmonyMethod(typeof(PLAMilira_Compat), "Prefix_AdjustReinforcements", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	private static Type Resolve(string name)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(name, "PLAMilira_Compat");
	}

	private static bool PatchMethod(string typeColonMethod, string prefix)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		MethodInfo methodInfo = AccessTools.Method(typeColonMethod, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Error("[PLAMilira_Compat] METHOD NOT FOUND: " + typeColonMethod);
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_Compat), prefix, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		Log.Message("[PLAMilira_Compat] Patched: " + typeColonMethod + " -> " + prefix);
		return true;
	}

	public static void PushRand() => CompatUtility.PushRand();
    public static void PopRand() => CompatUtility.PopRand();
    private static bool WrapRand(string m)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected O, but got Unknown
		//IL_0062: Expected O, but got Unknown
		MethodInfo methodInfo = AccessTools.Method(m, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Message("[PLAMilira_Compat] RNG method not found: " + m);
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_Compat), "PushRand", (Type[])null), new HarmonyMethod(typeof(PLAMilira_Compat), "PopRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	private static void TryRegExt(string typeName, string method, params Type[] parTypes)
	{
		MethodInfo methodInfo = AccessTools.Method(typeName + ":" + method, parTypes, (Type[])null);
		if (methodInfo == null)
		{
			Log.Message("[PLAMilira_Compat] ext method not found " + typeName + ":" + method);
		}
		else
		{
			TryReg(methodInfo);
		}
	}

	public static bool Prefix_AdjustTECPoints(int change, bool showMessage)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedAdjustTECPoints(change, showMessage);
		return false;
	}

	public static bool Prefix_AdjustReinforcements(int change, bool showMessage)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedAdjustReinforcements(change, showMessage);
		return false;
	}

	public static bool Prefix_UnlockingTec(int ticks, object unlockTec)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedUnlockingTec(ticks, (unlockTec as Def)?.defName ?? "");
		return false;
	}

	public static void SyncedAdjustTECPoints(int change, bool showMessage)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_Church_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj != null)
			{
				AccessTools.Method(obj.GetType(), "AdjustTECPoints", new Type[2]
				{
					typeof(int),
					typeof(bool)
				}, (Type[])null)?.Invoke(obj, new object[2] { change, showMessage });
			}
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMiliraCompat] SyncedAdjustTECPoints error: {arg}");
		}
	}

	public static void SyncedAdjustReinforcements(int change, bool showMessage)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj != null)
			{
				AccessTools.Method(obj.GetType(), "AdjustReinforcements", new Type[2]
				{
					typeof(int),
					typeof(bool)
				}, (Type[])null)?.Invoke(obj, new object[2] { change, showMessage });
			}
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMiliraCompat] SyncedAdjustReinforcements error: {arg}");
		}
	}

	public static void SyncedUnlockingTec(int unlockTick, string tecDefName)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_GameComponent:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj == null)
			{
				return;
			}
			Type type = AccessTools.TypeByName("PLAMilira.PLAMiliraFactionTecDef");
			if (!(type == null))
			{
				Type type2 = typeof(DefDatabase<>).MakeGenericType(type);
				object obj2 = type2.GetMethod("GetNamed", new Type[2]
				{
					typeof(string),
					typeof(bool)
				})?.Invoke(null, new object[2] { tecDefName, false });
				if (obj2 != null)
				{
					AccessTools.Method(obj.GetType(), "UnlockingTec", new Type[2]
					{
						typeof(int),
						type
					}, (Type[])null)?.Invoke(obj, new object[2] { unlockTick, obj2 });
				}
			}
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMiliraCompat] SyncedUnlockingTec error: {arg}");
		}
	}
}
