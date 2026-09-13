using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350MeleeCooldownMp
    {
        private static FieldInfo lastOverride;
        private static bool installed;
        internal static void Apply(Harmony harmony)
        {
            if (installed || !MP.enabled || !ModsConfig.IsActive("aliza.vanillameleemodes")) return;
            try
            {
                Type type = AccessTools.TypeByName("VMM_VanillaMeleeModes.Comps.VMM_PawnCompMeleeMode") ?? throw new TypeLoadException("VMM_PawnCompMeleeMode");
                lastOverride = AccessTools.DeclaredField(type, "lastPlayerOverrideTick");
                MethodInfo expose = AccessTools.DeclaredMethod(type, "PostExposeData", Type.EmptyTypes);
                if (lastOverride == null || lastOverride.FieldType != typeof(int) || expose == null) throw new InvalidOperationException("Melee cooldown serializer shape changed");
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_Light350MeleeCooldownMp), nameof(Expose)));
                installed = true;
                Log.Message("[MP-MeowOnlineShop][Light350-B11] VanillaMeleeModes: manual override cooldown serialization installed.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][Light350-B11] REQUIRED TARGET FAILED VanillaMeleeModes: " + e);
            }
        }

        private static void Expose(object __instance)
        {
            int value = (int)lastOverride.GetValue(__instance);
            Scribe_Values.Look(ref value, "meowMpVmmLastPlayerOverrideTick", -9999);
            lastOverride.SetValue(__instance, value);
        }
    }
}
