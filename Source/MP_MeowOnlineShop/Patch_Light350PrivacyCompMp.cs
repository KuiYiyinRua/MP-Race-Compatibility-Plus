using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350PrivacyCompMp
    {
        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || !ModsConfig.IsActive("abscon.privacy.please")) return;
            try
            {
                Type type = AccessTools.TypeByName("Privacy_Please.CompPawnThoughtData");
                var target = type == null ? null : AccessTools.DeclaredMethod(type, "Initialize", new[] { typeof(CompProperties) });
                if (target == null || !typeof(ThingComp).IsAssignableFrom(type) || target.ReturnType != typeof(void))
                    throw new InvalidOperationException("Privacy component Initialize signature changed");
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_Light350PrivacyCompMp), nameof(Initialized)));
                Log.Message("[MP-MeowOnlineShop][Light350-B12] Privacy: missing component properties initialization restored.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][Light350-B12] REQUIRED TARGET FAILED Privacy component initialization: " + e);
            }
        }

        private static void Initialized(ThingComp __instance, CompProperties __0)
        {
            // The original override initializes its Pawn cache but omits the
            // assignment made by ThingComp.Initialize. Retain the exact Def
            // properties supplied by the engine, including during initial load.
            if (__instance.props == null) __instance.props = __0;
        }
    }
}
