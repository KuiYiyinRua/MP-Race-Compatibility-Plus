using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class TaleOfMilira_Compat
{
	static TaleOfMilira_Compat()
	{
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira addon module skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		if (!MP.enabled)
		{
			return;
		}
		string[] array = new string[10]
		{
			"TaleOfMilira_TheBattlefield", "TaleOfMilira_TheBattlefield_m", "TaleOfMilira_TheChurchAndMilira", "TaleOfMilira_TheChurchAndMilira_m", "TaleOfMilira_TheDestroyOutpost", "TaleOfMilira_TheDestroyOutpost_m", "TaleOfMilira_TheMiliraSOSSignal", "TaleOfMilira_TheMiliraSOSSignal_m", "TaleOfMilira_TheRuinOutpost",
			"TaleOfMilira_TheRuinOutpost_m"
		};
		int num = 0;
		string[] array2 = array;
		foreach (string text in array2)
		{
			Type type = AccessTools.TypeByName("TheTaleofMilira." + text);
			if (type == null)
			{
				continue;
			}
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "Notify_CaravanArrived", new Type[1] { typeof(Caravan) }, (Type[])null);
			if (!(methodInfo == null))
			{
				try
				{
					MP.RegisterSyncDialogNodeTree(methodInfo);
					num++;
				}
				catch (Exception ex)
				{
					Log.Warning("[TaleOfMilira_Compat] failed to register " + text + ": " + ex.Message);
				}
			}
		}
		if (num > 0)
		{
			Log.Message($"[TaleOfMilira_Compat] registered {num} dialog sync methods");
		}
	}
}
