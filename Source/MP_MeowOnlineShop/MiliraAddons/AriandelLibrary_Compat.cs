using System;
using System.Collections.Generic;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class AriandelLibrary_Compat
{
	private const string NS = "AriandelLibrary.";

	private static readonly Harmony harmony;

	private static bool isInitialized;

	private static Type _callAidGroupType;

	private static Type _selectableType;

	private static FieldInfo _fiCurrentModeIndex;

	private static Type _withResourcesType;

	private static FieldInfo _fiAllowResourceFilling;

	private static Type _overlayType;

	private static FieldInfo _fiOverlayEnabled;

	private static Type _voidPawnMgrType;

	private static MethodInfo _miSummon;

	private static MethodInfo _miRecall;

	static AriandelLibrary_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("AriandelLibrary.Compat");
		if (!MP.enabled || !ModsConfig.IsActive("Ariandel.AriandelLibrary"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"[AriandelLibrary_Compat] init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (isInitialized)
		{
			return;
		}
		isInitialized = true;
		_callAidGroupType = Resolve("AriandelLibrary.RoyalTitlePermitWorker_CallAid_Group");
		_selectableType = Resolve("AriandelLibrary.CompPeriodicGenerator_Selectable");
		_withResourcesType = Resolve("AriandelLibrary.CompGenerator_Periodic_WithResources");
		_overlayType = Resolve("AriandelLibrary.CompOverlayGraphic");
		int num = 0;
		int num2 = 0;
		(string, Type)[] array = new(string, Type)[4]
		{
			("RoyalTitlePermitWorker_CallAid_Group", _callAidGroupType),
			("CompPeriodicGenerator_Selectable", _selectableType),
			("CompGenerator_Periodic_WithResources", _withResourcesType),
			("CompOverlayGraphic", _overlayType)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, Type) tuple = array[i];
			if (tuple.Item2 != null)
			{
				num++;
				continue;
			}
			Log.Message("[AriandelLibrary_Compat] MISSING type: " + tuple.Item1);
			num2++;
		}
		Log.Message($"[AriandelLibrary_Compat] type check: {num} found, {num2} missing");
		LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
	}

	private static void LatePatch()
	{
		RegisterSyncMethods();
		RegisterPermitWorker();
		RegisterAllCompSyncFields();
		PatchAllGizmoMethods();
		PatchVoidPawnSync();
		int num = 0;
		int num2 = 0;
		FieldInfo[] array = new FieldInfo[3] { _fiCurrentModeIndex, _fiAllowResourceFilling, _fiOverlayEnabled };
		foreach (FieldInfo fieldInfo in array)
		{
			if (fieldInfo != null)
			{
				num++;
			}
			else
			{
				num2++;
			}
		}
		Log.Message($"[AriandelLibrary_Compat] startup: {num} field caches ok, {num2} null. OK");
	}

	private static void RegisterSyncMethods()
	{
		TryReg(typeof(AriandelLibrary_Compat).GetMethod("SyncBoolField", BindingFlags.Static | BindingFlags.Public, null, new Type[3]
		{
			typeof(ThingComp),
			typeof(string),
			typeof(bool)
		}, null));
		TryReg(typeof(AriandelLibrary_Compat).GetMethod("SyncIntField", BindingFlags.Static | BindingFlags.Public, null, new Type[3]
		{
			typeof(ThingComp),
			typeof(string),
			typeof(int)
		}, null));
	}

	private static void RegisterPermitWorker()
	{
		if (_callAidGroupType == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(_callAidGroupType, "OrderForceTarget", new Type[1] { typeof(LocalTargetInfo) }, (Type[])null);
		if (methodInfo == null)
		{
			return;
		}
		try
		{
			MP.RegisterSyncMethod(methodInfo, (SyncType[])null).SetContext((SyncContext)2);
		}
		catch
		{
		}
	}

	private static void RegisterAllCompSyncFields()
	{
		if (_selectableType != null)
		{
			_fiCurrentModeIndex = AccessTools.DeclaredField(_selectableType, "currentModeIndex");
			TryRegField(_fiCurrentModeIndex);
		}
		if (_withResourcesType != null)
		{
			_fiAllowResourceFilling = AccessTools.DeclaredField(_withResourcesType, "allowResourceFilling");
			TryRegField(_fiAllowResourceFilling);
		}
		if (_overlayType != null)
		{
			_fiOverlayEnabled = AccessTools.DeclaredField(_overlayType, "overlayEnabled");
			TryRegField(_fiOverlayEnabled);
		}
	}

	private static void PatchAllGizmoMethods()
	{
		PatchGizmo(_selectableType, "CompGetGizmosExtra", "WrapSelectableGizmos");
		PatchGizmo(_withResourcesType, "CompGetGizmosExtra", "WrapWithResourcesGizmos");
		PatchGizmo(_overlayType, "CompGetGizmosExtra", "WrapOverlayGizmos");
	}

	private static void PatchGizmo(Type type, string methodName, string wrapperName)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.PatchMethodPostfix(harmony, type, methodName, wrapperName, typeof(AriandelLibrary_Compat));
	}

	public static void WrapSelectableGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiCurrentModeIndex == null))
		{
			__result = WrapSelectableInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapSelectableInner(IEnumerable<Gizmo> source, ThingComp comp)
	{
		foreach (Gizmo g in source)
		{
			Command_Action cmd = (Command_Action)(object)((g is Command_Action) ? g : null);
			if (cmd != null)
			{
				Action origAction = cmd.action;
				cmd.action = delegate
				{
					int num = (int)_fiCurrentModeIndex.GetValue(comp);
					origAction();
					int num2 = (int)_fiCurrentModeIndex.GetValue(comp);
					if (!MP.IsExecutingSyncCommand && num != num2)
					{
						SyncIntField(comp, "currentModeIndex", num2);
						SyncIntField(comp, "tickNum", 0);
						AccessTools.DeclaredField(((object)comp).GetType(), "cachedGizmo")?.SetValue(comp, null);
					}
				};
			}
			yield return g;
		}
	}

	public static void WrapWithResourcesGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiAllowResourceFilling == null))
		{
			__result = WrapToggleGizmo(__result, __instance, _fiAllowResourceFilling, "allowResourceFilling");
		}
	}

	public static void WrapOverlayGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiOverlayEnabled == null))
		{
			__result = WrapToggleGizmo(__result, __instance, _fiOverlayEnabled, "overlayEnabled");
		}
	}

	private static IEnumerable<Gizmo> WrapToggleGizmo(IEnumerable<Gizmo> source, ThingComp comp, FieldInfo fi, string fieldName)
	{
		foreach (Gizmo g in source)
		{
			Command_Toggle toggle = (Command_Toggle)(object)((g is Command_Toggle) ? g : null);
			if (toggle != null)
			{
				Action orig = toggle.toggleAction;
				toggle.toggleAction = delegate
				{
					orig?.Invoke();
					if (!MP.IsExecutingSyncCommand)
					{
						SyncBoolField(comp, fieldName, (bool)fi.GetValue(comp));
					}
				};
			}
			yield return g;
		}
	}

	public static void SyncBoolField(ThingComp comp, string fieldName, bool value)
	{
		if (comp != null)
		{
			AccessTools.Field(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
		}
	}

	public static void SyncIntField(ThingComp comp, string fieldName, int value)
	{
		if (comp != null)
		{
			AccessTools.Field(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
		}
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(fullName, "AriandelLibrary_Compat");
	}

	private static void TryRegField(FieldInfo f)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncField(f);
	}

	private static void TryReg(MethodInfo m)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void PatchVoidPawnSync()
	{
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Expected O, but got Unknown
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Expected O, but got Unknown
		_voidPawnMgrType = Resolve("AriandelLibrary.AriandelLibrary_GameComponent_VoidPawnManager");
		if (!(_voidPawnMgrType == null))
		{
			_miSummon = AccessTools.DeclaredMethod(_voidPawnMgrType, "TrySummonPawnByStaticID", new Type[3]
			{
				typeof(string),
				typeof(IntVec3),
				typeof(Map)
			}, (Type[])null);
			_miRecall = AccessTools.DeclaredMethod(_voidPawnMgrType, "RecallPawnGlobalByStaticID", new Type[1] { typeof(string) }, (Type[])null);
			BindingFlags bindingAttr = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			TryReg(typeof(AriandelLibrary_Compat).GetMethod("SyncedSummon", bindingAttr));
			TryReg(typeof(AriandelLibrary_Compat).GetMethod("SyncedRecall", bindingAttr));
			if (_miSummon != null)
			{
				harmony.Patch((MethodBase)_miSummon, new HarmonyMethod(typeof(AriandelLibrary_Compat), "Prefix_Summon", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			if (_miRecall != null)
			{
				harmony.Patch((MethodBase)_miRecall, new HarmonyMethod(typeof(AriandelLibrary_Compat), "Prefix_Recall", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	public static bool Prefix_Summon(string staticID, IntVec3 location, Map map)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedSummon(staticID, location, map);
		return true;
	}

	public static bool Prefix_Recall(string staticID)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedRecall(staticID);
		return false;
	}

	public static void SyncedSummon(string staticID, IntVec3 location, Map map)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			GameComponent val = GenCollection.FirstOrDefault<GameComponent>(Current.Game.components, (Predicate<GameComponent>)((GameComponent c) => _voidPawnMgrType.IsInstanceOfType(c)));
			if (val != null)
			{
				_miSummon?.Invoke(val, new object[3] { staticID, location, map });
			}
		}
		catch (Exception ex)
		{
			Log.Error("[AriandelLibraryCompat] SyncedSummon: " + ex.Message);
		}
	}

	public static void SyncedRecall(string staticID)
	{
		try
		{
			GameComponent val = GenCollection.FirstOrDefault<GameComponent>(Current.Game.components, (Predicate<GameComponent>)((GameComponent c) => _voidPawnMgrType.IsInstanceOfType(c)));
			if (val != null)
			{
				_miRecall?.Invoke(val, new object[1] { staticID });
			}
		}
		catch (Exception ex)
		{
			Log.Error("[AriandelLibraryCompat] SyncedRecall: " + ex.Message);
		}
	}
}
