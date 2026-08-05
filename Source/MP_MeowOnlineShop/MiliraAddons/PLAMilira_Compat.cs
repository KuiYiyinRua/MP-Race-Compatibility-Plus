using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_Compat
{
	private static bool isInitialized;

	private static readonly Harmony harmony;

	private static Type _harrierWin;

	private static Type _destroyerWin;

	private static Type _milianWin;

	private static Type _churchMilianWin;

	private static Type _supplyWin;

	private static readonly BindingFlags AllStat;

	static PLAMilira_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("PLAMilira.Compat");
		AllStat = BindingFlags.Static | BindingFlags.Public;
		if (!MP.enabled)
		{
			Log.Message("[PLAMilira_Compat] MP not enabled, skipping");
			return;
		}
		if (!ModsConfig.IsActive("sleepycot.wingsofdemocracy"))
		{
			Log.Message("[PLAMilira_Compat] Mod not installed, skipping");
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMilira_Compat] init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			RegisterSyncMethods();
			LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
		}
	}

	private static void RegisterSyncMethods()
	{
		string[] array = new string[10] { "SyncedHarrierStrike", "SyncedDestroyerStrike", "SyncedMilianStrike", "SyncedSupplyDrop", "SyncedOrbitalTrader", "SyncedGiveQuest", "SyncedGameCondition", "SyncedResearchProgress", "SyncedSilverDrop", "SyncedSecretSuccess" };
		foreach (string name in array)
		{
			TryReg(typeof(PLAMilira_Compat).GetMethod(name, AllStat));
		}
	}

	private static void TryReg(MethodInfo m)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void RegField(Type type, string field)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncField(type, field);
	}

	private static void LatePatch()
	{
		//IL_037e: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Expected O, but got Unknown
		//IL_0478: Unknown result type (might be due to invalid IL or missing references)
		//IL_0485: Expected O, but got Unknown
		//IL_0404: Unknown result type (might be due to invalid IL or missing references)
		//IL_0411: Expected O, but got Unknown
		string text = "PLAMilira.";
		_harrierWin = Resolve(text + "Dialog_Harrier_ArrowWindow");
		_destroyerWin = Resolve(text + "Dialog_Destroyer_ArrowWindow");
		_milianWin = Resolve(text + "Dialog_Milian_ArrowWindow");
		_churchMilianWin = Resolve(text + "Dialog_Church_Milian_ArrowWindow");
		_supplyWin = Resolve(text + "Dialog_Supply_ArrowWindow");
		Type type = Resolve(text + "Dialog_Secret_ArrowWindow");
		PatchMethod("PLAMilira.Dialog_Harrier_ArrowWindow:StartTargetingWithThing", "Prefix_HarrierStartTargeting");
		PatchMethod("PLAMilira.Dialog_Destroyer_ArrowWindow:StartTargetingWithThing", "Prefix_DestroyerStartTargeting");
		PatchMethod("PLAMilira.Dialog_Milian_ArrowWindow:ExecuteCombo", "Prefix_MilianExecuteCombo");
		PatchMethod("PLAMilira.Dialog_Church_Milian_ArrowWindow:ExecuteCombo", "Prefix_MilianExecuteCombo");
		PatchMethod("PLAMilira.Dialog_Supply_ArrowWindow:ExecuteCombo", "Prefix_SupplyExecuteCombo");
		string[] array = new string[4] { "PLAMilira.Dialog_Supply_ArrowWindow:CallTecPointResearchHelp", "PLAMilira.Dialog_Supply_ArrowWindow:CallTecPointSilverHelp", "PLAMilira.PortableSecretConsole:GenerateSecretCode", "PLAMilira.PLAMilira_GameComponent:GameComponentTick" };
		string[] array2 = array;
		foreach (string m in array2)
		{
			WrapRand(m);
		}
		TryRegExt("PLAMilira.PLAmiliraUtility", "AddNewQueuedIncident", typeof(IncidentDef), typeof(int), typeof(IncidentParms), typeof(int));
		TryRegExt("PLAMilira.PLAmiliraUtility", "TrySpawnQuest", typeof(QuestScriptDef), typeof(Map), typeof(bool), typeof(bool));
		TryRegExt("PLAMilira.PLAmiliraUtility", "TryFireIncidentNow", typeof(IncidentDef), typeof(IncidentParms), typeof(bool));
		Type type2 = Resolve("PLAMilira.PLAMilira_Church_Faction_Handler");
		Type type3 = Resolve("PLAMilira.PLAMilira_GameComponent");
		Type type4 = Resolve("PLAMilira.PLAMilira_Faction_Handler");
		if (type2 != null)
		{
			RegField(type2, "tecPoint");
		}
		if (type3 != null)
		{
			RegField(type3, "isUnlocking");
		}
		if (type4 != null)
		{
			RegField(type4, "reinforcements");
		}
		TryReg(typeof(PLAMilira_Compat).GetMethod("SyncedAdjustTECPoints", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
		{
			typeof(int),
			typeof(bool)
		}, null));
		TryReg(typeof(PLAMilira_Compat).GetMethod("SyncedAdjustReinforcements", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
		{
			typeof(int),
			typeof(bool)
		}, null));
		TryReg(typeof(PLAMilira_Compat).GetMethod("SyncedUnlockingTec", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
		{
			typeof(int),
			typeof(string)
		}, null));
		if (type2 != null)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type2, "AdjustTECPoints", new Type[2]
			{
				typeof(int),
				typeof(bool)
			}, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_Compat), "Prefix_AdjustTECPoints", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		if (type3 != null)
		{
			Type type5 = Resolve("PLAMilira.PLAMiliraFactionTecDef");
			if (type5 != null)
			{
				MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type3, "UnlockingTec", new Type[2]
				{
					typeof(int),
					type5
				}, (Type[])null);
				if (methodInfo2 != null)
				{
					harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(PLAMilira_Compat), "Prefix_UnlockingTec", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
		}
		if (type4 != null)
		{
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(type4, "AdjustReinforcements", new Type[2]
			{
				typeof(int),
				typeof(bool)
			}, (Type[])null);
			if (methodInfo3 != null)
			{
				harmony.Patch((MethodBase)methodInfo3, new HarmonyMethod(typeof(PLAMilira_Compat), "Prefix_AdjustReinforcements", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	private static Type Resolve(string name)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(name, "PLAMilira_Compat");
	}

	private static bool PatchMethod(string typeColonMethod, string prefix)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		MethodInfo methodInfo = AccessTools.Method(typeColonMethod, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Error("[PLAMilira_Compat] METHOD NOT FOUND: " + typeColonMethod);
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_Compat), prefix, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		Log.Message("[PLAMilira_Compat] Patched: " + typeColonMethod + " -> " + prefix);
		return true;
	}

	private static bool WrapRand(string m)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected O, but got Unknown
		//IL_0062: Expected O, but got Unknown
		MethodInfo methodInfo = AccessTools.Method(m, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Message("[PLAMilira_Compat] RNG method not found: " + m);
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(PLAMilira_Compat), "PushRand", (Type[])null), new HarmonyMethod(typeof(PLAMilira_Compat), "PopRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	private static void TryRegExt(string typeName, string method, params Type[] parTypes)
	{
		MethodInfo methodInfo = AccessTools.Method(typeName + ":" + method, parTypes, (Type[])null);
		if (methodInfo == null)
		{
			Log.Message("[PLAMilira_Compat] ext method not found " + typeName + ":" + method);
		}
		else
		{
			TryReg(methodInfo);
		}
	}

	private static int AdjustSilverForCooldown(string cdKey, int needSilver)
	{
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer)
		{
			return needSilver;
		}
		object obj = AccessTools.Method("PLAMilira.PLAMilira_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
		if (obj == null)
		{
			return needSilver;
		}
		object obj2 = obj.GetType().GetMethod("get_CooldownManager")?.Invoke(obj, null);
		if (obj2 == null)
		{
			return needSilver;
		}
		MethodInfo method = obj2.GetType().GetMethod("GetCooldownTicksLeft", new Type[1] { typeof(string) });
		if (method == null)
		{
			return needSilver;
		}
		int num = (int)(method.Invoke(obj2, new object[1] { cdKey }) ?? ((object)0));
		if (num > 0)
		{
			needSilver = (int)Math.Round((double)needSilver * 1.5);
			Messages.Message(TranslatorFormattedStringExtensions.Translate("PLAMilira_Dialog_ArrowWindowInCooldownSilverDouble", needSilver), MessageTypeDefOf.NeutralEvent, true);
		}
		return needSilver;
	}

	public static bool Prefix_HarrierStartTargeting(object __instance, string thingDefName, int cooldownTicks, string successMessage, int needSilver, float highLightRange)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Expected O, but got Unknown
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		needSilver = AdjustSilverForCooldown("HarrierSupport", needSilver);
		((Window)__instance).Close(true);
		string capturedName = thingDefName;
		int capturedCd = cooldownTicks;
		int capturedSilver = needSilver;
		Find.Targeter.BeginTargeting(new TargetingParameters
		{
			canTargetLocations = true,
			canTargetBuildings = false,
			canTargetPawns = false
		}, (Action<LocalTargetInfo>)delegate(LocalTargetInfo target)
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			if (target.IsValid && GenGrid.InBounds(target.Cell, Find.CurrentMap))
			{
				SyncedHarrierStrike(capturedName, target.Cell, capturedCd, capturedSilver);
			}
		}, (Pawn)null, (Action)null, (Texture2D)null, true);
		return false;
	}

	public static bool Prefix_DestroyerStartTargeting(object __instance, string thingDefName, int cooldownTicks, string successMessage, int needSilver, float highLightRange)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Expected O, but got Unknown
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		needSilver = AdjustSilverForCooldown("DestroyerSupport", needSilver);
		((Window)__instance).Close(true);
		string capturedName = thingDefName;
		int capturedCd = cooldownTicks;
		int capturedSilver = needSilver;
		Find.Targeter.BeginTargeting(new TargetingParameters
		{
			canTargetLocations = true,
			canTargetBuildings = false,
			canTargetPawns = false
		}, (Action<LocalTargetInfo>)delegate(LocalTargetInfo target)
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			if (target.IsValid && GenGrid.InBounds(target.Cell, Find.CurrentMap))
			{
				SyncedDestroyerStrike(capturedName, target.Cell, capturedCd, capturedSilver);
			}
		}, (Pawn)null, (Action)null, (Texture2D)null, true);
		return false;
	}

	public static bool Prefix_MilianExecuteCombo(object combo, object __instance)
	{
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Expected O, but got Unknown
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (combo == null)
		{
			return true;
		}
		Type type = combo.GetType();
		IList drops = type.GetField("Drops")?.GetValue(combo) as IList;
		int num = AdjustSilverForCooldown("MilianSupport", (int)(type.GetField("NeedSilver")?.GetValue(combo) ?? ((object)0)));
		int num2 = (int)(type.GetField("CooldownTicks")?.GetValue(combo) ?? ((object)0));
		object obj = type.GetField("FactionDef")?.GetValue(combo);
		FactionDef val = (FactionDef)((obj is FactionDef) ? obj : null);
		FactionDef capturedFaction = val;
		int capturedSilver = num;
		int capturedCd = num2;
		((Window)__instance).Close(true);
		Find.Targeter.BeginTargeting(new TargetingParameters
		{
			canTargetLocations = true,
			canTargetBuildings = false,
			canTargetPawns = false
		}, (Action<LocalTargetInfo>)delegate(LocalTargetInfo target)
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_017f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0126: Unknown result type (might be due to invalid IL or missing references)
			//IL_014c: Unknown result type (might be due to invalid IL or missing references)
			if (target.IsValid && GenGrid.InBounds(target.Cell, Find.CurrentMap))
			{
				List<PawnKindDef> list = new List<PawnKindDef>();
				List<ThingDef> list2 = new List<ThingDef>();
				List<Pair<int, int>> list3 = new List<Pair<int, int>>();
				if (drops != null)
				{
					foreach (object item in drops)
					{
						Type type2 = item.GetType();
						object obj2 = type2.GetField("pawnKindDef")?.GetValue(item);
						PawnKindDef val2 = (PawnKindDef)((obj2 is PawnKindDef) ? obj2 : null);
						object obj3 = type2.GetField("buildingDef")?.GetValue(item);
						ThingDef val3 = (ThingDef)((obj3 is ThingDef) ? obj3 : null);
						int num3 = (int)(type2.GetField("minCount")?.GetValue(item) ?? ((object)1));
						int num4 = (int)(type2.GetField("maxCount")?.GetValue(item) ?? ((object)1));
						if (val2 != null)
						{
							list.Add(val2);
							list3.Add(new Pair<int, int>(num3, num4));
						}
						if (val3 != null)
						{
							list2.Add(val3);
							list3.Add(new Pair<int, int>(num3, num4));
						}
					}
				}
				SyncedMilianStrike(target.Cell, list.ToArray(), list2.ToArray(), list3.ToArray(), capturedFaction, capturedSilver, capturedCd);
			}
		}, (Pawn)null, (Action)null, (Texture2D)null, true);
		return false;
	}

	public static bool Prefix_SupplyExecuteCombo(object combo, object __instance)
	{
		//IL_0397: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d7: Expected O, but got Unknown
		//IL_033f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0350: Unknown result type (might be due to invalid IL or missing references)
		//IL_0355: Unknown result type (might be due to invalid IL or missing references)
		//IL_035c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0363: Unknown result type (might be due to invalid IL or missing references)
		//IL_0380: Expected O, but got Unknown
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (combo == null || __instance == null)
		{
			return true;
		}
		Type type = combo.GetType();
		object obj = type.GetField("TraderKindDef")?.GetValue(combo);
		TraderKindDef val = (TraderKindDef)((obj is TraderKindDef) ? obj : null);
		IList list = type.GetField("QuestScriptDef")?.GetValue(combo) as IList;
		IList list2 = type.GetField("Drops")?.GetValue(combo) as IList;
		float num = (float)(type.GetField("ResearchProgressAmount")?.GetValue(combo) ?? ((object)0f));
		int silverGain = (int)(type.GetField("SilverGainAmount")?.GetValue(combo) ?? ((object)0));
		int needSilver = AdjustSilverForCooldown("SupplySupport", (int)(type.GetField("NeedSilver")?.GetValue(combo) ?? ((object)0)));
		int cdTicks = (int)(type.GetField("CooldownTicks")?.GetValue(combo) ?? ((object)0));
		int tecCost = (int)(type.GetField("TecPointCost")?.GetValue(combo) ?? ((object)0));
		object obj2 = type.GetField("FactionDef")?.GetValue(combo);
		FactionDef val2 = (FactionDef)((obj2 is FactionDef) ? obj2 : null);
		if (val != null)
		{
			Faction faction = Find.FactionManager.FirstFactionOfDef(val2);
			SyncedOrbitalTrader(Find.CurrentMap, val, faction, needSilver, cdTicks);
			return false;
		}
		if (list != null && list.Count > 0)
		{
			object obj3 = list[0];
			QuestScriptDef questDef = (QuestScriptDef)((obj3 is QuestScriptDef) ? obj3 : null);
			SyncedGiveQuest(questDef, needSilver, cdTicks);
			return false;
		}
		if (num != 0f)
		{
			SyncedResearchProgress(num, tecCost, cdTicks);
			return false;
		}
		if (list2 != null && list2.Count > 0)
		{
			Dictionary<ThingDef, int> dropDict = new Dictionary<ThingDef, int>();
			foreach (object item in list2)
			{
				Type type2 = item.GetType();
				object obj4 = type2.GetField("thingDef")?.GetValue(item);
				ThingDef val3 = (ThingDef)((obj4 is ThingDef) ? obj4 : null);
				int num2 = (int)(type2.GetField("count")?.GetValue(item) ?? ((object)0));
				if (val3 != null && num2 > 0)
				{
					dropDict[val3] = num2;
				}
			}
			((Window)__instance).Close(true);
			Find.Targeter.BeginTargeting(new TargetingParameters
			{
				canTargetLocations = true,
				canTargetBuildings = false,
				canTargetPawns = false
			}, (Action<LocalTargetInfo>)delegate(LocalTargetInfo target)
			{
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0029: Unknown result type (might be due to invalid IL or missing references)
				if (target.IsValid && GenGrid.InBounds(target.Cell, Find.CurrentMap))
				{
					SyncedSupplyDrop(target.Cell, needSilver, cdTicks, dropDict);
				}
			}, (Pawn)null, (Action)null, (Texture2D)null, true);
			return false;
		}
		if (silverGain > 0)
		{
			((Window)__instance).Close(true);
			Find.Targeter.BeginTargeting(new TargetingParameters
			{
				canTargetLocations = true,
				canTargetBuildings = false,
				canTargetPawns = false
			}, (Action<LocalTargetInfo>)delegate(LocalTargetInfo target)
			{
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0029: Unknown result type (might be due to invalid IL or missing references)
				if (target.IsValid && GenGrid.InBounds(target.Cell, Find.CurrentMap))
				{
					SyncedSilverDrop(target.Cell, Find.CurrentMap, silverGain, tecCost, cdTicks);
				}
			}, (Pawn)null, (Action)null, (Texture2D)null, true);
			return false;
		}
		return true;
	}

	public static void PushRand()
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.PushRand();
	}

	public static void PopRand()
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.PopRand();
	}

	public static void SyncedHarrierStrike(string thingDefName, IntVec3 cell, int cdTicks, int needSilver)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		Map currentMap = Find.CurrentMap;
		if (currentMap == null || !GenGrid.InBounds(cell, currentMap))
		{
			return;
		}
		Map val = TryPaySilver(needSilver);
		if (val != null || needSilver <= 0)
		{
			ThingDef named = DefDatabase<ThingDef>.GetNamed(thingDefName, false);
			if (named != null)
			{
				GenSpawn.Spawn(named, cell, currentMap, (WipeMode)0);
				RecordCooldown("HarrierSupport", cdTicks);
			}
		}
	}

	public static void SyncedDestroyerStrike(string thingDefName, IntVec3 cell, int cdTicks, int needSilver)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		Map currentMap = Find.CurrentMap;
		if (currentMap == null || !GenGrid.InBounds(cell, currentMap))
		{
			return;
		}
		Map val = TryPaySilver(needSilver);
		if (val != null || needSilver <= 0)
		{
			ThingDef named = DefDatabase<ThingDef>.GetNamed(thingDefName, false);
			if (named != null)
			{
				GenSpawn.Spawn(named, cell, currentMap, (WipeMode)0);
				RecordCooldown("DestroyerSupport", cdTicks);
			}
		}
	}

	public unsafe static void SyncedMilianStrike(IntVec3 cell, PawnKindDef[] pawnKinds, ThingDef[] buildingDefs, Pair<int, int>[] minMaxCounts, FactionDef factionDef, int needSilver, int cdTicks)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_020c: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fa: Unknown result type (might be due to invalid IL or missing references)
		Map currentMap = Find.CurrentMap;
		if (currentMap == null || !GenGrid.InBounds(cell, currentMap))
		{
			return;
		}
		Map val = TryPaySilver(needSilver);
		if (val == null && needSilver > 0)
		{
			return;
		}
		currentMap = val ?? currentMap;
		Faction val2 = Find.FactionManager.FirstFactionOfDef(factionDef);
		List<Pawn> list = new List<Pawn>();
		List<Thing> list2 = new List<Thing>();
		int num = 0;
		Rand.PushState();
		Rand.Seed = ((object)(*(IntVec3*)(&cell))/*cast due to constrained. prefix*/).GetHashCode() ^ Find.TickManager.TicksGame;
		try
		{
			if (pawnKinds != null)
			{
				foreach (PawnKindDef val3 in pawnKinds)
				{
					if (val3 == null)
					{
						num++;
						continue;
					}
					int num2 = ((minMaxCounts == null || num >= minMaxCounts.Length) ? 1 : Rand.RangeInclusive(minMaxCounts[num].First, minMaxCounts[num].Second));
					num++;
					for (int j = 0; j < num2; j++)
					{
						list.Add(PawnGenerator.GeneratePawn(val3, val2, (PlanetTile?)null));
					}
				}
			}
			if (buildingDefs != null)
			{
				foreach (ThingDef val4 in buildingDefs)
				{
					if (val4 == null)
					{
						num++;
						continue;
					}
					int num3 = ((minMaxCounts == null || num >= minMaxCounts.Length) ? 1 : Rand.RangeInclusive(minMaxCounts[num].First, minMaxCounts[num].Second));
					num++;
					for (int l = 0; l < num3; l++)
					{
						Thing val5 = ThingMaker.MakeThing(val4, (ThingDef)null);
						val5.SetFaction(val2, (Pawn)null);
						list2.Add(val5);
					}
				}
			}
		}
		finally
		{
			Rand.PopState();
		}
		List<Thing> list3 = new List<Thing>();
		list3.AddRange((IEnumerable<Thing>)list);
		list3.AddRange(list2);
		DropPodUtility.DropThingsNear(cell, currentMap, (IEnumerable<Thing>)list3, 110, false, false, true, true, true, val2);
		if (list.Count > 0)
		{
			Type type = AccessTools.TypeByName("RimWorld.LordMaker");
			Type type2 = AccessTools.TypeByName("RimWorld.LordJob_AssistColony");
			if (type != null && type2 != null)
			{
				MethodInfo methodInfo = type.GetMethods().FirstOrDefault((MethodInfo m) => m.Name == "MakeNewLord" && m.GetParameters().Length == 4);
				ConstructorInfo constructorInfo = type2.GetConstructors().FirstOrDefault((ConstructorInfo c) => c.GetParameters().Length == 2);
				if (methodInfo != null && constructorInfo != null)
				{
					methodInfo.Invoke(null, new object[4]
					{
						val2,
						constructorInfo.Invoke(new object[2] { val2, cell }),
						currentMap,
						list
					});
				}
			}
		}
		RecordCooldown("MilianSupport", cdTicks);
	}

	public static void SyncedSupplyDrop(IntVec3 cell, int needSilver, int cooldownTicks, Dictionary<ThingDef, int> drops)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		Map currentMap = Find.CurrentMap;
		if (currentMap == null || !GenGrid.InBounds(cell, currentMap))
		{
			return;
		}
		Map val = TryPaySilver(needSilver);
		if (val == null && needSilver > 0)
		{
			return;
		}
		currentMap = val ?? currentMap;
		if (drops != null && drops.Count > 0)
		{
			MethodInfo methodInfo = AccessTools.Method("PLAMilira.Supply_Skyfaller_Manager:SpawnSkyfallerAt", new Type[3]
			{
				typeof(Map),
				typeof(IntVec3),
				typeof(Dictionary<ThingDef, int>)
			}, (Type[])null);
			if (methodInfo != null)
			{
				methodInfo.Invoke(null, new object[3] { currentMap, cell, drops });
			}
		}
		RecordCooldown("SupplySupport", cooldownTicks);
	}

	public static void SyncedOrbitalTrader(Map map, TraderKindDef traderKind, Faction faction, int silver, int cdTicks)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected O, but got Unknown
		if (map != null && traderKind != null)
		{
			Map val = TryPaySilver(silver);
			if (val != null || silver <= 0)
			{
				Find.Storyteller.incidentQueue.Add(IncidentDefOf.OrbitalTraderArrival, Find.TickManager.TicksGame + 2500, new IncidentParms
				{
					target = (IIncidentTarget)(object)map,
					faction = faction,
					traderKind = traderKind,
					forced = true
				}, 600);
				RecordCooldown("SupplySupport", cdTicks);
			}
		}
	}

	public static void SyncedGiveQuest(QuestScriptDef questDef, int silver, int cdTicks)
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		Map val = TryPaySilver(silver);
		if (val != null || silver <= 0)
		{
			Find.Storyteller.incidentQueue.Add(IncidentDefOf.GiveQuest_Random, Find.TickManager.TicksGame + 2500, new IncidentParms
			{
				target = (IIncidentTarget)(object)Find.World,
				questScriptDef = questDef,
				faction = Faction.OfPlayer,
				forced = true
			}, 600);
			RecordCooldown("SupplySupport", cdTicks);
		}
	}

	public static void SyncedGameCondition(GameConditionDef def, int duration, int mapDuration, Map map, int cdTicks)
	{
		if (map != null && def != null)
		{
			GameCondition val = GameConditionMaker.MakeCondition(def, duration);
			val.Duration = mapDuration;
			map.GameConditionManager.RegisterCondition(val);
			RecordCooldown("GameCondition", cdTicks);
		}
	}

	public static void SyncedResearchProgress(float progress, int tecCost, int cdTicks)
	{
		if (tecCost > 0)
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_Church_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj != null)
			{
				int num = (int)(obj.GetType().GetMethod("get_TECPoint", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj, null) ?? ((object)0));
				if (num < tecCost)
				{
					return;
				}
				AccessTools.Method(obj.GetType(), "AdjustTECPoints", new Type[2]
				{
					typeof(int),
					typeof(bool)
				}, (Type[])null)?.Invoke(obj, new object[2]
				{
					-tecCost,
					true
				});
			}
		}
		ResearchProjectDef project = Find.ResearchManager.GetProject((KnowledgeCategoryDef)null);
		if (project != null)
		{
			Find.ResearchManager.AddProgress(project, progress, (Pawn)null);
		}
		RecordCooldown("SupplySupport", cdTicks);
	}

	public static void SyncedSilverDrop(IntVec3 cell, Map map, int silver, int tecCost, int cdTicks)
	{
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		if (map == null || !GenGrid.InBounds(cell, map))
		{
			return;
		}
		if (tecCost > 0)
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_Church_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj != null)
			{
				int num = (int)(obj.GetType().GetMethod("get_TECPoint", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj, null) ?? ((object)0));
				if (num < tecCost)
				{
					return;
				}
				AccessTools.Method(obj.GetType(), "AdjustTECPoints", new Type[2]
				{
					typeof(int),
					typeof(bool)
				}, (Type[])null)?.Invoke(obj, new object[2]
				{
					-tecCost,
					true
				});
			}
		}
		if (silver > 0)
		{
			Dictionary<ThingDef, int> dictionary = new Dictionary<ThingDef, int> { 
			{
				ThingDefOf.Silver,
				silver
			} };
			AccessTools.Method("PLAMilira.Supply_Skyfaller_Manager:SpawnSkyfallerAt", new Type[3]
			{
				typeof(Map),
				typeof(IntVec3),
				typeof(Dictionary<ThingDef, int>)
			}, (Type[])null)?.Invoke(null, new object[3] { map, cell, dictionary });
		}
		RecordCooldown("SupplySupport", cdTicks);
	}

	public static void SyncedSecretSuccess(int questId, Thing console)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		Find.SignalManager.SendSignal(new Signal($"Quest{questId}.SecretSuccess", true));
		if (console != null && !console.Destroyed)
		{
			console.Destroy((DestroyMode)0);
		}
	}

	private static void RecordCooldown(string key, int ticks)
	{
		object obj = AccessTools.Method("PLAMilira.PLAMilira_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
		if (obj != null)
		{
			object obj2 = obj.GetType().GetMethod("get_CooldownManager")?.Invoke(obj, null);
			obj2?.GetType().GetMethod("RegisterRecord")?.Invoke(obj2, new object[3] { key, ticks, true });
		}
	}

	private static Map TryPaySilver(int amount)
	{
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		if (amount <= 0)
		{
			return Find.CurrentMap;
		}
		object obj = AccessTools.Method("PLAMilira.PLAMilira_PlayerHome_MapUtility:TryPaySilverFromBestMap", new Type[1] { typeof(int) }, (Type[])null)?.Invoke(null, new object[1] { amount });
		Map val = (Map)((obj is Map) ? obj : null);
		if (val == null)
		{
			Messages.Message(TranslatorFormattedStringExtensions.Translate("PLAMilira_Dialog_ArrowWindowNoSilver", amount), MessageTypeDefOf.RejectInput, true);
		}
		return val;
	}

	public static bool Prefix_AdjustTECPoints(int change, bool showMessage)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedAdjustTECPoints(change, showMessage);
		return false;
	}

	public static bool Prefix_AdjustReinforcements(int change, bool showMessage)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedAdjustReinforcements(change, showMessage);
		return false;
	}

	public static bool Prefix_UnlockingTec(int ticks, object unlockTec)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedUnlockingTec(ticks, (unlockTec as Def)?.defName ?? "");
		return false;
	}

	public static void SyncedAdjustTECPoints(int change, bool showMessage)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_Church_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj != null)
			{
				AccessTools.Method(obj.GetType(), "AdjustTECPoints", new Type[2]
				{
					typeof(int),
					typeof(bool)
				}, (Type[])null)?.Invoke(obj, new object[2] { change, showMessage });
			}
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMiliraCompat] SyncedAdjustTECPoints error: {arg}");
		}
	}

	public static void SyncedAdjustReinforcements(int change, bool showMessage)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_Faction_Handler:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj != null)
			{
				AccessTools.Method(obj.GetType(), "AdjustReinforcements", new Type[2]
				{
					typeof(int),
					typeof(bool)
				}, (Type[])null)?.Invoke(obj, new object[2] { change, showMessage });
			}
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMiliraCompat] SyncedAdjustReinforcements error: {arg}");
		}
	}

	public static void SyncedUnlockingTec(int unlockTick, string tecDefName)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		try
		{
			object obj = AccessTools.Method("PLAMilira.PLAMilira_GameComponent:get_Instance", (Type[])null, (Type[])null)?.Invoke(null, null);
			if (obj == null)
			{
				return;
			}
			Type type = AccessTools.TypeByName("PLAMilira.PLAMiliraFactionTecDef");
			if (!(type == null))
			{
				Type type2 = typeof(DefDatabase<>).MakeGenericType(type);
				object obj2 = type2.GetMethod("GetNamed", new Type[2]
				{
					typeof(string),
					typeof(bool)
				})?.Invoke(null, new object[2] { tecDefName, false });
				if (obj2 != null)
				{
					AccessTools.Method(obj.GetType(), "UnlockingTec", new Type[2]
					{
						typeof(int),
						type
					}, (Type[])null)?.Invoke(obj, new object[2] { unlockTick, obj2 });
				}
			}
		}
		catch (Exception arg)
		{
			Log.Error($"[PLAMiliraCompat] SyncedUnlockingTec error: {arg}");
		}
	}
}
