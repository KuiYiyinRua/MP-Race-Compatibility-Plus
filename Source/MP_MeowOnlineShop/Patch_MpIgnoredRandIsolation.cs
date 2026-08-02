using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer deliberately omits trace recording for a few high-volume
    /// Rand consumers (WildAnimalSpawner, WildPlantSpawner,
    /// SteadyEnvironmentEffects, TempTerrainManager and
    /// IntermittentSteamSprayer). When TickList order or a rejoin changes the
    /// interleaving, those untraced draws silently shift the shared map Rand
    /// stream; the next traced draw then shows a different state (Desync-198:
    /// first divergent trace is a Milira turret draw on map 20). Give each
    /// verified boundary its own deterministic stream so its draws cannot
    /// contaminate the synchronized map stream.
    /// </summary>
    internal static class Patch_MpIgnoredRandIsolation
    {
        private const int SaltWildAnimal = 0x57414E49; // "WANI"
        private const int SaltWildPlant = 0x57494C44; // "WILD"
        private const int SaltSteady = 0x53544154; // "STAT"
        private const int SaltTemp = 0x54454D50; // "TEMP"
        private const int SaltSteam = 0x53544541; // "STEA"

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            int patched = 0;
            patched += PatchIfResolved(
                harmony,
                typeof(WildAnimalSpawner),
                "WildAnimalSpawnerTick",
                nameof(WildAnimalPrefix)) ? 1 : 0;
            patched += PatchIfResolved(
                harmony,
                typeof(WildPlantSpawner),
                "WildPlantSpawnerTick",
                nameof(WildPlantPrefix)) ? 1 : 0;
            patched += PatchIfResolved(
                harmony,
                typeof(SteadyEnvironmentEffects),
                "SteadyEnvironmentEffectsTick",
                nameof(SteadyPrefix)) ? 1 : 0;
            patched += PatchIfResolved(
                harmony,
                typeof(TempTerrainManager),
                "Tick",
                nameof(TempPrefix)) ? 1 : 0;
            patched += PatchIfResolved(
                harmony,
                typeof(IntermittentSteamSprayer),
                "SteamSprayerTick",
                nameof(SteamPrefix)) ? 1 : 0;

            Log.Message(
                "[MP-MeowOnlineShop] MP ignored-tick Rand isolation active: " +
                patched + "/5 verified map/thing boundaries.");
        }

        private static bool PatchIfResolved(
            Harmony harmony,
            Type owner,
            string methodName,
            string prefixName)
        {
            MethodInfo target = AccessTools.Method(owner, methodName);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MpIgnoredRandIsolation),
                prefixName);
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_MpIgnoredRandIsolation),
                nameof(Finalizer));
            if (target == null || prefix == null || finalizer == null)
                return false;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                });
            return true;
        }

        private static void WildAnimalPrefix(
            Map ___map,
            ref bool __state)
        {
            BeginMapScope(___map, SaltWildAnimal, ref __state);
        }

        private static void WildPlantPrefix(
            Map ___map,
            ref bool __state)
        {
            BeginMapScope(___map, SaltWildPlant, ref __state);
        }

        private static void SteadyPrefix(
            Map ___map,
            ref bool __state)
        {
            BeginMapScope(___map, SaltSteady, ref __state);
        }

        private static void TempPrefix(
            Map ___map,
            ref bool __state)
        {
            BeginMapScope(___map, SaltTemp, ref __state);
        }

        private static void SteamPrefix(
            Thing ___parent,
            ref bool __state)
        {
            BeginThingScope(___parent, SaltSteam, ref __state);
        }

        private static void BeginMapScope(
            Map map,
            int salt,
            ref bool state)
        {
            state = false;
            if (!MP.IsInMultiplayer || map == null ||
                Scribe.mode != LoadSaveMode.Inactive)
            {
                return;
            }

            int seed = Gen.HashCombineInt(salt, map.uniqueID);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            state = true;
        }

        private static void BeginThingScope(
            Thing thing,
            int salt,
            ref bool state)
        {
            state = false;
            if (!MP.IsInMultiplayer || thing == null ||
                thing.Map == null || Scribe.mode != LoadSaveMode.Inactive)
            {
                return;
            }

            int seed = Gen.HashCombineInt(salt, thing.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, thing.thingIDNumber);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);
            Rand.PushState(seed);
            state = true;
        }

        private static Exception Finalizer(
            Exception __exception,
            bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
