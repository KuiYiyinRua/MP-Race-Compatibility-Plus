using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// [RH2] Rimmu-Nation Security's remote-trigger gizmo calls
    /// CompProjectileSprayer.Fire(), which consumes Rand, spawns/launches
    /// projectiles and sets the saved `fired` flag. Multiplayer already syncs
    /// CompExplosive.StartWick, so only Fire() needs registration here.
    /// </summary>
    internal static class Patch_RimmuNationSecurityMp
    {
        private const string SprayerTypeName = "ChickenExplosives.CompProjectileSprayer";

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type sprayerType = AccessTools.TypeByName(SprayerTypeName);
                MethodInfo fire = sprayerType == null
                    ? null
                    : AccessTools.Method(sprayerType, "Fire", Type.EmptyTypes);
                if (fire == null)
                {
                    Log.Message("[MP-MeowOnlineShop] RimmuNation Security sync skipped (target type not resolved).");
                    return;
                }

                MP.RegisterSyncMethod(fire, null);
                Log.Message("[MP-MeowOnlineShop] RimmuNation Security MP patch active: CompProjectileSprayer.Fire synced.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] RimmuNation Security MP compat restore failed: " + e.Message);
            }
        }
    }
}
