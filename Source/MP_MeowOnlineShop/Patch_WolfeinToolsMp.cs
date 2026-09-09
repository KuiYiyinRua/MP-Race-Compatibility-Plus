using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_WolfeinToolsMp
    {
        private static bool applied;

        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || harmony == null || !ModsConfig.IsActive("melondove.wolfeinrace"))
                return;
            applied = true;
            WolfeinSessionSettings.ApplyBase(harmony);

            // Exact instance callbacks verified against workshop 3473140562, SHA256 98090B88...
            // Keep index changes, verb rebuilding and shield charge consumption in one command.
            int count = 0;
            count += Register("Wolfein.CompToolSwitcher", "<GetWeaponGizmos>b__21_0");
            count += Register("Wolfein.CompChargeEnergyShield", "<CompGetWornGizmosExtra>b__8_0");
            count += Register("Wolfein.CompCauseHediff_ArtificialMoonApparatus", "<CompGetGizmosExtra>b__15_1");
            count += Register("Wolfein.Building_TurretGunForceAiming", "<GetGizmos>b__70_0");
            count += Register("Wolfein.Building_TurretGunForceAiming", "ExtractShell");
            count += Register("Wolfein.Building_TurretGunForceAiming", "ResetForcedTarget");
            count += Register("Wolfein.Building_TurretGunForceAiming", "OrderAttack", typeof(LocalTargetInfo));
            count += Register("Wolfein.CompThingContainer_IntegratedRepairUnit", "CancelLoad");
            // Persistent comms dialogs serialize their action and linkLateBind delegates on rejoin.
            Type serialization = AccessTools.TypeByName("Multiplayer.Client.DelegateSerialization");
            harmony.Patch(AccessTools.Method(serialization, "CheckMethodAllowed"),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinToolsMp), nameof(AllowCommsDelegate)));
            Log.Message("[MP-MeowOnlineShop] Wolfein base action targets: " + count + "/8.");
        }

        private static bool AllowCommsDelegate(MethodInfo method, ref MethodInfo __result)
        {
            Type type = method?.DeclaringType;
            if (type?.DeclaringType?.FullName != "Wolfein.FactionDialog" ||
                (!method.Name.StartsWith("<RequestDataQuest>", StringComparison.Ordinal) && !method.Name.StartsWith("<OKToRoot>", StringComparison.Ordinal))) return true;
            __result = method;
            return false;
        }

        private static int Register(string typeName, string name, params Type[] args)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null ? null : AccessTools.DeclaredMethod(type, name, args);
            if (method == null)
            {
                Log.Error("[MP-MeowOnlineShop] Required Wolfein target missing: " + typeName + "." + name);
                return 0;
            }
            MP.RegisterSyncMethod(method, null);
            return 1;
        }
    }
}
