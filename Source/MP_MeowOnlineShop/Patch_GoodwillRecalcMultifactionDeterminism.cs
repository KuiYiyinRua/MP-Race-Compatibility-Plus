using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-167: `GoodwillSituationManager.GoodwillManagerTick ->
    /// RecalculateAll -> Recalculate -> CheckHostilityChanged` calls
    /// `Faction.OfPlayer.Notify_GoodwillSituationsChanged(other, ...)`, which
    /// sends a relation-kind-changed letter through `GetNextLetterID` and
    /// mutates the relation only when that peer's player faction disagrees.
    /// In a multifaction session each peer's `Faction.OfPlayer` is its own
    /// faction, so the host's recalculation can change a faction's hostility
    /// and allocate the shared letter ID while the client stays on a different
    /// world path. The first divergent trace is exactly that: host
    /// `GoodwillSituationManager.RecalculateAll -> CheckHostilityChanged ->
    /// Notify_RelationKindChanged -> LetterMaker.MakeLetter -> GetNextLetterID`
    /// versus client `WorldPawnsTick` at the same tick.
    ///
    /// Fix: evaluate `RecalculateAll` under Multiplayer's shared spectator
    /// faction context in a multifaction session, so every peer makes the same
    /// hostility decision, mutates the same relation and allocates the same
    /// letter IDs. Singleplayer is untouched; failures fail open.
    /// </summary>
    internal static class Patch_GoodwillRecalcMultifactionDeterminism
    {
        private static bool _applied;
        private static bool _loggedActive;
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;
        private static FieldInfo _ofPlayerField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(GoodwillSituationManager),
                    "RecalculateAll",
                    new[] { typeof(bool) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_GoodwillRecalcMultifactionDeterminism),
                    nameof(RecalculateAllPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_GoodwillRecalcMultifactionDeterminism),
                    nameof(RecalculateAllFinalizer));

                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");
                Type multiplayerType = AccessTools.TypeByName(
                    "Multiplayer.Client.Multiplayer");
                _worldCompProperty = multiplayerType == null
                    ? null
                    : AccessTools.Property(multiplayerType, "WorldComp");
                _spectatorFactionField = null;
                if (_worldCompProperty != null &&
                    _worldCompProperty.PropertyType != null)
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
                        "[MP-MeowOnlineShop] Goodwill recalculation multifaction " +
                        "determinism target resolution failed; hostility letters " +
                        "can desync multifaction sessions.");
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
                    "[MP-MeowOnlineShop] Goodwill recalculation multifaction " +
                    "determinism active: RecalculateAll uses the shared spectator " +
                    "faction context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Goodwill recalculation multifaction " +
                    "determinism apply failed: " + e.Message);
            }
        }

        private static void RecalculateAllPrefix(ref Faction __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer)
                return;
            if (!MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) ||
                !multifaction)
            {
                return;
            }

            try
            {
                Faction spectator = TryGetSpectatorFaction();
                FactionManager factionManager = Find.FactionManager;
                if (spectator == null || factionManager == null)
                    return;

                __state = _ofPlayerField.GetValue(factionManager) as Faction;
                _ofPlayerField.SetValue(factionManager, spectator);

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Goodwill recalculation running under " +
                        "spectator faction context.");
                }
            }
            catch
            {
                __state = null;
            }
        }

        private static void RecalculateAllFinalizer(Faction __state)
        {
            if (__state == null)
                return;

            try
            {
                FactionManager factionManager = Find.FactionManager;
                if (factionManager != null)
                    _ofPlayerField.SetValue(factionManager, __state);
            }
            catch
            {
                // Fail open; Multiplayer continues with its own context.
            }
        }

        private static Faction TryGetSpectatorFaction()
        {
            try
            {
                object worldComp = _worldCompProperty.GetValue(null, null);
                return worldComp == null
                    ? null
                    : _spectatorFactionField.GetValue(worldComp) as Faction;
            }
            catch
            {
                return null;
            }
        }
    }
}
