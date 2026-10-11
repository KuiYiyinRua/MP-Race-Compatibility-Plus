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

	private static FieldInfo _fiInstalled;

	private static FieldInfo _fiInRecipe;

	private static FieldInfo _fiUninstallRecipe;

	private static FieldInfo _fiAutoReplenish;

	private static FieldInfo _fiTmpResources;

	private static FieldInfo _fiAutoUseModifyAbility;

	private static FieldInfo _fiAutoUseModifyAbilityConsumptive;

	private static MethodInfo _miAddToInstall;

	private static MethodInfo _miAddToUninstall;

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
			// Research submission is synchronized atomically by MilianModification_StateSync.
			// GenerateRecipeDef must also run natively during SpawnSetup/load on every peer.
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
			// Installation UI synchronization is owned by MilianModification_Transactions.
			Log.Message($"[MilianCompat] installed component hooks: {num}; recipe UI owned by MilianTransactions");

		}
		catch (Exception arg)
		{
			Log.Error($"MilianModification_Compat: DoPatch FAILED - {arg}");
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

}
