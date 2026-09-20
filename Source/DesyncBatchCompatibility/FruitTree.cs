using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class FruitTree
    {
        static Type rendererType;
        static FieldInfo matureGrowth;

        internal static void Apply(Harmony harmony)
        {
            var tree = Bootstrap.Type("Nivarian_Race.Code.Comps.ThingComps.Comp_FruitTree");
            rendererType = Bootstrap.Type("Nivarian_Race.Code.Comps.ThingComps.Comp_PlantRenderer");
            matureGrowth = Bootstrap.Field(Bootstrap.Type("Nivarian_Race.Code.Comps.ThingComps.CompProperties_PlantRenderer"), "matureGrowth", typeof(float));
            var getter = AccessTools.PropertyGetter(tree, "IsMature") ?? throw new MissingMethodException(tree.FullName, "get_IsMature");
            if (getter.ReturnType != typeof(bool)) throw new InvalidOperationException("Fruit maturity signature changed");
            harmony.Patch(getter, prefix: new HarmonyMethod(typeof(FruitTree), nameof(Maturity)));
        }

        internal static bool Maturity(ThingComp __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            var plant = __instance.parent as Plant;
            float threshold = 0.99f;
            if (plant != null)
            {
                // Use the same configured threshold, but never the renderer's unsaved/main-thread cache.
                foreach (var comp in plant.AllComps)
                    if (rendererType.IsInstanceOfType(comp))
                    {
                        threshold = (float)matureGrowth.GetValue(comp.props);
                        break;
                    }
            }
            __result = plant != null && plant.Growth >= threshold;
            return false;
        }
    }
}
