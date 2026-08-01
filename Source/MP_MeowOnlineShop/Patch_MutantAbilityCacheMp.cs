using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Prevents local interface evaluation from lazily constructing and assigning
    /// IDs to Pawn_MutantTracker abilities. Multiplayer already protects the normal
    /// Pawn_AbilityTracker cache, but RigorMortis reaches the mutant cache from its
    /// postfix even when the protected vanilla getter was skipped.
    /// </summary>
    internal static class Patch_MutantAbilityCacheMp
    {
        internal const string HarmonyId = "mp.meowonlineshop.mutantabilitycache";

        private static FieldInfo _abilitiesField;

        internal static void Apply(Harmony harmony)
        {
            _abilitiesField = AccessTools.Field(typeof(Pawn_MutantTracker), "abilities");
            MethodInfo getter = AccessTools.PropertyGetter(
                typeof(Pawn_MutantTracker), nameof(Pawn_MutantTracker.AllAbilitiesForReading));
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MutantAbilityCacheMp), nameof(AllAbilitiesForReadingPrefix));

            if (harmony == null || getter == null || prefix == null || _abilitiesField == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Mutant ability cache guard target resolution failed; " +
                    "local UI may still initialize simulation abilities.");
                return;
            }

            harmony.Patch(
                getter,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First });

            Log.Message(
                "[MP-MeowOnlineShop] Mutant ability cache guard active: " +
                "interface reads cannot lazily create mutant abilities or Ability IDs.");
        }

        private static bool AllAbilitiesForReadingPrefix(
            Pawn_MutantTracker __instance,
            ref List<Ability> __result)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface)
                return true;

            try
            {
                // Return an already serialized/simulation-created cache when one
                // exists. Otherwise expose an ephemeral empty list to UI callers.
                // The next deterministic tick or sync command remains responsible
                // for creating the real abilities on every peer in the same order.
                __result = _abilitiesField.GetValue(__instance) as List<Ability>
                           ?? new List<Ability>();
                return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce(
                    "[MP-MeowOnlineShop] Mutant ability cache guard failed open: " + e.Message,
                    0x4D554143);
                return true;
            }
        }
    }
}
