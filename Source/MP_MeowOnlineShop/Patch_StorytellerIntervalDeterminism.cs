using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-152: "一瞬間刷新大量的事件" - the storyteller world tick processes
    /// one interval of every story comp (OnOffCycle, CategoryMTB, RandomQuest,
    /// TaleOfMilira, ...) in a single burst. The 3.0.81 fix isolates only
    /// `NaturalRandomQuestChooser.ChooseNaturalRandomQuest`; in 152 the first
    /// divergent trace (tick 1849323) has the identical world Rand state on
    /// both peers but one peer is still in `OnOffCycle.GenerateIncident` while
    /// the other has already entered `CategoryMTB.MakeIntervalIncidents`, so
    /// the burst consumes the shared world Rand through different comps and
    /// desyncs ("Wrong random state for the world").
    ///
    /// Fix: in multiplayer, run the whole `Storyteller.StorytellerTick()` body
    /// inside an unconditional deterministic Rand scope seeded from the world
    /// tick and the current faction. Every comp's interval Rand then comes
    /// from the same isolated sequence on every peer instead of the live world
    /// stream, so the burst can no longer advance the synchronized world Rand
    /// asymmetrically. Singleplayer is untouched; failures fail open.
    /// </summary>
    internal static class Patch_StorytellerIntervalDeterminism
    {
        private const int StorytellerIntervalSeedOffset = 0x5354494E;
        private const int StorytellerIntervalWorldSeedOffset = 0x53545752;

        [ThreadStatic]
        private static Map _mapForRandPop;

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
                MethodInfo target = AccessTools.Method(
                    typeof(Storyteller),
                    "StorytellerTick",
                    Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_StorytellerIntervalDeterminism),
                    nameof(StorytellerTickPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_StorytellerIntervalDeterminism),
                    nameof(StorytellerTickFinalizer));

                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller interval determinism " +
                        "target resolution failed; event bursts can still desync.");
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
                    "[MP-MeowOnlineShop] Storyteller interval determinism active: " +
                    "the whole event burst runs in a deterministic Rand scope.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Storyteller interval determinism apply " +
                    "failed: " + e.Message);
            }
        }

        private static void StorytellerTickPrefix(ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer)
                return;

            try
            {
                int seed = Gen.HashCombineInt(
                    StorytellerIntervalSeedOffset,
                    Find.TickManager?.TicksAbs ?? 0);
                Faction faction = Faction.OfPlayer;
                seed = Gen.HashCombineInt(
                    seed,
                    faction?.loadID ?? 0);

                if (DeterministicRandScope.Begin(
                        null,
                        seed,
                        StorytellerIntervalWorldSeedOffset,
                        ref __state,
                        out Map mapForPop,
                        ignoreGate: true))
                {
                    _mapForRandPop = mapForPop;
                    if (!_loggedActive)
                    {
                        _loggedActive = true;
                        Log.Message(
                            "[MP-MeowOnlineShop] Storyteller interval uses a " +
                            "deterministic Rand scope " +
                            $"(faction={faction?.Name ?? "null"}).");
                    }
                }
                else
                {
                    __state = 0;
                }
            }
            catch (Exception e)
            {
                __state = 0;
                _mapForRandPop = null;
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storyteller interval determinism " +
                        "prefix failed open: " + e.Message);
                }
            }
        }

        private static Exception StorytellerTickFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
            {
                try
                {
                    DeterministicRandScope.End(__state, _mapForRandPop);
                }
                finally
                {
                    _mapForRandPop = null;
                }
            }

            return __exception;
        }
    }
}
