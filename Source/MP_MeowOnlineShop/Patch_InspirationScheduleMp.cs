using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RimWorld 1.6 performs random inspiration checks from Pawn.TickInterval.
    /// TickInterval is driven by the serialized tickDelta/VTR scheduler. During
    /// a cold async-time rejoin, one peer can reach the catch-up boundary four
    /// ticks earlier and consume map Rand alone (Desync-50). For spawned map
    /// pawns, move only the random-start check to the end of every Thing.DoTick
    /// and gate it by the exact stable 100-tick pawn hash interval. Existing
    /// inspiration duration ticking remains in TickInterval unchanged.
    /// </summary>
    internal static class Patch_InspirationScheduleMp
    {
        private const int InspirationInterval = 100;
        private static MethodInfo _checkStartRandomInspiration;

        [ThreadStatic]
        private static bool _executingCanonicalCheck;

        internal static void Apply(Harmony harmony)
        {
            _checkStartRandomInspiration = AccessTools.Method(
                typeof(InspirationHandler),
                "CheckStartRandomInspiration");
            MethodInfo doTick = AccessTools.Method(typeof(Thing), nameof(Thing.DoTick));
            MethodInfo checkPrefix = AccessTools.Method(
                typeof(Patch_InspirationScheduleMp),
                nameof(CheckStartPrefix));
            MethodInfo doTickPostfix = AccessTools.Method(
                typeof(Patch_InspirationScheduleMp),
                nameof(ThingDoTickPostfix));

            if (_checkStartRandomInspiration == null || doTick == null ||
                checkPrefix == null || doTickPostfix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Canonical inspiration schedule target " +
                    "resolution failed; patch skipped.");
                return;
            }

            harmony.Patch(
                _checkStartRandomInspiration,
                prefix: new HarmonyMethod(checkPrefix)
                {
                    priority = Priority.First
                });
            harmony.Patch(
                doTick,
                postfix: new HarmonyMethod(doTickPostfix)
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Spawned-pawn inspiration checks use a " +
                "canonical 100-tick schedule in multiplayer.");
        }

        private static bool CheckStartPrefix(InspirationHandler __instance)
        {
            if (!MP.IsInMultiplayer || _executingCanonicalCheck)
                return true;

            Pawn pawn = __instance?.pawn;
            // Preserve vanilla scheduling for caravans, world pawns, and every
            // single-player path. Only async/map TickList pawns need this guard.
            return pawn == null || !pawn.Spawned || pawn.Map == null;
        }

        private static void ThingDoTickPostfix(Thing __instance)
        {
            if (!MP.IsInMultiplayer)
                return;

            Pawn pawn = __instance as Pawn;
            InspirationHandler handler = pawn?.mindState?.inspirationHandler;
            if (handler == null || !pawn.Spawned || pawn.Map == null ||
                !pawn.IsHashIntervalTick(InspirationInterval))
            {
                return;
            }

            _executingCanonicalCheck = true;
            try
            {
                _checkStartRandomInspiration.Invoke(handler, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
            finally
            {
                _executingCanonicalCheck = false;
            }
        }
    }
}
