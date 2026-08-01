using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for Ratkin Weapons+
    /// (bbb.ratkinweapon.morefailure, assembly RatkinWeapons).
    /// </summary>
    internal static class Patch_RatkinWeaponsMp
    {
        private const string BayonetCompTypeName = "RatkinWeapons.CompBayonet";
        private const string AntiTankTrapTypeName = "RatkinWeapons.Building_ATtrap";

        private static bool _applied;

        internal static void Apply()
        {
            if (_applied)
                return;

            _applied = true;

            Type bayonetType = AccessTools.TypeByName(BayonetCompTypeName);
            Type trapType = AccessTools.TypeByName(AntiTankTrapTypeName);
            if (bayonetType == null && trapType == null)
            {
                Log.Message("[MP-MeowOnlineShop] Ratkin Weapons+ MP: target assembly not active; patch skipped.");
                return;
            }

            int resolved = 0;
            const int expected = 2;

            try
            {
                MethodInfo bayonetAct = bayonetType?.GetMethod(
                    "BayonetAct",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(LocalTargetInfo) },
                    null);

                if (bayonetAct != null)
                {
                    MP.RegisterSyncMethod(bayonetAct, null).SetContext(SyncContext.MapSelected);
                    resolved++;
                    Log.Message("[MP-MeowOnlineShop] Ratkin Weapons+ MP: registered CompBayonet.BayonetAct(LocalTargetInfo).");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: CompBayonet.BayonetAct(LocalTargetInfo) not resolved.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: bayonet sync registration failed: " + e);
            }

            try
            {
                MethodInfo toggle = FindGeneratedToggleMethod(trapType, "GetGizmos");
                if (toggle != null)
                {
                    MP.RegisterSyncMethod(toggle, null).SetContext(SyncContext.MapSelected);
                    resolved++;
                    Log.Message("[MP-MeowOnlineShop] Ratkin Weapons+ MP: registered anti-tank trap auto-rearm toggle " + toggle.Name + ".");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: anti-tank trap auto-rearm toggle not resolved.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: trap toggle sync registration failed: " + e);
            }

            Log.Message($"[MP-MeowOnlineShop] Ratkin Weapons+ MP targets resolved={resolved}/{expected}.");
        }

        private static MethodInfo FindGeneratedToggleMethod(Type type, string parentMethod)
        {
            if (type == null)
                return null;

            string prefix = "<" + parentMethod + ">b__";
            return type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal)
                            && m.Name.EndsWith("_1", StringComparison.Ordinal)
                            && m.ReturnType == typeof(void)
                            && m.GetParameters().Length == 0)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }
}
