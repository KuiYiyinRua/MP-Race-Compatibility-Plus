using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Skip-abduction's outcome selects a victim from
    /// MapPawns.FreeColonistsSpawned.  That vanilla convenience property is
    /// filtered by Faction.OfPlayer, which is peer-local in a multifaction
    /// session.  An enemy ritual can consequently choose from a different
    /// candidate list (and consume a different map Rand sequence) on a client.
    /// Resolve the candidate list under the attacked map's owning player
    /// faction for the narrowly-scoped outcome executor.
    /// </summary>
    internal static class Patch_PsychicRitualSkipAbductionMultifaction
    {
        private static bool _applied;
        private static FieldInfo _ofPlayerField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(PsychicRitualToil_SkipAbduction), "ApplyOutcome");
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_PsychicRitualSkipAbductionMultifaction),
                    nameof(ApplyOutcomePrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_PsychicRitualSkipAbductionMultifaction),
                    nameof(ApplyOutcomeFinalizer));
                _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");

                if (target == null || prefix == null || finalizer == null || _ofPlayerField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Skip-abduction psychic-ritual " +
                        "multifaction target resolution failed.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

                Log.Message(
                    "[MP-MeowOnlineShop] Skip-abduction psychic-ritual " +
                    "multifaction victim selection uses the map owner faction.");
            }
            catch (Exception e)
            {
                _applied = false;
                Log.Warning(
                    "[MP-MeowOnlineShop] Skip-abduction psychic-ritual " +
                    "multifaction patch failed: " + e.Message);
            }
        }

        private static void ApplyOutcomePrefix(PsychicRitual psychicRitual, ref Faction __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || psychicRitual?.Map == null ||
                !MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) || !multifaction)
            {
                return;
            }

            try
            {
                Faction owner = psychicRitual.Map.ParentFaction;
                FactionManager factionManager = Find.FactionManager;
                if (owner == null || !owner.IsPlayer || factionManager == null)
                    return;

                __state = _ofPlayerField.GetValue(factionManager) as Faction;
                _ofPlayerField.SetValue(factionManager, owner);
            }
            catch
            {
                __state = null;
            }
        }

        private static void ApplyOutcomeFinalizer(Faction __state)
        {
            if (__state == null)
                return;

            try
            {
                FactionManager factionManager = Find.FactionManager;
                if (factionManager != null)
                    _ofPlayerField.SetValue(factionManager, __state);
            }
            catch
            {
                // Leave the original exception path intact if context restoration fails.
            }
        }
    }
}
