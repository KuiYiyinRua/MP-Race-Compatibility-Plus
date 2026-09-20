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
public static class MilianModification_Compat
{
	private const string NS = "MilianModification.";

	private static readonly Harmony harmony;

	private static readonly BindingFlags AllStat;

	private static Type _compModType;

	private static Type _utilType;

	private static Type _slotType;

	private static Type _componentDefType;

	private static Type _gcType;

	private static Type _itabType;

	private static Type _tPawnColumn_AutoReplenish;

	private static Type _tPawnColumn_AutoUseModifyAbility;

	private static Type _tPawnColumn_AutoUseModifyAbilityConsumptive;

	private static Type _tPawnColumnComponent;

	private static FieldInfo _fiInstalled;

	private static FieldInfo _fiInRecipe;

	private static FieldInfo _fiUninstallRecipe;

	private static FieldInfo _fiAutoReplenish;

	private static FieldInfo _fiTmpResources;

	private static FieldInfo _fiAutoUseModifyAbility;

	private static FieldInfo _fiAutoUseModifyAbilityConsumptive;

	private static MethodInfo _miAddToInstall;

	private static MethodInfo _miAddToUninstall;

	private static MethodInfo _miFindSlots;

	private static MethodInfo _miRemoveSlots;

	private static int _cancelInstallBeforeCount;

	[ThreadStatic]
	private static Dictionary<int, Dictionary<int, int>> _recipeSnapshots;

	static MilianModification_Compat()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("Ancot.MilianModification.Compat");
		AllStat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		if (!MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification"))
		{
			return;
		}
		try
		{
			LongEventHandler.ExecuteWhenFinished((Action)DoPatch);
		}
		catch (Exception arg)
		{
			Log.Error($"MilianModification_Compat: init failed - {arg}");
		}
	}

