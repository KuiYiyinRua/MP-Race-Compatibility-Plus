using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Live-syncs the simulation-affecting fields of MiliraImperium's
/// <c>Settings_MI</c>. The mod settings window writes fields directly from GUI
/// controls, so this patch diffs the tracked fields on every window draw and
/// dispatches one bounded sync command per changed field. Dictionary-based
/// config tables are left to the join-time config hot sync.
/// </summary>
[StaticConstructorOnStartup]
public static class Patch_MiliraSettingsMp
{
	private const string HarmonyId = "mp.meowonlineshop.miliraimperium.settings";
	private const int DispatchIntervalTicks = 10;

	private static readonly Harmony harmony;
	private static Type settingsMainType;
	private static Type settingsType;
	private static PropertyInfo instanceProperty;
	private static FieldInfo settingsField;
	private static readonly Dictionary<string, FieldInfo> TrackedFields = new Dictionary<string, FieldInfo>();
	private static readonly Dictionary<string, object> LastDispatched = new Dictionary<string, object>();
	private static int nextDispatchTick;
	private static bool initialized;
	private static bool syncRegistered;

	private static readonly string[] TrackedFieldNames =
	{
		"Weapon_Patch2_MI",
		"AI_EntityReinforcements_CanUse",
		"AI_VoidWarshipSupport_CanUse",
		"Weapon_Patch4_MI",
		"Weapon_Patch5_MI",
		"Weapon_Patch6_MI",
		"Weapon_Patch9_MI",
		"Weapon_Patch11_MI",
		"Weapon_Patch_Civilian_Damage_MI",
		"Shield_CapacityFactor_MI",
		"Turret_Damage_Factor_MI",
		"Turret_Health_Factor_MI",
		"Weapon_Cost_Factor_MI",
		"Damage_Penetration_Factor_MI",
		"Permit_CooldownFactor_MI",
		"EnablePlanetkillerIntercept",
		"UseSunBlastFX_MI",
		"EnableUniqueRecruits",
		"PirateEquipmentUnlocked_MI",
		"EnableOrbitalStrikeDelayedSound",
		"EnableTradeShipShadow",
		"EnableIntroLetters",
		"EnableDoomStage4_MI",
		"EnableDoomStage2And3_MI",
		"EnableFirewalls",
		"MI_ModSetting_MiliraPirateDifficulty",
		"EnableDisruptorRandomizedDamage",
		"PsiShieldBanKillEnabled",
		"DialogModSettingGuideEnabled"
	};

	static Patch_MiliraSettingsMp()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira settings sync skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		harmony = new Harmony(HarmonyId);
		if (!MP.enabled || !ModsConfig.IsActive("Ariandel.MiliraImperium"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Warning("[MP-MeowOnlineShop] Milira settings sync init failed - " + arg);
		}
	}

	private static void Initialize()
	{
		settingsMainType = AccessTools.TypeByName("MiliraImperium.Settings_MI_Main");
		settingsType = AccessTools.TypeByName("MiliraImperium.Settings_MI");
		if (settingsMainType == null || settingsType == null)
		{
			return;
		}
		instanceProperty = AccessTools.Property(settingsMainType, "Instance");
		settingsField = AccessTools.Field(settingsMainType, "settings_MI");
		if (instanceProperty == null || settingsField == null)
		{
			return;
		}
		foreach (string fieldName in TrackedFieldNames)
		{
			FieldInfo field = AccessTools.Field(settingsType, fieldName);
			if (field != null)
			{
				TrackedFields[fieldName] = field;
			}
		}
		if (TrackedFields.Count == 0)
		{
			return;
		}

		try
		{
			MP.RegisterSyncMethod(typeof(Patch_MiliraSettingsMp), nameof(SyncSettingsField));
			syncRegistered = true;
		}
		catch (Exception e)
		{
			Log.Warning("[MP-MeowOnlineShop] Milira settings sync method registration failed: " + e.Message);
			return;
		}

		MethodInfo target = AccessTools.Method(settingsMainType, "DoSettingsWindowContents", new Type[1] { typeof(Rect) }, (Type[])null);
		MethodInfo prefix = AccessTools.Method(typeof(Patch_MiliraSettingsMp), "SettingsWindowPrefix");
		MethodInfo postfix = AccessTools.Method(typeof(Patch_MiliraSettingsMp), "SettingsWindowPostfix");
		if (target == null || prefix == null || postfix == null)
		{
			Log.Warning("[MP-MeowOnlineShop] Milira settings window patch target resolution failed.");
			return;
		}
		harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
		Log.Message("[MP-MeowOnlineShop] Milira settings live sync active: fields=" + TrackedFields.Count + ".");
	}

	private static object GetSettingsObject()
	{
		try
		{
			object main = instanceProperty?.GetValue(null);
			return main == null ? null : settingsField?.GetValue(main);
		}
		catch
		{
			return null;
		}
	}

	public static void SettingsWindowPrefix()
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || initialized)
		{
			return;
		}
		object settings = GetSettingsObject();
		if (settings == null)
		{
			return;
		}
		foreach (KeyValuePair<string, FieldInfo> pair in TrackedFields)
		{
			try
			{
				LastDispatched[pair.Key] = pair.Value.GetValue(settings);
			}
			catch
			{
			}
		}
		initialized = true;
	}

	public static void SettingsWindowPostfix()
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !initialized || !syncRegistered)
		{
			return;
		}
		object settings = GetSettingsObject();
		if (settings == null)
		{
			return;
		}
		bool canDispatch = Find.TickManager?.TicksGame >= nextDispatchTick;
		if (!canDispatch)
		{
			return;
		}
		foreach (KeyValuePair<string, FieldInfo> pair in TrackedFields)
		{
			object current;
			try
			{
				current = pair.Value.GetValue(settings);
			}
			catch
			{
				continue;
			}
			if (!LastDispatched.TryGetValue(pair.Key, out object previous) || Equals(previous, current))
			{
				continue;
			}
			DispatchField(pair.Key, pair.Value, current);
			LastDispatched[pair.Key] = current;
			nextDispatchTick = (Find.TickManager?.TicksGame ?? 0) + DispatchIntervalTicks;
		}
	}

	private static void DispatchField(string fieldName, FieldInfo field, object value)
	{
		try
		{
			if (field.FieldType == typeof(bool))
			{
				SyncSettingsField(fieldName, (bool)value, 0, 0f);
			}
			else if (field.FieldType == typeof(float))
			{
				SyncSettingsField(fieldName, false, 0, (float)value);
			}
			else if (field.FieldType.IsEnum)
			{
				SyncSettingsField(fieldName, false, (int)value, 0f);
			}
		}
		catch
		{
		}
	}

	public static void SyncSettingsField(string fieldName, bool boolValue, int intValue, float floatValue)
	{
		if (string.IsNullOrEmpty(fieldName) || !TrackedFields.TryGetValue(fieldName, out FieldInfo field))
		{
			return;
		}
		object settings = GetSettingsObject();
		if (settings == null)
		{
			return;
		}
		try
		{
			if (field.FieldType == typeof(bool))
			{
				field.SetValue(settings, boolValue);
			}
			else if (field.FieldType == typeof(float))
			{
				field.SetValue(settings, floatValue);
			}
			else if (field.FieldType.IsEnum)
			{
				field.SetValue(settings, Enum.ToObject(field.FieldType, intValue));
			}
		}
		catch
		{
		}
	}
}
