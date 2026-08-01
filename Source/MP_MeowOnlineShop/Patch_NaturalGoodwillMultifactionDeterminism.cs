using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-121/122: in a multifaction session each peer's `Faction.OfPlayer`
    /// points to its own player faction. `Faction.CheckReachNaturalGoodwill`
    /// compares `BaseGoodwillWith(OfPlayer)` and, when the natural-goodwill
    /// timer fires, calls `TryAffectGoodwillWith(OfPlayer, ...)` which records
    /// history events, changes relations, and allocates a shared
    /// `GetNextMessageID` through `Messages.Message`. The host trace shows that
    /// exact path (`FactionTick -> CheckReachNaturalGoodwill ->
    /// TryAffectGoodwillWith -> Message..ctor -> GetNextMessageID`) while the
    /// client stays on a different world path at the same tick, so only one
    /// peer advances the shared ID stream.
    ///
    /// Fix: while `CheckReachNaturalGoodwill` runs in a multifaction session,
    /// temporarily switch `FactionManager.ofPlayer` to Multiplayer's spectator
    /// faction (the same shared context Multiplayer uses for world-tick
    /// evaluation). Every peer then evaluates the same faction pair, advances
    /// the same `naturalGoodwillTimer`, and allocates the same IDs.
    /// </summary>
    internal static class Patch_NaturalGoodwillMultifactionDeterminism
    {
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;
        private static FieldInfo _ofPlayerField;
        private static bool _applied;
        private static bool _loggedActive;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(Faction),
                    "CheckReachNaturalGoodwill",
                    Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_NaturalGoodwillMultifactionDeterminism),
                    nameof(Prefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_NaturalGoodwillMultifactionDeterminism),
                    nameof(Finalizer));

                _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
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
                        "[MP-MeowOnlineShop] Natural-goodwill multifaction determinism " +
                        "target resolution failed; per-player goodwill messages can desync.");
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
                    "[MP-MeowOnlineShop] Natural-goodwill multifaction determinism active: " +
                    "CheckReachNaturalGoodwill uses the shared spectator faction context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Natural-goodwill multifaction determinism failed: " +
                    e.Message);
            }
        }

        private static void Prefix(ref Faction __state)
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
                        "[MP-MeowOnlineShop] Natural-goodwill check running under " +
                        $"spectator faction context.");
                }
            }
            catch
            {
                __state = null;
            }
        }

        private static void Finalizer(Faction __state)
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
                // Fail open; Multiplayer will continue with its own context.
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
