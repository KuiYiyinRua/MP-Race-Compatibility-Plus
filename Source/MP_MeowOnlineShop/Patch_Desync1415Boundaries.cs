using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    // Desync-14 starts after an earlier, unrecorded RNG/ID divergence; its
    // first saved action is DebugToolsGeneral.Kill. Desync-15 first diverges
    // at a successful job completion, not at Melee Animation's next draw.
    // These fixes cover verified unsafe boundaries, not a proven complete
    // explanation of either bundle. No gameplay RNG is suppressed here.
    internal static class Patch_Desync1415Boundaries
    {
        private static Func<Need, bool> evaluateOverflow;
        private static FieldInfo syncNeedLevel;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                var kill = AccessTools.Method(typeof(DebugToolsGeneral), "Kill", Type.EmptyTypes);
                if (kill == null || kill.ReturnType != typeof(void))
                    throw new MissingMethodException("DebugToolsGeneral.Kill()");
                harmony.Patch(kill, prefix: new HarmonyMethod(typeof(Patch_Desync1415Boundaries), nameof(KillPrefix)));
                Log.Message("[MP-MeowOnlineShop] Debug Kill: stable thing order; local motes excluded.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILURE Debug Kill boundary: " + e);
            }

            var drawType = AccessTools.TypeByName("NeedBarOverflow.Patches.Need_DrawOnGUI");
            if (drawType == null) return;
            try
            {
                var common = AccessTools.TypeByName("NeedBarOverflow.DisableNeedOverflow.Common");
                var evaluate = AccessTools.Method(common, "CanOverflow_Evaluate", new[] { typeof(Need) });
                var query = AccessTools.Method(common, "CanOverflow", new[] { typeof(Need) });
                var draw = AccessTools.Method(drawType, "ShowDevGizmos", new[] { typeof(Need), typeof(Rect) });
                syncNeedLevel = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.SyncFields"), "SyncNeedLevel");
                if (evaluate == null || !evaluate.IsStatic || evaluate.ReturnType != typeof(bool)
                    || query == null || !query.IsStatic || query.ReturnType != typeof(bool)
                    || draw == null || !draw.IsStatic || draw.ReturnType != typeof(void)
                    || syncNeedLevel == null || !syncNeedLevel.IsStatic
                    || !typeof(ISyncField).IsAssignableFrom(syncNeedLevel.FieldType))
                    throw new MissingMethodException("Need Bar Overflow / Multiplayer need synchronization API");

                evaluateOverflow = (Func<Need, bool>)Delegate.CreateDelegate(typeof(Func<Need, bool>), evaluate);
                harmony.Patch(query, prefix: new HarmonyMethod(typeof(Patch_Desync1415Boundaries), nameof(OverflowPrefix)));
                harmony.Patch(draw,
                    prefix: new HarmonyMethod(typeof(Patch_Desync1415Boundaries), nameof(NeedButtonsPrefix)),
                    finalizer: new HarmonyMethod(typeof(Patch_Desync1415Boundaries), nameof(NeedButtonsFinalizer)));
                Log.Message("[MP-MeowOnlineShop] Need Bar Overflow: dev buttons use MP's existing need field; live overflow eligibility in MP.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILURE Need Bar Overflow boundary: " + e);
            }
        }

        private static bool KillPrefix()
        {
            if (!MP.IsInMultiplayer) return true;
            var map = Find.CurrentMap;
            var cell = UI.MouseCell(); // MP DebugSync supplies the command's cursor.
            if (map == null || !cell.InBounds(map)) return false;
            // Snapshot before killing: death/explosion callbacks change the grid.
            // Never sort the live grid or include process-local visual objects.
            var targets = map.thingGrid.ThingsAt(cell)
                .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                .OrderBy(t => t.thingIDNumber).ToArray();
            foreach (var thing in targets)
                if (!thing.Destroyed) thing.Kill();
            return false;
        }

        private static bool OverflowPrefix(Need __0, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            // The original cache uses object hashes and a local last-query tick.
            // UI queries and async maps must not seed eligibility for simulation.
            // Direct evaluation also makes button-side cache removal irrelevant.
            __result = evaluateOverflow(__0);
            return false;
        }

        private static bool NeedButtonsPrefix(Need __0, out bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            // NBO changes the need inside a prefix, BEFORE MP's DrawOnGUI watch.
            // A nested scope restores the value before the outer watcher sees it.
            // Reuse the native field/serializer/debug permission, never register
            // a second handler for the same field.
            var field = syncNeedLevel.GetValue(null) as ISyncField;
            if (field == null)
            {
                Log.ErrorOnce("[MP-MeowOnlineShop] MP need field unavailable; overflow dev buttons blocked.", 1415001);
                return false;
            }
            MP.WatchBegin();
            __state = true;
            field.Watch(__0);
            return true;
        }

        private static void NeedButtonsFinalizer(bool __state)
        {
            if (__state) MP.WatchEnd();
        }
    }
}
