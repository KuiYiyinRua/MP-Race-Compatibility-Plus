using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenChezhouFlight
    {
        private static FieldInfo cache, activeIds;
        internal static void Apply(Harmony harmony)
        {
            var flight = AccessTools.TypeByName("ChezhouLib.ClThingComp.ThingComp_RaceFly");
            var action = AccessTools.DeclaredMethod(flight, "<CompGetGizmosExtra>b__67_0", Type.EmptyTypes);
            if (action == null) throw new MissingMethodException("Chezhou flight action (6154D02F binary)");
            // The callback changes auto-flight fields and hediffs outside the ordered
            // job boundary. Sync its complete body, including moving transitions.
            MP.RegisterSyncMethod(action, null);
            var pathing = AccessTools.TypeByName("ChezhouLib.Utils.FlyPathingGlobal");
            cache = AccessTools.Field(pathing, "_compCache");
            activeIds = AccessTools.Field(pathing, "activeFlightPawnIds");
            if (cache == null || activeIds == null) throw new MissingFieldException("Chezhou flight caches");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Game), "ExposeSmallComponents"),
                prefix: new HarmonyMethod(typeof(RavenChezhouFlight), nameof(BeforeLoad)));
            harmony.Patch(AccessTools.DeclaredMethod(pathing, "GetFlyCompCached"),
                prefix: new HarmonyMethod(typeof(RavenChezhouFlight), nameof(ValidateCachedPawn)));
        }
        private static void BeforeLoad()
        {
            if (!MP.enabled || Scribe.mode != LoadSaveMode.LoadingVars) return;
            (cache.GetValue(null) as IDictionary)?.Clear();
            var ids = activeIds.GetValue(null);
            AccessTools.Method(ids.GetType(), "Clear").Invoke(ids, null);
        }
        private static void ValidateCachedPawn(Pawn pawn)
        {
            if (!MP.IsInMultiplayer || pawn == null) return;
            var dictionary = cache.GetValue(null) as IDictionary;
            if (dictionary != null && dictionary.Contains(pawn.thingIDNumber)
                && dictionary[pawn.thingIDNumber] is ThingComp comp && comp.parent != pawn)
                dictionary.Remove(pawn.thingIDNumber);
        }
    }
}
