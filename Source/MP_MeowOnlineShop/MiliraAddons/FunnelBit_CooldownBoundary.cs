using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class FunnelBit_CooldownBoundary
{
    static Func<Pawn, bool> fixExpired;

    static FunnelBit_CooldownBoundary()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("rabiosus.funnelmilian")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                var type = AccessTools.TypeByName("FunnelBit.FunnelBitAbilityCooldownFixer") ?? throw new TypeLoadException("FunnelBitAbilityCooldownFixer");
                var fix = AccessTools.DeclaredMethod(type, "FixExpiredCooldowns", new[] { typeof(Pawn) }) ?? throw new MissingMethodException("FixExpiredCooldowns(Pawn)");
                if (!fix.IsStatic || fix.ReturnType != typeof(bool)) throw new InvalidOperationException("Unexpected cooldown fixer signature");
                var tick = AccessTools.DeclaredMethod(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.AbilitiesTick), Type.EmptyTypes) ?? throw new MissingMethodException("AbilitiesTick");
                var harmony = new Harmony("Meow.FunnelBit.CooldownBoundary");
                harmony.Patch(fix, prefix: new HarmonyMethod(typeof(FunnelBit_CooldownBoundary), nameof(BeforeFix)));
                fixExpired = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), fix);
                harmony.Patch(tick, postfix: new HarmonyMethod(typeof(FunnelBit_CooldownBoundary), nameof(AfterTick)));
                Log.Message("[FunnelBit_CooldownBoundary] REQUIRED_TARGETS_OK cooldown repair after native ability ticks, interface read-only");
            }
            catch (Exception e) { Log.Error("[FunnelBit_CooldownBoundary] REQUIRED_TARGET_FAILED " + e); }
        });
    }

    static bool BeforeFix(ref bool __result)
    {
        if (!MP.IsInMultiplayer || !MP.InInterface) return true;
        __result = false;
        return false;
    }

    static void AfterTick(Pawn_AbilityTracker __instance)
    {
        if (!MP.IsInMultiplayer || MP.InInterface || __instance.pawn?.apparel == null) return;
        // Let vanilla cooldown completion award charges and send notifications
        // before applying the mod's own fallback for expired apparel abilities.
        fixExpired(__instance.pawn);
    }
}
