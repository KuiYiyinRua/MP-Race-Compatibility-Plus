using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-82 / Desync-94: in a multifaction session Multiplayer's own
    /// DateNotifierPatch pushes each peer's local RealPlayerFaction before
    /// DateNotifier.DateNotifierTick runs.  Map.IsPlayerHome is
    /// Faction.OfPlayer-relative, so the body then evaluates the season from
    /// each player's OWN home map: different maps, latitudes and async mapTicks
    /// per peer.  The season message (shared GetNextMessageID) and the first
    /// summer warning letter (shared GetNextLetterID) therefore fire on one
    /// peer at a different world tick than the other, which desyncs the shared
    /// unique-ID stream and kicks the client into the rejoin loop.
    ///
    /// Fix: while a multifaction session ticks the world notifier, evaluate it
    /// under Multiplayer's spectator faction (the same context Multiplayer
    /// itself establishes for the world tick, where its
    /// Map_IsPlayerHome_Spectator_Patch makes every player's home map eligible)
    /// and under the real world TicksGame captured before Multiplayer's prefix
    /// shifted it to a per-player map tick count.  All peers then pick the same
    /// home map, the same season boundary tick and allocate the same IDs.
    ///
    /// The capture prefix must run BEFORE Multiplayer's DateNotifierPatch
    /// prefix (default priority), the enforce prefix/finalizer must nest
    /// INSIDE it, so this installs one Priority.First prefix and one
    /// Priority.Low prefix/finalizer pair on DateNotifierTick.
    /// </summary>
    internal static class Patch_DateNotifierMultifactionDeterminism
    {
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;
        private static FieldInfo _ofPlayerField;

        private static bool _captureActive;
        private static int _capturedTicksGame;

        private static bool _enforced;
        private static int _savedTicksGame;
        private static Faction _savedFaction;

        internal static void Apply(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(DateNotifier), "DateNotifierTick");

            // FactionManager.ofPlayer is internal to Assembly-CSharp; the
            // installed Multiplayer assembly writes it directly, this mod must
            // go through reflection.
            _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");

            Type multiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            _worldCompProperty = multiplayerType == null
                ? null
                : AccessTools.Property(multiplayerType, "WorldComp");
            _spectatorFactionField = null;
            if (_worldCompProperty != null && _worldCompProperty.PropertyType != null)
            {
                _spectatorFactionField = AccessTools.Field(
                    _worldCompProperty.PropertyType, "spectatorFaction");
            }

            MethodInfo capturePrefix = AccessTools.Method(
                typeof(Patch_DateNotifierMultifactionDeterminism), nameof(CapturePrefix));
            MethodInfo enforcePrefix = AccessTools.Method(
                typeof(Patch_DateNotifierMultifactionDeterminism), nameof(EnforcePrefix));
            MethodInfo restoreFinalizer = AccessTools.Method(
                typeof(Patch_DateNotifierMultifactionDeterminism), nameof(RestoreFinalizer));

            if (target == null || capturePrefix == null || enforcePrefix == null ||
                restoreFinalizer == null || _worldCompProperty == null ||
                _spectatorFactionField == null || _ofPlayerField == null ||
                _spectatorFactionField.FieldType != typeof(Faction))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DateNotifier multifaction determinism target resolution failed; " +
                    "season messages/letters remain per-player and can desync multifaction sessions.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(capturePrefix) { priority = Priority.First });
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(enforcePrefix) { priority = Priority.Low },
                finalizer: new HarmonyMethod(restoreFinalizer) { priority = Priority.Low });

            Log.Message(
                "[MP-MeowOnlineShop] DateNotifier multifaction determinism active: " +
                "season evaluation uses the shared spectator faction context and world ticks on every peer.");
        }

        /// <summary>
        /// Runs before Multiplayer's DateNotifierPatch prefix and captures the
        /// world tick counter before Multiplayer replaces it with the local
        /// player's home-map async tick count.
        /// </summary>
        private static void CapturePrefix()
        {
            _captureActive = false;
            if (!MP.IsInMultiplayer)
                return;
            if (!MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) || !multifaction)
                return;

            TickManager tickManager = Find.TickManager;
            if (tickManager == null)
                return;

            _capturedTicksGame = tickManager.TicksGame;
            _captureActive = true;
        }

        /// <summary>
        /// Runs after Multiplayer's DateNotifierPatch prefix and replaces its
        /// per-player faction/tick context with the shared spectator context so
        /// every peer evaluates the identical season transition.
        /// </summary>
        private static void EnforcePrefix()
        {
            _enforced = false;
            if (!_captureActive)
                return;

            TickManager tickManager = Find.TickManager;
            FactionManager factionManager = Find.FactionManager;
            Faction spectator = TryGetSpectatorFaction();
            if (tickManager == null || factionManager == null || spectator == null)
                return;

            _savedTicksGame = tickManager.TicksGame;
            try
            {
                _savedFaction = _ofPlayerField.GetValue(factionManager) as Faction;
                _enforced = true;

                _ofPlayerField.SetValue(factionManager, spectator);
                tickManager.DebugSetTicksGame(_capturedTicksGame);
            }
            catch (Exception e)
            {
                // Fail open: leaving Multiplayer's per-player context untouched
                // is strictly better than breaking DateNotifierTick.
                _enforced = false;
                _savedFaction = null;
                Log.Warning(
                    "[MP-MeowOnlineShop] DateNotifier multifaction context enforce failed: " + e.Message);
            }
        }

        /// <summary>
        /// Restores Multiplayer's per-player context; Multiplayer's own
        /// finalizer then unwraps its prefix state as usual.
        /// </summary>
        private static void RestoreFinalizer()
        {
            if (!_enforced)
                return;
            _enforced = false;

            try
            {
                FactionManager factionManager = Find.FactionManager;
                if (factionManager != null)
                    _ofPlayerField.SetValue(factionManager, _savedFaction);

                TickManager tickManager = Find.TickManager;
                if (tickManager != null)
                    tickManager.DebugSetTicksGame(_savedTicksGame);
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DateNotifier multifaction context restore failed: " + e.Message);
            }

            _savedFaction = null;
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
                // Fail open: leaving Multiplayer's per-player context untouched
                // is strictly better than breaking DateNotifierTick.
                return null;
            }
        }
    }
}
