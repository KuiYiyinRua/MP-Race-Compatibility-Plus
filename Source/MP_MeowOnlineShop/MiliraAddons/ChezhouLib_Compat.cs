using System;
using System.Collections.Generic;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class ChezhouLib_Compat
{
	private const string NS = "ChezhouLib.ClThingComp.";

	private static Type _dualWeaponType;

	private static Type _switchModeType;

	private static Type _raceFlyType;

	private static FieldInfo _fiAutoSwitch;

	private static FieldInfo _fiCurrentMode;

	private static FieldInfo _fiCurrentModeIndex;

	private static FieldInfo _fiIsActionFiy;

	private static MethodInfo _miSwitchToMode;

	private static readonly Harmony harmony;

	private static bool _initialized;

	static ChezhouLib_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("Chezhou.ChezhouLib.Compat");
		if (!MP.enabled || !ModsConfig.IsActive("Chezhou.ChezhouLib.lib"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"[ChezhouLib_Compat] init FAILED: {arg}");
		}
	}

	private static void Initialize()
	{
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Expected O, but got Unknown
		//IL_0210: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Expected O, but got Unknown
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		_dualWeaponType = Resolve("ChezhouLib.ClThingComp.ThingComp_DualWeapon");
		_switchModeType = Resolve("ChezhouLib.ClThingComp.ThingComp_SwitchMode");
		_raceFlyType = Resolve("ChezhouLib.ClThingComp.ThingComp_RaceFly");
		if (_dualWeaponType != null)
		{
			_fiAutoSwitch = AccessTools.DeclaredField(_dualWeaponType, "autoSwitchEnabled");
			_fiCurrentMode = AccessTools.DeclaredField(_dualWeaponType, "currentMode");
			TryRegField(_dualWeaponType, "autoSwitchEnabled");
			TryRegField(_dualWeaponType, "currentMode");
			TryReg(typeof(ChezhouLib_Compat).GetMethod("SyncBoolField", BindingFlags.Static | BindingFlags.Public));
			TryReg(typeof(ChezhouLib_Compat).GetMethod("SyncCurrentMode", BindingFlags.Static | BindingFlags.Public));
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_dualWeaponType, "CompGetGizmosExtra", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(ChezhouLib_Compat), "Postfix_DualGizmos", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		if (_switchModeType != null)
		{
			_fiCurrentModeIndex = AccessTools.DeclaredField(_switchModeType, "currentModeIndex");
			_miSwitchToMode = AccessTools.DeclaredMethod(_switchModeType, "SwitchToMode", new Type[1] { typeof(int) }, (Type[])null);
			TryRegField(_switchModeType, "currentModeIndex");
			if (_miSwitchToMode != null)
			{
				TryReg(_miSwitchToMode);
			}
		}
		if (_raceFlyType != null)
		{
			_fiIsActionFiy = AccessTools.DeclaredField(_raceFlyType, "isActionFiy");
			TryRegField(_raceFlyType, "isActionFiy");
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(_raceFlyType, "CompGetGizmosExtra", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, new HarmonyMethod(typeof(ChezhouLib_Compat), "Postfix_RaceFlyGizmos", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		Log.Message("ChezhouLib_Compat: OK");
	}

	public static void Postfix_DualGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiCurrentMode == null))
		{
			__result = WrapDualGizmos(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapDualGizmos(IEnumerable<Gizmo> source, ThingComp comp)
	{
		foreach (Gizmo g in source)
		{
			Command_Toggle toggle = (Command_Toggle)(object)((g is Command_Toggle) ? g : null);
			if (toggle != null)
			{
				Action origToggle = toggle.toggleAction;
				toggle.toggleAction = delegate
				{
					origToggle?.Invoke();
					if (!MP.IsExecutingSyncCommand)
					{
						SyncBoolField(comp, "autoSwitchEnabled", (bool)_fiAutoSwitch.GetValue(comp));
					}
				};
			}
			else
			{
				Command_Action cmd = (Command_Action)(object)((g is Command_Action) ? g : null);
				if (cmd != null)
				{
					Action origAction = cmd.action;
					cmd.action = delegate
					{
						int num = (int)_fiCurrentMode.GetValue(comp);
						origAction();
						int num2 = (int)_fiCurrentMode.GetValue(comp);
						if (!MP.IsExecutingSyncCommand && num != num2)
						{
							SyncCurrentMode(comp, num2);
						}
					};
				}
			}
			yield return g;
		}
	}

	public static void Postfix_RaceFlyGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiIsActionFiy == null))
		{
			__result = WrapRaceFlyGizmos(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapRaceFlyGizmos(IEnumerable<Gizmo> source, ThingComp comp)
	{
		foreach (Gizmo g in source)
		{
			Command_Action cmd = (Command_Action)(object)((g is Command_Action) ? g : null);
			if (cmd != null && cmd.action != null)
			{
				Action origAction = cmd.action;
				cmd.action = delegate
				{
					bool flag = (bool)_fiIsActionFiy.GetValue(comp);
					origAction();
					bool flag2 = (bool)_fiIsActionFiy.GetValue(comp);
					if (!MP.IsExecutingSyncCommand && flag != flag2)
					{
						SyncBoolField(comp, "isActionFiy", flag2);
					}
				};
			}
			yield return g;
		}
	}

	public static void SyncCurrentMode(ThingComp comp, int mode)
	{
		if (comp != null && !(_fiCurrentMode == null))
		{
			object value = Enum.ToObject(_fiCurrentMode.FieldType, mode);
			_fiCurrentMode.SetValue(comp, value);
		}
	}

	public static void SyncBoolField(ThingComp comp, string fieldName, bool value)
	{
		if (comp != null)
		{
			if (fieldName == "autoSwitchEnabled" && _fiAutoSwitch != null)
			{
				_fiAutoSwitch.SetValue(comp, value);
			}
			else
			{
				AccessTools.Field(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
			}
		}
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(fullName, "ChezhouLib_Compat");
	}

	private static void TryReg(MethodInfo m, string label = "")
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void TryRegField(Type type, string field)
	{
		if (type != null)
		{
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncField(AccessTools.DeclaredField(type, field));
		}
	}
}
