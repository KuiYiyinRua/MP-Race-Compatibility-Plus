using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer's TickPatch iterates Find.Maps in reverse list order for
    /// command execution and map ticking. After a rejoin the map list order can
    /// differ between peers even though the maps themselves match, so commands
    /// due at the same tick execute in different order and the command Rand
    /// state lists diverge while every trace is still equal (Desync-202).
    /// Temporarily sort the live map list by uniqueID for the duration of
    /// RunCmds/DoTick and restore the original order afterwards.
    /// </summary>
    internal static class Patch_CommandOrderDeterminism
    {
        private sealed class MapOrderState
        {
            internal List<Map> original;
        }

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            try
            {
                Type tickPatchType =
                    AccessTools.TypeByName("Multiplayer.Client.TickPatch");
                MethodInfo runCmds = AccessTools.Method(tickPatchType, "RunCmds");
                MethodInfo doTick = AccessTools.Method(tickPatchType, "DoTick");
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_CommandOrderDeterminism),
                    nameof(MapOrderPrefix));
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_CommandOrderDeterminism),
                    nameof(MapOrderPostfix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_CommandOrderDeterminism),
                    nameof(MapOrderFinalizer));

                if (runCmds == null || doTick == null ||
                    prefix == null || postfix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Command-order determinism targets " +
                        "unresolved; skipped.");
                    return;
                }

                HarmonyMethod prefixMethod = new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                };
                HarmonyMethod postfixMethod = new HarmonyMethod(postfix)
                {
                    priority = Priority.Last
                };
                HarmonyMethod finalizerMethod = new HarmonyMethod(finalizer)
                {
                    priority = Priority.Last
                };

                harmony.Patch(
                    runCmds,
                    prefix: prefixMethod,
                    postfix: postfixMethod,
                    finalizer: finalizerMethod);
                harmony.Patch(
                    doTick,
                    prefix: prefixMethod,
                    postfix: postfixMethod,
                    finalizer: finalizerMethod);

                Log.Message(
                    "[MP-MeowOnlineShop] Command-order determinism active: " +
                    "map iteration is sorted by uniqueID during RunCmds/DoTick.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Command-order determinism install " +
                    "failed: " + e);
            }
        }

        private static void MapOrderPrefix(ref MapOrderState __state)
        {
            if (!MP.IsInMultiplayer || Find.Maps == null ||
                Find.Maps.Count <= 1)
            {
                return;
            }

            try
            {
                Map current = Find.CurrentMap;
                MapOrderState state = new MapOrderState
                {
                    original = new List<Map>(Find.Maps)
                };

                Find.Maps.Sort((left, right) =>
                {
                    int leftId = left?.uniqueID ?? int.MaxValue;
                    int rightId = right?.uniqueID ?? int.MaxValue;
                    return leftId.CompareTo(rightId);
                });

                if (Current.Game != null)
                {
                    int newIndex = current != null
                        ? Find.Maps.IndexOf(current)
                        : 0;
                    if (newIndex < 0)
                        newIndex = 0;
                    Current.Game.currentMapIndex = (sbyte)newIndex;
                }

                __state = state;
            }
            catch
            {
                __state = null;
            }
        }

        private static void MapOrderPostfix(ref MapOrderState __state)
        {
            RestoreMapOrder(ref __state);
        }

        private static Exception MapOrderFinalizer(
            ref MapOrderState __state,
            Exception __exception)
        {
            RestoreMapOrder(ref __state);
            return __exception;
        }

        private static void RestoreMapOrder(ref MapOrderState __state)
        {
            if (__state == null || Find.Maps == null || Current.Game == null)
            {
                __state = null;
                return;
            }

            try
            {
                // The sorted snapshot is only an ordering hint. Commands inside
                // RunCmds/DoTick can add or remove maps (caravan formation,
                // gravship takeoff, quest site cleanup). Blindly restoring the
                // pre-tick list resurrects disposed maps in Find.Maps, which
                // makes ColonistBar.CheckRecacheEntries and Vehicle Framework
                // scan every disposed map forever. Rebuild from the live
                // membership, keep the original relative order for surviving
                // maps, and append new maps deterministically.
                Map currentMap = Find.CurrentMap;
                List<Map> current = new List<Map>(Find.Maps);
                List<Map> restored = new List<Map>(current.Count);
                foreach (Map map in __state.original)
                {
                    if (current.Remove(map))
                        restored.Add(map);
                }

                current.Sort((left, right) =>
                {
                    int leftId = left?.uniqueID ?? int.MaxValue;
                    int rightId = right?.uniqueID ?? int.MaxValue;
                    return leftId.CompareTo(rightId);
                });
                restored.AddRange(current);

                Find.Maps.Clear();
                Find.Maps.AddRange(restored);

                int restoredIndex = currentMap != null
                    ? restored.IndexOf(currentMap)
                    : -1;
                if (restoredIndex < 0)
                    restoredIndex = 0;
                Current.Game.currentMapIndex = (sbyte)restoredIndex;
            }
            catch
            {
                // Restoring must never throw into the game loop.
            }
            finally
            {
                __state = null;
            }
        }
    }
}
