using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_DebugActions
{
    static PLAMilira_DebugActions()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                string[,] targets = {
                    { "CompDelayedMiliraSpawnOnWakeup", "Spawn" },
                    { "CompDelayedPawnSpawnFlag", "Spawn" },
                    { "CompEnemyMilianTowerSpawnOnWakeup", "Spawn" },
                    { "CompEnemyMilianTower_Kill", "TryKillAndDrop" },
                    { "CompEnemyNPCSpawner", "TrySpawn" }
                };
                for (int i = 0; i < targets.GetLength(0); i++)
                {
                    var type = AccessTools.TypeByName("PLAMilira." + targets[i, 0]) ?? throw new TypeLoadException(targets[i, 0]);
                    var method = AccessTools.DeclaredMethod(type, targets[i, 1], Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, targets[i, 1]);
                    if (method.IsStatic || method.ReturnType != typeof(void)) throw new InvalidOperationException("Unexpected developer action: " + method);
                    MP.RegisterSyncMethod(method).SetDebugOnly();
                }
                Log.Message("[PLAMilira_DebugActions] REQUIRED_TARGETS_OK five native developer spawn/kill commands");
            }
            catch (Exception e) { Log.Error("[PLAMilira_DebugActions] REQUIRED_TARGET_FAILED " + e); }
        });
    }
}