	private static void DoPatch()
	{
		//IL_036d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0382: Unknown result type (might be due to invalid IL or missing references)
		//IL_038e: Expected O, but got Unknown
		//IL_038e: Expected O, but got Unknown
		//IL_03e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ef: Expected O, but got Unknown
		//IL_04d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f4: Expected O, but got Unknown
		//IL_04f4: Expected O, but got Unknown
		//IL_0680: Unknown result type (might be due to invalid IL or missing references)
		//IL_068d: Expected O, but got Unknown
		//IL_0559: Unknown result type (might be due to invalid IL or missing references)
		//IL_0565: Expected O, but got Unknown
		try
		{
			_compModType = Resolve("MilianModification.CompModification");
			_utilType = Resolve("MilianModification.MilianModificationUtility");
			_slotType = Resolve("MilianModification.ComponentSlot");
			_componentDefType = Resolve("MilianModification.MilianComponentDef");
			_gcType = Resolve("MilianModification.MiliraGameComponent_MilianComponentRecipe");
			_itabType = Resolve("MilianModification.ITab_Pawn_MilianModification");
			if (_compModType == null || _utilType == null || _slotType == null || _componentDefType == null || _gcType == null)
			{
				Log.Message("[MilianCompat] TYPES NULL");
				return;
			}
			_fiInstalled = AccessTools.DeclaredField(_compModType, "componentSlotInstalled");
			_fiInRecipe = AccessTools.DeclaredField(_compModType, "componentSlotInRecipe");
			_fiUninstallRecipe = AccessTools.DeclaredField(_compModType, "slotUninstallRecipe");
			_fiAutoReplenish = AccessTools.DeclaredField(_compModType, "autoReplenish");
			_fiTmpResources = AccessTools.DeclaredField(_gcType, "tmpResources");
			_miAddToInstall = AccessTools.Method(_utilType, "AddSlotToInstallRecipe", new Type[3]
			{
				typeof(Pawn),
				_slotType,
				_componentDefType
			}, (Type[])null);
			_miAddToUninstall = AccessTools.Method(_utilType, "AddSlotToUninstallRecipe", new Type[2]
			{
				typeof(Pawn),
				_slotType
			}, (Type[])null);
			RegField(_fiInstalled);
			RegField(_fiInRecipe);
			RegField(_fiUninstallRecipe);
			RegField(_fiAutoReplenish);
			_fiAutoUseModifyAbility = AccessTools.DeclaredField(_compModType, "autoUseModifyAbility");
			_fiAutoUseModifyAbilityConsumptive = AccessTools.DeclaredField(_compModType, "autoUseModifyAbility_Consumptive");
			RegField(_fiAutoUseModifyAbility);
			RegField(_fiAutoUseModifyAbilityConsumptive);
			TryReg(typeof(MilianModification_Compat).GetMethod("SyncedAddToInstallRecipe", AllStat));
			TryReg(typeof(MilianModification_Compat).GetMethod("SyncedAddToUninstallRecipe", AllStat));
			TryReg(typeof(MilianModification_Compat).GetMethod("SyncedGenerateRecipeDef", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(string) }, null));
			if (_miAddToInstall != null)
			{
				TryReg(_miAddToInstall);
			}
			if (_miAddToUninstall != null)
			{
				TryReg(_miAddToUninstall);
			}
			TryReg(typeof(MilianModification_Compat).GetMethod("SyncBoolField", BindingFlags.Static | BindingFlags.Public, null, new Type[3]
			{
				typeof(ThingComp),
				typeof(string),
				typeof(bool)
			}, null));
			int num = 0;
			MethodInfo target = AccessTools.Method(_gcType, "GenerateRecipeDef", new Type[1] { typeof(RecipeDef).MakeByRefType() }, (Type[])null);
			if (PatchPrefix(target, "Prefix_GenerateRecipeDef"))
			{
				num++;
			}
			MethodInfo methodInfo = AccessTools.Method(_utilType, "InstallComponent", new Type[2]
			{
				typeof(Pawn),
				_componentDefType
			}, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopDetRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
				harmony.Patch((MethodBase)methodInfo, postfix: new HarmonyMethod(typeof(MilianModification_Compat), "Postfix_InstallComponentAbilityCache"));
				num++;
				Log.Message("[MilianCompat] InstallComponent now invalidates Pawn ability cache on every peer.");
			}
			MethodInfo methodInfo2 = AccessTools.Method(_compModType, "Notify_InstallPostFinished", new Type[2]
			{
				typeof(List<>).MakeGenericType(_slotType),
				_componentDefType
			}, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(MilianModification_Compat), "Prefix_InstallPostFinished", (Type[])null), new HarmonyMethod(typeof(MilianModification_Compat), "Postfix_InstallPostFinished", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
				Log.Message("[MilianCompat] Notify_InstallPostFinished player-faction normalization patch OK");
			}
			Type milianComponentHediffCompType = Resolve("MilianModification.HediffComp_MilianComponent");
			MethodInfo milianComponentRemoved = milianComponentHediffCompType == null
				? null
				: AccessTools.DeclaredMethod(milianComponentHediffCompType, "CompPostPostRemoved", Type.EmptyTypes);
			if (milianComponentRemoved != null)
			{
				harmony.Patch(
					(MethodBase)milianComponentRemoved,
					postfix: new HarmonyMethod(
						typeof(MilianModification_Compat),
						"Postfix_MilianComponentRemovedAbilityCache"));
				Log.Message("[MilianCompat] HediffComp_MilianComponent.CompPostPostRemoved invalidates Pawn ability cache.");
			}
			_miFindSlots = AccessTools.Method(_utilType, "FindSlotsInstalledWithCompoentDef", (Type[])null, (Type[])null);
			_miRemoveSlots = AccessTools.Method(_utilType, "RemoveSlotsInDictionary", (Type[])null, (Type[])null);
			TryReg(typeof(MilianModification_Compat).GetMethod("SyncedCancelInstall", AllStat));
			TryReg(typeof(MilianModification_Compat).GetMethod("SyncedCancelUninstall", AllStat));
			if (_itabType != null)
			{
				MethodInfo methodInfo3 = AccessTools.DeclaredMethod(_itabType, "ButtonCancelInstall", new Type[2]
				{
					typeof(Rect),
					_slotType
				}, (Type[])null);
				if (methodInfo3 != null)
				{
					harmony.Patch((MethodBase)methodInfo3, new HarmonyMethod(typeof(MilianModification_Compat), "Prefix_CancelInstall", (Type[])null), new HarmonyMethod(typeof(MilianModification_Compat), "Postfix_CancelInstall", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Message("[MilianCompat] ButtonCancelInstall patch OK");
				}
				MethodInfo methodInfo4 = AccessTools.DeclaredMethod(_itabType, "ButtonUninstall", new Type[2]
				{
					typeof(Rect),
					_slotType
				}, (Type[])null);
				if (methodInfo4 != null)
				{
					harmony.Patch((MethodBase)methodInfo4, (HarmonyMethod)null, new HarmonyMethod(typeof(MilianModification_Compat), "Postfix_ButtonUninstall", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Message("[MilianCompat] ButtonUninstall patch OK");
				}
			}
			Log.Message($"[MilianCompat] patched {num}/3 methods");
			_tPawnColumn_AutoReplenish = Resolve("MilianModification.PawnColumnWorker_AutoReplenishMilian");
			_tPawnColumn_AutoUseModifyAbility = Resolve("MilianModification.PawnColumnWorker_AutoUseModifyAbility");
			_tPawnColumn_AutoUseModifyAbilityConsumptive = Resolve("MilianModification.PawnColumnWorker_AutoUseModifyAbilityConsumptive");
			_tPawnColumnComponent = Resolve("MilianModification.PawnColumnWorker_MilianComponent");
			num += PatchSetValue(_tPawnColumn_AutoReplenish, "Postfix_AutoReplenish_SetValue");
			num += PatchSetValue(_tPawnColumn_AutoUseModifyAbility, "Postfix_AutoUseModifyAbility_SetValue");
			num += PatchSetValue(_tPawnColumn_AutoUseModifyAbilityConsumptive, "Postfix_AutoUseConsumptive_SetValue");
			TryReg(typeof(MilianModification_Compat).GetMethod("SyncedCancelInstall_Column", AllStat));
			if (_tPawnColumnComponent != null)
			{
				MethodInfo methodInfo5 = AccessTools.Method(typeof(WindowStack), "Add", new Type[1] { typeof(Window) }, (Type[])null);
				if (methodInfo5 != null)
				{
					harmony.Patch((MethodBase)methodInfo5, new HarmonyMethod(typeof(MilianModification_Compat), "Prefix_WindowStackAdd_FloatMenu", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Message("[MilianCompat] WindowStack.Add FloatMenu patch OK");
				}
			}
		}
		catch (Exception arg)
		{
			Log.Error($"MilianModification_Compat: DoPatch FAILED - {arg}");
		}
	}

	public static void Prefix_CancelInstall(object __instance, object slot)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !MP.InInterface)
		{
			return;
		}
		try
		{
			Pawn value = Traverse.Create(__instance).Property("SelPawn", (object[])null).GetValue<Pawn>();
			if (value == null)
			{
				_cancelInstallBeforeCount = -1;
				return;
			}
			object value2 = Traverse.Create(__instance).Property("Comp", (object[])null).GetValue();
			if (value2 == null)
			{
				_cancelInstallBeforeCount = -1;
			}
			else
			{
				_cancelInstallBeforeCount = ((_fiInRecipe.GetValue(value2) is IDictionary dictionary) ? dictionary.Count : (-1));
			}
		}
		catch
		{
			_cancelInstallBeforeCount = -1;
		}
	}

	public static void Prefix_InstallPostFinished(object __instance, ref bool __state)
	{
		__state = false;
		if (!MP.IsInMultiplayer || __instance == null)
		{
			return;
		}
		try
		{
			Pawn pawn = GetPawnFromModification(__instance);
			if (pawn != null && pawn.Faction != null && pawn.Faction.def != null && pawn.Faction.def.isPlayer && !pawn.IsPlayerControlled)
			{
				// MilianModification gates the cooldown reset on Faction.OfPlayer-relative
			// IsPlayerControlled. That value is not stable when multifaction is active.
				// The faction definition is save data and is the same on every peer.
				__state = true;
			}
		}
		catch
		{
			__state = false;
		}
	}

	public static void Postfix_InstallPostFinished(object __instance, bool __state)
	{
		if (!__state)
		{
			return;
		}
		try
		{
			Pawn pawn = GetPawnFromModification(__instance);
			if (pawn == null)
			{
				return;
			}
			if (pawn.abilities != null)
			{
				foreach (Ability ability in pawn.abilities.AllAbilitiesForReading)
				{
					if (ability.UsesCharges)
					{
						ability.RemainingCharges = 0;
					}
					ability.StartCooldown(ability.def.cooldownTicksRange.RandomInRange);
				}
			}
			StartConsulCarrierCooldown(pawn);
		}
		catch (Exception ex)
		{
			Log.Warning("[MilianCompat] InstallPostFinished player-faction normalization skipped: " + ex.Message);
		}
	}

	private static Pawn GetPawnFromModification(object instance)
	{
		ThingComp comp = instance as ThingComp;
		return comp?.parent as Pawn;
	}

	private static void StartConsulCarrierCooldown(Pawn pawn)
	{
		Type carrierType = AccessTools.TypeByName("Milira.CompMechCarrier_Consul");
		if (carrierType == null || pawn == null || !(pawn is ThingWithComps thingWithComps))
		{
			return;
		}
		foreach (ThingComp comp in thingWithComps.AllComps)
		{
			if (comp == null || !carrierType.IsInstanceOfType(comp))
			{
				continue;
			}
			PropertyInfo recoverTicks = AccessTools.Property(comp.GetType(), "RecoverTicks");
			MethodInfo startCooldown = AccessTools.Method(comp.GetType(), "StartCooldown", new Type[1] { typeof(int) });
			if (recoverTicks != null && startCooldown != null)
			{
				object value = recoverTicks.GetValue(comp, null);
				if (value is int ticks)
				{
					startCooldown.Invoke(comp, new object[1] { ticks });
				}
			}
			return;
		}
	}

	public static void Postfix_CancelInstall(object __instance, object slot)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			Pawn value = Traverse.Create(__instance).Property("SelPawn", (object[])null).GetValue<Pawn>();
			if (value == null)
			{
				return;
			}
			object value2 = Traverse.Create(__instance).Property("Comp", (object[])null).GetValue();
			if (value2 != null)
			{
				int num = ((_fiInRecipe.GetValue(value2) is IDictionary dictionary) ? dictionary.Count : (-1));
				if (num != _cancelInstallBeforeCount && num >= 0 && _cancelInstallBeforeCount >= 0)
				{
					SyncedCancelInstall(value, Convert.ToInt32(slot));
				}
			}
		}
		catch
		{
		}
	}

	private static object GetComp(Pawn pawn)
	{
		if (pawn == null || _compModType == null)
		{
			return null;
		}
		foreach (ThingComp allComp in ((ThingWithComps)pawn).AllComps)
		{
			if (allComp != null && _compModType.IsInstanceOfType(allComp))
			{
				return allComp;
			}
		}
		return null;
	}

	public static void SyncedCancelInstall(Pawn pawn, int slotIndex)
	{
		if (pawn == null)
		{
			return;
		}
		try
		{
			object comp = GetComp(pawn);
			if (comp == null || !(_fiInRecipe.GetValue(comp) is IDictionary { Count: not 0 } dictionary))
			{
				return;
			}
			object key = Enum.ToObject(_slotType, slotIndex);
			if (dictionary.Contains(key))
			{
				object obj = dictionary[key];
				if (obj != null)
				{
					object obj2 = _miFindSlots?.Invoke(null, new object[2] { dictionary, obj });
					_miRemoveSlots?.Invoke(null, new object[2] { dictionary, obj2 });
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("[MilianCompat] SyncedCancelInstall: " + ex.Message);
		}
	}

	public static void SyncedCancelInstall_Column(Pawn pawn, int slotIndex)
	{
		SyncedCancelInstall(pawn, slotIndex);
	}

	public static void Prefix_WindowStackAdd_FloatMenu(ref Window window)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return;
		}
		Window obj = window;
		FloatMenu val = (FloatMenu)(object)((obj is FloatMenu) ? obj : null);
		if (val == null)
		{
			return;
		}
		try
		{
			_recipeSnapshots = new Dictionary<int, Dictionary<int, int>>();
			foreach (Map map in Find.Maps)
			{
				foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
				{
					object comp = GetComp(item);
					if (comp == null || !(_fiInRecipe.GetValue(comp) is IDictionary dictionary))
					{
						continue;
					}
					Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
					foreach (object key2 in dictionary.Keys)
					{
						dictionary2[Convert.ToInt32(key2)] = 1;
					}
					_recipeSnapshots[((Thing)item).thingIDNumber] = dictionary2;
				}
			}
			if (!(AccessTools.Field(typeof(FloatMenu), "options")?.GetValue(val) is List<FloatMenuOption> list))
			{
				return;
			}
            foreach (FloatMenuOption item2 in list)
            {
                if (item2 == null || item2.action == null || IsMedicalSurgeryOption(item2))
                {
                    continue;
                }
				Action origAction = item2.action;
				item2.action = delegate
				{
					origAction?.Invoke();
					if (!MP.IsExecutingSyncCommand && _recipeSnapshots != null)
					{
						foreach (Map map2 in Find.Maps)
						{
							foreach (Pawn item3 in map2.mapPawns.AllPawnsSpawned)
							{
								object comp2 = GetComp(item3);
								if (comp2 != null && _fiInRecipe.GetValue(comp2) is IDictionary dictionary3 && _recipeSnapshots.TryGetValue(((Thing)item3).thingIDNumber, out var value))
								{
									foreach (int key3 in value.Keys)
									{
										object key = Enum.ToObject(_slotType, key3);
										if (!dictionary3.Contains(key))
										{
											SyncedCancelInstall(item3, key3);
											break;
										}
									}
								}
							}
						}
					}
				};
			}
		}
		catch
		{
        }
    }

    private static bool IsMedicalSurgeryOption(FloatMenuOption option)
    {
        MethodInfo method = option?.action?.Method;
        if (method == null)
        {
            return false;
        }

        string declaringType = method.DeclaringType?.FullName ?? string.Empty;
        string methodName = method.Name ?? string.Empty;
        return declaringType.IndexOf("HealthCardUtility", StringComparison.Ordinal) >= 0
            || declaringType.IndexOf("MedicalCareUtility", StringComparison.Ordinal) >= 0
            || declaringType.IndexOf("MedicalRecipesUtility", StringComparison.Ordinal) >= 0
            || methodName.IndexOf("GenerateSurgeryOption", StringComparison.Ordinal) >= 0;
    }

    public static void SyncedCancelUninstall(Pawn pawn, int slotIndex)
	{
		if (pawn == null)
		{
			return;
		}
		try
		{
			object comp = GetComp(pawn);
			if (comp == null || !(_fiUninstallRecipe.GetValue(comp) is IDictionary { Count: not 0 } dictionary))
			{
				return;
			}
			object key = Enum.ToObject(_slotType, slotIndex);
			if (dictionary.Contains(key))
			{
				object obj = dictionary[key];
				if (obj != null)
				{
					object obj2 = _miFindSlots?.Invoke(null, new object[2] { dictionary, obj });
					_miRemoveSlots?.Invoke(null, new object[2] { dictionary, obj2 });
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("[MilianCompat] SyncedCancelUninstall: " + ex.Message);
		}
	}

	public static void Postfix_ButtonUninstall(object __instance, object slot)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			Pawn value = Traverse.Create(__instance).Property("SelPawn", (object[])null).GetValue<Pawn>();
			if (value == null)
			{
				return;
			}
			object comp = GetComp(value);
			if (comp != null && _fiUninstallRecipe.GetValue(comp) is IDictionary dictionary)
			{
				if (!dictionary.Contains(slot))
				{
					SyncedCancelUninstall(value, Convert.ToInt32(slot));
				}
			}
		}
		catch
		{
		}
	}

	public static bool Prefix_AddToInstall(Pawn pawn, object slot, object componentDef)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		try
		{
			int slotIndex = Convert.ToInt32(slot);
			Def val = (Def)componentDef;
			SyncedAddToInstallRecipe(pawn, slotIndex, val?.defName ?? "");
		}
		catch (Exception ex)
		{
			Log.Warning("[MilianCompat] AddToInstall sync skipped: " + ex.Message);
		}
		return false;
	}

