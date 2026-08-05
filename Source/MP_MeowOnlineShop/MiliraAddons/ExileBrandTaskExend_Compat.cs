using System;
using System.Collections.Generic;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class ExileBrandTaskExend_Compat
{
	private const string NS = "ExileBrandTaskExend.";

	private static readonly Harmony harmony;

	private static Type _tShuttleCharging;

	private static Type _tQuestLineComp;

	private static Type _tRaidScheduler;

	private static Type _tIncidentWorker;

	static ExileBrandTaskExend_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("ExileBrandTaskExend.Compat");
		if (MP.enabled)
		{
			_tShuttleCharging = AccessTools.TypeByName("ExileBrandTaskExend.CompEBTEShuttleCharging");
			_tQuestLineComp = AccessTools.TypeByName("ExileBrandTaskExend.GameComponent_EBTEQuestLine");
			_tRaidScheduler = AccessTools.TypeByName("ExileBrandTaskExend.QuestPart_EBTERaidScheduler");
			_tIncidentWorker = AccessTools.TypeByName("ExileBrandTaskExend.IncidentWorker_GiveEBTETaskQuest");
			RegisterSyncMethods();
			RegisterRandomIsolation();
			int num = 0;
			num += PG(_tShuttleCharging, "CompGetGizmosExtra", "WrapShuttleGizmos");
			if (num > 0)
			{
				Log.Message($"[ExileBrandTaskExend_Compat] {num} patches applied");
			}
		}
	}

	private static int PG(Type type, string methodName, string wrapperName)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected O, but got Unknown
		if (type == null)
		{
			return 0;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, methodName, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return 0;
		}
		harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(ExileBrandTaskExend_Compat), wrapperName, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		return 1;
	}

	private static void RegisterSyncMethods()
	{
		if (_tShuttleCharging != null)
		{
			Reg(AccessTools.DeclaredMethod(_tShuttleCharging, "TryStartCharging", (Type[])null, (Type[])null));
		}
		if (_tQuestLineComp != null)
		{
			Reg(AccessTools.DeclaredMethod(_tQuestLineComp, "TryStartCurrentTask", (Type[])null, (Type[])null));
			Reg(AccessTools.DeclaredMethod(_tQuestLineComp, "ScheduleRetry", (Type[])null, (Type[])null));
			Reg(AccessTools.DeclaredMethod(_tQuestLineComp, "ResetState", (Type[])null, (Type[])null));
		}
		if (_tRaidScheduler != null)
		{
			Reg(AccessTools.DeclaredMethod(_tRaidScheduler, "TriggerRaid", (Type[])null, (Type[])null));
		}
		if (_tIncidentWorker != null)
		{
			Reg(AccessTools.DeclaredMethod(_tIncidentWorker, "TryGenerateQuest", (Type[])null, (Type[])null));
		}
		static void Reg(MethodInfo m)
		{
			if (m != null)
			{
				MP.RegisterSyncMethod(m, (SyncType[])null);
			}
		}
	}

	private static void RegisterRandomIsolation()
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Expected O, but got Unknown
		//IL_0066: Expected O, but got Unknown
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected O, but got Unknown
		//IL_00d3: Expected O, but got Unknown
		if (_tQuestLineComp != null)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_tQuestLineComp, "RandomRetryDelayTicks", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		if (_tRaidScheduler != null)
		{
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(_tRaidScheduler, "QuestPartTick", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	public static void WrapShuttleGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null)
		{
			__result = WrapShuttleInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapShuttleInner(IEnumerable<Gizmo> source, ThingComp comp)
	{
		foreach (Gizmo g in source)
		{
			Command_Action cmd = (Command_Action)(object)((g is Command_Action) ? g : null);
			if (cmd != null)
			{
				Action orig = cmd.action;
				cmd.action = delegate
				{
					orig?.Invoke();
				};
			}
			yield return g;
		}
	}
}
