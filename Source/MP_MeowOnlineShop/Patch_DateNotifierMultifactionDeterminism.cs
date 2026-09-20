using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-82 / Desync-94 / async-time season spam: Multiplayer's own
    /// DateNotifierPatch pushes each peer's local RealPlayerFaction before
    /// DateNotifier.DateNotifierTick runs and evaluates the season against a
    /// per-map async tick counter.  Map.IsPlayerHome is Faction.OfPlayer-
    /// relative, so the body then evaluates the season from each player's OWN
    /// home map: different maps, latitudes and async mapTicks per peer.  The
    /// season message (shared GetNextMessageID) and the first summer warning
    /// letter (shared GetNextLetterID) can therefore fire on one peer at a
    /// different world tick than the other, and switching map speeds can make
    /// the selected map's season jump back and forth, repeating the message.
    ///
    /// Fix: evaluate the world notifier against the monotonic shared world tick
    /// (AsyncWorldTime.worldTicks) on every multiplayer session, and only
    /// additionally pin the faction to Multiplayer's spectator faction while a
    /// multifaction session ticks the world notifier.  All peers then pick the
    /// same home map, the same season boundary tick and allocate the same IDs.
    ///
    /// The capture prefix must run BEFORE Multiplayer's DateNotifierPatch
    /// prefix (default priority), the enforce prefix/finalizer must nest
    /// INSIDE it, so this installs one Priority.First prefix and one
    /// Priority.Low prefix and Priority.First finalizer on DateNotifierTick.
    /// </summary>
    internal static class Patch_DateNotifierMultifactionDeterminism
    {
        private static PropertyInfo _worldCompProperty;
        private static PropertyInfo _asyncWorldTimeProperty;
        private static FieldInfo _worldTicksField;
        private static FieldInfo _spectatorFactionField;
        private static FieldInfo _ofPlayerField;

        private static bool _captureActive;
        private static int _capturedTicksGame;

        private static bool _enforced;
        private static int _savedTicksGame;
        private static Faction _savedFaction;
        private static bool _savedFactionSet;

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
            _asyncWorldTimeProperty = multiplayerType == null
                ? null
                : AccessTools.Property(multiplayerType, "AsyncWorldTime");
            _worldTicksField = _asyncWorldTimeProperty == null ||
                               _asyncWorldTimeProperty.PropertyType == null
                ? null
                : AccessTools.Field(
                    _asyncWorldTimeProperty.PropertyType,
                    "worldTicks");
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
            MethodInfo suppressPrefix = AccessTools.Method(
                typeof(Patch_DateNotifierMultifactionDeterminism), nameof(SuppressDateNotifierPrefix));

            if (target == null || capturePrefix == null || enforcePrefix == null ||
                restoreFinalizer == null || suppressPrefix == null || _worldCompProperty == null ||
                _asyncWorldTimeProperty == null || _worldTicksField == null ||
                _spectatorFactionField == null || _ofPlayerField == null ||
                _spectatorFactionField.FieldType != typeof(Faction))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] DateNotifier multifaction determinism target resolution failed; " +
                    "season messages/letters remain per-map and can repeat or desync.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(capturePrefix) { priority = Priority.First });
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(enforcePrefix) { priority = Priority.Low },
                finalizer: new HarmonyMethod(restoreFinalizer) { priority = Priority.First });
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(suppressPrefix) { priority = Priority.Last });

            Log.Message(
                "[MP-MeowOnlineShop] DateNotifier multifaction determinism active: " +
                "season evaluation uses the shared world tick and spectator faction context on every peer; " +
                "season notification letters/messages are suppressed in multiplayer to keep letter IDs deterministic.");
        }

        /// <summary>
        /// Runs before Multiplayer's DateNotifierPatch prefix and captures the
        /// shared world tick counter before Multiplayer replaces it with a
        /// per-map async tick count. The world tick is monotonic, so switching
        /// map speeds cannot make the season value oscillate.
        /// </summary>
        private static void CapturePrefix()
        {
            _captureActive = false;
            if (!MP.IsInMultiplayer)
                return;

            TickManager tickManager = Find.TickManager;
            object worldTime = _asyncWorldTimeProperty?.GetValue(null, null);
            if (tickManager == null || worldTime == null ||
                _worldTicksField == null)
            {
                return;
            }

            _capturedTicksGame = (int)_worldTicksField.GetValue(worldTime);
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
            _savedFactionSet = false;
            if (!_captureActive)
                return;

            TickManager tickManager = Find.TickManager;
            FactionManager factionManager = Find.FactionManager;
            if (tickManager == null)
                return;

            _savedTicksGame = tickManager.TicksGame;
            bool multifaction =
                MpRuntimeInfo.TryGetMultifactionActive(out bool active) &&
                active;
            try
            {
                if (multifaction && factionManager != null)
                {
                    Faction spectator = TryGetSpectatorFaction();
                    if (spectator == null)
                        return;

                    _savedFaction =
                        _ofPlayerField.GetValue(factionManager) as Faction;
                    _savedFactionSet = true;
                    _ofPlayerField.SetValue(factionManager, spectator);
                }

                _enforced = true;
                tickManager.DebugSetTicksGame(_capturedTicksGame);
            }
            catch (Exception e)
            {
                // Fail open: leaving Multiplayer's per-player context untouched
                // is strictly better than breaking DateNotifierTick.
                _enforced = false;
                _savedFaction = null;
                _savedFactionSet = false;
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
                if (factionManager != null && _savedFactionSet)
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
            _savedFactionSet = false;
        }

        private static bool SuppressDateNotifierPrefix()
        {
            // The vanilla body allocates a shared GetNextLetterID when the
            // season changes. Multiplayer's per-player DateNotifier context can
            // make that transition fire on one peer only (seen with Vehicle
            // Framework/Caravan UI loadouts). Season letters/messages are purely
            // informational; suppressing them on every multiplayer peer keeps
            // the letter-ID stream identical while single-player is unchanged.
            return !MP.IsInMultiplayer;
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
