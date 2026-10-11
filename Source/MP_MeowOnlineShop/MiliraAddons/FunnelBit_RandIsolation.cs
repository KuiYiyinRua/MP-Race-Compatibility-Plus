using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using MP_MeowOnlineShop.MiliraAddonCompat;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class FunnelBit_RandIsolation
{
	private enum DetRandType
	{
		Thing,
		Tick
	}

	private const string NS = "FunnelBit.";

	static FunnelBit_RandIsolation()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		if (!MP.enabled || !ModsConfig.IsActive("rabiosus.funnelmilian"))
		{
			return;
		}
		try
		{
			PatchAll();
		}
		catch (Exception arg)
		{
			Log.Error($"[FunnelBit_RandIsolation] init failed: {arg}");
		}
	}

	private static void PatchAll()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected O, but got Unknown
		Harmony harmony = new Harmony("FunnelBit.RandIsolation");
		int num = 0;
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick", DetRandType.Thing);
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick_Attacking", DetRandType.Thing);
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick_MeleeAttacking", DetRandType.Thing);
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick_Launching", DetRandType.Thing);
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick_MovingToAttackCell", DetRandType.Thing);
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick_PostAttackWaiting", DetRandType.Thing);
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick_Returning", DetRandType.Thing);
		num += PatchDetRand(harmony, "FunnelBit.Projectile_FunnelBit", "Tick_SpecialAction", DetRandType.Thing);
		num += PatchCompTick(harmony);
		num += PatchChargingIteration(harmony);
		num += PatchDetRand(harmony, "FunnelBit.GameComponent_FunnelBit", "GameComponentTick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.FunnelBitMoveWorker_DragoonEx", "Tick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.FunnelBitMoveWorker_MeleeChargeEx", "Tick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.FunnelBitMoveWorker_DragoonBasic", "Tick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.FunnelBitTacticsWorker_OrbitEnemy", "Tick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.DroneWorkWorker", "Tick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.DroneWorkMover", "Tick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.DroneWorkMoveWorker_SmoothDampPath", "Tick", DetRandType.Tick);
		num += PatchDetRand(harmony, "FunnelBit.FunnelBitAttackWorker_Beam", "Tick", DetRandType.Tick);
		num += PatchPushPopRand(harmony, "FunnelBit.FunnelBitEffectHandler", "Tick");
		num += PatchPushPopRand(harmony, "FunnelBit.FunnelBitTrailRenderer", "Tick");
		Log.Message($"[FunnelBit_RandIsolation] patched {num} targets");
	}

	public static void PushThing(Thing __instance) => CompatUtility.PushDetRand_Thing(__instance);

	public static void PushComp(ThingComp __instance) => CompatUtility.PushDetRand_ThingComp(__instance);

	// LaunchSlotForAbility can add a slot to the list currently being ticked.
	// Keep the original order and visit each slot present at the start of this tick once.
	public static List<T>.Enumerator ChargingSlots<T>(List<T> slots)
	{
		return MP.IsInMultiplayer ? new List<T>(slots).GetEnumerator() : slots.GetEnumerator();
	}

	public static IEnumerable<CodeInstruction> SnapshotChargingSlots(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo method && method.Name == "GetEnumerator" &&
				method.DeclaringType != null && method.DeclaringType.IsGenericType &&
				method.DeclaringType.GetGenericTypeDefinition() == typeof(List<>) &&
				method.DeclaringType.GetGenericArguments()[0].FullName == "FunnelBit.FunnelBitSlot")
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = typeof(FunnelBit_RandIsolation)
					.GetMethod(nameof(ChargingSlots)).MakeGenericMethod(method.DeclaringType.GetGenericArguments());
				replaced++;
			}
			yield return instruction;
		}
		if (replaced != 2)
			throw new InvalidOperationException("FunnelBit.TickCharging expected two slot-list enumerators, found " + replaced);
	}

	private static int PatchChargingIteration(Harmony harmony)
	{
		Type type = ResolveType("FunnelBit.FunnelBitManager");
		MethodInfo method = type == null ? null : AccessTools.DeclaredMethod(type, "TickCharging", new[] { typeof(int) });
		if (method == null)
		{
			Log.Error("[FunnelBit_RandIsolation] REQUIRED_TARGET_FAILED TickCharging(int)");
			return 0;
		}
		try
		{
			harmony.Patch(method, transpiler: new HarmonyMethod(typeof(FunnelBit_RandIsolation), nameof(SnapshotChargingSlots)));
			Log.Message("[FunnelBit_RandIsolation] REQUIRED_TARGETS_OK TickCharging slot snapshots");
			return 1;
		}
		catch (Exception error)
		{
			Log.Error("[FunnelBit_RandIsolation] REQUIRED_TARGET_FAILED TickCharging slot snapshots: " + error);
			return 0;
		}
	}

	private static int PatchCompTick(Harmony harmony)
	{
		Type type = ResolveType("FunnelBit.CompTurretGun_FunnelBit");
		MethodInfo method = type == null ? null : AccessTools.DeclaredMethod(type, "CompTick", Type.EmptyTypes);
		if (method == null)
		{
			Log.Error("[FunnelBit_RandIsolation] required CompTurretGun_FunnelBit.CompTick missing");
			return 0;
		}
		try
		{
			harmony.Patch(method, new HarmonyMethod(typeof(FunnelBit_RandIsolation), nameof(PushComp)),
				null, null, new HarmonyMethod(typeof(CompatUtility), nameof(CompatUtility.PopDetRandFinalizer)));
			return 1;
		}
		catch (Exception error)
		{
			Log.Error("[FunnelBit_RandIsolation] CompTick patch failed: " + error);
			return 0;
		}
	}

	private static int PatchDetRand(Harmony harmony, string typeName, string methodName, DetRandType randType)
	{
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Expected O, but got Unknown
		//IL_0103: Expected O, but got Unknown
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected O, but got Unknown
		//IL_00cd: Expected O, but got Unknown
		Type type = ResolveType(typeName);
		if (type == null)
		{
			return 0;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, methodName, Type.EmptyTypes, (Type[])null);
		if (methodInfo == null)
		{
			methodInfo = AccessTools.DeclaredMethod(type, methodName, new Type[1] { typeof(int) }, (Type[])null);
		}
		if (methodInfo == null)
		{
			methodInfo = AccessTools.DeclaredMethod(type, methodName, new Type[1] { ResolveType("FunnelBit.Projectile_FunnelBit") }, (Type[])null);
		}
		if (methodInfo == null)
		{
			return 0;
		}
		try
		{
			if (randType == DetRandType.Thing)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(FunnelBit_RandIsolation), nameof(PushThing)), null, null, new HarmonyMethod(typeof(CompatUtility), nameof(CompatUtility.PopDetRandFinalizer)));
			}
			else
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushDetRand_Tick", (Type[])null), null, null, new HarmonyMethod(typeof(CompatUtility), nameof(CompatUtility.PopDetRandFinalizer)));
			}
			return 1;
		}
		catch
		{
			return 0;
		}
	}

	private static int PatchPushPopRand(Harmony harmony, string typeName, string methodName)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Expected O, but got Unknown
		//IL_006b: Expected O, but got Unknown
		Type type = ResolveType(typeName);
		if (type == null)
		{
			return 0;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, methodName, Type.EmptyTypes, (Type[])null);
		if (methodInfo == null)
		{
			return 0;
		}
		try
		{
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PushRand", (Type[])null), new HarmonyMethod(typeof(global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility), "PopRand", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
			return 1;
		}
		catch
		{
			return 0;
		}
	}

	private static Type ResolveType(string fullName)
	{
		return global::MP_MeowOnlineShop.MiliraAddonCompat.CompatUtility.ResolveTypeSilent(fullName);
	}
}
