using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynAbilityBoundary
    {
        static Type onlyAi;
        internal static void Apply(Harmony harmony)
        {
            onlyAi = AccessTools.TypeByName("ASEL.CompAbilityOnlyForAI") ?? throw new TypeLoadException("ASEL.CompAbilityOnlyForAI");
            var getter = AccessTools.DeclaredPropertyGetter(onlyAi, "ShouldHideGizmo") ?? throw new MissingMemberException(onlyAi.FullName, "ShouldHideGizmo");
            harmony.Patch(getter, prefix: new HarmonyMethod(typeof(MonolynAbilityBoundary), nameof(Hide)));
            var tick = AccessTools.DeclaredMethod(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.AbilitiesTick), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Pawn_AbilityTracker).FullName, nameof(Pawn_AbilityTracker.AbilitiesTick));
            harmony.Patch(tick, postfix: new HarmonyMethod(typeof(MonolynAbilityBoundary), nameof(Tick)));
            Log.Message("[MonolynAbilityBoundary] resolved=AI-only ability visibility is pure; player cleanup in native ability tick; missing=0");
        }
        static bool Hide(ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            // Native UI visibility removes an ability from player pawns. A
            // local selection/inspection must not mutate shared ability state.
            __result = true;
            return false;
        }
        static void Tick(Pawn_AbilityTracker __instance)
        {
            if (!MP.IsInMultiplayer || __instance.pawn?.Faction != Faction.OfPlayer) return;
            // Remove after the native indexed iteration finishes; removing inside
            // AbilityComp.CompTick would skip the next ability in that iteration.
            foreach (var ability in __instance.abilities.ToArray())
                if (ability.comps.Any(c => onlyAi.IsInstanceOfType(c))) __instance.RemoveAbility(ability.def);
        }
    }
}
