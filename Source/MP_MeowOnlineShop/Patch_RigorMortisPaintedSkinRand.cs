using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-565: Rigor Mortis' painted-skin path duplicates a pawn. During
    /// AlienRace.AlienComp.CopyAlienData, lazy render-node construction may
    /// call AlienPartGenerator body-addon code which consumes Verse.Rand.
    /// Whether that render tree is already initialized is peer-local, so the
    /// render-only calls can advance the synchronized simulation stream on one
    /// peer but not the other.
    ///
    /// Keep the simulation Rand used by Duplicate/SetFaction/apparel copying
    /// untouched. Only the render initialization made inside
    /// CompYinAndMalevolent.TryDuplicatePawn runs in a deterministic nested
    /// scope. The AlienComp.CompRenderNodes hook remains as a fallback for
    /// render-node paths that bypass PawnRenderer.EnsureGraphicsInitialized.
    /// </summary>
    internal static class Patch_RigorMortisPaintedSkinRand
    {
        private const string RigorCompTypeName =
            "RigorMortis.CompYinAndMalevolent";
        private const string AlienCompTypeName =
            "AlienRace.AlienPartGenerator+AlienComp";
        private const int PaintedSkinRenderSeed = 0x50534B49; // "PSKI"

        [ThreadStatic]
        private static int _paintedSkinDuplicateDepth;

        [ThreadStatic]
        private static int _renderScopeDepth;

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
                Type rigorCompType = AccessTools.TypeByName(RigorCompTypeName);
                Type alienCompType = AccessTools.TypeByName(AlienCompTypeName);
                MethodInfo duplicateMethod = rigorCompType == null
                    ? null
                    : AccessTools.Method(
                        rigorCompType,
                        "TryDuplicatePawn",
                        new[]
                        {
                            typeof(Pawn),
                            typeof(Pawn).MakeByRefType(),
                            typeof(Faction)
                        });
                MethodInfo renderNodesMethod = alienCompType == null
                    ? null
                    : AccessTools.Method(
                        alienCompType,
                        "CompRenderNodes",
                        Type.EmptyTypes);
                MethodInfo graphicsInitMethod = AccessTools.Method(
                    typeof(PawnRenderer),
                    "EnsureGraphicsInitialized",
                    Type.EmptyTypes);
                MethodInfo duplicatePrefix = AccessTools.Method(
                    typeof(Patch_RigorMortisPaintedSkinRand),
                    nameof(PaintedSkinDuplicatePrefix));
                MethodInfo duplicateFinalizer = AccessTools.Method(
                    typeof(Patch_RigorMortisPaintedSkinRand),
                    nameof(PaintedSkinDuplicateFinalizer));
                MethodInfo renderPrefix = AccessTools.Method(
                    typeof(Patch_RigorMortisPaintedSkinRand),
                    nameof(AlienRenderNodesPrefix));
                MethodInfo renderFinalizer = AccessTools.Method(
                    typeof(Patch_RigorMortisPaintedSkinRand),
                    nameof(AlienRenderNodesFinalizer));
                MethodInfo graphicsInitPrefix = AccessTools.Method(
                    typeof(Patch_RigorMortisPaintedSkinRand),
                    nameof(PawnRendererGraphicsInitPrefix));
                MethodInfo graphicsInitFinalizer = AccessTools.Method(
                    typeof(Patch_RigorMortisPaintedSkinRand),
                    nameof(PawnRendererGraphicsInitFinalizer));

                if (rigorCompType == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] RigorMortis painted-skin Rand " +
                        "isolation skipped: CompYinAndMalevolent was not resolved.");
                    return;
                }

                if (duplicateMethod == null || renderNodesMethod == null ||
                    graphicsInitMethod == null ||
                    duplicatePrefix == null || duplicateFinalizer == null ||
                    renderPrefix == null || renderFinalizer == null ||
                    graphicsInitPrefix == null || graphicsInitFinalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] RigorMortis painted-skin Rand " +
                        "isolation skipped: target/signature changed " +
                        $"(duplicate={(duplicateMethod != null)}, " +
                        $"alienRender={(renderNodesMethod != null)}, " +
                        $"graphicsInit={(graphicsInitMethod != null)}).");
                    return;
                }

                harmony.Patch(
                    duplicateMethod,
                    prefix: new HarmonyMethod(duplicatePrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(duplicateFinalizer)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(
                    renderNodesMethod,
                    prefix: new HarmonyMethod(renderPrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(renderFinalizer)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(
                    graphicsInitMethod,
                    prefix: new HarmonyMethod(graphicsInitPrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(graphicsInitFinalizer)
                    {
                        priority = Priority.Last
                    });

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] RigorMortis painted-skin Rand " +
                        "isolation active: PawnRenderer.EnsureGraphicsInitialized " +
                        "and AlienRace render-node generation are isolated during " +
                        "CompYinAndMalevolent.TryDuplicatePawn.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] RigorMortis painted-skin Rand " +
                        "isolation apply failed: " + e.Message);
                }
            }
        }

        private static void PaintedSkinDuplicatePrefix(ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;

            _paintedSkinDuplicateDepth++;
            __state = true;
        }

        private static Exception PaintedSkinDuplicateFinalizer(
            Exception __exception,
            bool __state)
        {
            if (__state && _paintedSkinDuplicateDepth > 0)
                _paintedSkinDuplicateDepth--;
            return __exception;
        }

        private static bool BeginPaintedSkinRenderScope(Thing parent)
        {
            if (!MP.IsInMultiplayer || _paintedSkinDuplicateDepth <= 0 ||
                _renderScopeDepth > 0 || parent == null)
                return false;

            try
            {
                int seed = Gen.HashCombineInt(
                    PaintedSkinRenderSeed,
                    parent.thingIDNumber);
                seed = Gen.HashCombineInt(seed, parent.def?.shortHash ?? 0);

                Rand.PushState(seed);
                _renderScopeDepth++;
                return true;
            }
            catch (Exception e)
            {
                _renderScopeDepth = 0;
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] RigorMortis painted-skin Rand " +
                        "scope failed open: " + e.Message);
                }

                return false;
            }
        }

        private static bool BeginPaintedSkinRenderScope(int seed)
        {
            if (!MP.IsInMultiplayer || _paintedSkinDuplicateDepth <= 0 ||
                _renderScopeDepth > 0)
                return false;

            try
            {
                Rand.PushState(seed);
                _renderScopeDepth++;
                return true;
            }
            catch (Exception e)
            {
                _renderScopeDepth = 0;
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] RigorMortis painted-skin Rand " +
                        "scope failed open: " + e.Message);
                }

                return false;
            }
        }
        private static void EndPaintedSkinRenderScope(bool state)
        {
            if (!state)
                return;

            try
            {
                Rand.PopState();
            }
            finally
            {
                if (_renderScopeDepth > 0)
                    _renderScopeDepth--;
            }
        }

        private static void PawnRendererGraphicsInitPrefix(ref bool __state)
        {
            // This RimWorld reference does not expose PawnRenderer.pawn. The
            // renderer scope only needs a deterministic isolated Rand stream;
            // the AlienComp scope below still uses the concrete parent seed.
            __state = BeginPaintedSkinRenderScope(PaintedSkinRenderSeed);
        }

        private static Exception PawnRendererGraphicsInitFinalizer(
            Exception __exception,
            bool __state)
        {
            EndPaintedSkinRenderScope(__state);
            return __exception;
        }

        private static void AlienRenderNodesPrefix(
            ThingComp __instance,
            ref bool __state)
        {
            __state = BeginPaintedSkinRenderScope(__instance?.parent);
        }

        private static Exception AlienRenderNodesFinalizer(
            Exception __exception,
            bool __state)
        {
            EndPaintedSkinRenderScope(__state);
            return __exception;
        }
    }
}