	public static bool Prefix_AddToUninstall(Pawn pawn, object slot)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		try
		{
			SyncedAddToUninstallRecipe(pawn, Convert.ToInt32(slot));
		}
		catch (Exception ex)
		{
			Log.Warning("[MilianCompat] AddToUninstall sync skipped: " + ex.Message);
		}
		return false;
	}

	public static bool Prefix_GenerateRecipeDef(object __instance)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		try
		{
			string resourcesData = "";
			if (_fiTmpResources?.GetValue(__instance) is IDictionary dictionary)
			{
				List<string> list = new List<string>();
				foreach (object key in dictionary.Keys)
				{
					Def val = (Def)((key is Def) ? key : null);
					if (val != null && dictionary[key] is int num && num > 0)
					{
						list.Add($"{val.defName}:{num}");
					}
				}
				resourcesData = string.Join("|", list);
			}
			SyncedGenerateRecipeDef(resourcesData);
		}
		catch (Exception ex)
		{
			Log.Warning("[MilianCompat] GenerateRecipeDef sync skipped: " + ex.Message);
		}
		return true;
	}

	public static void SyncedGenerateRecipeDef(string resourcesData)
	{
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Expected O, but got Unknown
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Expected O, but got Unknown
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected O, but got Unknown
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Expected O, but got Unknown
		try
		{
			if (string.IsNullOrEmpty(resourcesData))
			{
				return;
			}
			RecipeDef named = DefDatabase<RecipeDef>.GetNamed("Milira_ResearchMilianComponent", false);
			if (named == null)
			{
				return;
			}
			named.ingredients.Clear();
			string[] array = resourcesData.Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string text in array)
			{
				string[] array2 = text.Split(':');
				if (array2.Length == 2 && int.TryParse(array2[1], out var result) && result > 0)
				{
					ThingDef named2 = DefDatabase<ThingDef>.GetNamed(array2[0], false);
					if (named2 != null)
					{
						IngredientCount val = new IngredientCount();
						val.SetBaseCount((float)result);
						val.filter = new ThingFilter();
						val.filter.SetAllow(named2, true);
						named.ingredients.Add(val);
					}
				}
			}
			ThingDef uiUnit = DefDatabase<ThingDef>.GetNamed("Milian_UniversalInterfaceUnit", false);
			if (uiUnit != null && !GenCollection.Any<IngredientCount>(named.ingredients, (Predicate<IngredientCount>)delegate(IngredientCount val3)
			{
				ThingFilter value = Traverse.Create((object)val3).Field("filter").GetValue<ThingFilter>();
				return value != null && value.Allows(uiUnit);
			}))
			{
				IngredientCount val2 = new IngredientCount();
				val2.SetBaseCount(1f);
				val2.filter = new ThingFilter();
				val2.filter.SetAllow(uiUnit, true);
				named.ingredients.Add(val2);
			}
		}
		catch (Exception ex)
		{
			Log.Error("[MilianCompat] SyncedGenerateRecipeDef: " + ex.Message);
		}
	}

	public static void SyncedAddToInstallRecipe(Pawn pawn, int slotIndex, string componentDefName)
	{
		if (pawn == null || string.IsNullOrEmpty(componentDefName))
		{
			return;
		}
		try
		{
			object obj = ResolveDef(componentDefName);
			if (obj != null)
			{
				object obj2 = Enum.ToObject(_slotType, slotIndex);
				_miAddToInstall?.Invoke(null, new object[3] { pawn, obj2, obj });
			}
		}
		catch (Exception ex)
		{
			Log.Error("[MilianCompat] SyncedAddToInstallRecipe: " + ex.Message);
		}
	}

	public static void SyncedAddToUninstallRecipe(Pawn pawn, int slotIndex)
	{
		if (pawn == null)
		{
			return;
		}
		try
		{
			object obj = Enum.ToObject(_slotType, slotIndex);
			_miAddToUninstall?.Invoke(null, new object[2] { pawn, obj });
		}
		catch (Exception ex)
		{
			Log.Error("[MilianCompat] SyncedAddToUninstallRecipe: " + ex.Message);
		}
	}

    public static void Postfix_InstallComponentAbilityCache(Pawn milian)
    {
        InvalidateAbilityCache(milian);
    }

	public static void Postfix_MilianComponentRemovedAbilityCache(HediffComp __instance)
	{
		InvalidateAbilityCache(__instance?.parent?.pawn);
	}

	private static void InvalidateAbilityCache(Pawn pawn)
	{
		if (!MP.IsInMultiplayer || pawn?.abilities == null)
		{
			return;
		}

		// MilianModification adds/removes Hediffs whose Ability lists are lazy.
		// Invalidate on every peer at the same simulation boundary so the next
		// AbilitiesTick constructs the same Ability objects and IDs.
		pawn.abilities.Notify_TemporaryAbilitiesChanged();
	}

	private static void RegField(FieldInfo fi)
	{
		if (fi != null)
		{
			try
			{
				MP.RegisterSyncField(fi);
			}
			catch
			{
			}
		}
	}

	private static void TryReg(MethodInfo m)
	{
		if (m == null)
		{
			return;
		}
		try
		{
			MP.RegisterSyncMethod(m, (SyncType[])null);
		}
		catch
		{
		}
	}

	private static bool PatchPrefix(MethodInfo target, string handler)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		if (target == null)
		{
			Log.Message("[MilianCompat] " + handler + " target=null, SKIPPED");
			return false;
		}
		harmony.Patch((MethodBase)target, new HarmonyMethod(typeof(MilianModification_Compat), handler, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	private static object ResolveDef(string defName)
	{
		Type type = typeof(DefDatabase<>).MakeGenericType(_componentDefType);
		return type.GetMethod("GetNamed", new Type[2]
		{
			typeof(string),
			typeof(bool)
		})?.Invoke(null, new object[2] { defName, false });
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveTypeSilent(fullName);
	}

	public static void SyncBoolField(ThingComp comp, string fieldName, bool value)
	{
		if (comp != null)
		{
			AccessTools.Field(((object)comp).GetType(), fieldName)?.SetValue(comp, value);
		}
	}

	private static int PatchSetValue(Type colType, string handler)
	{
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Expected O, but got Unknown
		if (colType == null)
		{
			return 0;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(colType, "SetValue", new Type[3]
		{
			typeof(Pawn),
			typeof(bool),
			typeof(PawnTable)
		}, (Type[])null);
		if (methodInfo == null)
		{
			return 0;
		}
		harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(MilianModification_Compat), handler, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		Log.Message("[MilianCompat] patched " + colType.Name + ".SetValue -> " + handler);
		return 1;
	}

	private static ThingComp GetCompByType(Pawn pawn, Type compType)
	{
		if (pawn == null || compType == null)
		{
			return null;
		}
		foreach (ThingComp allComp in ((ThingWithComps)pawn).AllComps)
		{
			if (compType.IsInstanceOfType(allComp))
			{
				return allComp;
			}
		}
		return null;
	}

	public static void Postfix_AutoReplenish_SetValue(Pawn pawn, bool value)
	{
		if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand && !(_compModType == null))
		{
			ThingComp compByType = GetCompByType(pawn, _compModType);
			if (compByType != null)
			{
				SyncBoolField(compByType, "autoReplenish", value);
			}
		}
	}

	public static void Postfix_AutoUseModifyAbility_SetValue(Pawn pawn, bool value)
	{
		if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand && !(_compModType == null))
		{
			ThingComp compByType = GetCompByType(pawn, _compModType);
			if (compByType != null)
			{
				SyncBoolField(compByType, "autoUseModifyAbility", value);
			}
		}
	}

	public static void Postfix_AutoUseConsumptive_SetValue(Pawn pawn, bool value)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _compModType == null)
		{
			return;
		}
		ThingComp compByType = GetCompByType(pawn, _compModType);
		if (compByType != null)
		{
			SyncBoolField(compByType, "autoUseModifyAbility_Consumptive", value);
			if (value && _fiAutoUseModifyAbility != null)
			{
				SyncBoolField(compByType, "autoUseModifyAbility", (bool)_fiAutoUseModifyAbility.GetValue(compByType));
			}
		}
	}
}
