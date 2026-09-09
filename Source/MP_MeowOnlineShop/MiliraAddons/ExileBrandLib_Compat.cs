using System;
using System.Collections.Generic;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class ExileBrandLib_Compat
{
	private static bool isInitialized;

	private static readonly Harmony harmony;

	private static Type _tObserverComp;

	private static FieldInfo _fiEventFired;

	static ExileBrandLib_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("ExileBrandLib.Compat.RandIsolation");
		if (!MP.enabled)
		{
			Log.Message("[ExileBrandLib_Compat] MP not enabled, skipping");
			return;
		}
		if (!ModsConfig.IsActive("chezhou.Kind.MiliraExpansionExileBrand"))
		{
			Log.Message("[ExileBrandLib_Compat] Mod not installed, skipping");
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"ExileBrandLib_Compat: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			RegisterSyncWorkers();
			RegisterGameCompSyncMethods();
			RegisterVerbSyncMethods();
			RegisterDialogSync();
			RegisterBrantFleetSync();
			RegisterRandomIsolation();
			LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
			Log.Message("ExileBrandLib_Compat: OK");
		}
	}

	private static void RegisterSyncWorkers()
	{
		MP.RegisterSyncWorker<IntVec3>((SyncWorkerDelegate<IntVec3>)SyncIntVec3, typeof(IntVec3), true, false);
		MP.RegisterSyncWorker<List<IntVec3>>((SyncWorkerDelegate<List<IntVec3>>)SyncIntVec3List, typeof(List<IntVec3>), true, false);
		Type flyingRouteType = AccessTools.TypeByName("ExileBrandLib.Pojo.FlyingRoute");
		if (!(flyingRouteType != null))
		{
			return;
		}
		SyncWorkerDelegate<object> val = delegate(SyncWorker sync, ref object route)
		{
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_004d: Expected O, but got Unknown
			//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
			//IL_017b: Unknown result type (might be due to invalid IL or missing references)
			//IL_018b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Unknown result type (might be due to invalid IL or missing references)
			//IL_007e: Unknown result type (might be due to invalid IL or missing references)
			//IL_008c: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fd: Unknown result type (might be due to invalid IL or missing references)
			if (sync.isWriting)
			{
				object obj = route;
				List<IntVec3> list = (List<IntVec3>)AccessTools.Field(flyingRouteType, "strikePoints").GetValue(obj);
				FieldInfo fieldInfo = AccessTools.Field(flyingRouteType, "map");
				Map val2 = (Map)fieldInfo.GetValue(obj);
				sync.Write<int>(list.Count);
				foreach (IntVec3 item in list)
				{
					sync.Write<int>(item.x);
					sync.Write<int>(item.y);
					sync.Write<int>(item.z);
				}
				sync.Write<Map>(val2);
			}
			else
			{
				int num = sync.Read<int>();
				List<IntVec3> list2 = new List<IntVec3>();
				for (int i = 0; i < num; i++)
				{
					int num2 = sync.Read<int>();
					int num3 = sync.Read<int>();
					int num4 = sync.Read<int>();
					list2.Add(new IntVec3(num2, num3, num4));
				}
				Map val3 = sync.Read<Map>();
				if (list2.Count >= 2)
				{
					ConstructorInfo constructor = flyingRouteType.GetConstructor(new Type[3]
					{
						typeof(IntVec3),
						typeof(IntVec3),
						typeof(Map)
					});
					route = constructor.Invoke(new object[3]
					{
						list2[0],
						list2[1],
						val3
					});
				}
				else if (list2.Count == 1)
				{
					ConstructorInfo constructor2 = flyingRouteType.GetConstructor(new Type[3]
					{
						typeof(IntVec3),
						typeof(Map),
						typeof(Vector2?)
					});
					route = constructor2.Invoke(new object[3]
					{
						list2[0],
						val3,
						null
					});
				}
			}
		};
		MP.RegisterSyncWorker<object>(val, flyingRouteType, false, true);
	}

	private static void SyncIntVec3(SyncWorker sync, ref IntVec3 vec)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		if (sync.isWriting)
		{
			sync.Write<int>(vec.x);
			sync.Write<int>(vec.y);
			sync.Write<int>(vec.z);
		}
		else
		{
			vec = new IntVec3(sync.Read<int>(), sync.Read<int>(), sync.Read<int>());
		}
	}

	private static void SyncIntVec3List(SyncWorker sync, ref List<IntVec3> list)
	{
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		if (sync.isWriting)
		{
			sync.Write<int>(list.Count);
			{
				foreach (IntVec3 item in list)
				{
					sync.Write<int>(item.x);
					sync.Write<int>(item.y);
					sync.Write<int>(item.z);
				}
				return;
			}
		}
		int num = sync.Read<int>();
		list = new List<IntVec3>(num);
		for (int i = 0; i < num; i++)
		{
			list.Add(new IntVec3(sync.Read<int>(), sync.Read<int>(), sync.Read<int>()));
		}
	}

	private static void RegisterGameCompSyncMethods()
	{
		Type type = AccessTools.TypeByName("ExileBrandLib.ExileGameComp.GameComp_NukeStrike");
		if (type != null)
		{
			MP.RegisterSyncMethod(type, "AddStrikePlan", (SyncType[])null);
			MP.RegisterSyncMethod(type, "CancelAllPlans", (SyncType[])null);
		}
		Type type2 = AccessTools.TypeByName("ExileBrandLib.ExileGameComp.GameComp_OrbitalLaserSupport");
		if (type2 != null)
		{
			MP.RegisterSyncMethod(type2, "QueueDeployment", (SyncType[])null);
		}
		Type type3 = AccessTools.TypeByName("ExileBrandLib.ExileGameComp.GameComp_FlyingManage");
		if (type3 != null)
		{
			MP.RegisterSyncMethod(type3, "AddRoute", (SyncType[])null);
		}
	}

	private static void RegisterVerbSyncMethods()
	{
		Type type = AccessTools.TypeByName("ExileBrandLib.ExileVerb.Verb_SwitchEquipmentSet");
		if (type != null)
		{
			Reg(AccessTools.Method(type, "TryCastShot", (Type[])null, (Type[])null));
		}
		static void Reg(MethodInfo m)
		{
			if (m != null)
			{
				MP.RegisterSyncMethod(m, (SyncType[])null);
			}
		}
	}

	private static void RegisterDialogSync()
	{
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Expected O, but got Unknown
		_tObserverComp = AccessTools.TypeByName("ExileBrandLib.ExileGameComp.GameComp_MiliraObserver");
		if (_tObserverComp != null)
		{
			MethodInfo methodInfo = AccessTools.Method(_tObserverComp, "TryFireEvent", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				_fiEventFired = AccessTools.Field(_tObserverComp, "eventFired");
				MP.RegisterSyncMethod(typeof(ExileBrandLib_Compat), "SyncMiliraEvent", (SyncType[])null);
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(ExileBrandLib_Compat), "Prefix_TryFireEvent_Opt", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				Log.Message("[ExileBrandLib_Compat] Optimized GameComp_MiliraObserver.TryFireEvent for MP");
			}
		}
	}

	public static bool Prefix_TryFireEvent_Opt(Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!MP.IsExecutingSyncCommand)
		{
			return false;
		}
		return true;
	}

	public static void SyncMiliraEvent(Map map)
	{
		GameComponent component = Current.Game.GetComponent(_tObserverComp);
		if (component != null && !(bool)(_fiEventFired?.GetValue(component) ?? ((object)false)) && map != null && !(map.wealthWatcher.WealthTotal < 35000f))
		{
			AccessTools.Method(_tObserverComp, "TryFireEvent", (Type[])null, (Type[])null)?.Invoke(component, new object[1] { map });
		}
	}

	private static void RegisterBrantFleetSync()
	{
		Type type = AccessTools.TypeByName("ExileBrandLib.Fleet.BrantFleetUtility");
		if (type != null)
		{
			Reg(AccessTools.Method(type, "TryRecoverBrant", (Type[])null, (Type[])null));
			Reg(AccessTools.Method(type, "TryReleaseBrant", (Type[])null, (Type[])null));
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
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Expected O, but got Unknown
		//IL_006b: Expected O, but got Unknown
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Expected O, but got Unknown
		//IL_00dd: Expected O, but got Unknown
		Type type = AccessTools.TypeByName("ExileBrandLib.Comp.Patch_Pawn_Kill_MiliraRevive");
		if (type != null)
		{
			MethodInfo methodInfo = AccessTools.Method(type, "Prefix", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRandFinalizer", (Type[])null));
			}
		}
		Type type2 = AccessTools.TypeByName("ExileBrandLib.ExileVerb.Verb_BeamSaberMelee");
		if (type2 != null)
		{
			MethodInfo methodInfo2 = AccessTools.Method(type2, "ApplyBurnHediff", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRandFinalizer", (Type[])null));
			}
		}
	}

	private static void LatePatch()
	{
		string[] array = new string[4] { "ExileBrandLib.ExileVerb.Verb_NukeStrikeTriple", "ExileBrandLib.ExileVerb.Verb_OrbitalLaserClear", "ExileBrandLib.ExileVerb.Verb_FlyingEagle500", "ExileBrandLib.ExileVerb.Verb_SwitchEquipmentSet" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			Type type = AccessTools.TypeByName(text);
			if (type == null)
			{
				continue;
			}
			MethodInfo methodInfo = AccessTools.Method(type, "GetGizmos", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				try
				{
					MP.RegisterSyncMethodLambda(type, "GetGizmos", 0, (Type[])null, (ParentMethodType)0);
				}
				catch
				{
				}
			}
		}
	}
}
