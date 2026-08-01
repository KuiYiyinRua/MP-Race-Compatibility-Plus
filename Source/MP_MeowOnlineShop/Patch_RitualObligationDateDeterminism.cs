using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-163: Ideology date-triggered ritual obligations ("festivals")
    /// run inside `IdeoManager.IdeoManagerTick` on every world tick.
    /// `RitualObligationTrigger_Date.Tick` gates the obligation creation on
    /// `Faction.OfPlayer.ideos.Has(ritual.ideo)` (when `mustBePlayerIdeo`) and
    /// derives the trigger tick from `Find.AnyPlayerHomeMap` longitude. In a
    /// multifaction session each peer's `Faction.OfPlayer` points to its own
    /// player faction, so the host's ideo festival creates a `RitualObligation`
    /// (allocating `GetNextRitualObligationID`, mutating `activeObligations`
    /// and sending a letter) on one peer only while the client stays on a
    /// different world path. The first divergent trace is exactly that:
    /// host `IdeoManagerTick -> RitualObligationTrigger_Date -> GetNextRitualObligationID`
    /// versus client `WorldPawnsTick -> InspirationHandler` at the same tick.
    ///
    /// Fix: while the date trigger ticks in a multifaction session, pin
    /// `FactionManager.ofPlayer` to the faction that owns the ritual's ideo on
    /// every peer, so the eligibility check and home-map longitude evaluate
    /// identically and the obligation is created on both peers. Singleplayer is
    /// untouched; resolution failures fail open.
    /// </summary>
    internal static class Patch_RitualObligationDateDeterminism
    {
        private static bool _applied;
        private static bool _loggedActive;
        private static FieldInfo _ofPlayerField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(RitualObligationTrigger_Date), "Tick");
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_RitualObligationDateDeterminism),
                    nameof(TickPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_RitualObligationDateDeterminism),
                    nameof(TickFinalizer));

                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");

                if (target == null || prefix == null || finalizer == null ||
                    _ofPlayerField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ritual obligation date determinism " +
                        "target resolution failed; date festivals can desync " +
                        "multifaction sessions.");
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
                    "[MP-MeowOnlineShop] Ritual obligation date determinism active: " +
                    "date-trigger festivals use the owning ideo's faction context.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ritual obligation date determinism apply " +
                    "failed: " + e.Message);
            }
        }

        private static void TickPrefix(
            RitualObligationTrigger_Date __instance,
            ref Faction __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || __instance?.ritual?.ideo == null)
                return;
            if (!MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) ||
                !multifaction)
            {
                return;
            }

            try
            {
                Faction owner = FindOwnerFaction(__instance.ritual.ideo);
                FactionManager factionManager = Find.FactionManager;
                if (owner == null || factionManager == null)
                    return;

                __state = _ofPlayerField.GetValue(factionManager) as Faction;
                _ofPlayerField.SetValue(factionManager, owner);

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Ritual obligation date trigger running " +
                        "under owning-ideo faction context.");
                }
            }
            catch
            {
                __state = null;
            }
        }

        private static void TickFinalizer(Faction __state)
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

        private static Faction FindOwnerFaction(Ideo ideo)
        {
            return Find.FactionManager?.AllFactionsListForReading?
                .Where(faction => faction?.ideos != null && faction.ideos.Has(ideo))
                .OrderBy(faction => faction.loadID)
                .FirstOrDefault();
        }
    }
}
