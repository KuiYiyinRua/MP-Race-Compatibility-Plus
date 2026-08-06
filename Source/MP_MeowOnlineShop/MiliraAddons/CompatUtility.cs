using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class CompatUtility
{
	private const string RandLogPrefix = "[MiliraMP]";

	private static int _detRandCounter;

	public static bool ShouldSync => MP.IsInMultiplayer && !MP.IsExecutingSyncCommand;

	public static bool ShouldNotSync => !MP.IsInMultiplayer || MP.IsExecutingSyncCommand;

	public static Type ResolveType(string fullName, string caller = null)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type type = assembly.GetType(fullName);
			if (type != null)
			{
				return type;
			}
		}
		if (caller != null)
		{
			Log.Warning("[" + caller + "] Type not found: " + fullName);
		}
		return null;
	}

	public static Type ResolveTypeSilent(string fullName)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type type = assembly.GetType(fullName);
			if (type != null)
			{
				return type;
			}
		}
		return null;
	}

	public static void TryRegisterSyncMethod(MethodInfo m)
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

	public static void TryRegisterSyncField(FieldInfo f)
	{
		if (f == null)
		{
			return;
		}
		try
		{
			MP.RegisterSyncField(f);
		}
		catch
		{
		}
	}

	public static void TryRegisterSyncMethod(Type type, string methodName, params Type[] paramTypes)
	{
		if (!(type == null))
		{
			MethodInfo m = AccessTools.DeclaredMethod(type, methodName, paramTypes, (Type[])null);
			TryRegisterSyncMethod(m);
		}
	}

	public static void TryRegisterSyncField(Type type, string fieldName)
	{
		if (!(type == null))
		{
			FieldInfo f = AccessTools.DeclaredField(type, fieldName);
			TryRegisterSyncField(f);
		}
	}

	public static FieldInfo RegField(Type type, string fieldName)
	{
		if (type == null)
		{
			return null;
		}
		FieldInfo fieldInfo = AccessTools.DeclaredField(type, fieldName);
		TryRegisterSyncField(fieldInfo);
		return fieldInfo;
	}

	public static void RegField(FieldInfo f)
	{
		if (f != null)
		{
			TryRegisterSyncField(f);
		}
	}

	public static void RegMethod(MethodInfo m)
	{
		if (m != null)
		{
			TryRegisterSyncMethod(m);
		}
	}

	public static void PushDetRand_Thing(Thing thing)
	{
		if (!MP.IsInMultiplayer || thing == null)
		{
			return;
		}
		try
		{
			Rand.PushState(thing.thingIDNumber ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_Thing error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_ThingComp(ThingComp comp)
	{
		if (!MP.IsInMultiplayer || comp?.parent == null)
		{
			return;
		}
		try
		{
			Rand.PushState(((Thing)comp.parent).thingIDNumber ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_ThingComp error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_GameCondition(GameCondition cond)
	{
		if (!MP.IsInMultiplayer || cond == null)
		{
			return;
		}
		try
		{
			Rand.PushState(((object)cond).GetHashCode() ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_GameCondition error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_CompAbility(CompAbilityEffect effect)
	{
		if (!MP.IsInMultiplayer || (effect as AbilityComp)?.parent?.pawn == null)
		{
			return;
		}
		try
		{
			Rand.PushState(((Thing)((AbilityComp)effect).parent.pawn).thingIDNumber ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_CompAbility error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_Tick()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		try
		{
			Rand.PushState(Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_Tick error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_TickCounter()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		try
		{
			Rand.PushState(Find.TickManager.TicksGame ^ ++_detRandCounter);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_TickCounter error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_Pawn(Pawn __instance)
	{
		if (!MP.IsInMultiplayer || __instance == null)
		{
			return;
		}
		try
		{
			Rand.PushState(((Thing)__instance).thingIDNumber ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_Pawn error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_Map(Map map)
	{
		if (!MP.IsInMultiplayer || map == null)
		{
			return;
		}
		try
		{
			Rand.PushState(map.uniqueID ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_Map error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_MapComponent(MapComponent mc)
	{
		if (!MP.IsInMultiplayer || mc?.map == null)
		{
			return;
		}
		try
		{
			Rand.PushState(mc.map.uniqueID ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_MapComponent error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_PawnTracker(object tracker)
	{
		if (!MP.IsInMultiplayer || tracker == null)
		{
			return;
		}
		try
		{
			object obj = AccessTools.Field(tracker.GetType(), "pawn")?.GetValue(tracker);
			int num = (obj as Pawn)?.thingIDNumber ?? tracker.GetHashCode();
			Rand.PushState(num ^ Find.TickManager.TicksGame);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_PawnTracker error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PushDetRand_Seed(int seed)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		try
		{
			Rand.PushState(seed);
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushDetRand_Seed error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PopDetRand()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		try
		{
			Rand.PopState();
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PopDetRand error: {1}", "[MiliraMP]", arg));
		}
	}

	public static Exception PopDetRandFinalizer(Exception __exception)
	{
		if (MP.IsInMultiplayer)
		{
			try
			{
				Rand.PopState();
			}
			catch (Exception arg)
			{
				Log.Error(string.Format("{0} PopDetRandFinalizer error: {1}", "[MiliraMP]", arg));
			}
		}
		return __exception;
	}

	public static void PushRand()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		try
		{
			Rand.PushState();
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PushRand error: {1}", "[MiliraMP]", arg));
		}
	}

	public static void PopRand()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		try
		{
			Rand.PopState();
		}
		catch (Exception arg)
		{
			Log.Error(string.Format("{0} PopRand error: {1}", "[MiliraMP]", arg));
		}
	}

	public static Exception PopRandFinalizer(Exception __exception)
	{
		if (MP.IsInMultiplayer)
		{
			try
			{
				Rand.PopState();
			}
			catch (Exception arg)
			{
				Log.Error(string.Format("{0} PopRandFinalizer error: {1}", "[MiliraMP]", arg));
			}
		}
		return __exception;
	}

	public static bool WrapDetRandTick(Harmony harmony, string typeName)
	{
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Expected O, but got Unknown
		//IL_006c: Expected O, but got Unknown
		Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(typeName, (string)null);
		if (typeInAnyAssembly == null)
		{
			return false;
		}
		MethodInfo methodInfo = AccessTools.Method(typeInAnyAssembly, "Tick", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return false;
		}
		try
		{
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(CompatUtility), "PushDetRand_Thing", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(CompatUtility), "PopDetRandFinalizer", (Type[])null));
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool WrapDetRandCompTick(Harmony harmony, string typeName)
	{
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Expected O, but got Unknown
		//IL_006c: Expected O, but got Unknown
		Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(typeName, (string)null);
		if (typeInAnyAssembly == null)
		{
			return false;
		}
		MethodInfo methodInfo = AccessTools.Method(typeInAnyAssembly, "CompTick", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return false;
		}
		try
		{
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(CompatUtility), "PushDetRand_ThingComp", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(CompatUtility), "PopDetRandFinalizer", (Type[])null));
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool WrapDetRandConditionTick(Harmony harmony, string typeName)
	{
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Expected O, but got Unknown
		//IL_006c: Expected O, but got Unknown
		Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(typeName, (string)null);
		if (typeInAnyAssembly == null)
		{
			return false;
		}
		MethodInfo methodInfo = AccessTools.Method(typeInAnyAssembly, "GameConditionTick", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return false;
		}
		try
		{
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(CompatUtility), "PushDetRand_GameCondition", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(CompatUtility), "PopDetRandFinalizer", (Type[])null));
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool WrapDetRandStatic(Harmony harmony, string typeName, string methodName)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_0068: Expected O, but got Unknown
		Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(typeName, (string)null);
		if (typeInAnyAssembly == null)
		{
			return false;
		}
		MethodInfo methodInfo = AccessTools.Method(typeInAnyAssembly, methodName, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return false;
		}
		try
		{
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(CompatUtility), "PushDetRand_Tick", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(CompatUtility), "PopDetRandFinalizer", (Type[])null));
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool PatchMethod(Harmony harmony, string typeColonMethod, string prefix, Type patchType)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		MethodInfo methodInfo = AccessTools.Method(typeColonMethod, (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(patchType, prefix, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	public static bool PatchMethod(Harmony harmony, Type type, string methodName, string prefix, Type patchType, Type[] paramTypes = null)
	{
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Expected O, but got Unknown
		if (type == null)
		{
			return false;
		}
		MethodInfo methodInfo = ((paramTypes != null) ? AccessTools.DeclaredMethod(type, methodName, paramTypes, (Type[])null) : AccessTools.DeclaredMethod(type, methodName, (Type[])null, (Type[])null));
		if (methodInfo == null)
		{
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(patchType, prefix, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	public static bool PatchMethodPostfix(Harmony harmony, Type type, string methodName, string postfix, Type patchType, Type[] paramTypes = null)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Expected O, but got Unknown
		if (type == null)
		{
			return false;
		}
		MethodInfo methodInfo = ((paramTypes != null) ? AccessTools.DeclaredMethod(type, methodName, paramTypes, (Type[])null) : AccessTools.DeclaredMethod(type, methodName, (Type[])null, (Type[])null));
		if (methodInfo == null)
		{
			return false;
		}
		harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(patchType, postfix, (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		return true;
	}

	public static IEnumerable<Gizmo> WrapGizmoList(IEnumerable<Gizmo> source, Action<bool> onToggleChanged = null, Action onActionBefore = null, Action onActionAfter = null, Type gizmoActionAndToggleType = null, string toggleActionFieldName = "toggleAction", string mainActionFieldName = "mainAction")
	{
		foreach (Gizmo g in source)
		{
			Gizmo obj = g;
			Command_Toggle toggle = (Command_Toggle)(object)((obj is Command_Toggle) ? obj : null);
			if (toggle != null && onToggleChanged != null)
			{
				Action orig = toggle.toggleAction;
				_ = toggle.isActive;
				toggle.toggleAction = delegate
				{
					onActionBefore?.Invoke();
					orig?.Invoke();
					bool obj2 = toggle.isActive();
					onActionAfter?.Invoke();
					onToggleChanged(obj2);
				};
			}
			else if (gizmoActionAndToggleType != null && gizmoActionAndToggleType.IsInstanceOfType(g))
			{
				FieldInfo fiToggle = AccessTools.DeclaredField(gizmoActionAndToggleType, toggleActionFieldName);
				FieldInfo fiMain = AccessTools.DeclaredField(gizmoActionAndToggleType, mainActionFieldName);
				if (fiToggle != null)
				{
					Action origToggle = fiToggle.GetValue(g) as Action;
					if (origToggle != null)
					{
						fiToggle.SetValue(g, (Action)delegate
						{
							onActionBefore?.Invoke();
							origToggle();
							onActionAfter?.Invoke();
							Gizmo obj2 = g;
							Command_Toggle val = (Command_Toggle)(object)((obj2 is Command_Toggle) ? obj2 : null);
							if (val != null)
							{
								onToggleChanged?.Invoke(val.isActive());
							}
						});
					}
				}
				if (fiMain != null)
				{
					Action origMain = fiMain.GetValue(g) as Action;
					if (origMain != null)
					{
						fiMain.SetValue(g, (Action)delegate
						{
							onActionBefore?.Invoke();
							origMain();
							onActionAfter?.Invoke();
						});
					}
				}
			}
			yield return g;
		}
	}

	public static IEnumerable<Gizmo> WrapSingleToggle(IEnumerable<Gizmo> source, Action<bool> syncAction)
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
					if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand)
					{
						syncAction(toggle.isActive());
					}
				};
			}
			yield return g;
		}
	}

	public static T FindCompByMapAndID<T>(int mapIndex, int thingID) where T : ThingComp
	{
		if (Find.Maps == null)
		{
			return default(T);
		}
		foreach (Map map in Find.Maps)
		{
			if (map.Index != mapIndex)
			{
				continue;
			}
			foreach (Thing allThing in map.listerThings.AllThings)
			{
				if (allThing.thingIDNumber == thingID)
				{
					return ThingCompUtility.TryGetComp<T>(allThing);
				}
			}
		}
		return default(T);
	}

	public static Thing FindThingByMapAndID(int mapIndex, int thingID)
	{
		if (Find.Maps == null)
		{
			return null;
		}
		foreach (Map map in Find.Maps)
		{
			if (map.Index != mapIndex)
			{
				continue;
			}
			foreach (Thing allThing in map.listerThings.AllThings)
			{
				if (allThing.thingIDNumber == thingID)
				{
					return allThing;
				}
			}
		}
		return null;
	}

	public static void WrapTopFloatMenuOptions(Func<Action, Action> wrapAction)
	{
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Expected O, but got Unknown
		if (!(AccessTools.Field(typeof(WindowStack), "windows")?.GetValue(Find.WindowStack) is IList { Count: not 0 } list))
		{
			return;
		}
		object obj = list[list.Count - 1];
		FloatMenu val = (FloatMenu)((obj is FloatMenu) ? obj : null);
		if (val == null || !(AccessTools.Field(typeof(FloatMenu), "options")?.GetValue(val) is IList list2))
		{
			return;
		}
		foreach (FloatMenuOption item in list2)
		{
			FloatMenuOption val2 = item;
			if (val2.action != null)
			{
				val2.action = wrapAction(val2.action);
			}
		}
	}

	public static void SyncBoolField(ThingComp target, string fieldName, bool value)
	{
		if (target != null)
		{
			AccessTools.Field(((object)target).GetType(), fieldName)?.SetValue(target, value);
		}
	}

	public static void SyncIntField(object target, string fieldName, int value)
	{
		if (target != null)
		{
			AccessTools.Field(target.GetType(), fieldName)?.SetValue(target, value);
		}
	}

	public static void DebugLog(string caller, string message)
	{
		if (MP.IsInMultiplayer)
		{
			Log.Message("[" + caller + "] " + message);
		}
	}
}
