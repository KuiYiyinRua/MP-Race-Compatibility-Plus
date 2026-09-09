using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenHubQueries
    {
        private static Type hubType;
        private static FieldInfo activeHub;
        internal static void Apply(Harmony harmony)
        {
            var system = AccessTools.TypeByName("RavenRace.Features.CentralHub.GameComponent_RavenCentralHubSystem");
            hubType = AccessTools.TypeByName("RavenRace.Buildings.Building_SunRaiserInjector");
            activeHub = AccessTools.Field(system, "activeHub");
            if (hubType == null || activeHub == null) throw new TypeLoadException("Raven active hub lookup");
            harmony.Patch(AccessTools.DeclaredMethod(system, "ResolveActiveHub"),
                prefix: new HarmonyMethod(typeof(RavenHubQueries), nameof(Resolve)));
        }
        private static bool Resolve(GameComponent __instance, ref object __result)
        {
            if (!MP.IsInMultiplayer) return true;
            var cached = activeHub.GetValue(__instance) as Thing;
            if (cached != null && cached.Spawned && !cached.Destroyed) { __result = cached; return false; }
            var selected = Find.Maps.OrderBy(m => m.uniqueID)
                .SelectMany(m => m.listerBuildings.allBuildingsColonist.OrderBy(b => b.thingIDNumber))
                .FirstOrDefault(b => hubType.IsInstanceOfType(b));
            if (!MP.InInterface) activeHub.SetValue(__instance, selected);
            __result = selected;
            return false;
        }
    }
}
