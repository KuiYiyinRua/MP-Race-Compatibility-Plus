using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Odyssey gravship abandonment can leave modded things such as
    /// Milira_DroneFreight with a stale mapIndexOrState pointing at the removed
    /// map. Verse.Thing.Spawned then logs
    /// "Thing ... is associated with invalid map index N" on every GUI access,
    /// which AncotLibrary.MainButtonWorker_Drones triggers each frame and can
    /// leave the main button bar/continue UI stuck.
    ///
    /// This guard treats a positive map index that is no longer in Find.Maps as
    /// "not spawned", resets the thing to the unspawned state once, and returns
    /// null for Map so the stale object cannot break other callers. The reset
    /// is deterministic and local; it does not add sync commands.
    ///
    /// Thing.Spawned is left untouched: vanilla already returns false for an
    /// out-of-range map index. Only Thing.Map needs a guard because the vanilla
    /// getter indexes Find.Maps directly and can throw.
    /// </summary>
    internal static class Patch_InvalidMapIndexSafety
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> MapIndexRef =
            TryGetMapIndexRef();
        private static readonly HashSet<int> LoggedHeals =
            new HashSet<int>();
        private static int _healCount;
        private static bool _applied;

        private static AccessTools.FieldRef<Thing, sbyte> TryGetMapIndexRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
            }
            catch
            {
                return null;
            }
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            MethodInfo mapGetter = AccessTools.PropertyGetter(
                typeof(Thing),
                nameof(Thing.Map));

            if (mapGetter == null || MapIndexRef == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Invalid map index safety skipped: " +
                    $"map={mapGetter != null} fieldRef={MapIndexRef != null}.");
                return;
            }

            harmony.Patch(
                mapGetter,
                prefix: new HarmonyMethod(
                    typeof(Patch_InvalidMapIndexSafety),
                    nameof(MapPrefix))
                {
                    priority = Priority.First
                });

            Log.Message(
                "[MP-MeowOnlineShop] Invalid map index safety active: stale " +
                "mapIndexOrState is reset from the Thing.Map hot path only; " +
                "Thing.Spawned keeps the vanilla fast path.");
        }

        private static bool MapPrefix(Thing __instance, ref Map __result)
        {
            if (__instance == null)
                return true;

            sbyte mapIndex = MapIndexRef(__instance);
            if (mapIndex < 0)
                return true;

            if (TryResetStaleIndex(__instance, mapIndex))
            {
                __result = null;
                return false;
            }

            return true;
        }

        private static bool TryResetStaleIndex(Thing thing, sbyte mapIndex)
        {
            if (thing == null || MapIndexRef == null ||
                mapIndex < 0 || Find.Maps == null ||
                mapIndex < Find.Maps.Count)
            {
                return false;
            }

            try
            {
                thing.ForceSetStateToUnspawned();

                if (_healCount < 5 &&
                    LoggedHeals.Add(thing.thingIDNumber))
                {
                    _healCount++;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Reset stale map index for " +
                        $"{thing.ToStringSafe()} id={thing.thingIDNumber} " +
                        $"old={mapIndex} maps={Find.Maps.Count}.");
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Failed to reset stale map index for " +
                    $"{thing.ToStringSafe()} id={thing.thingIDNumber}: " +
                    e.Message);
                return false;
            }
        }
    }
}
