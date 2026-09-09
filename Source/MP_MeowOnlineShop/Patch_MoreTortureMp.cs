using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// COF More Torture (`cof.moretorture`) exposes gizmos on torture-bed
    /// hediffs and buildings. The stable executors are registered here:
    /// StartProgress/StopProgress, stage up/down, and bed victim/progress
    /// methods. Multiplayer only broadcasts in interface context, so the
    /// deterministic CompPostTick auto-start path stays local.
    /// </summary>
    internal static class Patch_MoreTortureMp
    {
        private const string PackageId = "cof.moretorture";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            int registered = 0;
            Type indicator = AccessTools.TypeByName("COF_Torture.HediffComp.HediffComp_ExecuteIndicator");
            registered += TryRegister(indicator, "StartProgress", Type.EmptyTypes);
            registered += TryRegister(indicator, "StopProgress", Type.EmptyTypes);

            Type switchable = AccessTools.TypeByName("COF_Torture.HediffComp.HediffComp_SwitchAbleSeverity");
            registered += TryRegister(switchable, "upStage", Type.EmptyTypes);
            registered += TryRegister(switchable, "downStage", Type.EmptyTypes);

            Type bed = AccessTools.TypeByName("COF_Torture.Things.Building_TortureBed");
            registered += TryRegister(bed, "ReleaseVictim", Type.EmptyTypes);
            registered += TryRegister(bed, "SetVictim", new[] { typeof(Pawn) });
            registered += TryRegister(bed, "startExecuteProgress", Type.EmptyTypes);
            registered += TryRegister(bed, "stopExecuteProgress", Type.EmptyTypes);

            if (registered == 0)
                Log.Warning("[MP-MeowOnlineShop] More Torture target resolution failed; patch skipped.");
            else
                Log.Message("[MP-MeowOnlineShop] More Torture MP patch active: " + registered + " sync methods.");
        }

        private static int TryRegister(Type type, string methodName, Type[] argTypes)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName, argTypes);
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null).SetContext(SyncContext.CurrentMap);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] More Torture sync registration failed on " +
                    (type?.Name ?? "?") + "." + methodName + ": " + e.Message);
                return 0;
            }
        }
    }
}
