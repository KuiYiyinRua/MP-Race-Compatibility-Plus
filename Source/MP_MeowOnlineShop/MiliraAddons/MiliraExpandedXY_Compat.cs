using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class MiliraExpandedXY_Compat
{
	private class TrackedString
	{
		public string Value;
	}

	private const string NS = "MiliraExpandedXY.";

	private static readonly Harmony harmony;

	private static bool isInitialized;

	private static Type _tunerType;

	private static Type _gizmoPanelType;

	private static Type _solringenType;

	private static Type _shieldCoreType;

	private static FieldInfo _fiRegenThreshold;

	private static FieldInfo _fiEntropyThreshold;

	private static FieldInfo _fiWeaponThreshold;

	private static FieldInfo _fiShieldThreshold;

	private static FieldInfo _fiEnableRegen;

	private static FieldInfo _fiEnableEntropy;

	private static FieldInfo _fiEnableWeapon;

	private static FieldInfo _fiEnableShield;

	private static MethodInfo _mDoJump;

	private static MethodInfo _mGetFlyerDef;

	private static Type _jumpVerbType;

	private static Type _bladeJumpVerbType;

	private static Type _abilityType;

	private static readonly ConditionalWeakTable<ThingComp, TrackedString> _tunerSnapshots;

	private static FieldInfo _fiCombatMode => (_solringenType != null) ? AccessTools.Field(_solringenType, "CombatMode") : null;

	static MiliraExpandedXY_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("XiyueYM.MiliraExpandedXY.Compat");
		_tunerSnapshots = new ConditionalWeakTable<ThingComp, TrackedString>();
		if (!MP.enabled)
		{
			Log.Message("[MiliraExpandedXY_Compat] MP not enabled, skipping");
			return;
		}
		if (!ModsConfig.IsActive("XiyueYM.MiliraExpandedXY"))
		{
			Log.Message("[MiliraExpandedXY_Compat] Mod not installed, skipping");
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"MiliraExpandedXY_Compat: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Expected O, but got Unknown
		if (isInitialized)
		{
			return;
		}
		isInitialized = true;
		Type type = Resolve("MiliraExpandedXY.MEXY_SharedChargeGizmoComp");
		if (type != null)
		{
			TryReg(type, "SetRemainingCharges");
			TryRegField(type, "remainingCharges");
		}
		Type type2 = Resolve("MiliraExpandedXY.Weapons.MultiModeWeapon_Verb");
		if (type2 != null)
		{
			MethodInfo m = AccessTools.DeclaredMethod(type2, "SetMode", new Type[1] { typeof(int) }, (Type[])null);
			TryReg(m);
		}
		_solringenType = Resolve("MiliraExpandedXY.Buildings.Solringen_ApplyAoeHediff_ThingComp");
		if (_solringenType != null)
		{
			TryReg(typeof(MiliraExpandedXY_Compat).GetMethod("SyncSolringenMode", BindingFlags.Static | BindingFlags.Public));
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_solringenType, "CompGetGizmosExtra", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(MiliraExpandedXY_Compat), "WrapSolringenGizmos", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
	}

	private static void LatePatch()
	{
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e4: Expected O, but got Unknown
		//IL_0256: Unknown result type (might be due to invalid IL or missing references)
		//IL_0262: Expected O, but got Unknown
		//IL_049d: Unknown result type (might be due to invalid IL or missing references)
		//IL_04a9: Expected O, but got Unknown
		//IL_05d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e5: Expected O, but got Unknown
		//IL_050f: Unknown result type (might be due to invalid IL or missing references)
		//IL_051b: Expected O, but got Unknown
		_abilityType = Resolve("MiliraExpandedXY.Abilities.LuxPlumeSkill_Ability");
		if (_abilityType != null)
		{
			MethodInfo m = AccessTools.DeclaredMethod(_abilityType, "Activate", new Type[2]
			{
				typeof(LocalTargetInfo),
				typeof(LocalTargetInfo)
			}, (Type[])null);
			TryReg(m);
			MethodInfo methodInfo = AccessTools.DeclaredMethod(_abilityType, "QueueCastingJob", new Type[2]
			{
				typeof(LocalTargetInfo),
				typeof(LocalTargetInfo)
			}, (Type[])null);
			if (methodInfo != null)
			{
				try
				{
					MP.RegisterSyncMethod(methodInfo, (SyncType[])null);
				}
				catch (Exception arg)
				{
					Log.Error($"[MiliraExpandedXY_Compat] QueueCastingJob register FAILED: {arg}");
				}
			}
		}
		Type type = Resolve("MiliraExpandedXY.FlyUtilities");
		if (type != null)
		{
			_mDoJump = AccessTools.DeclaredMethod(type, "DoJump", new Type[6]
			{
				typeof(Pawn),
				typeof(LocalTargetInfo),
				typeof(VerbProperties),
				typeof(Ability),
				typeof(LocalTargetInfo),
				typeof(ThingDef)
			}, (Type[])null);
			if (_mDoJump != null)
			{
				try
				{
					MP.RegisterSyncMethod(_mDoJump, (SyncType[])null);
				}
				catch (Exception arg2)
				{
					Log.Error($"[MiliraExpandedXY_Compat] DoJump register FAILED: {arg2}");
				}
			}
		}
		_jumpVerbType = Resolve("MiliraExpandedXY.Abilities.Lux_PawnFlyer_Verb_CastAbility");
		if (_jumpVerbType != null)
		{
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(_jumpVerbType, "TryCastShot", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, new HarmonyMethod(typeof(MiliraExpandedXY_Compat), "JumpTryCastShotPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		_bladeJumpVerbType = Resolve("MiliraExpandedXY.Abilities.Blade_Lux_PawnFlyer_Verb_CastAbility");
		if (_bladeJumpVerbType != null)
		{
			_mGetFlyerDef = AccessTools.DeclaredMethod(_bladeJumpVerbType, "GetFlyerDef", (Type[])null, (Type[])null);
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(_bladeJumpVerbType, "TryCastShot", (Type[])null, (Type[])null);
			if (methodInfo3 != null)
			{
				harmony.Patch((MethodBase)methodInfo3, (HarmonyMethod)null, new HarmonyMethod(typeof(MiliraExpandedXY_Compat), "BladeJumpTryCastShotPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		Type type2 = Resolve("MiliraExpandedXY.DamageAbsorber_CompUseEffect");
		if (type2 != null)
		{
			TryReg(type2, "DoEffect", new Type[1] { typeof(Pawn) });
		}
		Type type3 = Resolve("MiliraExpandedXY.PsyonicCrystal_CompUseEffect");
		if (type3 != null)
		{
			TryReg(type3, "DoEffect", new Type[1] { typeof(Pawn) });
		}
		_tunerType = Resolve("MiliraExpandedXY.Apparels.PsyonicTuner_Main_ThingComp");
		if (_tunerType == null)
		{
			Log.Message("[MiliraExpandedXY_Compat] PsyonicTuner_Main_ThingComp NOT FOUND");
			return;
		}
		_fiRegenThreshold = AccessTools.DeclaredField(_tunerType, "regenThreshold");
		_fiEntropyThreshold = AccessTools.DeclaredField(_tunerType, "entropyThreshold");
		_fiWeaponThreshold = AccessTools.DeclaredField(_tunerType, "weaponThreshold");
		_fiShieldThreshold = AccessTools.DeclaredField(_tunerType, "shieldThreshold");
		_fiEnableRegen = AccessTools.DeclaredField(_tunerType, "enableRegen");
		_fiEnableEntropy = AccessTools.DeclaredField(_tunerType, "enableEntropy");
		_fiEnableWeapon = AccessTools.DeclaredField(_tunerType, "enableWeapon");
		_fiEnableShield = AccessTools.DeclaredField(_tunerType, "enableShield");
		TryRegField(_tunerType, "regenThreshold");
		TryRegField(_tunerType, "entropyThreshold");
		TryRegField(_tunerType, "weaponThreshold");
		TryRegField(_tunerType, "shieldThreshold");
		TryRegField(_tunerType, "enableRegen");
		TryRegField(_tunerType, "enableEntropy");
		TryRegField(_tunerType, "enableWeapon");
		TryRegField(_tunerType, "enableShield");
		TryReg(typeof(MiliraExpandedXY_Compat).GetMethod("SyncTunerValues", BindingFlags.Static | BindingFlags.Public));
		_gizmoPanelType = Resolve("MiliraExpandedXY.CompPawnPsionicGizmo+Gizmo_Panel");
		if (_gizmoPanelType != null)
		{
			MethodInfo methodInfo4 = AccessTools.DeclaredMethod(_gizmoPanelType, "DrawCfg", (Type[])null, (Type[])null);
			if (methodInfo4 != null)
			{
				harmony.Patch((MethodBase)methodInfo4, (HarmonyMethod)null, new HarmonyMethod(typeof(MiliraExpandedXY_Compat), "DrawCfgPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
			Type type4 = Resolve("MiliraExpandedXY.CompPawnPsionicGizmo+Gizmo_Panel+SliderWin");
			if (type4 != null)
			{
				MethodInfo methodInfo5 = AccessTools.DeclaredMethod(type4, "Close", new Type[1] { typeof(bool) }, (Type[])null);
				if (methodInfo5 != null)
				{
					harmony.Patch((MethodBase)methodInfo5, (HarmonyMethod)null, new HarmonyMethod(typeof(MiliraExpandedXY_Compat), "SliderWinClosePostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
		}
		_shieldCoreType = Resolve("MiliraExpandedXY.Apparels.ShieldCore_Main_CompShield");
		if (_shieldCoreType != null)
		{
			TryRegField(_shieldCoreType, "boostDuration");
			TryRegField(_shieldCoreType, "boostCooldown");
			TryRegField(_shieldCoreType, "neuroLinked");
			TryReg(typeof(MiliraExpandedXY_Compat).GetMethod("SyncShieldBoost", BindingFlags.Static | BindingFlags.Public));
		}
		if (_gizmoPanelType != null)
		{
			MethodInfo methodInfo6 = AccessTools.DeclaredMethod(_gizmoPanelType, "DrawShieldBoostMode", (Type[])null, (Type[])null);
			if (methodInfo6 != null)
			{
				harmony.Patch((MethodBase)methodInfo6, (HarmonyMethod)null, new HarmonyMethod(typeof(MiliraExpandedXY_Compat), "DrawShieldBoostModePostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	public static void SyncTunerValues(ThingComp tuner, float regen, float entropy, float weapon, float shield, bool enRegen, bool enEntropy, bool enWeapon, bool enShield)
	{
		if (tuner != null)
		{
			_fiRegenThreshold?.SetValue(tuner, regen);
			_fiEntropyThreshold?.SetValue(tuner, entropy);
			_fiWeaponThreshold?.SetValue(tuner, weapon);
			_fiShieldThreshold?.SetValue(tuner, shield);
			_fiEnableRegen?.SetValue(tuner, enRegen);
			_fiEnableEntropy?.SetValue(tuner, enEntropy);
			_fiEnableWeapon?.SetValue(tuner, enWeapon);
			_fiEnableShield?.SetValue(tuner, enShield);
		}
	}

	private static string ReadTunerSnapshot(ThingComp tuner, out float regen, out float entropy, out float weapon, out float shield, out bool enRegen, out bool enEntropy, out bool enWeapon, out bool enShield)
	{
		regen = (float)_fiRegenThreshold.GetValue(tuner);
		entropy = (float)_fiEntropyThreshold.GetValue(tuner);
		weapon = (float)_fiWeaponThreshold.GetValue(tuner);
		shield = (float)_fiShieldThreshold.GetValue(tuner);
		enRegen = (bool)_fiEnableRegen.GetValue(tuner);
		enEntropy = (bool)_fiEnableEntropy.GetValue(tuner);
		enWeapon = (bool)_fiEnableWeapon.GetValue(tuner);
		enShield = (bool)_fiEnableShield.GetValue(tuner);
		return $"{regen:F3}|{entropy:F3}|{weapon:F3}|{shield:F3}|{enRegen}|{enEntropy}|{enWeapon}|{enShield}";
	}

	public static void DrawCfgPostfix(object __instance)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || __instance == null)
		{
			return;
		}
		object obj = (_gizmoPanelType?.GetField("tuner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(__instance);
		ThingComp val = (ThingComp)((obj is ThingComp) ? obj : null);
		if (val != null && !(((object)val).GetType() != _tunerType))
		{
			float regen;
			float entropy;
			float weapon;
			float shield;
			bool enRegen;
			bool enEntropy;
			bool enWeapon;
			bool enShield;
			string text = ReadTunerSnapshot(val, out regen, out entropy, out weapon, out shield, out enRegen, out enEntropy, out enWeapon, out enShield);
			TrackedString orCreateValue = _tunerSnapshots.GetOrCreateValue(val);
			if (!(orCreateValue.Value == text))
			{
				orCreateValue.Value = text;
				SyncTunerValues(val, regen, entropy, weapon, shield, enRegen, enEntropy, enWeapon, enShield);
			}
		}
	}

	public static void SliderWinClosePostfix(object __instance, bool doCloseSound)
	{
		if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand && __instance != null)
		{
			object obj = (_gizmoPanelType?.GetField("tuner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(__instance);
			ThingComp val = (ThingComp)((obj is ThingComp) ? obj : null);
			if (val != null && !(((object)val).GetType() != _tunerType))
			{
				float regen;
				float entropy;
				float weapon;
				float shield;
				bool enRegen;
				bool enEntropy;
				bool enWeapon;
				bool enShield;
				string value = ReadTunerSnapshot(val, out regen, out entropy, out weapon, out shield, out enRegen, out enEntropy, out enWeapon, out enShield);
				_tunerSnapshots.GetOrCreateValue(val).Value = value;
				SyncTunerValues(val, regen, entropy, weapon, shield, enRegen, enEntropy, enWeapon, enShield);
			}
		}
	}

	public static void SyncShieldBoost(ThingComp shieldComp, float boostDuration, float boostCooldown, bool neuroLinked, string hediffDefName)
	{
		if (shieldComp == null || _shieldCoreType == null)
		{
			return;
		}
		try
		{
			FieldInfo fieldInfo = AccessTools.Field(_shieldCoreType, "boostDuration");
			FieldInfo fieldInfo2 = AccessTools.Field(_shieldCoreType, "boostCooldown");
			FieldInfo fieldInfo3 = AccessTools.Field(_shieldCoreType, "neuroLinked");
			fieldInfo?.SetValue(shieldComp, boostDuration);
			fieldInfo2?.SetValue(shieldComp, boostCooldown);
			fieldInfo3?.SetValue(shieldComp, neuroLinked);
			MethodInfo methodInfo = AccessTools.Method(_shieldCoreType, "RefillToFull", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				methodInfo.Invoke(shieldComp, null);
			}
			else
			{
				FieldInfo fieldInfo4 = AccessTools.Field(typeof(CompShield), "energy");
				if (fieldInfo4 != null)
				{
					float num = (float)AccessTools.Property(_shieldCoreType, "EnergyMax")?.GetValue(shieldComp);
					fieldInfo4.SetValue(shieldComp, num);
				}
			}
			if (!neuroLinked || hediffDefName == null)
			{
				return;
			}
			HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
			if (namedSilentFail != null)
			{
				ThingWithComps parent = shieldComp.parent;
				IThingHolder obj = ((parent != null) ? ((Thing)parent).ParentHolder : null);
				Pawn val = (obj as Pawn_ApparelTracker)?.pawn;
				if (val != null)
				{
					val.health.AddHediff(namedSilentFail, (BodyPartRecord)null, (DamageInfo?)null, (DamageWorker.DamageResult)null);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[MiliraExpandedXY_Compat] SyncShieldBoost failed: " + ex.Message);
		}
	}

	public static void DrawShieldBoostModePostfix(object __instance)
	{
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Expected O, but got Unknown
		if (!MP.IsInMultiplayer || __instance == null || MP.IsExecutingSyncCommand || _shieldCoreType == null)
		{
			return;
		}
		try
		{
			if (!(AccessTools.Field(typeof(WindowStack), "windows")?.GetValue(Find.WindowStack) is IList { Count: not 0 } list))
			{
				return;
			}
			object obj = list[list.Count - 1];
			FloatMenu val = (FloatMenu)((obj is FloatMenu) ? obj : null);
			if (val == null)
			{
				return;
			}
			object obj2 = (_gizmoPanelType?.GetField("shield", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(__instance);
			ThingComp val2 = (ThingComp)((obj2 is ThingComp) ? obj2 : null);
			if (val2 == null || !_shieldCoreType.IsInstanceOfType(val2) || !(AccessTools.Field(typeof(FloatMenu), "options")?.GetValue(val) is IList list2))
			{
				return;
			}
			foreach (FloatMenuOption item in list2)
			{
				FloatMenuOption val3 = item;
				Action orig = val3.action;
				if (orig == null)
				{
					continue;
				}
				ThingComp captured = val2;
				val3.action = delegate
				{
					orig();
					if (MP.IsInMultiplayer)
					{
						float boostDuration = (float)AccessTools.Field(_shieldCoreType, "boostDuration")?.GetValue(captured);
						float boostCooldown = (float)AccessTools.Field(_shieldCoreType, "boostCooldown")?.GetValue(captured);
						bool neuroLinked = (bool)AccessTools.Field(_shieldCoreType, "neuroLinked")?.GetValue(captured);
						string hediffDefName = null;
						CompProperties props = captured.props;
						if (props != null)
						{
							FieldInfo fieldInfo = AccessTools.Field(((object)props).GetType(), "shieldBoostHediff");
							if (fieldInfo != null)
							{
								object value = fieldInfo.GetValue(props);
								HediffDef val4 = (HediffDef)((value is HediffDef) ? value : null);
								if (val4 != null)
								{
									hediffDefName = ((Def)val4).defName;
								}
							}
						}
						SyncShieldBoost(captured, boostDuration, boostCooldown, neuroLinked, hediffDefName);
					}
				};
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[MiliraExpandedXY_Compat] DrawShieldBoostModePostfix error: " + ex.Message);
		}
	}

	public static void SyncDoJump(int pawnThingID, IntVec3 currentTargetCell, IntVec3 abilityTargetCell, string flyerDefName, string abilityDefName)
	{
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsExecutingSyncCommand || _mDoJump == null)
		{
			return;
		}
		Pawn val = null;
		foreach (Map map in Find.Maps)
		{
			foreach (Pawn allPawn in map.mapPawns.AllPawns)
			{
				if (((Thing)allPawn).thingIDNumber == pawnThingID)
				{
					val = allPawn;
					break;
				}
			}
			if (val != null)
			{
				break;
			}
		}
		if (val == null)
		{
			return;
		}
		try
		{
			AbilityDef namedSilentFail = DefDatabase<AbilityDef>.GetNamedSilentFail(abilityDefName);
			Pawn_AbilityTracker abilities = val.abilities;
			Ability val2 = ((abilities != null) ? abilities.GetAbility(namedSilentFail, false) : null);
			if (val2 != null)
			{
				ThingDef val3 = ((flyerDefName != null) ? DefDatabase<ThingDef>.GetNamedSilentFail(flyerDefName) : null);
				VerbProperties val4 = val2.verb?.verbProps;
				_mDoJump.Invoke(null, new object[6]
				{
					val,
					(object)new LocalTargetInfo(currentTargetCell),
					val4,
					val2,
					(object)new LocalTargetInfo(abilityTargetCell),
					val3
				});
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[MiliraExpandedXY_Compat] SyncDoJump failed: " + ex.Message);
		}
	}

	public static void JumpTryCastShotPostfix(object __instance, bool __result)
	{
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !__result || __instance == null || _jumpVerbType == null || !_jumpVerbType.IsInstanceOfType(__instance))
		{
			return;
		}
		try
		{
			object obj = AccessTools.PropertyGetter(typeof(Verb), "CasterPawn")?.Invoke(__instance, null);
			Pawn val = (Pawn)((obj is Pawn) ? obj : null);
			if (val != null)
			{
				object obj2 = AccessTools.Field(typeof(Verb_CastAbility), "ability")?.GetValue(__instance);
				string text = (obj2 as Ability)?.def?.defName;
				LocalTargetInfo val2 = (LocalTargetInfo)(AccessTools.Field(typeof(Verb), "currentTarget")?.GetValue(__instance) ?? ((object)default(LocalTargetInfo)));
				LocalTargetInfo val3 = (LocalTargetInfo)(AccessTools.PropertyGetter(typeof(Verb), "CurrentTarget")?.Invoke(__instance, null) ?? ((object)default(LocalTargetInfo)));
				object obj3 = AccessTools.PropertyGetter(_jumpVerbType, "JumpFlyerDef")?.Invoke(__instance, null);
				ThingDef val4 = (ThingDef)((obj3 is ThingDef) ? obj3 : null);
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[MiliraExpandedXY_Compat] JumpTryCastShotPostfix error: " + ex.Message);
		}
	}

	public static void BladeJumpTryCastShotPostfix(object __instance, bool __result)
	{
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !__result || __instance == null || _bladeJumpVerbType == null || !_bladeJumpVerbType.IsInstanceOfType(__instance))
		{
			return;
		}
		try
		{
			object obj = AccessTools.PropertyGetter(typeof(Verb), "CasterPawn")?.Invoke(__instance, null);
			Pawn val = (Pawn)((obj is Pawn) ? obj : null);
			if (val != null)
			{
				object obj2 = AccessTools.Field(typeof(Verb_CastAbility), "ability")?.GetValue(__instance);
				string text = (obj2 as Ability)?.def?.defName;
				LocalTargetInfo val2 = (LocalTargetInfo)(AccessTools.Field(typeof(Verb), "currentTarget")?.GetValue(__instance) ?? ((object)default(LocalTargetInfo)));
				LocalTargetInfo val3 = (LocalTargetInfo)(AccessTools.PropertyGetter(typeof(Verb), "CurrentTarget")?.Invoke(__instance, null) ?? ((object)default(LocalTargetInfo)));
				object obj3 = _mGetFlyerDef?.Invoke(__instance, null);
				ThingDef val4 = (ThingDef)((obj3 is ThingDef) ? obj3 : null);
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[MiliraExpandedXY_Compat] BladeJumpTryCastShotPostfix error: " + ex.Message);
		}
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(fullName, "MiliraExpandedXY_Compat");
	}

	private static void TryReg(MethodInfo m)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void TryReg(Type type, string method)
	{
		TryReg(AccessTools.DeclaredMethod(type, method, (Type[])null, (Type[])null));
	}

	private static void TryReg(Type type, string method, Type[] paramTypes)
	{
		TryReg(AccessTools.DeclaredMethod(type, method, paramTypes, (Type[])null));
	}

	private static void TryRegField(Type type, string field)
	{
		if (AccessTools.DeclaredField(type, field) == null)
		{
			return;
		}
		try
		{
			MP.RegisterSyncField(type, field);
		}
		catch
		{
		}
	}

	public static void SyncSolringenMode(ThingComp comp, bool combatMode)
	{
		if (comp != null && !(_solringenType == null))
		{
			AccessTools.DeclaredField(_solringenType, "CombatMode")?.SetValue(comp, combatMode);
		}
	}

	public static void WrapSolringenGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (!MP.IsInMultiplayer || __instance == null || __result == null)
		{
			return;
		}
		List<Gizmo> list = __result.ToList();
		for (int i = 0; i < list.Count; i++)
		{
			Gizmo val = list[i];
			Command_Action val2 = (Command_Action)(object)((val is Command_Action) ? val : null);
			if (val2 == null)
			{
				continue;
			}
			Action orig = val2.action;
			val2.action = delegate
			{
				orig?.Invoke();
				if (!MP.IsExecutingSyncCommand)
				{
					SyncSolringenMode(__instance, (bool)_fiCombatMode.GetValue(__instance));
				}
			};
		}
		__result = list;
	}
}
