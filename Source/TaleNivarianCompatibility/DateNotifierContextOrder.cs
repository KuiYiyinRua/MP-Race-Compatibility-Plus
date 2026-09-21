using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class DateNotifierContextOrder
    {
        internal static void Apply(Harmony harmony)
        {
            var core = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_DateNotifierMultifactionDeterminism");
            if (core == null) return;
            var restore = AccessTools.DeclaredMethod(core, "RestoreFinalizer")
                ?? throw new MissingMethodException(core.FullName, "RestoreFinalizer");
            var target = AccessTools.DeclaredMethod(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick));
            var installed = Harmony.GetPatchInfo(target)?.Finalizers.SingleOrDefault(p => p.PatchMethod == restore)
                ?? throw new InvalidOperationException("DateNotifier restore finalizer is missing");
            // Harmony runs finalizers from high to low priority, just like prefixes.
            // The inner spectator override must unwind BEFORE MP pops its outer
            // real-player faction scope. Low priority restores the local faction
            // after MP has already restored the world/command faction.
            if (installed.priority < Priority.First)
            {
                harmony.Unpatch(target, restore);
                harmony.Patch(target, finalizer: new HarmonyMethod(restore) { priority = Priority.First });
            }
            Log.Message("[TaleNivarianCompat] DateNotifier inner faction restore runs before MP outer restore.");
        }
    }
}

