using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class MiliraXian_NeiyuLaw_WeaponSwitch
{
	private static readonly Harmony harmony;

	private static bool isInitialized;

	private static Type modeSwitchCompType;

	private static Type switchCtxType;

	private static MethodInfo trySwitchMethod;

	static MiliraXian_NeiyuLaw_WeaponSwitch()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected O, but got Unknown
		harmony = new Harmony("MiliraXian.NeiyuLaw.WeaponSwitch");
		if (!MP.enabled)
		{
			Log.Message("[MiliraXian_NeiyuLaw_WeaponSwitch] MP not enabled, skipping");
			return;
		}
		if (!ModsConfig.IsActive("HeChuanRiver.MiliraXian.NeiyuLaw"))
		{
			Log.Message("[MiliraXian_NeiyuLaw_WeaponSwitch] Mod not installed, skipping");
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error($"MiliraXian_NeiyuLaw_WeaponSwitch: init failed - {arg}");
		}
	}

	private static void Initialize()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			modeSwitchCompType = AccessTools.TypeByName("MiliraXian.Characters.Neiyu.Comp_ModeSwitchWeapon");
			switchCtxType = AccessTools.TypeByName("MiliraXian.Characters.Neiyu.Comp_ModeSwitchWeapon+SwitchContext");
			trySwitchMethod = ((modeSwitchCompType != null) ? AccessTools.Method(modeSwitchCompType, "TrySwitchTo", (Type[])null, (Type[])null) : null);
			if (trySwitchMethod == null || switchCtxType == null)
			{
				Log.Warning("MiliraXian_NeiyuLaw_WeaponSwitch: types not found, skipping");
				return;
			}
			RegisterSwitchContextSyncWorker();
			MP.RegisterSyncMethod(trySwitchMethod, (SyncType[])null);
			Log.Message("MiliraXian_NeiyuLaw_WeaponSwitch: OK");
		}
	}

	private static void RegisterSwitchContextSyncWorker()
	{
		Type delegateType = typeof(SyncWorkerDelegate<>).MakeGenericType(switchCtxType);
		DynamicMethod dynamicMethod = new DynamicMethod("DynamicSyncSwitchCtx", typeof(void), new Type[2]
		{
			typeof(SyncWorker),
			switchCtxType.MakeByRefType()
		}, typeof(MiliraXian_NeiyuLaw_WeaponSwitch).Module);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		LocalBuilder local = iLGenerator.DeclareLocal(typeof(object));
		MethodInfo method = typeof(MiliraXian_NeiyuLaw_WeaponSwitch).GetMethod("SyncSwitchContextHelper", BindingFlags.Static | BindingFlags.NonPublic);
		iLGenerator.Emit(OpCodes.Ldarg_1);
		iLGenerator.Emit(OpCodes.Ldobj, switchCtxType);
		iLGenerator.Emit(OpCodes.Box, switchCtxType);
		iLGenerator.Emit(OpCodes.Stloc, local);
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Ldloca, local);
		iLGenerator.Emit(OpCodes.Call, method);
		iLGenerator.Emit(OpCodes.Ldarg_1);
		iLGenerator.Emit(OpCodes.Ldloc, local);
		iLGenerator.Emit(OpCodes.Unbox, switchCtxType);
		iLGenerator.Emit(OpCodes.Ldobj, switchCtxType);
		iLGenerator.Emit(OpCodes.Stobj, switchCtxType);
		iLGenerator.Emit(OpCodes.Ret);
		Delegate obj = dynamicMethod.CreateDelegate(delegateType);
		MethodInfo methodInfo = typeof(MP).GetMethods(BindingFlags.Static | BindingFlags.Public).First((MethodInfo m) => m.Name == "RegisterSyncWorker" && m.IsGenericMethod && m.GetParameters().Length == 4);
		methodInfo.MakeGenericMethod(switchCtxType).Invoke(null, new object[4] { obj, switchCtxType, false, false });
	}

	private static void SyncSwitchContextHelper(SyncWorker sync, ref object boxed)
	{
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Expected O, but got Unknown
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		if (sync.isWriting)
		{
			Pawn val = (Pawn)switchCtxType.GetField("Pawn").GetValue(boxed);
			sync.Write<Pawn>(val);
			ThingWithComps val2 = (ThingWithComps)switchCtxType.GetField("SourceThing").GetValue(boxed);
			sync.Write<ThingWithComps>(val2);
			int num = (int)switchCtxType.GetField("Location").GetValue(boxed);
			sync.Write<int>(num);
			int num2 = (int)switchCtxType.GetField("CurrentIndex").GetValue(boxed);
			sync.Write<int>(num2);
			return;
		}
		boxed = Activator.CreateInstance(switchCtxType);
		switchCtxType.GetField("Pawn").SetValue(boxed, sync.Read<Pawn>());
		switchCtxType.GetField("SourceThing").SetValue(boxed, sync.Read<ThingWithComps>());
		switchCtxType.GetField("Location").SetValue(boxed, sync.Read<int>());
		switchCtxType.GetField("CurrentIndex").SetValue(boxed, sync.Read<int>());
		ThingWithComps val3 = (ThingWithComps)switchCtxType.GetField("SourceThing").GetValue(boxed);
		if (((val3 != null) ? val3.AllComps : null) == null)
		{
			return;
		}
		foreach (ThingComp allComp in val3.AllComps)
		{
			if (modeSwitchCompType.IsInstanceOfType(allComp))
			{
				switchCtxType.GetField("Comp").SetValue(boxed, allComp);
				break;
			}
		}
	}
}
