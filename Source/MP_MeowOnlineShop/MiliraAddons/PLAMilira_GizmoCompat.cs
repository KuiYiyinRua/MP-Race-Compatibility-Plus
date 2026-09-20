using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_GizmoCompat
{
	private const string NS = "PLAMilira.";

	private static readonly Harmony harmony;

	private static bool isInitialized;

	private static Type _switchType;

	private static Type _switchNewType;

	private static Type _turretType;

	private static Type _antiballisticType;

	private static Type _antiHarrierType;

	private static Type _naglfarMidBuffType;

	private static Type _naglfarRightBuffType;

	private static Type _naglfarLeftArmorType;

	private static Type _naglfarMidArmorType;

	private static Type _commandAddHediffsType;

	private static Type _changeSupportType;

	private static Type _gizmoTurretType;

	private static FieldInfo _fiSwitchIsAltVerb;

	private static FieldInfo _fiSwitchNewIsAltVerb;

	private static FieldInfo _fiFireAtWill;

	private static FieldInfo _fiPLAMiliraCurrentTarget;

	private static FieldInfo _fiPLAMiliraForcedTarget;

	private static FieldInfo _fiPLAMiliraIsForcetargetDowned;

	private static FieldInfo _fiSwitchOn;

	private static FieldInfo _fiSwitchOnAntiAir;

	private static FieldInfo _fiShowGizmo;

	private static FieldInfo _fiShouldAutoLaunch;

	private static FieldInfo _fiNaglfarMidBuffIsOn;

	private static FieldInfo _fiNaglfarRightBuffIsOn;

	private static FieldInfo _fiNaglfarLeftArmorIsOn;

	private static FieldInfo _fiNaglfarMidArmorIsOn;

	private static FieldInfo _fiChangeChoice;

	private static FieldInfo _fiChangeIsAutoChoose;

	private static readonly ConditionalWeakTable<object, object> _snapFaw;

	private static readonly ConditionalWeakTable<object, object> _snapTarget;

	static PLAMilira_GizmoCompat()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("PLAMilira.GizmoCompat");
		_snapFaw = new ConditionalWeakTable<object, object>();
		_snapTarget = new ConditionalWeakTable<object, object>();
		if (!MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMilira_GizmoCompat] init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			_switchType = Resolve("PLAMilira.CompSwitch_New");
			_switchNewType = Resolve("PLAMilira.CompSwitch_New_New");
			_turretType = Resolve("PLAMilira.CompPLAMilira_TurretGun_Building");
			_antiballisticType = Resolve("PLAMilira.CompAntiballistic");
			_antiHarrierType = Resolve("PLAMilira.CompAntiHarrierLauncher");
			_naglfarMidBuffType = Resolve("PLAMilira.CompNaglfar_Middle_Buff");
			_naglfarRightBuffType = Resolve("PLAMilira.CompNaglfar_Right_Buff");
			_naglfarLeftArmorType = Resolve("PLAMilira.CompNaglfar_Left_Armor");
			_naglfarMidArmorType = Resolve("PLAMilira.CompNaglfar_Middle_Armor");
			_commandAddHediffsType = Resolve("PLAMilira.Command_AddHediffs");
			_changeSupportType = Resolve("PLAMilira.Comp_ChangeSupport");
			_gizmoTurretType = Resolve("PLAMilira.Gizmo_PLAMilira_TurretGunBuilding");
			LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
		}
	}

	private static void LatePatch()
	{
		try
		{
			RegisterFields();
			RegisterSyncMethods();
			PatchGizmoOnGUI();
			PatchCommandProcessInput();
			PatchCompGizmoMethods();
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMilira_GizmoCompat] LatePatch FAILED: {arg}");
		}
	}

	private static void RegisterFields()
	{
		RegField(_switchType, "isAltVerbSelected", out _fiSwitchIsAltVerb);
		RegField(_switchNewType, "isAltVerbSelected", out _fiSwitchNewIsAltVerb);
		RegField(_turretType, "fireAtWill", out _fiFireAtWill);
		RegField(_turretType, "currentTarget", out _fiPLAMiliraCurrentTarget);
		RegField(_turretType, "forcedTarget", out _fiPLAMiliraForcedTarget);
		RegField(_turretType, "isForcetargetDowned", out _fiPLAMiliraIsForcetargetDowned);
		RegField(_antiballisticType, "switchOn", out _fiSwitchOn);
		RegField(_antiballisticType, "switchOnAntiAir", out _fiSwitchOnAntiAir);
		RegField(_antiballisticType, "showGizmo", out _fiShowGizmo);
		RegField(_antiHarrierType, "shouldAutoLaunch", out _fiShouldAutoLaunch);
		RegField(_naglfarMidBuffType, "isOn", out _fiNaglfarMidBuffIsOn);
		RegField(_naglfarRightBuffType, "isOn", out _fiNaglfarRightBuffIsOn);
		RegField(_naglfarLeftArmorType, "isOn", out _fiNaglfarLeftArmorIsOn);
		RegField(_naglfarMidArmorType, "isOn", out _fiNaglfarMidArmorIsOn);
		RegField(_changeSupportType, "choice", out _fiChangeChoice);
		RegField(_changeSupportType, "IsAutoChooseChoice", out _fiChangeIsAutoChoose);
	}

	private static void RegisterSyncMethods()
	{
		TryReg(typeof(PLAMilira_GizmoCompat).GetMethod("SyncBoolField", BindingFlags.Static | BindingFlags.Public));
		TryReg(typeof(PLAMilira_GizmoCompat).GetMethod("SyncIntField", BindingFlags.Static | BindingFlags.Public));
		TryReg(typeof(PLAMilira_GizmoCompat).GetMethod("SyncTurretTargets_PLAMilira", BindingFlags.Static | BindingFlags.Public));
		if (_switchType != null)
		{
			TryReg(AccessTools.DeclaredMethod(_switchType, "SwitchVerb", (Type[])null, (Type[])null));
		}
		if (_switchNewType != null)
		{
			TryReg(AccessTools.DeclaredMethod(_switchNewType, "SwitchVerb", (Type[])null, (Type[])null));
		}
		if (_antiHarrierType != null)
		{
			TryReg(AccessTools.DeclaredMethod(_antiHarrierType, "TryLaunchInterception", (Type[])null, (Type[])null));
		}
	}

	private static void PatchGizmoOnGUI()
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Expected O, but got Unknown
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Expected O, but got Unknown
		if (!(_gizmoTurretType == null))
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_gizmoTurretType, "ProcessTargetingInput", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_GizmoCompat), "Prefix_ProcessTargetingInput", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(_gizmoTurretType, "GizmoOnGUI", new Type[3]
			{
				typeof(Vector2),
				typeof(float),
				typeof(GizmoRenderParms)
			}, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, new HarmonyMethod(typeof(PLAMilira_GizmoCompat), "Postfix_TurretGizmoOnGUI", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	public static bool Prefix_ProcessTargetingInput(object __instance)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (!(AccessTools.Field(_gizmoTurretType, "comps")?.GetValue(__instance) is IList { Count: not 0 } list))
		{
			return true;
		}
		foreach (object item in list)
		{
			if (_fiFireAtWill != null && item != null && (bool)_fiFireAtWill.GetValue(item))
			{
				return true;
			}
		}
		Messages.Message("炮台自动开火已关闭，无法强制指定目标", MessageTypeDefOf.RejectInput, false);
		return false;
	}

	private static int GetCompIndex(ThingComp comp, Type compType)
	{
		if (comp?.parent == null || compType == null)
		{
			return -1;
		}
		ThingWithComps parent = comp.parent;
		List<ThingComp> list = ((parent != null) ? parent.AllComps : null);
		if (list == null)
		{
			return -1;
		}
		int num = 0;
		foreach (ThingComp item in list)
		{
			if (compType.IsInstanceOfType(item))
			{
				if (item == comp)
				{
					return num;
				}
				num++;
			}
		}
		return -1;
	}

	public static void Postfix_TurretGizmoOnGUI(object __instance)
	{
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0212: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Expected O, but got Unknown
		//IL_027b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0280: Unknown result type (might be due to invalid IL or missing references)
		//IL_0285: Unknown result type (might be due to invalid IL or missing references)
		//IL_0290: Expected O, but got Unknown
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0188: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0309: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0310: Unknown result type (might be due to invalid IL or missing references)
		//IL_0318: Expected O, but got Unknown
		//IL_02ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e8: Expected O, but got Unknown
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !(AccessTools.Field(_gizmoTurretType, "comps")?.GetValue(__instance) is IList list))
		{
			return;
		}
		bool flag2 = default(bool);
		bool flag4 = default(bool);
		LocalTargetInfo currentTarget = default(LocalTargetInfo);
		foreach (object item in list)
		{
			ThingComp val = (ThingComp)((item is ThingComp) ? item : null);
			if (val == null || val.parent == null || !((Thing)val.parent).Spawned)
			{
				continue;
			}
			int compIndex = GetCompIndex(val, _turretType);
			if (compIndex < 0)
			{
				continue;
			}
			if (_fiFireAtWill != null)
			{
				bool flag = (bool)_fiFireAtWill.GetValue(item);
				object orCreateValue = _snapFaw.GetOrCreateValue(item);
				int num;
				if (orCreateValue is bool)
				{
					flag2 = (bool)orCreateValue;
					num = 1;
				}
				else
				{
					num = 0;
				}
				bool flag3 = (byte)((uint)num & (flag2 ? 1u : 0u)) != 0;
				if (flag != flag3)
				{
					_snapFaw.Remove(item);
					_snapFaw.Add(item, flag);
					SyncBoolField((ThingComp)item, "fireAtWill", flag);
					if (!flag && _fiPLAMiliraForcedTarget != null)
					{
						_fiPLAMiliraForcedTarget.SetValue(item, LocalTargetInfo.Invalid);
						_fiPLAMiliraCurrentTarget?.SetValue(item, LocalTargetInfo.Invalid);
						_fiPLAMiliraIsForcetargetDowned?.SetValue(item, false);
						_snapTarget.Remove(item);
						_snapTarget.Add(item, false);
						SyncTurretTargets_PLAMilira((ThingComp)item, LocalTargetInfo.Invalid, LocalTargetInfo.Invalid, isForcetargetDowned: false);
					}
				}
			}
			if (!(_fiPLAMiliraForcedTarget != null))
			{
				continue;
			}
			LocalTargetInfo val2 = (LocalTargetInfo)_fiPLAMiliraForcedTarget.GetValue(item);
			object orCreateValue2 = _snapTarget.GetOrCreateValue(item);
			int num2;
			if (orCreateValue2 is bool)
			{
				flag4 = (bool)orCreateValue2;
				num2 = 1;
			}
			else
			{
				num2 = 0;
			}
			bool flag5 = (byte)((uint)num2 & (flag4 ? 1u : 0u)) != 0;
			bool isValid = val2.IsValid;
			if (flag5 && !isValid)
			{
				_snapTarget.Remove(item);
				_snapTarget.Add(item, false);
				SyncTurretTargets_PLAMilira((ThingComp)item, LocalTargetInfo.Invalid, LocalTargetInfo.Invalid, isForcetargetDowned: false);
			}
			else if (!flag5 && isValid)
			{
				_snapTarget.Remove(item);
				_snapTarget.Add(item, true);
				LocalTargetInfo forcedTarget = val2;
				if (val2.HasThing && val2.Thing != null)
				{
					forcedTarget = new LocalTargetInfo(val2.Thing.Position);
				}
				currentTarget = new LocalTargetInfo(forcedTarget.Cell);
				SyncTurretTargets_PLAMilira((ThingComp)item, currentTarget, forcedTarget, isForcetargetDowned: false);
			}
			else if (!flag5 && !isValid && !(orCreateValue2 is bool))
			{
				_snapTarget.Remove(item);
				_snapTarget.Add(item, false);
			}
		}
	}

	private static void PatchCompGizmoMethods()
	{
		PatchGizmoList(_switchType, "CompGetGizmosExtra", "WrapList_Switch");
		PatchGizmoList(_switchNewType, "CompGetGizmosExtra", "WrapList_SwitchNew");
		PatchGizmoList(_antiballisticType, "CompGetGizmosExtra", "WrapList_Antiballistic");
		PatchGizmoList(_antiballisticType, "CompGetWornGizmosExtra", "WrapList_Antiballistic");
		PatchGizmoList(_antiballisticType, "GetGizmos", "WrapList_Antiballistic");
		PatchGizmoList(_antiHarrierType, "CompGetGizmosExtra", "WrapList_AntiHarrier");
		PatchGizmoList(_naglfarMidBuffType, "CompGetGizmosExtra", "WrapList_NaglfarMidBuff");
		PatchGizmoList(_naglfarMidBuffType, "CompGetWornGizmosExtra", "WrapList_NaglfarMidBuff");
		PatchGizmoList(_naglfarMidBuffType, "GetGizmos", "WrapList_NaglfarMidBuff");
		PatchGizmoList(_naglfarRightBuffType, "CompGetGizmosExtra", "WrapList_NaglfarRightBuff");
		PatchGizmoList(_naglfarRightBuffType, "CompGetWornGizmosExtra", "WrapList_NaglfarRightBuff");
		PatchGizmoList(_naglfarRightBuffType, "GetGizmos", "WrapList_NaglfarRightBuff");
		PatchGizmoList(_naglfarLeftArmorType, "CompGetGizmosExtra", "WrapList_NaglfarLeftArmor");
		PatchGizmoList(_naglfarLeftArmorType, "CompGetWornGizmosExtra", "WrapList_NaglfarLeftArmor");
		PatchGizmoList(_naglfarLeftArmorType, "GetGizmos", "WrapList_NaglfarLeftArmor");
		PatchGizmoList(_naglfarMidArmorType, "CompGetGizmosExtra", "WrapList_NaglfarMidArmor");
		PatchGizmoList(_naglfarMidArmorType, "CompGetWornGizmosExtra", "WrapList_NaglfarMidArmor");
		PatchGizmoList(_naglfarMidArmorType, "GetGizmos", "WrapList_NaglfarMidArmor");
		PatchGizmoList(_changeSupportType, "CompGetGizmosExtra", "WrapList_ChangeSupport");
	}

	private static void WrapListGeneric(ref IEnumerable<Gizmo> __result, ThingComp __instance, FieldInfo fi, string fieldName, Type compType)
	{
		if (MP.IsInMultiplayer && __instance != null && !(fi == null) && !(compType == null))
		{
			__result = WrapGizmoList(__result, __instance, fi, fieldName, compType);
		}
	}

	private static IEnumerable<Gizmo> WrapGizmoList(IEnumerable<Gizmo> source, ThingComp comp, FieldInfo fi, string fieldName, Type compType)
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
			else if (((object)g).GetType().Name == "Gizmo_ActionAndToggle" || ((object)g).GetType().BaseType?.Name == "Gizmo_ActionAndToggle")
			{
				FieldInfo taField = AccessTools.Field(((object)g).GetType(), "toggleAction");
				if (taField != null)
				{
					Action origAction = taField.GetValue(g) as Action;
					taField.SetValue(g, (Action)delegate
					{
						origAction?.Invoke();
						if (!MP.IsExecutingSyncCommand)
						{
							SyncBoolField(comp, fieldName, (bool)fi.GetValue(comp));
						}
					});
				}
			}
			yield return g;
		}
	}

	public static void WrapList_Switch(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiSwitchIsAltVerb == null) && !(_switchType == null))
		{
			__result = WrapGizmoList_Switch(__result, __instance, _fiSwitchIsAltVerb, _switchType);
		}
	}

	public static void WrapList_SwitchNew(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiSwitchNewIsAltVerb == null) && !(_switchNewType == null))
		{
			__result = WrapGizmoList_Switch(__result, __instance, _fiSwitchNewIsAltVerb, _switchNewType);
		}
	}

	private static IEnumerable<Gizmo> WrapGizmoList_Switch(IEnumerable<Gizmo> source, ThingComp comp, FieldInfo fi, Type compType)
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
					if (!MP.IsExecutingSyncCommand)
					{
						bool value = (bool)fi.GetValue(comp);
						SyncBoolField(comp, "isAltVerbSelected", value);
						CompEquippable val = ThingCompUtility.TryGetComp<CompEquippable>((Thing)(object)comp.parent);
						if (val != null && val.AllVerbs.Count >= 2)
						{
							GenList.Swap<Verb>((IList<Verb>)val.AllVerbs, 0, 1);
						}
					}
				};
			}
			yield return g;
		}
	}

	public static void WrapList_Antiballistic(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_antiballisticType == null))
		{
			__result = WrapGizmoList_Antiballistic(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapGizmoList_Antiballistic(IEnumerable<Gizmo> source, ThingComp comp)
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
						if (_fiSwitchOn != null)
						{
							SyncBoolField(comp, "switchOn", (bool)_fiSwitchOn.GetValue(comp));
						}
						if (_fiSwitchOnAntiAir != null)
						{
							SyncBoolField(comp, "switchOnAntiAir", (bool)_fiSwitchOnAntiAir.GetValue(comp));
						}
					}
				};
			}
			else if (((object)g).GetType().Name == "Gizmo_ActionAndToggle" || ((object)g).GetType().BaseType?.Name == "Gizmo_ActionAndToggle")
			{
				FieldInfo taField = AccessTools.Field(((object)g).GetType(), "toggleAction");
				if (taField != null)
				{
					Action origAction = taField.GetValue(g) as Action;
					taField.SetValue(g, (Action)delegate
					{
						origAction?.Invoke();
						if (!MP.IsExecutingSyncCommand)
						{
							if (_fiSwitchOn != null)
							{
								SyncBoolField(comp, "switchOn", (bool)_fiSwitchOn.GetValue(comp));
							}
							if (_fiSwitchOnAntiAir != null)
							{
								SyncBoolField(comp, "switchOnAntiAir", (bool)_fiSwitchOnAntiAir.GetValue(comp));
							}
						}
					});
				}
			}
			yield return g;
		}
	}

	public static void WrapList_AntiHarrier(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		WrapListGeneric(ref __result, __instance, _fiShouldAutoLaunch, "shouldAutoLaunch", _antiHarrierType);
	}

	public static void WrapList_NaglfarMidBuff(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		WrapListGeneric(ref __result, __instance, _fiNaglfarMidBuffIsOn, "isOn", _naglfarMidBuffType);
	}

	public static void WrapList_NaglfarRightBuff(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		WrapListGeneric(ref __result, __instance, _fiNaglfarRightBuffIsOn, "isOn", _naglfarRightBuffType);
	}

	public static void WrapList_NaglfarLeftArmor(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		WrapListGeneric(ref __result, __instance, _fiNaglfarLeftArmorIsOn, "isOn", _naglfarLeftArmorType);
	}

	public static void WrapList_NaglfarMidArmor(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		WrapListGeneric(ref __result, __instance, _fiNaglfarMidArmorIsOn, "isOn", _naglfarMidArmorType);
	}

	public static void WrapList_ChangeSupport(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiChangeChoice == null))
		{
			__result = WrapGizmoList_ChangeSupport(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapGizmoList_ChangeSupport(IEnumerable<Gizmo> source, ThingComp comp)
	{
		int lastChoice = (int)(_fiChangeChoice?.GetValue(comp) ?? ((object)0));
		bool lastAuto = _fiChangeIsAutoChoose != null && (bool)_fiChangeIsAutoChoose.GetValue(comp);
		foreach (Gizmo g in source)
		{
			Command_Toggle toggle = (Command_Toggle)(object)((g is Command_Toggle) ? g : null);
			if (toggle != null)
			{
				Action origAction = toggle.toggleAction;
				toggle.toggleAction = delegate
				{
					origAction?.Invoke();
					if (!MP.IsExecutingSyncCommand)
					{
						CheckAndSyncChangeSupport(comp, ref lastChoice, ref lastAuto);
					}
				};
			}
			else if (((object)g).GetType().Name == "Gizmo_ActionAndToggle" || ((object)g).GetType().BaseType?.Name == "Gizmo_ActionAndToggle")
			{
				FieldInfo taField = AccessTools.Field(((object)g).GetType(), "toggleAction");
				if (taField != null)
				{
					Action origAction2 = taField.GetValue(g) as Action;
					taField.SetValue(g, (Action)delegate
					{
						origAction2?.Invoke();
						if (!MP.IsExecutingSyncCommand)
						{
							CheckAndSyncChangeSupport(comp, ref lastChoice, ref lastAuto);
						}
					});
				}
			}
			else
			{
				Command_Action action = (Command_Action)(object)((g is Command_Action) ? g : null);
				if (action != null)
				{
					Action orig = action.action;
					action.action = delegate
					{
						orig?.Invoke();
						if (!MP.IsExecutingSyncCommand)
						{
							CheckAndSyncChangeSupport(comp, ref lastChoice, ref lastAuto);
						}
					};
				}
			}
			yield return g;
		}
	}

	private static void CheckAndSyncChangeSupport(ThingComp comp, ref int lastChoice, ref bool lastAuto)
	{
		if (_fiChangeChoice != null)
		{
			int num = (int)_fiChangeChoice.GetValue(comp);
			if (num != lastChoice)
			{
				lastChoice = num;
				SyncIntField(comp, "choice", num);
			}
		}
		if (_fiChangeIsAutoChoose != null)
		{
			bool flag = (bool)_fiChangeIsAutoChoose.GetValue(comp);
			if (flag != lastAuto)
			{
				lastAuto = flag;
				SyncBoolField(comp, "IsAutoChooseChoice", flag);
			}
		}
	}

	private static void PatchCommandProcessInput()
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Expected O, but got Unknown
		if (!(_commandAddHediffsType == null))
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_commandAddHediffsType, "ProcessInput", (Type[])null, (Type[])null);
			if (!(methodInfo == null))
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_GizmoCompat), "Prefix_ProcessInput", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	public static bool Prefix_ProcessInput(object __instance)
	{
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Expected O, but got Unknown
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected O, but got Unknown
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Expected O, but got Unknown
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (__instance == null)
		{
			return true;
		}
		object obj = AccessTools.Field(_commandAddHediffsType, "parentComp")?.GetValue(__instance);
		Map targetMap = (Map)(AccessTools.Field(_commandAddHediffsType, "targetMap")?.GetValue(__instance));
		Faction targetFaction = (Faction)(AccessTools.Field(_commandAddHediffsType, "targetFaction")?.GetValue(__instance));
		HediffDef hediffToAdd = (HediffDef)(AccessTools.Field(_commandAddHediffsType, "hediffToAdd")?.GetValue(__instance));
		IntVec3 position = (IntVec3)(AccessTools.Field(_commandAddHediffsType, "position")?.GetValue(__instance) ?? ((object)default(IntVec3)));
		SyncedProcessInput((ThingComp)obj, targetMap, targetFaction, hediffToAdd, position);
		return false;
	}

	public static void SyncedProcessInput(ThingComp parentComp, Map targetMap, Faction targetFaction, HediffDef hediffToAdd, IntVec3 position)
	{
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		if (parentComp == null)
		{
			return;
		}
		if (targetMap != null && targetFaction != null && hediffToAdd != null)
		{
			foreach (Pawn item in targetMap.mapPawns.AllPawnsSpawned)
			{
				if (((Thing)item).Faction != targetFaction)
				{
					continue;
				}
				Hediff val = item.health.hediffSet.GetFirstHediffOfDef(hediffToAdd, false);
				if (val == null)
				{
					val = item.health.AddHediff(hediffToAdd, (BodyPartRecord)null, (DamageInfo?)null, (DamageWorker.DamageResult)null);
				}
				if (val != null)
				{
					Hediff obj = val;
					obj.Severity += 5f;
					HediffComp_Disappears val2 = HediffUtility.TryGetComp<HediffComp_Disappears>(val);
					if (val2 != null)
					{
						val2.ticksToDisappear = 7501;
					}
				}
			}
		}
		FieldInfo fieldInfo = AccessTools.DeclaredField(((object)parentComp).GetType(), "cooldownTicks");
		FieldInfo fieldInfo2 = AccessTools.DeclaredField(((object)parentComp).GetType(), "props");
		if (fieldInfo != null && fieldInfo2 != null)
		{
			object value = fieldInfo2.GetValue(parentComp);
			FieldInfo fieldInfo3 = value?.GetType().GetField("cooldownTicksTotal");
			if (fieldInfo3 != null)
			{
				fieldInfo.SetValue(parentComp, fieldInfo3.GetValue(value));
			}
		}
		if (fieldInfo2 != null)
		{
			object value2 = fieldInfo2.GetValue(parentComp);
			object obj2 = (value2?.GetType().GetField("effecterDef"))?.GetValue(value2);
			EffecterDef val3 = (EffecterDef)((obj2 is EffecterDef) ? obj2 : null);
			if (val3 != null && targetMap != null && position.IsValid)
			{
				Effecter val4 = val3.Spawn();
				val4.Trigger(new TargetInfo(position, targetMap, false), new TargetInfo(position, targetMap, false), -1);
				val4.Cleanup();
			}
		}
	}

	public static void SyncBoolField(ThingComp comp, string fieldName, bool value)
	{
		if (comp != null)
		{
			AccessTools.DeclaredField(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
		}
	}

	public static void SyncIntField(ThingComp comp, string fieldName, int value)
	{
		if (comp != null)
		{
			AccessTools.DeclaredField(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
		}
	}

	public static void SyncTurretTargets_PLAMilira(ThingComp comp, LocalTargetInfo currentTarget, LocalTargetInfo forcedTarget, bool isForcetargetDowned)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		if (comp != null)
		{
			_fiPLAMiliraCurrentTarget?.SetValue(comp, currentTarget);
			_fiPLAMiliraForcedTarget?.SetValue(comp, forcedTarget);
			_fiPLAMiliraIsForcetargetDowned?.SetValue(comp, isForcetargetDowned);
		}
	}

	private static void RegField(Type type, string fieldName, out FieldInfo fi)
	{
		fi = null;
		if (type == null)
		{
			return;
		}
		fi = AccessTools.Field(type, fieldName);
		if (!(fi != null))
		{
			return;
		}
		try
		{
			MP.RegisterSyncField(fi);
		}
		catch
		{
		}
	}

	private static void PatchGizmoList(Type type, string methodName, string wrapperName)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.PatchMethodPostfix(harmony, type, methodName, wrapperName, typeof(PLAMilira_GizmoCompat));
	}

	private static void TryReg(MethodInfo m)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(fullName, "PLAMilira_GizmoCompat");
	}
}
