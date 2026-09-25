using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class FunnelBit_Compat
{
	private const string NS = "FunnelBit.";

	private static readonly Harmony harmony;

	private static bool isInitialized;

	private static Type _compTurretGunType;

	private static Type _gizmoBaseType;

	private static Type _toggleTargetEnum;

	private static FieldInfo _lastWorkScanHitTick;
	private static FieldInfo _workScanRoundRobinIndex;
	private static FieldInfo _nextWorkScanTicks;
	private static FieldInfo _workScanMissCounts;
	private static FieldInfo _isWarmingUp;
	private static FieldInfo _warmupStartedTick;
	private static FieldInfo _lastAttackTargetTick;
	private static FieldInfo _lastAttackedTarget;
	private static FieldInfo _arsenalNextScanTick;
	private static Type _arsenalCompType;
	private static FieldInfo _slotReservations;
	private static FieldInfo _slotReservationLastCleanupTick;
	private static FieldInfo _buildFrameReservations;
	private static MethodInfo _clearWorkReservations;
	[ThreadStatic] private static object _pendingToggleGizmo;
	[ThreadStatic] private static object _pendingToggleTarget;
	[ThreadStatic] private static bool _pendingToggleValue;

	static FunnelBit_Compat()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("FunnelBit.Compat");
		if (!MP.enabled || !ModsConfig.IsActive("rabiosus.funnelmilian"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"[FunnelBit_Compat] init failed - {arg}");
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

	private static void LatePatch()
	{
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Expected O, but got Unknown
		try
		{
			TryReg(typeof(FunnelBit_Compat).GetMethod("SyncFunnelBitToggle", BindingFlags.Static | BindingFlags.Public, null, new Type[3]
			{
				typeof(ThingComp),
				typeof(string),
				typeof(bool)
			}, null));
			_compTurretGunType = Resolve("FunnelBit.CompTurretGun_FunnelBit");
			PatchSnapshotState();
			_gizmoBaseType = Resolve("FunnelBit.Gizmo_FunnelBitBase");
			_toggleTargetEnum = Resolve("FunnelBit.FunnelGizmoToggleTarget");
			if (_compTurretGunType != null)
			{
				TryRegField("fireAtWill");
				TryRegField("autoFire");
				TryRegField("focusFire");
				TryRegField("autoFireAtFocus");
				TryRegField("onlyAutoFireWhileDraft");
				TryRegField("droneWorkEnabled");
			}
			if (_gizmoBaseType != null && _toggleTargetEnum != null)
			{
				MethodInfo methodInfo = AccessTools.DeclaredMethod(_gizmoBaseType, "ToggleFireAtWill", new Type[1] { _toggleTargetEnum }, (Type[])null);
				if (methodInfo != null)
				{
					harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(FunnelBit_Compat), nameof(Prefix_ToggleFireAtWill)), null, null, null);
				}
				MethodInfo getToggle = AccessTools.DeclaredMethod(_gizmoBaseType, "GetFireAtWill", new[] { _toggleTargetEnum });
				MethodInfo setToggle = AccessTools.DeclaredMethod(_gizmoBaseType, "SetFireAtWill", new[] { _toggleTargetEnum, typeof(bool) });
				Type layoutType = Resolve("FunnelBit.Gizmo_FunnelBitLayout");
				MethodInfo drawToggle = layoutType == null ? null : AccessTools.DeclaredMethod(layoutType, "DrawToggle");
				if (getToggle == null || setToggle == null || drawToggle == null)
					Log.Error("[FunnelBit_Compat] merged toggle targets missing; FunnelBit assembly changed");
				else
				{
					harmony.Patch(getToggle, postfix: new HarmonyMethod(typeof(FunnelBit_Compat), nameof(Postfix_GetFireAtWill)));
					harmony.Patch(setToggle, prefix: new HarmonyMethod(typeof(FunnelBit_Compat), nameof(Prefix_SetFireAtWill)));
					harmony.Patch(drawToggle, finalizer: new HarmonyMethod(typeof(FunnelBit_Compat), nameof(Finalizer_DrawToggle)));
				}
			}
			if (_compTurretGunType != null)
			{
				// Developer mode actions mutate live projectiles and must execute on every peer.
				TryReg(AccessTools.DeclaredMethod(_compTurretGunType, "<CompGetGizmosExtra>b__102_0", Type.EmptyTypes));
				TryReg(AccessTools.DeclaredMethod(_compTurretGunType, "<CompGetWornGizmosExtra>b__103_0", Type.EmptyTypes));
				MethodInfo defaultToggle = AccessTools.DeclaredMethod(_compTurretGunType, "<CompGetWornGizmosExtra>b__103_2", Type.EmptyTypes);
				if (defaultToggle == null)
					Log.Error("[FunnelBit_Compat] default worn toggle target missing; FunnelBit assembly changed");
				else
					harmony.Patch(defaultToggle, prefix: new HarmonyMethod(typeof(FunnelBit_Compat), nameof(Prefix_DefaultWornToggle)));
			}
			Type type = Resolve("FunnelBit.Verb_LaunchFunnelBit");
			if (type != null)
			{
				TryReg(AccessTools.DeclaredMethod(type, "TryCastShot", (Type[])null, (Type[])null));
			}
			TryRegApply("CompAbilityEffect_LaunchFunnelAbility");
			TryRegApply("CompAbilityEffect_LaunchLineAoE");
			TryRegApply("CompAbilityEffect_LaunchLinearShield");
			TryRegCustomTargeting("CompAbilityEffect_LaunchLineAoE", "<StartCustomTargeting>b__7_0");
			TryRegCustomTargeting("CompAbilityEffect_LaunchLinearShield", "<StartCustomTargeting>b__3_0");
		}
		catch (Exception arg)
		{
			Log.Error($"[FunnelBit_Compat] LatePatch failed: {arg}");
		}
	}

	private static void PatchSnapshotState()
	{
		if (_compTurretGunType == null)
		{
			Log.Error("[FunnelBit_Compat] Snapshot state target missing: CompTurretGun_FunnelBit");
			return;
		}
		_lastWorkScanHitTick = AccessTools.DeclaredField(_compTurretGunType, "lastWorkScanHitTick");
		_workScanRoundRobinIndex = AccessTools.DeclaredField(_compTurretGunType, "workScanRoundRobinIndex");
		_nextWorkScanTicks = AccessTools.DeclaredField(_compTurretGunType, "nextWorkScanTicks");
		_workScanMissCounts = AccessTools.DeclaredField(_compTurretGunType, "workScanMissCounts");
		_isWarmingUp = AccessTools.DeclaredField(_compTurretGunType, "isWarmingUp");
		_warmupStartedTick = AccessTools.DeclaredField(_compTurretGunType, "warmupStartedTick");
		_lastAttackTargetTick = AccessTools.DeclaredField(_compTurretGunType, "lastAttackTargetTick");
		_lastAttackedTarget = AccessTools.DeclaredField(_compTurretGunType, "lastAttackedTarget");
		if (_lastWorkScanHitTick == null || _workScanRoundRobinIndex == null || _nextWorkScanTicks == null ||
			_workScanMissCounts == null || _isWarmingUp == null || _warmupStartedTick == null || _lastAttackTargetTick == null || _lastAttackedTarget == null)
		{
			Log.Error("[FunnelBit_Compat] Snapshot state field resolution failed; FunnelBit assembly changed");
			return;
		}
		MethodInfo expose = AccessTools.DeclaredMethod(_compTurretGunType, "PostExposeData", Type.EmptyTypes);
		if (expose == null)
		{
			Log.Error("[FunnelBit_Compat] Snapshot state method missing: CompTurretGun_FunnelBit.PostExposeData");
			return;
		}
		harmony.Patch(expose, postfix: new HarmonyMethod(typeof(FunnelBit_Compat), nameof(ExposeTurretSnapshotState)));

		_arsenalCompType = Resolve("ArsenalMilian.CompFianchettoAssignedMechRepair");
		_arsenalNextScanTick = _arsenalCompType == null ? null : AccessTools.DeclaredField(_arsenalCompType, "nextScanTick");
		// This component inherits ThingComp.PostExposeData without an override.
		if (_arsenalNextScanTick != null)
		{
			MethodInfo exposeBase = AccessTools.DeclaredMethod(typeof(ThingComp), "PostExposeData", Type.EmptyTypes);
			if (exposeBase != null)
				harmony.Patch(exposeBase, postfix: new HarmonyMethod(typeof(FunnelBit_Compat), nameof(ExposeArsenalSnapshotState)));
		}
		Type slotReservationManager = Resolve("FunnelBit.DroneWorkSlotReservationManager");
		_slotReservations = slotReservationManager == null ? null : AccessTools.DeclaredField(slotReservationManager, "Reservations");
		_slotReservationLastCleanupTick = slotReservationManager == null ? null : AccessTools.DeclaredField(slotReservationManager, "lastCleanupTick");
		Type buildWorker = Resolve("FunnelBit.Worker_DroneWork_Build");
		_buildFrameReservations = buildWorker == null ? null : AccessTools.DeclaredField(buildWorker, "ReservedBuildFrames");
		Type workReservationManager = Resolve("FunnelBit.DroneWorkReservationManager");
		_clearWorkReservations = workReservationManager == null ? null : AccessTools.DeclaredMethod(workReservationManager, "ClearAll", Type.EmptyTypes);
		Type funnelManager = Resolve("FunnelBit.FunnelBitManager");
		MethodInfo clearSlots = funnelManager == null ? null : AccessTools.DeclaredMethod(funnelManager, "ClearAllStaticData", Type.EmptyTypes);
		if (_slotReservations == null || _slotReservationLastCleanupTick == null || clearSlots == null)
			Log.Error("[FunnelBit_Compat] slot reservation reset target missing; FunnelBit assembly changed");
		else
			harmony.Patch(clearSlots, postfix: new HarmonyMethod(typeof(FunnelBit_Compat), nameof(ClearSlotReservations)));
		if (_buildFrameReservations == null || _clearWorkReservations == null)
			Log.Error("[FunnelBit_Compat] work reservation reset target missing; FunnelBit assembly changed");
		Log.Message("[FunnelBit_Compat] snapshot fields: turret=8, arsenalClock=" + (_arsenalNextScanTick != null));
	}

	public static void ClearSlotReservations()
	{
		// FunnelBit resets its slot dictionary on a new game and on Multiplayer SaveAndReload.
		// A reservation keyed by an old turret must not survive that reset.
		(_slotReservations.GetValue(null) as IList)?.Clear();
		_slotReservationLastCleanupTick.SetValue(null, -1);
		(_buildFrameReservations?.GetValue(null) as IDictionary)?.Clear();
		_clearWorkReservations?.Invoke(null, null);
	}

	public static void ExposeTurretSnapshotState(object __instance)
	{
		int lastHit = (int)_lastWorkScanHitTick.GetValue(__instance);
		int roundRobin = (int)_workScanRoundRobinIndex.GetValue(__instance);
		int warmupTick = (int)_warmupStartedTick.GetValue(__instance);
		int attackTick = (int)_lastAttackTargetTick.GetValue(__instance);
		bool warming = (bool)_isWarmingUp.GetValue(__instance);
		LocalTargetInfo attacked = (LocalTargetInfo)_lastAttackedTarget.GetValue(__instance);
		List<int> next = ToList((int[])_nextWorkScanTicks.GetValue(__instance));
		List<int> misses = ToList((int[])_workScanMissCounts.GetValue(__instance));
		Scribe_Values.Look(ref lastHit, "mpFunnelLastWorkScanHitTick", -9999);
		Scribe_Values.Look(ref roundRobin, "mpFunnelWorkScanRoundRobinIndex", 0);
		Scribe_Values.Look(ref warmupTick, "mpFunnelWarmupStartedTick", 0);
		Scribe_Values.Look(ref attackTick, "mpFunnelLastAttackTargetTick", 0);
		Scribe_Values.Look(ref warming, "mpFunnelIsWarmingUp", false);
		Scribe_TargetInfo.Look(ref attacked, "mpFunnelLastAttackedTarget");
		Scribe_Collections.Look(ref next, "mpFunnelNextWorkScanTicks", LookMode.Value);
		Scribe_Collections.Look(ref misses, "mpFunnelWorkScanMissCounts", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			_lastWorkScanHitTick.SetValue(__instance, lastHit);
			_workScanRoundRobinIndex.SetValue(__instance, roundRobin);
			_warmupStartedTick.SetValue(__instance, warmupTick);
			_lastAttackTargetTick.SetValue(__instance, attackTick);
			_isWarmingUp.SetValue(__instance, warming);
			_lastAttackedTarget.SetValue(__instance, attacked);
			_nextWorkScanTicks.SetValue(__instance, next?.ToArray());
			_workScanMissCounts.SetValue(__instance, misses?.ToArray());
		}
	}

	private static List<int> ToList(int[] values) => values == null ? null : new List<int>(values);

	public static void ExposeArsenalSnapshotState(ThingComp __instance)
	{
		if (_arsenalCompType == null || !_arsenalCompType.IsInstanceOfType(__instance)) return;
		int next = (int)_arsenalNextScanTick.GetValue(__instance);
		Scribe_Values.Look(ref next, "mpArsenalAssignedRepairNextScanTick", 0);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
			_arsenalNextScanTick.SetValue(__instance, next);
	}

	public static void SyncFunnelBitToggle(ThingComp comp, string fieldName, bool value)
	{
		if (comp != null)
		{
			AccessTools.Field(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
		}
	}

	public static bool Prefix_ToggleFireAtWill(object __instance, object target)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand || _compTurretGunType == null || !_gizmoBaseType.IsInstanceOfType(__instance))
		{
			return true;
		}
		object obj = AccessTools.Field(_gizmoBaseType, "turretComp")?.GetValue(__instance);
		ThingComp val = (ThingComp)((obj is ThingComp) ? obj : null);
		if (val == null || !_compTurretGunType.IsInstanceOfType(val))
		{
			return true;
		}
		string fieldNameForToggleValue = GetFieldNameForToggleValue(target);
		if (fieldNameForToggleValue == null)
			return true;
		FieldInfo fieldInfo = AccessTools.DeclaredField(_compTurretGunType, fieldNameForToggleValue);
		if (fieldInfo == null)
			return true;
		SyncFunnelBitToggle(val, fieldNameForToggleValue, !(bool)fieldInfo.GetValue(val));
		_pendingToggleGizmo = __instance;
		_pendingToggleTarget = target;
		_pendingToggleValue = !(bool)fieldInfo.GetValue(val);
		return false;
	}

	public static void Postfix_GetFireAtWill(object __instance, object target, ref bool __result)
	{
		if (ReferenceEquals(__instance, _pendingToggleGizmo) && Equals(target, _pendingToggleTarget))
			__result = _pendingToggleValue;
	}

	public static bool Prefix_SetFireAtWill(object __instance, object target, bool value)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand || _compTurretGunType == null)
			return true;
		ThingComp comp = AccessTools.Field(_gizmoBaseType, "turretComp")?.GetValue(__instance) as ThingComp;
		string fieldName = GetFieldNameForToggleValue(target);
		if (comp == null || !_compTurretGunType.IsInstanceOfType(comp) || fieldName == null)
			return true;
		SyncFunnelBitToggle(comp, fieldName, value);
		return false;
	}

	public static Exception Finalizer_DrawToggle(Exception __exception)
	{
		_pendingToggleGizmo = null;
		_pendingToggleTarget = null;
		return __exception;
	}

	public static bool Prefix_DefaultWornToggle(ThingComp __instance)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
			return true;
		FieldInfo field = AccessTools.DeclaredField(_compTurretGunType, "fireAtWill");
		SyncFunnelBitToggle(__instance, "fireAtWill", !(bool)field.GetValue(__instance));
		return false;
	}

	private static void TryRegCustomTargeting(string className, string callbackName)
	{
		Type type = Resolve("FunnelBit." + className);
		MethodInfo method = type == null ? null : AccessTools.DeclaredMethod(type, callbackName, new[] { typeof(IntVec3), typeof(IntVec3) });
		if (method == null)
			Log.Error("[FunnelBit_Compat] custom targeting callback missing: " + className);
		else
			TryReg(method);
	}

	private static string GetFieldNameForToggleValue(object target)
	{
		if (!(target is Enum value))
		{
			return null;
		}
		return Convert.ToInt32(value) switch
		{
			0 => "fireAtWill", 
			2 => "fireAtWill", 
			3 => "focusFire", 
			4 => "autoFireAtFocus", 
			5 => "autoFire", 
			6 => "onlyAutoFireWhileDraft", 
			7 => "droneWorkEnabled", 
			_ => null, 
		};
	}

	private static void TryRegApply(string className)
	{
		Type type = Resolve("FunnelBit." + className);
		if (!(type == null))
		{
			TryReg(AccessTools.DeclaredMethod(type, "Apply", new Type[2]
			{
				typeof(LocalTargetInfo),
				typeof(LocalTargetInfo)
			}, (Type[])null));
		}
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(fullName, "FunnelBit_Compat");
	}

	private static void TryReg(MethodInfo m)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void TryRegField(string fieldName)
	{
		if (_compTurretGunType == null)
		{
			return;
		}
		try
		{
			MP.RegisterSyncField(_compTurretGunType, fieldName);
		}
		catch
		{
		}
	}
}
