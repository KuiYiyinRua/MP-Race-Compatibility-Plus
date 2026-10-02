using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    [StaticConstructorOnStartup]
    internal static class Patch_Desync344345Boundaries
    {
        private static readonly AccessTools.FieldRef<Hediff, List<Ability>> Abilities =
            AccessTools.FieldRefAccess<Hediff, List<Ability>>("abilities");

        static Patch_Desync344345Boundaries()
        {
            if (!MP.enabled) return;
            LongEventHandler.ExecuteWhenFinished(Apply);
        }

        private static void Apply()
        {
            if (ModsConfig.IsActive("Mehni.PickUpAndHaul") && CompatibilityPatchCategories.IsEnabled("gameplay"))
            {
                var h = new Harmony("mp.meowonlineshop.pickup-order");
                try
                {
                    var worker = AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory");
                    var comparer = AccessTools.Inner(worker, "ThingPositionComparer");
                    var compare = AccessTools.DeclaredMethod(comparer, "Compare", new[] { typeof(Thing), typeof(Thing) });
                    var closest = AccessTools.DeclaredMethod(worker, "FindClosestThing",
                        new[] { typeof(List<Thing>), typeof(IntVec3), typeof(int).MakeByRefType() });
                    if (compare?.ReturnType != typeof(int) || closest?.ReturnType != typeof(Thing))
                        throw new MissingMethodException("PickUpAndHaul distance comparison/closest selection signatures");
                    var unload = AccessTools.DeclaredMethod(AccessTools.TypeByName("PickUpAndHaul.JobDriver_UnloadYourHauledInventory"),
                        "FirstUnloadableThing", new[] { typeof(Pawn), typeof(HashSet<Thing>) });
                    if (unload?.ReturnType != typeof(ThingCount) ||
                        PatchProcessor.GetOriginalInstructions(unload).Count(i => IsUnloadSort(i.operand as MethodInfo)) != 1)
                        throw new MissingMethodException("PickUpAndHaul unload ordering signature/ThenBy call");
                    h.Patch(compare, postfix: new HarmonyMethod(typeof(Patch_Desync344345Boundaries), nameof(BreakDistanceTie)));
                    h.Patch(closest, prefix: new HarmonyMethod(typeof(Patch_Desync344345Boundaries), nameof(FindClosest)));
                    h.Patch(unload, transpiler: new HarmonyMethod(typeof(Patch_Desync344345Boundaries), nameof(OrderUnloadTies)));
                    Log.Message("[MP-MeowOnlineShop][Desync344] READY pickup distance ties and same-def unload ties use stable Thing IDs.");
                }
                catch (Exception e)
                {
                    h.UnpatchAll(h.Id);
                    Log.Error("[MP-MeowOnlineShop][Desync344] REQUIRED_TARGET_FAILED pickup ordering: " + e);
                }
            }
            if (ModsConfig.IsActive("Ancot.MiliraRace") && CompatibilityPatchCategories.IsEnabled("milira") &&
                !MiliraAddonCompat.MiliraMpCompatGate.ReferenceModActive)
            {
                var h = new Harmony("mp.meowonlineshop.milian-ability-tick");
                try
                {
                    var tick = AccessTools.DeclaredMethod(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.AbilitiesTick), Type.EmptyTypes);
                    if (tick == null || Abilities == null) throw new MissingMethodException("Milian ability tick/cache");
                    h.Patch(tick, prefix: new HarmonyMethod(typeof(Patch_Desync344345Boundaries), nameof(PrepareMilianAbilities)));
                    Log.Message("[MP-MeowOnlineShop][Desync345] READY Milian hediff abilities initialize at simulation tick independently of outer cache dirtiness.");
                }
                catch (Exception e)
                {
                    h.UnpatchAll(h.Id);
                    Log.Error("[MP-MeowOnlineShop][Desync345] REQUIRED_TARGET_FAILED ability tick: " + e);
                }
            }
        }

        private static bool IsUnloadSort(MethodInfo method)
        {
            return method != null && method.DeclaringType == typeof(Enumerable) && method.Name == "ThenBy" &&
                method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(Thing), typeof(string) });
        }

        private static IEnumerable<CodeInstruction> OrderUnloadTies(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!IsUnloadSort(instruction.operand as MethodInfo)) continue;
                count++;
                yield return CodeInstruction.Call(typeof(Patch_Desync344345Boundaries), nameof(StableUnloadOrder));
            }
            if (count != 1) throw new InvalidOperationException("PickUpAndHaul unload sort IL changed");
        }

        private static IOrderedEnumerable<Thing> StableUnloadOrder(IOrderedEnumerable<Thing> original)
        {
            // Keep native category/def precedence and removal/merged-stack logic.
            return MP.IsInMultiplayer ? original.ThenBy(t => t.thingIDNumber) : original;
        }

        private static void BreakDistanceTie(Thing x, Thing y, ref int __result)
        {
            if (MP.IsInMultiplayer && __result == 0 && x != null && y != null)
                __result = x.thingIDNumber.CompareTo(y.thingIDNumber);
        }

        private static bool FindClosest(List<Thing> searchSet, IntVec3 center, ref int index, ref Thing __result)
        {
            if (!MP.IsInMultiplayer) return true;
            index = -1;
            __result = null;
            if (searchSet == null || searchSet.Count == 0) return false;
            int distance = int.MaxValue;
            // Preserve the original list and return its real index, which the
            // caller removes. Only equal-distance selection changes.
            for (int i = 0; i < searchSet.Count; i++)
            {
                Thing candidate = searchSet[i];
                int current = (center - candidate.Position).LengthHorizontalSquared;
                if (index < 0 || current < distance ||
                    (current == distance && candidate.thingIDNumber < __result.thingIDNumber))
                {
                    index = i;
                    __result = candidate;
                    distance = current;
                }
            }
            return false;
        }

        private static void PrepareMilianAbilities(Pawn_AbilityTracker __instance)
        {
            Pawn pawn = __instance.pawn;
            if (!MP.IsInMultiplayer || MP.InInterface || pawn?.health == null ||
                !pawn.def.defName.StartsWith("Milian_", StringComparison.Ordinal)) return;
            bool changed = false;
            // Native hediff abilities are serialized, but the enclosing tracker
            // cache and its dirty flag are not. A stale clean cache must not skip
            // allocating a newly present hediff's simulation Ability IDs.
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff.def.abilities.NullOrEmpty() || Abilities(hediff) != null) continue;
                _ = hediff.AllAbilitiesForReading;
                changed = true;
            }
            if (changed) pawn.abilities.Notify_TemporaryAbilitiesChanged();
        }
    }
}
