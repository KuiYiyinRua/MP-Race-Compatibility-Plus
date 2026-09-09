using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Milira combat-drone projectiles call one of these visual helpers for
    /// their trail flecks. Each helper first consults local visual state
    /// (GenView.ShouldSpawnMotesAt), then consumes Verse.Rand to create a
    /// fleck. A host and a rejoining client can legitimately make a different
    /// visual decision, so those cosmetic draws must not advance the shared
    /// async map Rand stream (Desync-346).
    /// </summary>
    internal static class Patch_AncotTrailFleckRandIsolation
    {
        private const string FleckMakerTypeName = "AncotLibrary.AncotFleckMaker";
        private const int TrailFleckSeedSalt = 0x54524C46; // "TRLF"
        private const int TrailFleckWorldSeedOffset = 0x54524C47; // "TRLG"

        private static bool _applied;

        [ThreadStatic]
        private static Map _mapForRandPop;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;

            Type fleckMakerType = AccessTools.TypeByName(FleckMakerTypeName);
            MethodInfo ancotTarget = fleckMakerType == null
                ? null
                : AccessTools.Method(
                    fleckMakerType,
                    "ThrowTrailFleckUp",
                    new[]
                    {
                        typeof(Vector3),
                        typeof(Map),
                        typeof(Color),
                        typeof(FleckDef)
                    });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_AncotTrailFleckRandIsolation),
                nameof(ThrowTrailFleckUpPrefix));
            MethodInfo airPuffPrefix = AccessTools.Method(
                typeof(Patch_AncotTrailFleckRandIsolation),
                nameof(ThrowAirPuffUpPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_AncotTrailFleckRandIsolation),
                nameof(ThrowTrailFleckUpFinalizer));

            MethodInfo airPuffTarget = AccessTools.Method(
                typeof(FleckMaker),
                "ThrowAirPuffUp",
                new[] { typeof(Vector3), typeof(Map) });

            if (prefix == null || airPuffPrefix == null || finalizer == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Trail-fleck Rand isolation skipped " +
                    "(patch signatures could not be resolved).");
                return;
            }

            try
            {
                int patchedTargets = 0;

                if (ancotTarget != null)
                {
                    harmony.Patch(
                        ancotTarget,
                        prefix: new HarmonyMethod(prefix)
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(finalizer)
                        {
                            priority = Priority.Last
                        });
                    patchedTargets++;
                }

                if (airPuffTarget != null)
                {
                    harmony.Patch(
                        airPuffTarget,
                        prefix: new HarmonyMethod(airPuffPrefix)
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(finalizer)
                        {
                            priority = Priority.Last
                        });
                    patchedTargets++;
                }

                if (patchedTargets == 0)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Trail-fleck Rand isolation skipped " +
                        "(no supported visual helper was found).");
                    return;
                }

                _applied = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Ancot/Milira trail-fleck visual Rand " +
                    "isolation active in multiplayer (targets=" + patchedTargets + ").");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Trail-fleck Rand isolation install " +
                    "failed: " + e.Message);
            }
        }

        private static void ThrowTrailFleckUpPrefix(
            Vector3 loc,
            Map map,
            FleckDef fleckDef,
            ref int __state)
        {
            BeginVisualRandScope(loc, map, fleckDef, ref __state);
        }

        private static void ThrowAirPuffUpPrefix(
            Vector3 loc,
            Map map,
            ref int __state)
        {
            BeginVisualRandScope(loc, map, FleckDefOf.AirPuff, ref __state);
        }

        private static void BeginVisualRandScope(
            Vector3 loc,
            Map map,
            FleckDef fleckDef,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;

            if (!MP.IsInMultiplayer || map == null)
                return;

            IntVec3 cell = loc.ToIntVec3();
            int seed = Gen.HashCombineInt(TrailFleckSeedSalt, map.uniqueID);
            seed = Gen.HashCombineInt(seed, fleckDef?.shortHash ?? 0);
            seed = Gen.HashCombineInt(seed, cell.x);
            seed = Gen.HashCombineInt(seed, cell.z);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    TrailFleckWorldSeedOffset,
                    ref __state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
            }
        }

        private static Exception ThrowTrailFleckUpFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }
    }
}
