using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Nudity Matters More caches CanSeeNaked results in a process-local static
    /// dictionary. A late-joining client therefore takes the cache-miss branch
    /// while the host can take the cache-hit branch. The miss branch consumes
    /// Verse.Rand and caused the first trace difference in Desync-43.
    ///
    /// In multiplayer, replace that process-local, rolling-expiry decision with
    /// a pure result based on pawn IDs and the shared in-game day. Single-player
    /// behavior remains untouched.
    /// </summary>
    internal static class Patch_NudityMattersMoreMp
    {
        private const string PackageId = "dord.nuditymattersmore";
        private const string InfoHelperTypeName = "NudityMattersMore.InfoHelper";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type infoHelper = AccessTools.TypeByName(InfoHelperTypeName);
            var target = infoHelper == null
                ? null
                : AccessTools.Method(
                    infoHelper,
                    "CanSeeNaked",
                    new[] { typeof(Pawn), typeof(Pawn) });
            var prefix = AccessTools.Method(
                typeof(Patch_NudityMattersMoreMp),
                nameof(CanSeeNakedPrefix));

            if (target == null || target.ReturnType != typeof(bool) || prefix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][NudityMattersMore] CanSeeNaked signature " +
                    "not found; process-local cache guard was not applied.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First });
            Log.Message(
                "[MP-MeowOnlineShop][NudityMattersMore] multiplayer CanSeeNaked " +
                "uses a shared-day deterministic result and no live Rand/cache state.");
        }

        private static bool CanSeeNakedPrefix(
            Pawn observed,
            Pawn observer,
            ref bool __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            __result = DeterministicCanSeeNaked(observed, observer);
            return false;
        }

        private static bool DeterministicCanSeeNaked(Pawn observed, Pawn observer)
        {
            if (observed == null || observer == null ||
                observed.relations == null || observer.relations == null)
                return false;

            int opinion = observed.relations.OpinionOf(observer);
            if (opinion <= -30)
                return false;

            bool differentGenders = observed.gender != observer.gender;
            bool related = observed.relations.RelatedPawns.Contains(observer);
            bool lovers = IsLover(observed, observer);
            bool cannotSee =
                (differentGenders && related && !lovers) ||
                (differentGenders && IsPrudeOrHatesMen(observed));
            if (cannotSee)
                return false;

            bool relatedBothFemale =
                observed.gender == Gender.Female &&
                !differentGenders &&
                related;
            if (relatedBothFemale || lovers)
                return true;

            if (opinion < 15)
                return false;

            double probability =
                ((opinion * 0.875) +
                 (observed.gender == Gender.Female &&
                  observed.gender == observer.gender
                     ? 17.5
                     : -7.5)) /
                100.0;

            int day = Find.TickManager == null
                ? 0
                : Find.TickManager.TicksGame / GenDate.TicksPerDay;
            return StableUnitValue(
                       observed.thingIDNumber,
                       observer.thingIDNumber,
                       day) <
                   probability;
        }

        private static bool IsLover(Pawn first, Pawn second)
        {
            return first != second &&
                   (first.relations.DirectRelationExists(PawnRelationDefOf.Lover, second) ||
                    first.relations.DirectRelationExists(PawnRelationDefOf.Fiance, second) ||
                    first.relations.DirectRelationExists(PawnRelationDefOf.Spouse, second));
        }

        private static bool IsPrudeOrHatesMen(Pawn pawn)
        {
            if (pawn.story == null || pawn.story.traits == null)
                return false;

            TraitDef vtePrude =
                DefDatabase<TraitDef>.GetNamedSilentFail("VTE_Prude");
            if (vtePrude != null && pawn.story.traits.HasTrait(vtePrude))
                return true;

            return
                (pawn.story.traits.HasTrait(TraitDefOf.DislikesMen) &&
                 pawn.gender == Gender.Female) ||
                (pawn.story.traits.HasTrait(TraitDefOf.DislikesWomen) &&
                 pawn.gender == Gender.Male);
        }

        private static double StableUnitValue(
            int observedId,
            int observerId,
            int day)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)observedId) * 16777619u;
                hash = (hash ^ (uint)observerId) * 16777619u;
                hash = (hash ^ (uint)day) * 16777619u;
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
                return (hash & 0x00ffffffu) / 16777216.0;
            }
        }
    }
}
