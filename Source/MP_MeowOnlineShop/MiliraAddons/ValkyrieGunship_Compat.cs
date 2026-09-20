using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class ValkyrieGunship_Compat
{
	private const string NS = "ValkyrieGunship.";

	private static readonly Harmony harmony;

	private static Type _tFlight;

	private static Type _tShield;

	private static Type _tWeapon;

	private static Type _tAlphaEnding;

	private static Type _tShip;

	private static Type _tMountState;

	private static Type _tGizmoActionAndToggle;

	private static FieldInfo _fiAirborne;

	private static FieldInfo _fiAutoFire;

	private static FieldInfo _fiDestination;

	private static FieldInfo _fiOrbitMode;

	private static FieldInfo _fiOrbitCenter;

	private static FieldInfo _fiLandingQueued;

	private static FieldInfo _fiLandingCell;

	private static FieldInfo _fiShieldEnabled;

	private static FieldInfo _fiMounts;

	private static FieldInfo _fiEndingStarted;

	private static FieldInfo _fiExactPosition;

	private static FieldInfo _fiFacingAngle;

	private static FieldInfo _fiLiftTicks;

	private static FieldInfo _fiLandingTurning;

	private static FieldInfo _fiLandingDescending;

	private static FieldInfo _fiUpperTurretCurrentAngle;

	private static FieldInfo _fiUpperTurretLastCarrierAngle;

	private static Dictionary<int, (Vector3, float)> _lastFlightPositions;

	private static Dictionary<int, (int, bool, bool)> _lastAnimationStates;

	private static Dictionary<int, (float, float)> _lastTurretAngles;

	static ValkyrieGunship_Compat()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("ValkyrieGunship.Compat");
		_lastFlightPositions = new Dictionary<int, (Vector3, float)>();
		_lastAnimationStates = new Dictionary<int, (int, bool, bool)>();
		_lastTurretAngles = new Dictionary<int, (float, float)>();
		if (MP.enabled)
		{
			_tFlight = AccessTools.TypeByName("ValkyrieGunship.CompValkyrieFlight");
			_tShield = AccessTools.TypeByName("ValkyrieGunship.CompValkyrieShieldController");
			_tWeapon = AccessTools.TypeByName("ValkyrieGunship.CompValkyrieWeaponController");
			_tAlphaEnding = AccessTools.TypeByName("ValkyrieGunship.CompValkyrieAlphaEnding");
			_tShip = AccessTools.TypeByName("ValkyrieGunship.Building_ValkyrieGunship");
			_tMountState = AccessTools.TypeByName("ValkyrieGunship.WeaponMountState");
			_tGizmoActionAndToggle = AccessTools.TypeByName("AncotLibrary.Gizmo_ActionAndToggle");
			if (_tFlight != null)
			{
				_fiAirborne = AccessTools.DeclaredField(_tFlight, "airborne");
				_fiAutoFire = AccessTools.DeclaredField(_tFlight, "autoFire");
				_fiDestination = AccessTools.DeclaredField(_tFlight, "destination");
				_fiOrbitMode = AccessTools.DeclaredField(_tFlight, "orbitMode");
				_fiOrbitCenter = AccessTools.DeclaredField(_tFlight, "orbitCenter");
				_fiLandingQueued = AccessTools.DeclaredField(_tFlight, "landingQueued");
				_fiLandingCell = AccessTools.DeclaredField(_tFlight, "landingCell");
				_fiExactPosition = AccessTools.DeclaredField(_tFlight, "exactPosition");
				_fiFacingAngle = AccessTools.DeclaredField(_tFlight, "facingAngle");
				_fiLiftTicks = AccessTools.DeclaredField(_tFlight, "liftTicks");
				_fiLandingTurning = AccessTools.DeclaredField(_tFlight, "landingTurning");
				_fiLandingDescending = AccessTools.DeclaredField(_tFlight, "landingDescending");
			}
			if (_tShield != null)
			{
				_fiShieldEnabled = AccessTools.DeclaredField(_tShield, "shieldEnabled");
			}
			if (_tWeapon != null)
			{
				_fiMounts = AccessTools.DeclaredField(_tWeapon, "mounts");
				_fiUpperTurretCurrentAngle = AccessTools.DeclaredField(_tWeapon, "upperTurretCurrentAngle");
				_fiUpperTurretLastCarrierAngle = AccessTools.DeclaredField(_tWeapon, "upperTurretLastCarrierAngle");
			}
			if (_tAlphaEnding != null)
			{
				_fiEndingStarted = AccessTools.DeclaredField(_tAlphaEnding, "endingStarted");
			}
			RegisterSyncFields();
			RegisterSyncMethods();
			RegisterRpcMethods();
			RegisterRandomIsolation();
			PatchGizmos();
		}
	}

	private static void RegisterSyncFields()
	{
		Reg(_fiAirborne);
		Reg(_fiAutoFire);
		Reg(_fiOrbitMode);
		Reg(_fiLandingQueued);
		Reg(_fiDestination);
		Reg(_fiOrbitCenter);
		Reg(_fiLandingCell);
		Reg(_fiLandingTurning);
		Reg(_fiLandingDescending);
		Reg(_fiLiftTicks);
		Reg(_fiExactPosition);
		Reg(_fiFacingAngle);
		Reg(_fiShieldEnabled);
		Reg(_fiEndingStarted);
		static void Reg(FieldInfo f)
		{
			if (f != null)
			{
				global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncField(f);
			}
		}
	}

	private static void RegisterSyncMethods()
	{
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Expected O, but got Unknown
		//IL_0285: Unknown result type (might be due to invalid IL or missing references)
		//IL_0292: Expected O, but got Unknown
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Expected O, but got Unknown
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_0140: Expected O, but got Unknown
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Expected O, but got Unknown
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01de: Expected O, but got Unknown
		//IL_0218: Unknown result type (might be due to invalid IL or missing references)
		//IL_0224: Expected O, but got Unknown
		try
		{
			MP.RegisterSyncMethod(AccessTools.DeclaredMethod(typeof(CompLaunchable), "TryLaunch", new Type[2]
			{
				typeof(int),
				typeof(IntVec3)
			}, (Type[])null), (SyncType[])null);
		}
		catch
		{
		}
		if (_tFlight != null)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_tFlight, "Takeoff", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PostTakeoff", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(_tFlight, "TrySetFlightDestination", new Type[2]
			{
				typeof(IntVec3),
				typeof(bool)
			}, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PostTrySetFlightDestination", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(_tFlight, "ToggleOrbitMode", (Type[])null, (Type[])null);
			if (methodInfo3 != null)
			{
				harmony.Patch((MethodBase)methodInfo3, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PrefixToggleOrbitMode", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo4 = AccessTools.DeclaredMethod(_tFlight, "TryLandAt", new Type[1] { typeof(IntVec3) }, (Type[])null);
			if (methodInfo4 != null)
			{
				harmony.Patch((MethodBase)methodInfo4, (HarmonyMethod)null, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PostSyncFlightFields", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo5 = AccessTools.DeclaredMethod(_tFlight, "LandNow", (Type[])null, (Type[])null);
			if (methodInfo5 != null)
			{
				harmony.Patch((MethodBase)methodInfo5, (HarmonyMethod)null, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PostLandNow", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo6 = AccessTools.DeclaredMethod(_tFlight, "StopFlightOrder", (Type[])null, (Type[])null);
			if (methodInfo6 != null)
			{
				harmony.Patch((MethodBase)methodInfo6, (HarmonyMethod)null, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PostStopFlightOrder", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		if (_tWeapon != null)
		{
			MethodInfo methodInfo7 = AccessTools.DeclaredMethod(_tWeapon, "StartChoosingManualTarget", new Type[1] { typeof(int) }, (Type[])null);
			if (methodInfo7 != null)
			{
				harmony.Patch((MethodBase)methodInfo7, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PrefixStartManualTarget", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo8 = AccessTools.DeclaredMethod(_tWeapon, "ClearManualTargets", (Type[])null, (Type[])null);
			if (methodInfo8 != null)
			{
				MP.RegisterSyncMethod(methodInfo8, (SyncType[])null);
			}
		}
	}

	public static void PostTakeoff(ThingComp __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent != null && parent.Spawned)
			{
				RpcTakeoff(parent.Map.Index, parent.thingIDNumber);
			}
		}
	}

	public static void PostTrySetFlightDestination(ThingComp __instance, IntVec3 cell, bool showRejectMessage)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		if (MP.IsInMultiplayer)
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent != null && parent.Spawned)
			{
				RpcSetDestination(parent.Map.Index, parent.thingIDNumber, cell);
			}
		}
	}

	public static void PostStopFlightOrder(ThingComp __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent != null && parent.Spawned)
			{
				RpcStopFlight(parent.Map.Index, parent.thingIDNumber);
			}
		}
	}

	public static void PostLandNow(ThingComp __instance)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		if (MP.IsInMultiplayer)
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent != null && parent.Spawned)
			{
				int index = parent.Map.Index;
				int thingIDNumber = parent.thingIDNumber;
				IntVec3 position = parent.Position;
				Rot4 rotation = parent.Rotation;
				RpcLandNow(index, thingIDNumber, position, rotation.AsInt);
			}
		}
	}

	public static void PostSyncFlightFields(ThingComp __instance)
	{
		if (MP.IsInMultiplayer)
		{
			SyncFlightFields(__instance);
		}
	}

	public static void PostSyncWeaponFields(ThingComp __instance)
	{
		if (MP.IsInMultiplayer)
		{
			SyncWeaponMountsEnhanced(__instance);
		}
	}

	public static bool PrefixToggleOrbitMode(ThingComp __instance)
	{
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Thing thing = (Thing)(object)__instance.parent;
		if (_fiOrbitMode != null && (bool)_fiOrbitMode.GetValue(__instance))
		{
			_fiOrbitMode?.SetValue(__instance, false);
			_fiOrbitCenter?.SetValue(__instance, IntVec3.Invalid);
			_fiDestination?.SetValue(__instance, IntVec3.Invalid);
			SyncFlightFields(__instance);
			return false;
		}
		if (thing == null || !thing.Spawned)
		{
			return true;
		}
		Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), (Action<LocalTargetInfo>)delegate(LocalTargetInfo target)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
			if (GenGrid.InBounds(target.Cell, thing.Map))
			{
				_fiOrbitMode?.SetValue(__instance, true);
				_fiOrbitCenter?.SetValue(__instance, target.Cell);
				_fiDestination?.SetValue(__instance, IntVec3.Invalid);
				_fiLandingQueued?.SetValue(__instance, false);
				_fiLandingTurning?.SetValue(__instance, false);
				_fiLandingDescending?.SetValue(__instance, false);
				if (_fiLandingCell != null)
				{
					_fiLandingCell.SetValue(__instance, IntVec3.Invalid);
				}
				SyncFlightFields(__instance);
			}
		}, (Pawn)null, (Action)null, (Texture2D)null, true);
		return false;
	}

	public static bool PrefixStartManualTarget(ThingComp __instance, int mountIndex)
	{
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Expected O, but got Unknown
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!(_fiMounts?.GetValue(__instance) is IList list) || mountIndex < 0 || mountIndex >= list.Count)
		{
			return true;
		}
		object mount = list[mountIndex];
		if (mount == null)
		{
			return true;
		}
		FieldInfo fiManualTarget = AccessTools.Field(_tMountState, "ManualTarget");
		FieldInfo fiManualTargetCell = AccessTools.Field(_tMountState, "ManualTargetCell");
		FieldInfo fiCurrentTarget = AccessTools.Field(_tMountState, "CurrentTarget");
		if (fiManualTarget == null)
		{
			return true;
		}
		Thing thing = (Thing)(object)__instance.parent;
		if (thing == null || !thing.Spawned)
		{
			return true;
		}
		Find.Targeter.BeginTargeting(new TargetingParameters
		{
			canTargetPawns = true,
			canTargetBuildings = true,
			canTargetItems = true,
			canTargetLocations = true,
			canTargetFires = true
		}, (Action<LocalTargetInfo>)delegate(LocalTargetInfo target)
		{
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0092: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_0039: Unknown result type (might be due to invalid IL or missing references)
			//IL_0115: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_011a: Unknown result type (might be due to invalid IL or missing references)
			//IL_013d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0143: Unknown result type (might be due to invalid IL or missing references)
			Thing thing2 = target.Thing;
			if (thing2 != null)
			{
				fiManualTarget.SetValue(mount, thing2);
				fiManualTargetCell?.SetValue(mount, IntVec3.Invalid);
				fiCurrentTarget?.SetValue(mount, thing2);
			}
			else
			{
				fiManualTarget.SetValue(mount, null);
				FieldInfo fieldInfo = fiManualTargetCell;
				if ((object)fieldInfo != null)
				{
					object obj = mount;
					IntVec3 cell = target.Cell;
					fieldInfo.SetValue(obj, cell.ClampInsideMap(thing.Map));
				}
				fiCurrentTarget?.SetValue(mount, null);
			}
			object value = fiManualTarget.GetValue(mount);
			int targetThingID = (value as Thing)?.thingIDNumber ?? (-1);
			IntVec3 val = (IntVec3)((fiManualTargetCell != null) ? ((IntVec3)fiManualTargetCell.GetValue(mount)) : IntVec3.Invalid);
			SyncMountTarget(thing.Map.Index, thing.thingIDNumber, mountIndex, targetThingID, val.x, val.z);
		}, (Pawn)null, (Action)null, (Texture2D)null, true);
		return false;
	}

	private static void RegisterRpcMethods()
	{
		Reg("SyncWeaponHoldFire");
		Reg("SyncAirborne");
		Reg("SyncAutoFire");
		Reg("SyncOrbitMode");
		Reg("RpcSyncOrbitCenter");
		Reg("SyncLandingQueued");
		Reg("SyncLandingCell");
		Reg("SyncLandingTurning");
		Reg("SyncLandingDescending");
		Reg("RpcSetDestination");
		Reg("RpcTakeoff");
		Reg("RpcStopFlight");
		Reg("RpcLandNow");
		Reg("SyncShieldEnabled");
		Reg("SyncMountTarget");
		static void Reg(string name)
		{
			MethodInfo method = typeof(ValkyrieGunship_Compat).GetMethod(name, BindingFlags.Static | BindingFlags.Public);
			if (method != null)
			{
				MP.RegisterSyncMethod(method, (SyncType[])null);
			}
		}
	}

	private static void PatchGizmos()
	{
		int num = 0;
		num += PG(_tFlight, "CompGetGizmosExtra", "WrapFlightGizmos");
		num += PG(_tShield, "CompGetGizmosExtra", "WrapShieldGizmos");
		num += PG(_tWeapon, "CompGetGizmosExtra", "WrapWeaponGizmos");
		num += PG(_tAlphaEnding, "CompGetGizmosExtra", "WrapAlphaEndingGizmos");
		num += PG(_tShip, "GetGizmos", "WrapShipGizmos");
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
		harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(ValkyrieGunship_Compat), wrapperName, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		return 1;
	}

	private static void RegisterRandomIsolation()
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Expected O, but got Unknown
		//IL_0065: Expected O, but got Unknown
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected O, but got Unknown
		//IL_00d3: Expected O, but got Unknown
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Expected O, but got Unknown
		if (_tFlight != null)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_tFlight, "Takeoff", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRandFinalizer", (Type[])null));
			}
		}
		if (_tWeapon != null)
		{
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(_tWeapon, "HitBeamCell", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRandFinalizer", (Type[])null));
			}
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(_tWeapon, "UpdateUpperTurretAngle", (Type[])null, (Type[])null);
			if (methodInfo3 != null)
			{
				harmony.Patch((MethodBase)methodInfo3, (HarmonyMethod)null, new HarmonyMethod(typeof(ValkyrieGunship_Compat), "PostUpdateUpperTurretAngle", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	public static void WrapFlightGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null)
		{
			__result = WrapFlightInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapFlightInner(IEnumerable<Gizmo> source, ThingComp comp)
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
						SyncBoolFieldLocal(comp, _fiAutoFire, "autoFire");
					}
				};
			}
			else
			{
				Command_Action cmd = (Command_Action)(object)((g is Command_Action) ? g : null);
				if (cmd != null)
				{
					Action orig2 = cmd.action;
					cmd.action = delegate
					{
						orig2?.Invoke();
						if (!MP.IsExecutingSyncCommand)
						{
							SyncFlightFields(comp);
						}
					};
				}
				else if (_tGizmoActionAndToggle != null && _tGizmoActionAndToggle.IsInstanceOfType(g))
				{
					FieldInfo ta = AccessTools.Field(_tGizmoActionAndToggle, "toggleAction");
					if (ta != null)
					{
						Action orig3 = ta.GetValue(g) as Action;
						ta.SetValue(g, (Action)delegate
						{
							orig3?.Invoke();
							if (!MP.IsExecutingSyncCommand)
							{
								SyncFlightFields(comp);
							}
						});
					}
				}
			}
			yield return g;
		}
	}

	private static void SyncFlightFields(ThingComp comp)
	{
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		Thing parent = (Thing)(object)comp.parent;
		if (parent != null && parent.Spawned)
		{
			IntVec3 dest = (IntVec3)((_fiDestination != null) ? ((IntVec3)_fiDestination.GetValue(comp)) : IntVec3.Invalid);
			if (dest.IsValid)
			{
				RpcSetDestination(parent.Map.Index, parent.thingIDNumber, dest);
			}
			SyncBoolFieldLocal(comp, _fiAirborne, "airborne");
			SyncBoolFieldLocal(comp, _fiOrbitMode, "orbitMode");
			if (_fiOrbitCenter != null)
			{
				IntVec3 cell = (IntVec3)_fiOrbitCenter.GetValue(comp);
				RpcSyncOrbitCenter(parent.Map.Index, parent.thingIDNumber, cell);
			}
			SyncBoolFieldLocal(comp, _fiLandingQueued, "landingQueued");
			SyncBoolFieldLocal(comp, _fiLandingTurning, "landingTurning");
			SyncBoolFieldLocal(comp, _fiLandingDescending, "landingDescending");
			if (_fiLandingCell != null)
			{
				IntVec3 cell2 = (IntVec3)_fiLandingCell.GetValue(comp);
				SyncLandingCell(parent.Map.Index, parent.thingIDNumber, cell2);
			}
		}
	}

	public static void WrapShieldGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiShieldEnabled == null))
		{
			__result = WrapShieldInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapShieldInner(IEnumerable<Gizmo> source, ThingComp comp)
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
						SyncBoolFieldLocal(comp, _fiShieldEnabled, "shieldEnabled");
					}
				};
			}
			else if (_tGizmoActionAndToggle != null && _tGizmoActionAndToggle.IsInstanceOfType(g))
			{
				FieldInfo ta = AccessTools.Field(_tGizmoActionAndToggle, "toggleAction");
				if (ta != null)
				{
					Action orig2 = ta.GetValue(g) as Action;
					ta.SetValue(g, (Action)delegate
					{
						orig2?.Invoke();
						if (!MP.IsExecutingSyncCommand)
						{
							SyncBoolFieldLocal(comp, _fiShieldEnabled, "shieldEnabled");
						}
					});
				}
			}
			yield return g;
		}
	}

	public static void WrapWeaponGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiMounts == null))
		{
			__result = WrapWeaponInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapWeaponInner(IEnumerable<Gizmo> source, ThingComp comp)
	{
		foreach (Gizmo g in source)
		{
			Command_Action cmd = (Command_Action)(object)((g is Command_Action) ? g : null);
			if (cmd != null)
			{
				Action orig = cmd.action;
				cmd.action = delegate
				{
					//IL_014a: Unknown result type (might be due to invalid IL or missing references)
					//IL_013a: Unknown result type (might be due to invalid IL or missing references)
					//IL_014f: Unknown result type (might be due to invalid IL or missing references)
					//IL_0178: Unknown result type (might be due to invalid IL or missing references)
					orig?.Invoke();
					if (!MP.IsExecutingSyncCommand)
					{
						Thing parent = (Thing)(object)comp.parent;
						if (parent != null && parent.Spawned && _fiMounts?.GetValue(comp) is IList list)
						{
							for (int i = 0; i < list.Count; i++)
							{
								object obj = list[i];
								if (obj != null && !(_tMountState == null))
								{
									FieldInfo fieldInfo = AccessTools.Field(_tMountState, "CurrentTarget");
									FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "ManualTarget");
									FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "ManualTargetCell");
									FieldInfo fieldInfo4 = AccessTools.Field(_tMountState, "CooldownTicks");
									if (!(fieldInfo == null) && !(fieldInfo4 == null))
									{
										object value = fieldInfo.GetValue(obj);
										Thing currentTarget = (Thing)((value is Thing) ? value : null);
										object obj2 = fieldInfo2?.GetValue(obj);
										Thing manualTarget = (Thing)((obj2 is Thing) ? obj2 : null);
										IntVec3 manualTargetCell = (IntVec3)((fieldInfo3 != null) ? ((IntVec3)fieldInfo3.GetValue(obj)) : IntVec3.Invalid);
										int cooldownTicks = (int)fieldInfo4.GetValue(obj);
										SyncWeaponMountState(parent.Map.Index, parent.thingIDNumber, i, currentTarget, manualTarget, manualTargetCell, cooldownTicks);
									}
								}
							}
						}
					}
				};
			}
			yield return g;
		}
	}

	public static void WrapAlphaEndingGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null)
		{
			__result = WrapAlphaEndingInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapAlphaEndingInner(IEnumerable<Gizmo> source, ThingComp comp)
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
					if (!MP.IsExecutingSyncCommand && _fiEndingStarted != null)
					{
						SyncBoolFieldLocal(comp, _fiEndingStarted, "endingStarted");
					}
				};
			}
			yield return g;
		}
	}

	public static void WrapShipGizmos(ref IEnumerable<Gizmo> __result, object __instance)
	{
		if (MP.IsInMultiplayer && __instance != null)
		{
			__result = WrapShipInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapShipInner(IEnumerable<Gizmo> source, object instance)
	{
		foreach (Gizmo item in source)
		{
			yield return item;
		}
	}

	public static void SyncMountTarget(int mapIndex, int thingID, int mountIndex, int targetThingID, int cellX, int cellZ)
	{
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tWeapon);
		if (val == null || _fiMounts == null || _tMountState == null || !(_fiMounts.GetValue(val) is IList list) || mountIndex < 0 || mountIndex >= list.Count)
		{
			return;
		}
		object obj = list[mountIndex];
		if (obj == null)
		{
			return;
		}
		FieldInfo fieldInfo = AccessTools.Field(_tMountState, "ManualTarget");
		FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "ManualTargetCell");
		FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "CurrentTarget");
		if (targetThingID >= 0)
		{
			Thing val2 = FindThingByID(mapIndex, targetThingID);
			if (val2 != null)
			{
				fieldInfo?.SetValue(obj, val2);
				fieldInfo2?.SetValue(obj, IntVec3.Invalid);
				fieldInfo3?.SetValue(obj, val2);
			}
		}
		else if (cellX != -1)
		{
			fieldInfo?.SetValue(obj, null);
			fieldInfo2?.SetValue(obj, (object)new IntVec3(cellX, 0, cellZ));
			fieldInfo3?.SetValue(obj, null);
		}
	}

	private static Thing FindThingByID(int mapIndex, int thingID)
	{
		Map val = null;
		foreach (Map map in Find.Maps)
		{
			if (map.Index == mapIndex)
			{
				val = map;
				break;
			}
		}
		if (val == null)
		{
			return null;
		}
		foreach (Thing allThing in val.listerThings.AllThings)
		{
			if (allThing.thingIDNumber == thingID)
			{
				return allThing;
			}
		}
		return null;
	}

	public static void SyncFlightDestination(int mapIndex, int thingID, IntVec3 dest)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null && _fiDestination != null)
		{
			_fiDestination.SetValue(val, dest);
		}
	}

	public static void RpcSetDestination(int mapIndex, int thingID, IntVec3 dest)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			_fiDestination?.SetValue(val, dest);
			_fiLandingQueued?.SetValue(val, false);
			_fiLandingTurning?.SetValue(val, false);
			_fiLandingDescending?.SetValue(val, false);
			_fiLandingCell?.SetValue(val, IntVec3.Invalid);
		}
	}

	public static void RpcTakeoff(int mapIndex, int thingID)
	{
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			_fiAirborne?.SetValue(val, true);
			_fiLiftTicks?.SetValue(val, 180);
			_fiLandingQueued?.SetValue(val, false);
			_fiLandingTurning?.SetValue(val, false);
			_fiLandingDescending?.SetValue(val, false);
			_fiLandingCell?.SetValue(val, IntVec3.Invalid);
			_fiDestination?.SetValue(val, IntVec3.Invalid);
			_fiOrbitMode?.SetValue(val, false);
			_fiOrbitCenter?.SetValue(val, IntVec3.Invalid);
		}
	}

	public static void RpcStopFlight(int mapIndex, int thingID)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			_fiDestination?.SetValue(val, IntVec3.Invalid);
			_fiOrbitMode?.SetValue(val, false);
			_fiOrbitCenter?.SetValue(val, IntVec3.Invalid);
			_fiLandingQueued?.SetValue(val, false);
			_fiLandingTurning?.SetValue(val, false);
			_fiLandingDescending?.SetValue(val, false);
			_fiLandingCell?.SetValue(val, IntVec3.Invalid);
		}
	}

	public static void RpcLandNow(int mapIndex, int thingID, IntVec3 newPos, int rotAsInt)
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val == null)
		{
			return;
		}
		Thing parent = (Thing)(object)val.parent;
		if (parent != null)
		{
			_fiAirborne?.SetValue(val, false);
			_fiDestination?.SetValue(val, IntVec3.Invalid);
			_fiLandingQueued?.SetValue(val, false);
			_fiLandingTurning?.SetValue(val, false);
			_fiLandingDescending?.SetValue(val, false);
			_fiLandingCell?.SetValue(val, IntVec3.Invalid);
			_fiOrbitMode?.SetValue(val, false);
			_fiOrbitCenter?.SetValue(val, IntVec3.Invalid);
			_fiLiftTicks?.SetValue(val, 0);
			Rot4 rotation = default(Rot4);
			rotation = new Rot4(rotAsInt);
			_fiExactPosition?.SetValue(val, newPos.ToVector3Shifted());
			_fiFacingAngle?.SetValue(val, rotation.AsAngle);
			if (parent.Spawned)
			{
				parent.Position = newPos;
				parent.Rotation = rotation;
			}
		}
	}

	public static void SyncWeaponHoldFire(int mapIndex, int thingID, int mountIndex, bool holdFire)
	{
		ThingComp val = FindComp(mapIndex, thingID, _tWeapon);
		if (val != null && !(_fiMounts == null) && !(_tMountState == null) && _fiMounts.GetValue(val) is IList list && mountIndex >= 0 && mountIndex < list.Count)
		{
			AccessTools.Field(_tMountState, "HoldFire")?.SetValue(list[mountIndex], holdFire);
		}
	}

	private static ThingComp FindComp(int mapIndex, int thingID, Type compType)
	{
		Map val = null;
		foreach (Map map in Find.Maps)
		{
			if (map.Index == mapIndex)
			{
				val = map;
				break;
			}
		}
		if (val == null)
		{
			return null;
		}
		foreach (Thing allThing in val.listerThings.AllThings)
		{
			if (allThing.thingIDNumber != thingID)
			{
				continue;
			}
			ThingWithComps val2 = (ThingWithComps)(object)((allThing is ThingWithComps) ? allThing : null);
			if (val2 == null)
			{
				continue;
			}
			foreach (ThingComp allComp in val2.AllComps)
			{
				if (compType.IsInstanceOfType(allComp))
				{
					return allComp;
				}
			}
		}
		return null;
	}

	private static void SyncBoolFieldLocal(ThingComp comp, FieldInfo fi, string fieldName)
	{
		if (fi == null)
		{
			return;
		}
		bool value = (bool)fi.GetValue(comp);
		Thing parent = (Thing)(object)comp.parent;
		if (parent != null && parent.Spawned)
		{
			int index = parent.Map.Index;
			int thingIDNumber = parent.thingIDNumber;
			switch (fieldName)
			{
			case "airborne":
				SyncAirborne(index, thingIDNumber, value);
				break;
			case "autoFire":
				SyncAutoFire(index, thingIDNumber, value);
				break;
			case "orbitMode":
				SyncOrbitMode(index, thingIDNumber, value);
				break;
			case "landingQueued":
				SyncLandingQueued(index, thingIDNumber, value);
				break;
			case "landingTurning":
				SyncLandingTurning(index, thingIDNumber, value);
				break;
			case "landingDescending":
				SyncLandingDescending(index, thingIDNumber, value);
				break;
			case "shieldEnabled":
				SyncShieldEnabled(index, thingIDNumber, value);
				break;
			}
		}
	}

	public static void SyncAirborne(int mapIndex, int thingID, bool value)
	{
		SetFlightField(mapIndex, thingID, "airborne", value);
	}

	public static void SyncAutoFire(int mapIndex, int thingID, bool value)
	{
		SetFlightField(mapIndex, thingID, "autoFire", value);
	}

	public static void SyncOrbitMode(int mapIndex, int thingID, bool value)
	{
		SetFlightField(mapIndex, thingID, "orbitMode", value);
	}

	public static void RpcSyncOrbitCenter(int mapIndex, int thingID, IntVec3 cell)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			_fiOrbitCenter?.SetValue(val, cell);
		}
	}

	public static void SyncLandingQueued(int mapIndex, int thingID, bool value)
	{
		SetFlightField(mapIndex, thingID, "landingQueued", value);
	}

	public static void SyncLandingCell(int mapIndex, int thingID, IntVec3 cell)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			_fiLandingCell?.SetValue(val, cell);
		}
	}

	public static void SyncLandingTurning(int mapIndex, int thingID, bool value)
	{
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			_fiLandingTurning?.SetValue(val, value);
		}
	}

	public static void SyncLandingDescending(int mapIndex, int thingID, bool value)
	{
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			_fiLandingDescending?.SetValue(val, value);
		}
	}

	public static void SyncShieldEnabled(int mapIndex, int thingID, bool value)
	{
		ThingComp val = FindComp(mapIndex, thingID, _tShield);
		if (val != null)
		{
			_fiShieldEnabled?.SetValue(val, value);
		}
	}

	private static void SetFlightField(int mapIndex, int thingID, string fieldName, bool value)
	{
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null)
		{
			AccessTools.Field(_tFlight, fieldName)?.SetValue(val, value);
		}
	}

	public static void SyncFlightPositionAndAngle(int mapIndex, int thingID, Vector3 exactPosition, float facingAngle)
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null && !(_fiExactPosition == null) && !(_fiFacingAngle == null))
		{
			_fiExactPosition.SetValue(val, exactPosition);
			_fiFacingAngle.SetValue(val, facingAngle);
		}
	}

	public static void SyncFlightAnimation(int mapIndex, int thingID, int liftTicks, bool landingTurning, bool landingDescending)
	{
		ThingComp val = FindComp(mapIndex, thingID, _tFlight);
		if (val != null && !(_fiLiftTicks == null) && !(_fiLandingTurning == null) && !(_fiLandingDescending == null))
		{
			_fiLiftTicks.SetValue(val, liftTicks);
			_fiLandingTurning.SetValue(val, landingTurning);
			_fiLandingDescending.SetValue(val, landingDescending);
		}
	}

	public static void SyncUpperTurretAngles(int mapIndex, int thingID, float currentAngle, float lastCarrierAngle)
	{
		ThingComp val = FindComp(mapIndex, thingID, _tWeapon);
		if (val != null && !(_fiUpperTurretCurrentAngle == null) && !(_fiUpperTurretLastCarrierAngle == null))
		{
			_fiUpperTurretCurrentAngle.SetValue(val, currentAngle);
			_fiUpperTurretLastCarrierAngle.SetValue(val, lastCarrierAngle);
		}
	}

	public static void SyncWeaponMountState(int mapIndex, int thingID, int mountIndex, Thing currentTarget, Thing manualTarget, IntVec3 manualTargetCell, int cooldownTicks)
	{
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tWeapon);
		if (val != null && !(_fiMounts == null) && !(_tMountState == null) && _fiMounts.GetValue(val) is IList list && mountIndex >= 0 && mountIndex < list.Count)
		{
			object obj = list[mountIndex];
			if (obj != null)
			{
				FieldInfo fieldInfo = AccessTools.Field(_tMountState, "CurrentTarget");
				FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "ManualTarget");
				FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "ManualTargetCell");
				FieldInfo fieldInfo4 = AccessTools.Field(_tMountState, "CooldownTicks");
				fieldInfo?.SetValue(obj, currentTarget);
				fieldInfo2?.SetValue(obj, manualTarget);
				fieldInfo3?.SetValue(obj, manualTargetCell);
				fieldInfo4?.SetValue(obj, cooldownTicks);
			}
		}
	}

	public static void SyncWeaponMountBeamState(int mapIndex, int thingID, int mountIndex, int beamTicksLeft, IntVec3 beamTargetCell, int beamSeed)
	{
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		ThingComp val = FindComp(mapIndex, thingID, _tWeapon);
		if (val != null && !(_fiMounts == null) && !(_tMountState == null) && _fiMounts.GetValue(val) is IList list && mountIndex >= 0 && mountIndex < list.Count)
		{
			object obj = list[mountIndex];
			if (obj != null)
			{
				FieldInfo fieldInfo = AccessTools.Field(_tMountState, "BeamTicksLeft");
				FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "BeamTargetCell");
				FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "BeamSeed");
				fieldInfo?.SetValue(obj, beamTicksLeft);
				fieldInfo2?.SetValue(obj, beamTargetCell);
				fieldInfo3?.SetValue(obj, beamSeed);
			}
		}
	}

	public static void PostMoveTowardsPoint(ThingComp __instance)
	{
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || !MP.IsHosting)
		{
			return;
		}
		try
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent != null && parent.Spawned && !(_fiExactPosition == null) && !(_fiFacingAngle == null))
			{
				Vector3 val = (Vector3)_fiExactPosition.GetValue(__instance);
				float num = (float)_fiFacingAngle.GetValue(__instance);
				int thingIDNumber = parent.thingIDNumber;
				if (!_lastFlightPositions.TryGetValue(thingIDNumber, out var value) || value.Item1 != val || Math.Abs(value.Item2 - num) > 0.1f)
				{
					_lastFlightPositions[thingIDNumber] = (val, num);
					SyncFlightPositionAndAngle(parent.Map.Index, parent.thingIDNumber, val, num);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] PostMoveTowardsPoint error: " + ex.Message);
		}
	}

	public static void PostTickLandingTurn(ThingComp __instance)
	{
		if (!MP.IsInMultiplayer || !MP.IsHosting)
		{
			return;
		}
		try
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent != null && parent.Spawned && !(_fiLiftTicks == null) && !(_fiLandingTurning == null) && !(_fiLandingDescending == null))
			{
				int num = (int)_fiLiftTicks.GetValue(__instance);
				bool flag = (bool)_fiLandingTurning.GetValue(__instance);
				bool flag2 = (bool)_fiLandingDescending.GetValue(__instance);
				int thingIDNumber = parent.thingIDNumber;
				if (!_lastAnimationStates.TryGetValue(thingIDNumber, out var value) || value.Item1 != num || value.Item2 != flag || value.Item3 != flag2)
				{
					_lastAnimationStates[thingIDNumber] = (num, flag, flag2);
					SyncFlightAnimation(parent.Map.Index, parent.thingIDNumber, num, flag, flag2);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] PostTickLandingTurn error: " + ex.Message);
		}
	}

	public static void PostTickMount(ThingComp __instance, object mount, int index)
	{
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0120: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_019f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || !MP.IsHosting || mount == null || _tMountState == null)
		{
			return;
		}
		try
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent == null || !parent.Spawned)
			{
				return;
			}
			FieldInfo fieldInfo = AccessTools.Field(_tMountState, "CurrentTarget");
			FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "ManualTarget");
			FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "ManualTargetCell");
			FieldInfo fieldInfo4 = AccessTools.Field(_tMountState, "CooldownTicks");
			FieldInfo fieldInfo5 = AccessTools.Field(_tMountState, "BeamTicksLeft");
			FieldInfo fieldInfo6 = AccessTools.Field(_tMountState, "BeamTargetCell");
			FieldInfo fieldInfo7 = AccessTools.Field(_tMountState, "BeamSeed");
			if (!(fieldInfo == null) && !(fieldInfo4 == null))
			{
				object value = fieldInfo.GetValue(mount);
				Thing currentTarget = (Thing)((value is Thing) ? value : null);
				object obj = fieldInfo2?.GetValue(mount);
				Thing manualTarget = (Thing)((obj is Thing) ? obj : null);
				IntVec3 manualTargetCell = (IntVec3)((fieldInfo3 != null) ? ((IntVec3)fieldInfo3.GetValue(mount)) : IntVec3.Invalid);
				int cooldownTicks = (int)fieldInfo4.GetValue(mount);
				int num = ((fieldInfo5 != null) ? ((int)fieldInfo5.GetValue(mount)) : 0);
				IntVec3 beamTargetCell = (IntVec3)((fieldInfo6 != null) ? ((IntVec3)fieldInfo6.GetValue(mount)) : IntVec3.Invalid);
				int beamSeed = ((fieldInfo7 != null) ? ((int)fieldInfo7.GetValue(mount)) : 0);
				SyncWeaponMountState(parent.Map.Index, parent.thingIDNumber, index, currentTarget, manualTarget, manualTargetCell, cooldownTicks);
				if (num > 0 || beamTargetCell.IsValid)
				{
					SyncWeaponMountBeamState(parent.Map.Index, parent.thingIDNumber, index, num, beamTargetCell, beamSeed);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] PostTickMount error: " + ex.Message);
		}
	}

	public static void PostUpdateUpperTurretAngle(ThingComp __instance)
	{
		if (!MP.IsInMultiplayer || !MP.IsHosting)
		{
			return;
		}
		try
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent != null && parent.Spawned && !(_fiUpperTurretCurrentAngle == null) && !(_fiUpperTurretLastCarrierAngle == null))
			{
				float num = (float)_fiUpperTurretCurrentAngle.GetValue(__instance);
				float num2 = (float)_fiUpperTurretLastCarrierAngle.GetValue(__instance);
				int thingIDNumber = parent.thingIDNumber;
				if (!_lastTurretAngles.TryGetValue(thingIDNumber, out var value) || Math.Abs(value.Item1 - num) > 1f || Math.Abs(value.Item2 - num2) > 1f)
				{
					_lastTurretAngles[thingIDNumber] = (num, num2);
					SyncUpperTurretAngles(parent.Map.Index, parent.thingIDNumber, num, num2);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] PostUpdateUpperTurretAngle error: " + ex.Message);
		}
	}

	public static void PostTryRefreshAutoTarget(ThingComp __instance, object mount)
	{
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || !MP.IsHosting || mount == null || _tMountState == null)
		{
			return;
		}
		try
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent == null || !parent.Spawned || _fiMounts == null || !(_fiMounts.GetValue(__instance) is IList list))
			{
				return;
			}
			int num = -1;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] == mount)
				{
					num = i;
					break;
				}
			}
			if (num >= 0)
			{
				FieldInfo fieldInfo = AccessTools.Field(_tMountState, "CurrentTarget");
				FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "ManualTarget");
				FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "ManualTargetCell");
				FieldInfo fieldInfo4 = AccessTools.Field(_tMountState, "CooldownTicks");
				if (!(fieldInfo == null) && !(fieldInfo4 == null))
				{
					object value = fieldInfo.GetValue(mount);
					Thing currentTarget = (Thing)((value is Thing) ? value : null);
					object obj = fieldInfo2?.GetValue(mount);
					Thing manualTarget = (Thing)((obj is Thing) ? obj : null);
					IntVec3 manualTargetCell = (IntVec3)((fieldInfo3 != null) ? ((IntVec3)fieldInfo3.GetValue(mount)) : IntVec3.Invalid);
					int cooldownTicks = (int)fieldInfo4.GetValue(mount);
					SyncWeaponMountState(parent.Map.Index, parent.thingIDNumber, num, currentTarget, manualTarget, manualTargetCell, cooldownTicks);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] PostTryRefreshAutoTarget error: " + ex.Message);
		}
	}

	public static void PostResetCooldown(ThingComp __instance, object mount)
	{
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || !MP.IsHosting || mount == null || _tMountState == null)
		{
			return;
		}
		try
		{
			Thing parent = (Thing)(object)__instance.parent;
			if (parent == null || !parent.Spawned || _fiMounts == null || !(_fiMounts.GetValue(__instance) is IList list))
			{
				return;
			}
			int num = -1;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] == mount)
				{
					num = i;
					break;
				}
			}
			if (num >= 0)
			{
				FieldInfo fieldInfo = AccessTools.Field(_tMountState, "CurrentTarget");
				FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "ManualTarget");
				FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "ManualTargetCell");
				FieldInfo fieldInfo4 = AccessTools.Field(_tMountState, "CooldownTicks");
				if (!(fieldInfo4 == null))
				{
					object obj = fieldInfo?.GetValue(mount);
					Thing currentTarget = (Thing)((obj is Thing) ? obj : null);
					object obj2 = fieldInfo2?.GetValue(mount);
					Thing manualTarget = (Thing)((obj2 is Thing) ? obj2 : null);
					IntVec3 manualTargetCell = (IntVec3)((fieldInfo3 != null) ? ((IntVec3)fieldInfo3.GetValue(mount)) : IntVec3.Invalid);
					int cooldownTicks = (int)fieldInfo4.GetValue(mount);
					SyncWeaponMountState(parent.Map.Index, parent.thingIDNumber, num, currentTarget, manualTarget, manualTargetCell, cooldownTicks);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] PostResetCooldown error: " + ex.Message);
		}
	}

	private static void SyncWeaponMounts(ThingComp comp)
	{
		try
		{
			Thing parent = (Thing)(object)comp.parent;
			if (parent == null || !parent.Spawned || _fiMounts == null || _tMountState == null || !(_fiMounts.GetValue(comp) is IList list))
			{
				return;
			}
			FieldInfo fieldInfo = AccessTools.Field(_tMountState, "HoldFire");
			for (int i = 0; i < list.Count; i++)
			{
				object obj = list[i];
				if (obj != null)
				{
					bool holdFire = fieldInfo != null && (bool)fieldInfo.GetValue(obj);
					SyncWeaponHoldFire(parent.Map.Index, parent.thingIDNumber, i, holdFire);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] SyncWeaponMounts error: " + ex.Message);
		}
	}

	private static void SyncWeaponMountsEnhanced(ThingComp comp)
	{
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			Thing parent = (Thing)(object)comp.parent;
			if (parent == null || !parent.Spawned || _fiMounts == null || _tMountState == null || !(_fiMounts.GetValue(comp) is IList list))
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				object obj = list[i];
				if (obj != null)
				{
					FieldInfo fieldInfo = AccessTools.Field(_tMountState, "HoldFire");
					FieldInfo fieldInfo2 = AccessTools.Field(_tMountState, "CurrentTarget");
					FieldInfo fieldInfo3 = AccessTools.Field(_tMountState, "ManualTarget");
					FieldInfo fieldInfo4 = AccessTools.Field(_tMountState, "ManualTargetCell");
					FieldInfo fieldInfo5 = AccessTools.Field(_tMountState, "CooldownTicks");
					if (fieldInfo != null)
					{
						bool holdFire = (bool)fieldInfo.GetValue(obj);
						SyncWeaponHoldFire(parent.Map.Index, parent.thingIDNumber, i, holdFire);
					}
					if (fieldInfo2 != null)
					{
						object value = fieldInfo2.GetValue(obj);
						Thing currentTarget = (Thing)((value is Thing) ? value : null);
						object obj2 = fieldInfo3?.GetValue(obj);
						Thing manualTarget = (Thing)((obj2 is Thing) ? obj2 : null);
						IntVec3 manualTargetCell = (IntVec3)((fieldInfo4 != null) ? ((IntVec3)fieldInfo4.GetValue(obj)) : IntVec3.Invalid);
						int cooldownTicks = ((fieldInfo5 != null) ? ((int)fieldInfo5.GetValue(obj)) : 0);
						SyncWeaponMountState(parent.Map.Index, parent.thingIDNumber, i, currentTarget, manualTarget, manualTargetCell, cooldownTicks);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[ValkyrieGunship_Compat] SyncWeaponMountsEnhanced error: " + ex.Message);
		}
	}
}
