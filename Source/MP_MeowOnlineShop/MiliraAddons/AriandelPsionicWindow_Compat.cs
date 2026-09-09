using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class AriandelPsionicWindow_Compat
{
	private const string MiliraImperiumPackageId = "Ariandel.MiliraImperium";
	private const string AriandelLibraryPackageId = "Ariandel.AriandelLibrary";
	private const string FianchettoPackageId = "rabiosus.funnelmilian";

	private static readonly Harmony harmony;

	static AriandelPsionicWindow_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Expected O, but got Unknown
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		harmony = new Harmony("MiliraMP.AriandelPsionicWindow");
		if (!MP.enabled)
		{
			return;
		}
		if (!ModsConfig.IsActive(MiliraImperiumPackageId) &&
		    !ModsConfig.IsActive(AriandelLibraryPackageId) &&
		    !ModsConfig.IsActive(FianchettoPackageId))
		{
			Log.Message("[MiliraMP] AriandelPsionicWindow_Compat skipped: target Milira psionic-window addon not active.");
			return;
		}
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(AriandelPsionicWindow_Compat), "SyncGainAbility", (Type[])null, (Type[])null);
			MethodInfo methodInfo2 = AccessTools.Method(typeof(AriandelPsionicWindow_Compat), "SyncRemoveAbility", (Type[])null, (Type[])null);
			MP.RegisterSyncMethod(methodInfo, (SyncType[])null);
			MP.RegisterSyncMethod(methodInfo2, (SyncType[])null);
			harmony.Patch((MethodBase)AccessTools.Method(typeof(Pawn_AbilityTracker), "GainAbility", (Type[])null, (Type[])null), new HarmonyMethod(typeof(AriandelPsionicWindow_Compat), "GainAbility_Prefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			harmony.Patch((MethodBase)AccessTools.Method(typeof(Pawn_AbilityTracker), "RemoveAbility", (Type[])null, (Type[])null), new HarmonyMethod(typeof(AriandelPsionicWindow_Compat), "RemoveAbility_Prefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			Log.Message("[MiliraMP] AriandelPsionicWindow_Compat: initialized");
		}
		catch (Exception arg)
		{
			Log.Error($"[MiliraMP] AriandelPsionicWindow_Compat init error: {arg}");
		}
	}

	private static bool IsTargetAbility(AbilityDef def)
	{
		if (def?.modContentPack == null)
		{
			return false;
		}
		string packageId = def.modContentPack.PackageId;
		return packageId == MiliraImperiumPackageId
		       || packageId == AriandelLibraryPackageId
		       || packageId == FianchettoPackageId;
	}

	private static bool GainAbility_Prefix(Pawn_AbilityTracker __instance, AbilityDef def)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (!IsTargetAbility(def))
		{
			return true;
		}
		Pawn pawn = __instance.pawn;
		if (pawn == null || def == null)
		{
			return false;
		}
		SyncGainAbility(((Thing)pawn).thingIDNumber, ((Def)def).defName);
		return false;
	}

	private static bool RemoveAbility_Prefix(Pawn_AbilityTracker __instance, AbilityDef def)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (!IsTargetAbility(def))
		{
			return true;
		}
		Pawn pawn = __instance.pawn;
		if (pawn == null || def == null)
		{
			return false;
		}
		SyncRemoveAbility(((Thing)pawn).thingIDNumber, ((Def)def).defName);
		return false;
	}

	private static void SyncGainAbility(int pawnThingID, string abilityDefName)
	{
		Pawn val = FindPawnByID(pawnThingID);
		if (val?.abilities != null)
		{
			AbilityDef namedSilentFail = DefDatabase<AbilityDef>.GetNamedSilentFail(abilityDefName);
			if (namedSilentFail != null && val.abilities.GetAbility(namedSilentFail, false) == null)
			{
				val.abilities.abilities.Add(AbilityUtility.MakeAbility(namedSilentFail, val));
				val.abilities.Notify_TemporaryAbilitiesChanged();
			}
		}
	}

	private static void SyncRemoveAbility(int pawnThingID, string abilityDefName)
	{
		Pawn val = FindPawnByID(pawnThingID);
		if (val?.abilities == null)
		{
			return;
		}
		AbilityDef namedSilentFail = DefDatabase<AbilityDef>.GetNamedSilentFail(abilityDefName);
		if (namedSilentFail != null)
		{
			Ability ability = val.abilities.GetAbility(namedSilentFail, false);
			if (ability != null)
			{
				val.abilities.abilities.Remove(ability);
				val.abilities.Notify_TemporaryAbilitiesChanged();
			}
		}
	}

	private static Pawn FindPawnByID(int thingID)
	{
		if (Find.Maps == null)
		{
			return null;
		}
		foreach (Map map in Find.Maps)
		{
			foreach (Thing allThing in map.listerThings.AllThings)
			{
				if (allThing.thingIDNumber == thingID)
				{
					Pawn val = (Pawn)(object)((allThing is Pawn) ? allThing : null);
					if (val != null)
					{
						return val;
					}
				}
			}
		}
		return null;
	}
}
