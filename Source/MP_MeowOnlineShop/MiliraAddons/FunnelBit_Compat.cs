using System;
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

	static FunnelBit_Compat()
	{
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
					harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(FunnelBit_Compat), "Postfix_ToggleFireAtWill", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
			Type type = Resolve("FunnelBit.Verb_LaunchFunnelBit");
			if (type != null)
			{
				TryReg(AccessTools.DeclaredMethod(type, "TryCastShot", (Type[])null, (Type[])null));
			}
			TryRegApply("CompAbilityEffect_LaunchFunnelAbility");
			TryRegApply("CompAbilityEffect_LaunchLineAoE");
			TryRegApply("CompAbilityEffect_LaunchLinearShield");
		}
		catch (Exception arg)
		{
			Log.Error($"[FunnelBit_Compat] LatePatch failed: {arg}");
		}
	}

	public static void SyncFunnelBitToggle(ThingComp comp, string fieldName, bool value)
	{
		if (comp != null)
		{
			AccessTools.Field(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
		}
	}

	public static void Postfix_ToggleFireAtWill(object __instance, object target)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _compTurretGunType == null || !_gizmoBaseType.IsInstanceOfType(__instance))
		{
			return;
		}
		object obj = AccessTools.Field(_gizmoBaseType, "turretComp")?.GetValue(__instance);
		ThingComp val = (ThingComp)((obj is ThingComp) ? obj : null);
		if (val == null || !_compTurretGunType.IsInstanceOfType(val))
		{
			return;
		}
		string fieldNameForToggleValue = GetFieldNameForToggleValue(target);
		if (fieldNameForToggleValue != null)
		{
			FieldInfo fieldInfo = AccessTools.DeclaredField(_compTurretGunType, fieldNameForToggleValue);
			if (!(fieldInfo == null))
			{
				bool value = (bool)fieldInfo.GetValue(val);
				SyncFunnelBitToggle(val, fieldNameForToggleValue, value);
			}
		}
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
