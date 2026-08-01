using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Insect Girls gives its permanent passive hediff the vanilla
    /// HediffComp_HealPermanentWounds.  That comp stores a private random countdown.
    /// Old saves and async-map join snapshots can therefore reach the healing branch
    /// on only one peer.  Selecting a permanent wound and resetting the countdown then
    /// consumes a variable number of map Rand values and immediately desynchronizes the
    /// rest of the map tick.
    ///
    /// In multiplayer only, replace that private countdown for Insect Girls hediffs
    /// with a stateless, pawn-specific 15-30 day schedule.  Wound selection runs in a
    /// deterministic nested Rand scope, so it cannot advance the owning map's stream.
    /// Other users of HediffComp_HealPermanentWounds and all single-player behavior are
    /// left untouched.
    /// </summary>
    internal static class Patch_InsectGirlPermanentWoundMp
    {
        private const int TicksPerDay = 60000;
        private const int MinimumDays = 15;
        private const int DayRangeInclusive = 16;
        private const string InsectGirlHediffTypeName = "Kzi.Hediff_InsectGirlBuff";

        private static Type _insectGirlHediffType;

        internal static void Apply(Harmony harmony)
        {
            _insectGirlHediffType = AccessTools.TypeByName(InsectGirlHediffTypeName);
            MethodInfo target = AccessTools.Method(
                typeof(HediffComp_HealPermanentWounds),
                nameof(HediffComp_HealPermanentWounds.CompPostTickInterval));
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_InsectGirlPermanentWoundMp),
                nameof(CompPostTickIntervalPrefix));

            if (harmony == null || _insectGirlHediffType == null || target == null || prefix == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Insect Girls permanent-wound schedule patch skipped: " +
                    "target mod/type not present.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First });

            Log.Message(
                "[MP-MeowOnlineShop] Insect Girls permanent-wound MP schedule active: " +
                "stateless 15-30 day cadence and isolated wound-selection Rand.");
        }

        private static bool CompPostTickIntervalPrefix(
            HediffComp_HealPermanentWounds __instance,
            int delta)
        {
            if (!MP.IsInMultiplayer)
                return true;

            Hediff parent = __instance?.parent;
            Pawn pawn = parent?.pawn;
            if (parent == null || pawn == null ||
                _insectGirlHediffType == null ||
                !_insectGirlHediffType.IsInstanceOfType(parent))
            {
                return true;
            }

            int identity = Gen.HashCombineInt(
                pawn.thingIDNumber,
                parent.def?.shortHash ?? 0);
            int intervalDays = MinimumDays + (int)((uint)identity % DayRangeInclusive);
            int intervalTicks = intervalDays * TicksPerDay;

            if (!Gen.IsHashIntervalTick((Thing)pawn, intervalTicks, delta))
                return false;

            int cycle = Find.TickManager.TicksGame / intervalTicks;
            int seed = Gen.HashCombineInt(identity, cycle);
            Rand.PushState(seed);
            try
            {
                HediffComp_HealPermanentWounds.TryHealRandomPermanentWound(
                    pawn,
                    parent.LabelCap);
            }
            finally
            {
                Rand.PopState();
            }

            return false;
        }
    }

    /// <summary>
    /// Desync-151: the Insect Girls "阔儿来到这片地方觅吃了" auto-spawn runs in
    /// `Kzi.GameComponent_InsectGirl.GameComponentTick`, which calls
    /// `AutoSpawnNewPawn -> InsectGirlUtility.MapEdgeSpawner ->
    /// Find.RandomPlayerHomeMap`. `RandomPlayerHomeMap` is player-relative
    /// (`map.ParentFaction == Faction.OfPlayer`), so in multifaction/async
    /// sessions where the two peers tick under different
    /// `FactionManager.ofPlayer` contexts the auto-spawn can pick a different
    /// home map, register the new "kuoer" pawn in different maps' TickLists,
    /// and then desync the map ("Wrong random state on map 12").
    ///
    /// The 151 bundle shows the pawn-generation Rand sequence matching on both
    /// peers, then immediately after the spawn the Normal TickList members /
    /// map-local tick differ (host ticks `kuoer355591`, client ticks
    /// `Ostrich351869` at the same world tick). This is the same
    /// player-relative-evaluation class fixed for the caravan visit-site
    /// (3.0.85), Harbinger execution (3.0.86), work-site quest (3.0.78) and
    /// storyteller random quest (3.0.81).
    ///
    /// Fix: run the whole Insect Girls auto-spawn component tick under
    /// Multiplayer's spectator faction context, so `RandomPlayerHomeMap`
    /// evaluates the same map set on every peer. Singleplayer is untouched;
    /// failures fail open.
    /// </summary>
    internal static class Patch_InsectGirlSpawnFactionDeterminism
    {
        private const string InsectGirlComponentTypeName =
            "Kzi.GameComponent_InsectGirl";

        private static bool _applied;
        private static bool _loggedActive;
        private static bool _loggedFailure;
        private static FieldInfo _ofPlayerField;
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;

        private sealed class InsectGirlSpawnScopeState
        {
            internal bool Active;
            internal Faction SavedFaction;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type componentType =
                    AccessTools.TypeByName(InsectGirlComponentTypeName);
                MethodInfo target = componentType == null
                    ? null
                    : AccessTools.Method(
                        componentType,
                        "GameComponentTick",
                        Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_InsectGirlSpawnFactionDeterminism),
                    nameof(GameComponentTickPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_InsectGirlSpawnFactionDeterminism),
                    nameof(GameComponentTickFinalizer));

                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");
                Type multiplayerType = AccessTools.TypeByName(
                    "Multiplayer.Client.Multiplayer");
                _worldCompProperty = multiplayerType == null
                    ? null
                    : AccessTools.Property(multiplayerType, "WorldComp");
                _spectatorFactionField = null;
                if (_worldCompProperty?.PropertyType != null)
                {
                    _spectatorFactionField = AccessTools.Field(
                        _worldCompProperty.PropertyType,
                        "spectatorFaction");
                }

                if (target == null || prefix == null || finalizer == null ||
                    _ofPlayerField == null || _worldCompProperty == null ||
                    _spectatorFactionField == null ||
                    _spectatorFactionField.FieldType != typeof(Faction))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Insect Girls spawn faction " +
                        "determinism target resolution failed; auto-spawn can " +
                        "still desync.");
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
                    "[MP-MeowOnlineShop] Insect Girls auto-spawn faction " +
                    "determinism active: GameComponentTick runs under the " +
                    "spectator faction context on every peer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Insect Girls spawn faction " +
                    "determinism apply failed: " + e.Message);
            }
        }

        private static void GameComponentTickPrefix(
            ref InsectGirlSpawnScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer)
                return;

            try
            {
                Faction spectator = TryGetSpectatorFaction();
                FactionManager factionManager = Find.FactionManager;
                if (spectator == null || factionManager == null ||
                    _ofPlayerField == null)
                {
                    return;
                }

                Faction previous = _ofPlayerField.GetValue(
                    factionManager) as Faction;
                if (ReferenceEquals(previous, spectator))
                    return;

                _ofPlayerField.SetValue(factionManager, spectator);
                __state = new InsectGirlSpawnScopeState
                {
                    Active = true,
                    SavedFaction = previous
                };

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Insect Girls auto-spawn uses the " +
                        "spectator faction context (RandomPlayerHomeMap " +
                        "evaluation is now peer-identical).");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Insect Girls spawn faction " +
                        "context prefix failed open: " + e.Message);
                }
            }
        }

        private static Exception GameComponentTickFinalizer(
            Exception __exception,
            InsectGirlSpawnScopeState __state)
        {
            if (__state?.Active == true && _ofPlayerField != null)
            {
                try
                {
                    FactionManager factionManager = Find.FactionManager;
                    if (factionManager != null)
                        _ofPlayerField.SetValue(
                            factionManager,
                            __state.SavedFaction);
                }
                catch (Exception e)
                {
                    if (!_loggedFailure)
                    {
                        _loggedFailure = true;
                        Log.Warning(
                            "[MP-MeowOnlineShop] Insect Girls spawn faction " +
                            "context restore failed: " + e.Message);
                    }
                }
            }

            return __exception;
        }

        private static Faction TryGetSpectatorFaction()
        {
            try
            {
                object worldComp = _worldCompProperty?.GetValue(null, null);
                return worldComp == null
                    ? null
                    : _spectatorFactionField?.GetValue(worldComp) as Faction;
            }
            catch
            {
                return null;
            }
        }
    }
}
