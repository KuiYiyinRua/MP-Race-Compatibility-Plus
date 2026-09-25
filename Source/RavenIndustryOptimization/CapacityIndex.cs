using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RavenRace.Features.RavenConveyor;
using RavenRace.Features.RavenLiquidPipe;
using Verse;

namespace Meow.RavenIndustryOptimization
{
    internal static class CapacityIndex
    {
        private sealed class Inventory
        {
            internal int Total;
            internal long Epoch;
            internal readonly Dictionary<ThingDef, int> Counts = new Dictionary<ThingDef, int>();
        }

        private sealed class Recipe
        {
            internal long Epoch;
            internal readonly List<ThingDef> Order = new List<ThingDef>();
            internal readonly Dictionary<ThingDef, int> Required = new Dictionary<ThingDef, int>();
        }

        // These caches live for one synchronous conveyor pass, not one wall-clock frame
        // or an arbitrarily long interval. Every native inventory mutation invalidates.
        private static ConditionalWeakTable<IReadOnlyList<ConveyorVirtualStack>, Inventory> inventories = new ConditionalWeakTable<IReadOnlyList<ConveyorVirtualStack>, Inventory>();
        private static ConditionalWeakTable<RavenMachineRecipeDef, Recipe> recipes = new ConditionalWeakTable<RavenMachineRecipeDef, Recipe>();
        private static Func<ThingWithComps, RavenMachineRecipeDef> selectedRecipe;
        private static int scopeDepth;
        private static long epoch;

        internal static void Install(Harmony h)
        {
            Type policy = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.RavenConveyorInputCapacityPolicy") ?? throw new TypeLoadException("Raven input policy");
            var calculate = IndustryBootstrap.Method(policy, "Calculate");
            IndustryBootstrap.RequireUnpatched(calculate);
            selectedRecipe = (Func<ThingWithComps, RavenMachineRecipeDef>)Delegate.CreateDelegate(typeof(Func<ThingWithComps, RavenMachineRecipeDef>), IndustryBootstrap.Method(policy, "SelectedRecipeFor"));
            h.Patch(calculate, prefix: new HarmonyMethod(typeof(CapacityIndex), nameof(Calculate)));
            var simulate = IndustryBootstrap.Method(typeof(MapComponent_RavenConveyorSystem), "SimulatePackets");
            h.Patch(simulate, prefix: new HarmonyMethod(typeof(CapacityIndex), nameof(Begin)),
                finalizer: new HarmonyMethod(typeof(CapacityIndex), nameof(End)));
            foreach (string name in new[] { "Add", "Remove", "RemoveInvalid", "DropStacks" })
            {
                var method = IndustryBootstrap.Method(typeof(CompRavenConveyorMachineBuffer), name);
                IndustryBootstrap.RequireUnpatched(method);
                h.Patch(method, prefix: new HarmonyMethod(typeof(CapacityIndex), nameof(InvalidateInventory)));
            }
            // Runtime tuning can change from a synchronized command in the same tick.
            Type tuning = AccessTools.TypeByName("RavenRace.Features.RavenLiquidPipe.RavenIndustrialRuntimeTuning");
            IndustryBootstrap.Patch(h, tuning, "Invalidate", typeof(CapacityIndex), nameof(InvalidateRecipes));
        }

        private static void Begin(out bool __state)
        {
            __state = IndustrySettings.Active;
            if (__state && scopeDepth++ == 0) NextEpoch();
        }

        private static Exception End(Exception __exception, bool __state)
        {
            if (__state) --scopeDepth;
            return __exception;
        }

        internal static void Clear()
        {
            inventories = new ConditionalWeakTable<IReadOnlyList<ConveyorVirtualStack>, Inventory>();
            recipes = new ConditionalWeakTable<RavenMachineRecipeDef, Recipe>();
            epoch = 1;
        }

        private static void NextEpoch()
        {
            if (epoch == long.MaxValue) Clear();
            else epoch++;
        }

        private static void InvalidateInventory(List<ConveyorVirtualStack> stacks)
        {
            if (stacks != null && inventories.TryGetValue(stacks, out var inventory)) inventory.Epoch = 0;
        }

        private static void InvalidateRecipes() => NextEpoch();

        private static bool Calculate(ThingWithComps parent, IReadOnlyList<ConveyorVirtualStack> stacks,
            int maxInputCount, ThingDef incomingThingDef, ref int __result)
        {
            if (scopeDepth == 0 || stacks == null) return true;
            if (!RavenVirtualItemUtility.CanVirtualize(incomingThingDef)) { __result = 0; return false; }
            var inventory = inventories.GetValue(stacks, _ => new Inventory());
            if (inventory.Epoch != epoch)
            {
                inventory.Total = 0;
                inventory.Counts.Clear();
                for (int i = 0; i < stacks.Count; i++)
                {
                    var stack = stacks[i];
                    int amount = Math.Max(0, stack?.Count ?? 0);
                    inventory.Total += amount;
                    if (stack?.ThingDef != null)
                    {
                        inventory.Counts.TryGetValue(stack.ThingDef, out int previous);
                        inventory.Counts[stack.ThingDef] = previous + amount;
                    }
                }
                inventory.Epoch = epoch;
            }
            int available = Math.Max(0, maxInputCount - inventory.Total);
            var recipe = selectedRecipe(parent);
            if (recipe == null || recipe.dynamicStoneCutting || recipe.thingIngredients == null || recipe.thingIngredients.Count == 0)
            {
                __result = available;
                return false;
            }
            var indexed = recipes.GetValue(recipe, _ => new Recipe());
            if (indexed.Epoch != epoch)
            {
                indexed.Order.Clear();
                indexed.Required.Clear();
                foreach (var ingredient in recipe.thingIngredients)
                {
                    if (ingredient?.thingDef == null) continue;
                    if (!indexed.Required.TryGetValue(ingredient.thingDef, out int previous)) indexed.Order.Add(ingredient.thingDef);
                    indexed.Required[ingredient.thingDef] = previous + RavenIndustrialRecipeUtility.ThingIngredientCount(recipe, ingredient);
                }
                indexed.Epoch = epoch;
            }
            int missingOthers = 0;
            foreach (var def in indexed.Order)
            {
                if (def == incomingThingDef) continue;
                inventory.Counts.TryGetValue(def, out int stored);
                missingOthers += Math.Max(0, indexed.Required[def] - stored);
            }
            int unreserved = Math.Max(0, available - missingOthers);
            indexed.Required.TryGetValue(incomingThingDef, out int required);
            inventory.Counts.TryGetValue(incomingThingDef, out int current);
            __result = required <= 0 ? unreserved : Math.Max(unreserved, Math.Max(0, required - current));
            return false;
        }
    }
}
