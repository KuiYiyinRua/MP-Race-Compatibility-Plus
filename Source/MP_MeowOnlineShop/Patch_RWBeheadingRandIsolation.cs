using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-150: RW Beheading captures a pawn's head texture synchronously
    /// from simulation paths (`Hediff_MissingPart.PostAdd_Prefix` and
    /// `Pawn_Kill_Prefix`). `HeadCaptureUtility.TryCaptureHead` renders the
    /// pawn with an actual Unity `Camera.Render`, and the pawn render tree /
    /// Alien-Race body addons / wound overlays consume `Verse.Rand` during
    /// that capture.
    ///
    /// In the 150 bundle the first divergent trace (record 612, tick 1810524)
    /// has the identical map Rand state on both peers and the same pawn
    /// (`Human354750`), but the head-capture render walks different graphic
    /// branches (host `AlienPartGenerator.ExtendedGraphicTop.GetPath`, client
    /// `PawnWoundDrawer.WriteCache`) and therefore consumes the live map Rand
    /// stream differently. The desync is reported on map 1 right after the
    /// Harbinger tree / combat damage sequence.
    ///
    /// Fix: in multiplayer, run the whole `TryCaptureHead` body inside a
    /// deterministic nested Rand scope seeded from the pawn identity and the
    /// current game tick, so the rendering Rand can never advance the live
    /// synchronized stream and both peers consume the same isolated sequence.
    /// Singleplayer is untouched; failures fail open.
    /// </summary>
    internal static class Patch_RWBeheadingRandIsolation
    {
        private const string HeadCaptureUtilityTypeName =
            "RWBeheading.HeadCaptureUtility";
        private const int BeheadingSeedOffset = 0x48454144;

        [ThreadStatic]
        private static int _activeScopeDepth;

        private static bool _applied;
        private static bool _loggedActive;
        private static bool _loggedFailure;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type headCaptureType =
                    AccessTools.TypeByName(HeadCaptureUtilityTypeName);
                MethodInfo target = headCaptureType == null
                    ? null
                    : AccessTools.Method(
                        headCaptureType,
                        "TryCaptureHead",
                        new[] { typeof(Pawn), typeof(RotDrawMode) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_RWBeheadingRandIsolation),
                    nameof(TryCaptureHeadPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_RWBeheadingRandIsolation),
                    nameof(TryCaptureHeadFinalizer));

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] RWBeheading head-capture Rand " +
                        "isolation skipped (target mod not active or signature " +
                        "changed).");
                    return;
                }

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

                Log.Message(
                    "[MP-MeowOnlineShop] RWBeheading head-capture Rand isolation " +
                    "active: capture rendering uses a deterministic nested scope.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] RWBeheading head-capture Rand isolation " +
                    "apply failed: " + e.Message);
            }
        }

        private static void TryCaptureHeadPrefix(
            Pawn pawn,
            ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || pawn == null || _activeScopeDepth > 0)
                return;

            try
            {
                int seed = Gen.HashCombineInt(
                    BeheadingSeedOffset,
                    pawn.thingIDNumber);
                seed = Gen.HashCombineInt(
                    seed,
                    pawn.def?.shortHash ?? 0);
                seed = Gen.HashCombineInt(
                    seed,
                    Find.TickManager?.TicksGame ?? 0);

                Rand.PushState(seed);
                _activeScopeDepth = 1;
                __state = true;

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] RWBeheading head capture uses a " +
                        "deterministic nested Rand scope " +
                        $"(pawn={pawn.thingIDNumber}).");
                }
            }
            catch (Exception e)
            {
                __state = false;
                _activeScopeDepth = 0;
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] RWBeheading head-capture Rand " +
                        "isolation prefix failed open: " + e.Message);
                }
            }
        }

        private static Exception TryCaptureHeadFinalizer(
            Exception __exception,
            bool __state)
        {
            if (__state)
            {
                try
                {
                    Rand.PopState();
                }
                finally
                {
                    _activeScopeDepth = 0;
                }
            }

            return __exception;
        }
    }
}
