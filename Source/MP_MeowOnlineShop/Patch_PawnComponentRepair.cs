using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// A failed spawn/despawn cycle (for example an exception thrown by
    /// Multiplayer's DeferredStackTracing postfix) can leave a pawn marked
    /// Spawned while its spawn-time components were removed. Vanilla then
    /// throws NullReferenceException every tick and every draw for that pawn,
    /// which floods the log and can break the log window itself. Recreate the
    /// missing components once, before the broken call sites run.
    /// </summary>
    internal static class Patch_PawnComponentRepair
    {
        private static bool _applied;
        private static AccessTools.FieldRef<object, Pawn> _tweenerPawnRef;
        private static MethodInfo _createInitialComponents;
        private static MethodInfo _addComponentsForSpawn;
        private static readonly HashSet<int> _repaired = new HashSet<int>();

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            MethodInfo tick = AccessTools.Method(typeof(Pawn), "Tick") ??
                              typeof(Pawn).GetMethod(
                                  "Tick",
                                  BindingFlags.Instance |
                                  BindingFlags.Public |
                                  BindingFlags.NonPublic);
            MethodInfo preDraw = AccessTools.Method(
                typeof(PawnTweener),
                "PreDrawPosCalculation");
            MethodInfo tickPrefix = AccessTools.Method(
                typeof(Patch_PawnComponentRepair),
                nameof(TickPrefix));
            MethodInfo tickFinalizer = AccessTools.Method(
                typeof(Patch_PawnComponentRepair),
                nameof(TickFinalizer));
            MethodInfo preDrawPrefix = AccessTools.Method(
                typeof(Patch_PawnComponentRepair),
                nameof(PreDrawPrefix));
            MethodInfo preDrawFinalizer = AccessTools.Method(
                typeof(Patch_PawnComponentRepair),
                nameof(PreDrawFinalizer));

            _createInitialComponents = AccessTools.Method(
                typeof(PawnComponentsUtility),
                "CreateInitialComponents",
                new[] { typeof(Pawn) });
            _addComponentsForSpawn = AccessTools.Method(
                typeof(PawnComponentsUtility),
                "AddComponentsForSpawn",
                new[] { typeof(Pawn) });

            if (tick == null || preDraw == null || tickPrefix == null ||
                tickFinalizer == null || preDrawPrefix == null ||
                preDrawFinalizer == null || _createInitialComponents == null ||
                _addComponentsForSpawn == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Broken spawned-pawn component repair " +
                    "targets not resolved; broken pawns can still spam errors.");
                return;
            }

            try
            {
                harmony.Patch(
                    tick,
                    prefix: new HarmonyMethod(tickPrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(tickFinalizer)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(
                    preDraw,
                    prefix: new HarmonyMethod(preDrawPrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(preDrawFinalizer)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Broken spawned-pawn component repair active.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Broken spawned-pawn component repair " +
                    "init failed: " + e.Message);
            }
        }

        private static bool TickPrefix(Pawn __instance, ref bool __state)
        {
            __state = false;
            if (__instance != null && __instance.Spawned &&
                __instance.pather == null)
            {
                __state = true;
                TryRepair(__instance);
            }
            return true;
        }

        private static Exception TickFinalizer(
            Exception __exception,
            Pawn __instance,
            bool __state)
        {
            // The repair can still fail on an exotic broken pawn. Keep the
            // game tickable instead of letting that pawn NRE every tick.
            return __state && __exception is NullReferenceException
                ? null
                : __exception;
        }

        private static bool PreDrawPrefix(
            PawnTweener __instance,
            ref bool __state)
        {
            __state = false;
            if (__instance == null)
                return true;

            if (_tweenerPawnRef == null)
                _tweenerPawnRef = AccessTools.FieldRefAccess<Pawn>(
                    typeof(PawnTweener),
                    "pawn");

            Pawn pawn = _tweenerPawnRef?.Invoke(__instance);
            if (pawn != null && pawn.Spawned && pawn.pather == null)
            {
                __state = true;
                TryRepair(pawn);
            }
            return true;
        }

        private static Exception PreDrawFinalizer(
            Exception __exception,
            bool __state)
        {
            // Drawing is cosmetic; a broken pawn must never take down the
            // whole Update/OnGUI loop with per-frame exceptions.
            return __state && __exception is NullReferenceException
                ? null
                : __exception;
        }

        private static void TryRepair(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || !pawn.Spawned ||
                pawn.pather != null)
            {
                return;
            }

            if (!_repaired.Add(pawn.thingIDNumber))
                return;

            if (_repaired.Count > 512)
                _repaired.Clear();

            try
            {
                _createInitialComponents.Invoke(null, new object[] { pawn });
                _addComponentsForSpawn.Invoke(null, new object[] { pawn });
                Log.Warning(
                    "[MP-MeowOnlineShop] Repaired spawned pawn with missing " +
                    "components: " + pawn.ThingID);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Spawned-pawn component repair failed " +
                    "for " + pawn.ThingID + ": " + e.Message);
            }
        }
    }
}
