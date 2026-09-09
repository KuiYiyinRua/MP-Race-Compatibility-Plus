using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// MiliraImperium's ship-launch countdown is wall-clock based
/// (<c>Time.deltaTime</c>), which lets the two peers finish at different
/// ticks and then diverge while destroying the ship / launching colonists.
/// In multiplayer this patch replaces the wall-clock countdown with a shared
/// <c>TicksGame</c> deadline. Singleplayer keeps the original behavior.
/// </summary>
[StaticConstructorOnStartup]
public static class Patch_MiliraShipCountdownMp
{
	private const string HarmonyId = "mp.meowonlineshop.miliraimperium.shipcountdown";
	private const int CountdownTicks = 432;

	private static readonly Harmony harmony;
	private static Type shipCountdownType;
	private static FieldInfo timeLeftField;
	private static MethodInfo countdownEnded;
	private static int mpDeadlineTick = -1;

	static Patch_MiliraShipCountdownMp()
	{
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
			Log.Warning("[MP-MeowOnlineShop] MiliraImperium ship countdown MP patch failed - " + arg);
		}
	}

	private static void Initialize()
	{
		shipCountdownType = AccessTools.TypeByName("MiliraImperium.ShipCountdown");
		if (shipCountdownType == null)
		{
			return;
		}
		timeLeftField = AccessTools.DeclaredField(shipCountdownType, "timeLeft");
		countdownEnded = AccessTools.DeclaredMethod(shipCountdownType, "CountdownEnded", Type.EmptyTypes, null);
		if (timeLeftField == null || countdownEnded == null)
		{
			return;
		}

		harmony.Patch(
			AccessTools.DeclaredMethod(shipCountdownType, "InitiateCountdown", new Type[1] { typeof(Building) }, null),
			postfix: new HarmonyMethod(typeof(Patch_MiliraShipCountdownMp), "InitiatePostfix"));
		harmony.Patch(
			AccessTools.DeclaredMethod(shipCountdownType, "InitiateCountdown", new Type[1] { typeof(string) }, null),
			postfix: new HarmonyMethod(typeof(Patch_MiliraShipCountdownMp), "InitiatePostfix"));
		harmony.Patch(
			AccessTools.DeclaredMethod(shipCountdownType, "CancelCountdown", Type.EmptyTypes, null),
			postfix: new HarmonyMethod(typeof(Patch_MiliraShipCountdownMp), "CancelPostfix"));
		harmony.Patch(
			AccessTools.DeclaredMethod(shipCountdownType, "ShipCountdownUpdate", Type.EmptyTypes, null),
			prefix: new HarmonyMethod(typeof(Patch_MiliraShipCountdownMp), "UpdatePrefix"));

		Log.Message("[MP-MeowOnlineShop] MiliraImperium ship countdown MP tick sync active.");
	}

	public static void InitiatePostfix()
	{
		if (MP.IsInMultiplayer)
		{
			mpDeadlineTick = (Find.TickManager?.TicksGame ?? 0) + CountdownTicks;
		}
	}

	public static void CancelPostfix()
	{
		mpDeadlineTick = -1;
	}

	public static bool UpdatePrefix()
	{
		if (!MP.IsInMultiplayer || mpDeadlineTick < 0 || timeLeftField == null || countdownEnded == null)
		{
			return true;
		}
		try
		{
			int now = Find.TickManager?.TicksGame ?? 0;
			float remaining = Math.Max(0f, (mpDeadlineTick - now) / 60f);
			timeLeftField.SetValue(null, remaining);
			if (remaining <= 0f)
			{
				mpDeadlineTick = -1;
				timeLeftField.SetValue(null, -1000f);
				countdownEnded.Invoke(null, null);
				return false;
			}
			return false;
		}
		catch
		{
			return true;
		}
	}
}
