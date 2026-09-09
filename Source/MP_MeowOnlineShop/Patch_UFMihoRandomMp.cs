using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_UFMihoRandomMp
    {
        private static bool applied;
        // UF RNG idea: HKXluo & GPT5.4, https://git.liulikeji.cn/xingluo/uf-multiplayer-compat-pack
        // Independent scope implementation: Harmony __state + finalizer rather
        // than upstream's global stack/postfix, so exceptions also restore RNG.
        internal static void Apply(Harmony harmony)
        {
            if (applied || harmony == null || !MP.enabled) return;
            applied = true;
            if (!ModsConfig.IsActive("hkxluo.ufseries.multiplayer.compat"))
            {
                foreach (var name in new[] { "SRA.Projectile_MultiExplosive", "SRA.Projectile_MultiExplosive_NorthArcTrail" })
                {
                    var type = AccessTools.TypeByName(name);
                    var method = type == null ? null : AccessTools.DeclaredMethod(type, "Impact");
                    if (method == null || !typeof(Thing).IsAssignableFrom(type)) continue;
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patch_UFMihoRandomMp), nameof(UnityPrefix)),
                        finalizer: new HarmonyMethod(typeof(Patch_UFMihoRandomMp), nameof(UnityFinalizer)));
                }
            }
            // Research lead: Thaipho, Multiplayer Miho Patch, Workshop 3768871549:
            // https://steamcommunity.com/sharedfiles/filedetails/?id=3768871549
            // Its source was not obtainable; this is NOT a copy of that patch.
            // Native Miho code by Outremer / Fortified_Home was inspected locally.
            // Isolate only visual helpers; Launch/Impact and fragmentation retain
            // the simulation RNG stream. Vanilla Ability sync already covers UI.
            foreach (var name in new[] { "MihoSmallArcing.Projectile_ArcingBase", "MihoSmallArcing.Projectile_ArcingBaseNonFragment",
                "MihoClibanarArcing.Projectile_ClibanarArcing", "MihoClibanarArcing.Projectile_ClibanarArcing_Fragment" })
            {
                var type = AccessTools.TypeByName(name);
                if (type == null) continue;
                foreach (var nameOfMethod in new[] { "ThrowEffect", "ThrowDustPuffThick" })
                {
                    var method = AccessTools.DeclaredMethod(type, nameOfMethod);
                    if (method == null) continue;
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patch_UFMihoRandomMp), nameof(VisualPrefix)),
                        finalizer: new HarmonyMethod(typeof(Patch_UFMihoRandomMp), nameof(VisualFinalizer)));
                }
            }
            Log.Message("[MP-MeowOnlineShop] UF projectile / Miho visual RNG scopes registered.");
        }

        private static void UnityPrefix(Thing __instance, out UnityEngine.Random.State? __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer) return;
            __state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Gen.HashCombineInt(__instance.thingIDNumber, Find.TickManager.TicksGame));
        }
        private static Exception UnityFinalizer(Exception __exception, UnityEngine.Random.State? __state)
        {
            if (__state.HasValue) UnityEngine.Random.state = __state.Value;
            return __exception;
        }
        private static void VisualPrefix(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }
        private static Exception VisualFinalizer(Exception __exception, bool __state)
        {
            if (__state) Rand.PopState();
            return __exception;
        }
    }
}
