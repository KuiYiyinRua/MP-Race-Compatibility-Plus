using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Synchronizes Milira's "exit fortress mode" gizmo (Milira_ExitFortress).
/// The button invokes CompThingContainer_Milian.CancelLoad through a compiler-generated
/// Command_Action delegate, so without a sync registration only the clicking peer leaves
/// fortress mode. Registering the executor itself is safe: Multiplayer only broadcasts
/// calls made from the interface, while CompTick's deterministic auto-cancel stays local.
/// </summary>
[StaticConstructorOnStartup]
public static class MiliraFortressExitMp
{
    private const string LogTag = "[MP-MeowOnlineShop][MiliraFortressExit]";
    private const string CompTypeName = "Milira.CompThingContainer_Milian";
    private const string CancelLoadName = "CancelLoad";

    static MiliraFortressExitMp()
    {
        if (MiliraMpCompatGate.ReferenceModActive)
        {
            Log.Message(LogTag + " skipped: usamiseika.fixmod.miliramultiplayer is active.");
            return;
        }

        if (!MP.enabled || !ModsConfig.IsActive("Ancot.MiliraRace"))
            return;

        try
        {
            Type compType = CompatUtility.ResolveTypeSilent(CompTypeName);
            if (compType == null)
            {
                Log.Warning(LogTag + " failed: " + CompTypeName + " not found.");
                return;
            }

            MethodInfo cancelLoad = AccessTools.DeclaredMethod(
                compType,
                CancelLoadName,
                Type.EmptyTypes,
                null);
            if (cancelLoad == null)
            {
                Log.Warning(LogTag + " failed: " + CompTypeName + "::" + CancelLoadName + " not found.");
                return;
            }

            MP.RegisterSyncMethod(cancelLoad, null);
            Log.Message(LogTag + " active: " + CompTypeName + "::" + CancelLoadName + " registered.");
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " init failed: " + e);
        }
    }
}
