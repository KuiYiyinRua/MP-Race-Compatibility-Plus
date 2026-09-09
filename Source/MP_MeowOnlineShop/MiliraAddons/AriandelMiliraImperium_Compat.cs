using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class AriandelMiliraImperium_Compat
{
	private const string NS = "MiliraImperium.";

	private static bool isInitialized;

	private static readonly Harmony harmony;

	private static Type _dialogType;

	private static FieldInfo _fiAuraIsActive;

	private static FieldInfo _fiApparelIsActive;

	private static FieldInfo _fiPsyMode2;

	private static FieldInfo _fiShieldRender;

	private static FieldInfo _fiSecondaryVerbSelected;

	private static FieldInfo _fiLockEnabled;

	private static void LogOnce(string msg)
	{
		Log.Message(msg);
	}

	static AriandelMiliraImperium_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("Ariandel.MiliraImperium.Compat");
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
			Log.Error($"AriandelMiliraImperium_Compat: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			RegisterPauseAndCloseSync();
			RegisterGameCompSyncMethods();
			RegisterCompSyncMethods();
			ReplaceCurrentMapUsage("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window:TryCallImperiumGift");
			ReplaceCurrentMapUsage("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window:DrawCommsPage");
			LongEventHandler.ExecuteWhenFinished((Action)LatePatch);
			Log.Message("AriandelMiliraImperium_Compat: OK");
		}
	}

	private static void RegisterPauseAndCloseSync()
	{
		_dialogType = Resolve("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window");
	}

	private static void RegisterGameCompSyncMethods()
	{
		Type type = Resolve("MiliraImperium.MiliraImperium_GameComponent");
		if (!(type == null))
		{
			TryReg(type, "TryUpgradeNetworkLevel");
			TryReg(type, "StartImperiumGiftEvent");
			TryReg(type, "CallRawOrbitalTrader");
			TryReg(type, "CallFoodOrbitalTrader");
			TryReg(type, "CallTechOrbitalTrader");
			TryReg(type, "CallTributeCollectorOrbitalTrader");
			TryReg(type, "OnImperiumBecameHostile");
			TryRegField(type, "ImperiumSupport");
			TryRegField(type, "NetworkLevel");
			TryRegField(type, "FeiJiangRecruited");
			TryRegField(type, "DevLydiaRecruited");
			TryRegField(type, "nextAllowedTick");
			TryRegField(type, "nextAllowedTickI");
			TryRegField(type, "giftEventPending");
			TryRegField(type, "giftEventExecuteTick");
			TryRegField(type, "warpInCoolDown");
			TryReg(type, "StartCooldown");
			TryReg(type, "ClearCooldown");
		}
	}

	private static void RegisterCompSyncMethods()
	{
		Type type = Resolve("MiliraImperium.CompSecondaryVerb_Rework");
		if (type != null)
		{
			TryReg(AccessTools.DeclaredMethod(type, "SwitchVerb", (Type[])null, (Type[])null));
			TryRegField(type, "isSecondaryVerbSelected");
		}
		Type type2 = Resolve("MiliraImperium.CompMaustsAuraEmitter");
		if (type2 != null)
		{
			TryRegField(type2, "isActive");
		}
		Type type3 = Resolve("MiliraImperium.CompMaustsAuraEmitter_Apparel");
		if (type3 != null)
		{
			TryRegField(type3, "isActive");
		}
		Type type4 = Resolve("MiliraImperium.MI_Psycounter");
		if (type4 != null)
		{
			TryRegField(type4, "isMode2");
		}
		Type type5 = Resolve("MiliraImperium.Comp_DeflectorShield");
		if (type5 != null)
		{
			TryRegField(type5, "renderShader");
		}
		Type type6 = Resolve("MiliraImperium.HediffComp_MovingLockToggleTrait");
		if (type6 != null)
		{
			TryRegField(type6, "enabled");
		}
	}

	private static void ReplaceCurrentMapUsage(string typeColonMethod)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeColonMethod, (Type[])null, (Type[])null);
			if (!(methodInfo == null))
			{
				harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(AriandelMiliraImperium_Compat), "ReplaceCurrentMapTranspiler", (Type[])null), (HarmonyMethod)null);
			}
		}
		catch
		{
		}
	}

	private static Map GetCurrentMapSafe()
	{
		if (MP.IsInMultiplayer)
		{
			return null;
		}
		return Find.CurrentMap;
	}

	private static IEnumerable<CodeInstruction> ReplaceCurrentMapTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");
		MethodInfo replacement = AccessTools.Method(typeof(AriandelMiliraImperium_Compat), "GetCurrentMapSafe", (Type[])null, (Type[])null);
		bool patched = false;
		foreach (CodeInstruction ci in instructions)
		{
			int num;
			if (ci.opcode == OpCodes.Call)
			{
				object operand = ci.operand;
				if (operand is MethodInfo mi)
				{
					num = ((mi == target) ? 1 : 0);
					goto IL_00e1;
				}
			}
			num = 0;
			goto IL_00e1;
			IL_00e1:
			if (num != 0)
			{
				ci.operand = replacement;
				patched = true;
			}
			yield return ci;
		}
		if (!patched)
		{
			LogOnce("AriandelMiliraImperium_Compat: No Find.CurrentMap patched in " + (((object)baseMethod != null) ? GeneralExtensions.FullDescription(baseMethod) : null));
		}
	}

	private static void LatePatch()
	{
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected O, but got Unknown
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Expected O, but got Unknown
		RegisterAbilityEffects();
		RegisterPermitWorkers();
		TryReg(typeof(AriandelMiliraImperium_Compat).GetMethod("SyncBoolField", BindingFlags.Static | BindingFlags.Public));
		TryReg(typeof(AriandelMiliraImperium_Compat).GetMethod("SyncAuraToggle", BindingFlags.Static | BindingFlags.Public));
		TryReg(typeof(AriandelMiliraImperium_Compat).GetMethod("SyncMovingLockToggle", BindingFlags.Static | BindingFlags.Public));
		TryReg(typeof(AriandelMiliraImperium_Compat).GetMethod("SyncPsyMode", BindingFlags.Static | BindingFlags.Public));
		Type type = Resolve("MiliraImperium.CompSecondaryVerb_Rework");
		if (type != null)
		{
			_fiSecondaryVerbSelected = AccessTools.DeclaredField(type, "isSecondaryVerbSelected");
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "SwitchVerb", (Type[])null, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(AriandelMiliraImperium_Compat), "SwitchVerbPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		Type type2 = Resolve("MiliraImperium.CompMaustsAuraEmitter");
		if (type2 != null)
		{
			_fiAuraIsActive = AccessTools.DeclaredField(type2, "isActive");
			PatchGizmoMethod(type2, "CompGetGizmosExtra", "WrapAuraGizmos");
		}
		Type type3 = Resolve("MiliraImperium.CompMaustsAuraEmitter_Apparel");
		if (type3 != null)
		{
			_fiApparelIsActive = AccessTools.DeclaredField(type3, "isActive");
			PatchGizmoMethod(type3, "CompGetWornGizmosExtra", "WrapApparelGizmos");
		}
		Type type4 = Resolve("MiliraImperium.MI_Psycounter");
		if (type4 != null)
		{
			_fiPsyMode2 = AccessTools.DeclaredField(type4, "isMode2");
			PatchGizmoMethod(type4, "CompGetGizmosExtra", "WrapPsyGizmos");
		}
		Type type5 = Resolve("MiliraImperium.Comp_DeflectorShield");
		if (type5 != null)
		{
			_fiShieldRender = AccessTools.DeclaredField(type5, "renderShader");
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type5, "GetGizmos", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, new HarmonyMethod(typeof(AriandelMiliraImperium_Compat), "WrapShieldGizmos", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		Type type6 = Resolve("MiliraImperium.HediffComp_MovingLockToggleTrait");
		if (type6 != null)
		{
			_fiLockEnabled = AccessTools.DeclaredField(type6, "enabled");
			PatchGizmoMethod(type6, "CompGetGizmos", "WrapLockGizmos");
		}
	}

	private static void PatchGizmoMethod(Type compType, string methodName, string wrapperName)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.PatchMethodPostfix(harmony, compType, methodName, wrapperName, typeof(AriandelMiliraImperium_Compat));
	}

	private static IEnumerable<Gizmo> WrapToggles(IEnumerable<Gizmo> source, ThingComp comp, FieldInfo fi, string fieldName)
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
			yield return g;
		}
	}

	public static void WrapAuraGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiAuraIsActive == null))
		{
			__result = WrapAuraInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapAuraInner(IEnumerable<Gizmo> source, ThingComp comp)
	{
		FieldInfo fiLastToggle = AccessTools.DeclaredField(((object)comp).GetType(), "lastToggleTick");
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
						int lastToggleTick = (fiLastToggle != null) ? (int)fiLastToggle.GetValue(comp) : Find.TickManager.TicksGame;
						SyncAuraToggle(comp, "isActive", (bool)_fiAuraIsActive.GetValue(comp), lastToggleTick);
					}
				};
			}
			yield return g;
		}
	}

	public static void WrapApparelGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiApparelIsActive == null))
		{
			__result = WrapApparelInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapApparelInner(IEnumerable<Gizmo> source, ThingComp comp)
	{
		FieldInfo fiLastToggle = AccessTools.DeclaredField(((object)comp).GetType(), "lastToggleTick");
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
						int lastToggleTick = (fiLastToggle != null) ? (int)fiLastToggle.GetValue(comp) : Find.TickManager.TicksGame;
						SyncAuraToggle(comp, "isActive", (bool)_fiApparelIsActive.GetValue(comp), lastToggleTick);
					}
				};
			}
			yield return g;
		}
	}

	public static void WrapPsyGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && _fiPsyMode2 != null)
		{
			__result = WrapPsyInner(__result, __instance);
		}
	}

	private static IEnumerable<Gizmo> WrapPsyInner(IEnumerable<Gizmo> source, ThingComp comp)
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
						SyncPsyMode(comp, (bool)_fiPsyMode2.GetValue(comp));
					}
				};
			}
			yield return g;
		}
	}

	public static void WrapShieldGizmos(ref IEnumerable<Gizmo> __result, ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && _fiShieldRender != null)
		{
			__result = WrapToggles(__result, __instance, _fiShieldRender, "renderShader");
		}
	}

	public static void WrapLockGizmos(ref IEnumerable<Gizmo> __result, object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			HediffComp val = (HediffComp)((__instance is HediffComp) ? __instance : null);
			if (val != null && _fiLockEnabled != null)
			{
				__result = WrapLockInner(__result, val);
			}
		}
	}

	private static IEnumerable<Gizmo> WrapLockInner(IEnumerable<Gizmo> source, HediffComp comp)
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
						bool enabled = (bool)_fiLockEnabled.GetValue(comp);
						Pawn pawn = comp.Pawn;
						if (pawn != null)
						{
							SyncMovingLockToggle(pawn.thingIDNumber, enabled ? 1 : 0, enabled);
						}
					}
				};
			}
			yield return g;
		}
	}

	public static void SwitchVerbPostfix(ThingComp __instance)
	{
		if (MP.IsInMultiplayer && __instance != null && !(_fiSecondaryVerbSelected == null))
		{
			SyncBoolField(__instance, "isSecondaryVerbSelected", (bool)_fiSecondaryVerbSelected.GetValue(__instance));
		}
	}

	public static void SyncBoolField(ThingComp target, string fieldName, bool value)
	{
		if (target != null)
		{
			AccessTools.Field(((object)target).GetType(), fieldName)?.SetValue(target, value);
		}
	}

	public static void SyncAuraToggle(ThingComp target, string fieldName, bool value, int lastToggleTick)
	{
		if (target != null)
		{
			AccessTools.Field(((object)target).GetType(), fieldName)?.SetValue(target, value);
			AccessTools.DeclaredField(((object)target).GetType(), "lastToggleTick")?.SetValue(target, lastToggleTick);
		}
	}

	public static void SyncPsyMode(ThingComp target, bool value)
	{
		if (target != null)
		{
			AccessTools.Field(((object)target).GetType(), "isMode2")?.SetValue(target, value);
			if (target.parent?.Map != null)
			{
				target.parent.Map.mapDrawer.MapMeshDirty(target.parent.Position, MapMeshFlagDefOf.Things);
			}
		}
	}

	public static void SyncMovingLockToggle(int pawnId, int degree, bool enabled)
	{
		Pawn pawn = FindPawnById(pawnId);
		if (pawn?.health?.hediffSet == null)
		{
			return;
		}
		Type compType = Resolve("MiliraImperium.HediffComp_MovingLockToggleTrait");
		if (compType == null)
		{
			return;
		}
		foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
		{
			object comp = GetHediffCompOfType(hediff, compType);
			if (comp != null)
			{
				AccessTools.DeclaredField(compType, "enabled")?.SetValue(comp, enabled);
				MethodInfo apply = AccessTools.DeclaredMethod(compType, "ApplyTraitDegree", new Type[2] { typeof(Pawn), typeof(int) }, (Type[])null);
				try
				{
					apply?.Invoke(comp, new object[2] { pawn, degree });
				}
				catch
				{
				}
				return;
			}
		}
	}

	private static object GetHediffCompOfType(Hediff hediff, Type compType)
	{
		if (hediff == null || compType == null)
		{
			return null;
		}
		try
		{
			MethodInfo method = typeof(HediffUtility).GetMethods()
				.FirstOrDefault((MethodInfo m) => m.Name == "TryGetComp" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1);
			if (method == null)
			{
				return null;
			}
			return method.MakeGenericMethod(compType).Invoke(null, new object[1] { hediff });
		}
		catch
		{
			return null;
		}
	}

	private static Pawn FindPawnById(int pawnId)
	{
		if (Find.Maps != null)
		{
			foreach (Map map in Find.Maps)
			{
				if (map?.mapPawns?.AllPawnsSpawned == null)
				{
					continue;
				}
				foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
				{
					if (pawn != null && ((Thing)pawn).thingIDNumber == pawnId)
					{
						return pawn;
					}
				}
			}
		}
		try
		{
			foreach (Pawn pawn in Find.WorldPawns.AllPawnsAliveOrDead)
			{
				if (pawn != null && ((Thing)pawn).thingIDNumber == pawnId)
				{
					return pawn;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (Find.World?.worldObjects != null)
			{
				foreach (Caravan caravan in Find.World.worldObjects.Caravans)
				{
					if (caravan?.PawnsListForReading == null)
					{
						continue;
					}
					foreach (Pawn pawn in caravan.PawnsListForReading)
					{
						if (pawn != null && ((Thing)pawn).thingIDNumber == pawnId)
						{
							return pawn;
						}
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static void RegisterAbilityEffects()
	{
		string[] array = new string[16]
		{
			"CompAbilityEffect_CallingSupport", "CompAbilityEffect_MiliraRandomAirdrop", "CompAbilityEffect_AfterimageSlash", "CompAbilityEffect_Arteria", "CompAbilityEffect_BlinkStun", "CompAbilityEffect_Excalibur_Thunder", "CompAbilityEffect_ForceThrow", "CompAbilityEffect_GatherSkip", "CompAbilityEffect_Hellfire", "CompAbilityEffect_LightningBarrage",
			"CompAbilityEffect_MoonLight", "CompAbilityEffect_PsionicChokeAoE", "CompAbilityEffect_SmiteVital", "CompAbilityEffect_SpawnMeteorite", "CompAbilityEffect_Spear_Thunder", "CompAbilityEffect_WeaponChargeCost"
		};
		string[] array2 = array;
		foreach (string text in array2)
		{
			Type type = Resolve("MiliraImperium." + text);
			if (!(type == null))
			{
				MethodInfo m = AccessTools.DeclaredMethod(type, "Apply", new Type[2]
				{
					typeof(LocalTargetInfo),
					typeof(LocalTargetInfo)
				}, (Type[])null);
				TryReg(m);
			}
		}
	}

	private static void RegisterPermitWorkers()
	{
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b7: Expected O, but got Unknown
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Expected O, but got Unknown
		TryReg(typeof(AriandelMiliraImperium_Compat).GetMethod("SyncedPermitOrderForceTarget", BindingFlags.Static | BindingFlags.Public, null, new Type[5]
		{
			typeof(RoyalTitlePermitDef),
			typeof(Pawn),
			typeof(LocalTargetInfo),
			typeof(int),
			typeof(bool)
		}, null));
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			try
			{
				Type[] types = assembly.GetTypes();
				foreach (Type type in types)
				{
					if (type.IsAbstract || type.IsInterface)
					{
						continue;
					}
					MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "OrderForceTarget", new Type[1] { typeof(LocalTargetInfo) }, (Type[])null);
					if (!(methodInfo == null) && typeof(RoyalTitlePermitWorker).IsAssignableFrom(type))
					{
						try
						{
							harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(AriandelMiliraImperium_Compat), "Prefix_OrderForceTarget", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
			}
		}
		Type type2 = Resolve("MiliraImperium.PortableMiliraImperiumConsole");
		if (type2 != null)
		{
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type2, "GetWornGizmos", (Type[])null, (Type[])null);
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, new HarmonyMethod(typeof(AriandelMiliraImperium_Compat), "Debug_ConsoleGizmos", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	public static bool Prefix_OrderForceTarget(object __instance, LocalTargetInfo target)
	{
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return true;
		}
		RoyalTitlePermitWorker val = (RoyalTitlePermitWorker)((__instance is RoyalTitlePermitWorker) ? __instance : null);
		if (val?.def == null)
		{
			return true;
		}
		Pawn value = Traverse.Create((object)val).Field("caller").GetValue<Pawn>();
		if (value == null || ((Thing)value).MapHeld == null)
		{
			return true;
		}
		bool value2 = Traverse.Create((object)val).Field("free").GetValue<bool>();
		SyncedPermitOrderForceTarget(val.def, value, target, Find.TickManager.TicksGame, value2);
		return false;
	}

	public static void Debug_ConsoleGizmos(ref IEnumerable<Gizmo> __result, Apparel __instance)
	{
		if (__result != null && !(_dialogType == null))
		{
			__result = WrapConsoleGizmosInner(__result.ToList(), __instance);
		}
	}

	public static IEnumerable<Gizmo> WrapConsoleGizmosInner(IEnumerable<Gizmo> source, Apparel console)
	{
		foreach (Gizmo g in source)
		{
			Command_Action cmd = (Command_Action)(object)((g is Command_Action) ? g : null);
			if (cmd != null)
			{
				cmd.action = delegate
				{
					try
					{
						Pawn wearer = console.Wearer;
						if (wearer != null)
						{
							object obj = AccessTools.DeclaredConstructor(_dialogType, new Type[1] { typeof(Pawn) }, false)?.Invoke(new object[1] { wearer });
							Window val = (Window)((obj is Window) ? obj : null);
							if (val != null)
							{
								Find.WindowStack.Add(val);
							}
						}
					}
					catch (Exception arg)
					{
						Log.Error($"[MiliraCompat] Console action error: {arg}");
					}
				};
			}
			yield return g;
		}
	}

	public static void SyncedPermitOrderForceTarget(RoyalTitlePermitDef def, Pawn caller, LocalTargetInfo target, int hostTick, bool isFree)
	{
		//IL_01ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Expected O, but got Unknown
		try
		{
			RoyalTitlePermitWorker val = ((def != null) ? def.Worker : null);
			if (val == null || caller == null || ((Thing)caller).MapHeld == null)
			{
				return;
			}
			Traverse val2 = Traverse.Create((object)val);
			val2.Field("caller").SetValue((object)caller);
			val2.Field("map").SetValue((object)((Thing)caller).MapHeld);
			val2.Field("instigator").SetValue((object)caller);
			Faction val3 = null;
			if (caller.royalty != null)
			{
				foreach (Faction allFaction in Find.FactionManager.AllFactions)
				{
					try
					{
						if (caller.royalty.HasPermit(def, allFaction))
						{
							val3 = allFaction;
							break;
						}
					}
					catch
					{
					}
				}
			}
			if (val3 == null && _dialogType != null)
			{
				object obj2 = _dialogType.GetMethod("GetMiliraFaction", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
				if (obj2 is Faction)
				{
					val3 = (Faction)obj2;
				}
			}
			val2.Field("faction").SetValue((object)val3);
			val2.Field("_faction").SetValue((object)val3);
			val2.Field("calledFaction").SetValue((object)val3);
			val2.Field("free").SetValue((object)isFree);
			val2.Field("biocodeChance").SetValue((object)1f);
			AccessTools.DeclaredMethod(((object)val).GetType(), "OrderForceTarget", new Type[1] { typeof(LocalTargetInfo) }, (Type[])null)?.Invoke(val, new object[1] { target });
			if (caller.royalty != null && val3 != null)
			{
				FactionPermit permit = caller.royalty.GetPermit(def, val3);
				if (permit != null)
				{
					AccessTools.DeclaredField(((object)permit).GetType(), "lastUsedTick")?.SetValue(permit, hostTick);
				}
			}
		}
		catch (Exception arg)
		{
			Log.Error($"[MiliraCompat] SyncedPermitOrderForceTarget error: {arg}");
		}
	}

	private static Type Resolve(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveType(fullName, "AriandelMiliraImperium_Compat");
	}

	private static void TryReg(MethodInfo m, string label = "")
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(m);
	}

	private static void TryReg(Type type, string method)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(AccessTools.DeclaredMethod(type, method, (Type[])null, (Type[])null));
	}

	private static void TryReg(string typeColonMethod)
	{
		global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncMethod(AccessTools.Method(typeColonMethod, (Type[])null, (Type[])null));
	}

	private static void TryRegField(Type type, string field)
	{
		if (type != null)
		{
			global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.TryRegisterSyncField(AccessTools.DeclaredField(type, field));
		}
	}
}
