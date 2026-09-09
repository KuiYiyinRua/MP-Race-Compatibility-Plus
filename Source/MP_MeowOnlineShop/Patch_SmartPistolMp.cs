using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Smart Pistol's `Projectile_SmartBullet` lazily seeds its bezier offset
    /// inside `InitRandOffset` on first `ExactPosition` / `DrawAt` access. In
    /// multiplayer the projectile exists on every peer, but the first access
    /// can happen at different moments (tick vs render), so the two
    /// `Rand.Range` draws would diverge and permanently desync the projectile
    /// path and, depending on timing, the shared Rand stream.
    ///
    /// The narrow fix is to run `InitRandOffset` under a deterministic Rand
    /// scope keyed by the projectile's stable Thing ID + map ID, so both peers
    /// derive the same bezier offset without consuming the shared stream.
    /// </summary>
    internal static class Patch_SmartPistolMp
    {
        private const string PackageId = "rabiosus.smartpistol";
        private const string ProjectileTypeName = "RB_SmartPistol.Projectile_SmartBullet";
        private const int SeedOffset = 0x5A17;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type projectileType = AccessTools.TypeByName(ProjectileTypeName);
            MethodInfo initRandOffset = projectileType == null
                ? null
                : AccessTools.Method(projectileType, "InitRandOffset", Type.EmptyTypes);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_SmartPistolMp),
                nameof(InitRandOffsetPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_SmartPistolMp),
                nameof(InitRandOffsetFinalizer));

            if (initRandOffset == null || prefix == null || finalizer == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Smart Pistol target resolution failed; patch skipped.");
                return;
            }

            try
            {
                harmony.Patch(
                    initRandOffset,
                    prefix: new HarmonyMethod(prefix),
                    finalizer: new HarmonyMethod(finalizer));
                Log.Message("[MP-MeowOnlineShop] Smart Pistol MP patch active: bezier offset Rand is deterministic.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Smart Pistol patch failed: " + e.Message);
            }
        }

        private static void InitRandOffsetPrefix(object __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || __instance == null)
                return;

            var thing = __instance as Thing;
            if (thing == null)
                return;

            int seed = Gen.HashCombineInt(thing.thingIDNumber, SeedOffset);
            var map = thing.Map;
            if (map != null)
                seed = Gen.HashCombineInt(seed, map.uniqueID);

            Rand.PushState(seed);
            __state = true;
        }

        private static void InitRandOffsetFinalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }
}
