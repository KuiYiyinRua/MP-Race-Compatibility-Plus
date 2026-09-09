using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer uses Game.CurrentMap both for the player's viewed map and as
    /// a temporary simulation context while ticking async maps.  Its VTR patch
    /// must only publish the former.  Publishing the temporary simulation swap
    /// makes the server believe a player moved to another map/world and can make
    /// standalone map streaming omit commands for the map the player is really
    /// viewing.
    /// </summary>
    internal static class Patch_MultiplayerVtrContextGuard
    {
        private static readonly int InvalidMapId = -1;

        private static Type _vtrType;
        private static MethodInfo _vtrReset;
        private static FieldInfo _lastMovedToMapId;
        private static FieldInfo _reloadingField;
        private static PropertyInfo _tickingProperty;
        private static bool _loggedSuppression;
        private static bool _loggedInitialNormalization;
        private static bool _loggedRemovedMapNormalization;
        private static bool _forceInitialMapReport;
        private static bool _hasInitialMapReport;

        internal static void Apply(Harmony harmony)
        {
            _vtrType = AccessTools.TypeByName("Multiplayer.Client.Patches.VTRSync");
            if (_vtrType != null)
            {
                _vtrReset = AccessTools.Method(_vtrType, "Reset", Type.EmptyTypes);
                _lastMovedToMapId = AccessTools.Field(_vtrType, "lastMovedToMapId");
            }

            PatchDeterministicSimulationVtr(harmony);

            PatchVtrMapReport(harmony);
            PatchSessionBoundaries(harmony);

            MethodInfo target = _vtrType == null
                ? null
                : AccessTools.Method(
                    _vtrType,
                    "SendViewedMapUpdate",
                    new[] { typeof(int), typeof(int) });

            Type multiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            _reloadingField = AccessTools.Field(multiplayerType, "reloading");
            _tickingProperty = AccessTools.Property(multiplayerType, "Ticking");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MultiplayerVtrContextGuard),
                nameof(Prefix));

            if (target == null || prefix == null ||
                _tickingProperty == null || _tickingProperty.PropertyType != typeof(bool))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Multiplayer VTR context guard target resolution failed; " +
                    "temporary async-map CurrentMap changes are not filtered.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First });

            Log.Message(
                "[MP-MeowOnlineShop] Multiplayer VTR context guard active: " +
                "simulation-only CurrentMap transitions are not published as player view changes.");
        }

        private static void PatchVtrMapReport(Harmony harmony)
        {
            MethodInfo target = _vtrType == null
                ? null
                : AccessTools.Method(
                    _vtrType,
                    "SendViewedMapUpdate",
                    new[] { typeof(int), typeof(int) });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MultiplayerVtrContextGuard),
                nameof(NormalizeVtrMapReport));

            if (target == null || prefix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] VTR map-report normalization target resolution failed.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });
        }

        private static void PatchSessionBoundaries(Harmony harmony)
        {
            PatchBoundary(
                harmony,
                AccessTools.TypeByName("Multiplayer.Client.HostUtil"),
                "CreateSession",
                nameof(BeginVtrSession));
            PatchBoundary(
                harmony,
                AccessTools.TypeByName("Multiplayer.Client.Rejoiner"),
                "DoRejoin",
                nameof(BeginVtrRejoin));
            PatchBoundary(
                harmony,
                AccessTools.TypeByName("Multiplayer.Client.ClientJoiningState"),
                "StartState",
                nameof(BeginVtrSession));
        }

        private static void PatchBoundary(
            Harmony harmony,
            Type owner,
            string methodName,
            string prefixName)
        {
            MethodInfo target = owner == null
                ? null
                : AccessTools.Method(owner, methodName);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MultiplayerVtrContextGuard),
                prefixName);
            if (target == null || prefix == null)
                return;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });
        }

        private static void BeginVtrSession()
        {
            try
            {
                _vtrReset?.Invoke(null, null);
            }
            catch
            {
                try
                {
                    _lastMovedToMapId?.SetValue(null, InvalidMapId);
                }
                catch
                {
                    // A changed Multiplayer runtime must not block a session boundary.
                }
            }

            _forceInitialMapReport = true;
            _hasInitialMapReport = false;
        }

        private static void BeginVtrRejoin()
        {
            int lastMovedToMapId = ReadLastMovedToMapId();
            if (lastMovedToMapId == InvalidMapId)
            {
                BeginVtrSession();
                return;
            }

            try
            {
                _vtrReset?.Invoke(null, null);
                _lastMovedToMapId?.SetValue(null, lastMovedToMapId);
            }
            catch
            {
                try
                {
                    _lastMovedToMapId?.SetValue(null, lastMovedToMapId);
                }
                catch
                {
                    // A changed Multiplayer runtime must not block rejoin.
                }
            }

            // ServerLoadingState preserves ServerPlayer.currentMapId during
            // rejoin, so the first post-load report must continue from the
            // last valid viewed map instead of fabricating -1 -> current.
            _forceInitialMapReport = false;
            _hasInitialMapReport = true;
        }

        private static bool NormalizeVtrMapReport(ref int previous, int current)
        {
            if (!MP.IsInMultiplayer || current < 0)
                return true;

            // Multiplayer intentionally drops VTR updates while its snapshot
            // loader is active. Keep the initial-report marker armed so the
            // first post-load update is still sent as -1 -> current map.
            if (_forceInitialMapReport && IsMultiplayerReloading())
                return true;

            int lastMovedToMapId = ReadLastMovedToMapId();
            if (_forceInitialMapReport)
            {
                // HostUtil.CreateSession and ClientJoiningState.StartState
                // start a new initial-loading boundary. The vanilla client
                // can still compute the old viewed map from Game.CurrentMap
                // while loading, producing a false "1 -> 103" transition.
                previous = InvalidMapId;
                _forceInitialMapReport = false;
                _hasInitialMapReport = true;
                if (!_loggedInitialNormalization)
                {
                    _loggedInitialNormalization = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Normalized the initial VTR map report " +
                        "to -1 -> current map after host/initial-join.");
                }
                return true;
            }

            if (_hasInitialMapReport &&
                previous == InvalidMapId &&
                lastMovedToMapId >= 0 &&
                lastMovedToMapId != current)
            {
                // During DeinitAndRemoveMap, vanilla GetPreviousMapId returns
                // -1 for the transient invalid currentMapIndex.  That is not
                // a real player-view transition; the server still tracks the
                // last real map and validates the previous ID strictly.
                previous = lastMovedToMapId;
                if (!_loggedRemovedMapNormalization)
                {
                    _loggedRemovedMapNormalization = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Normalized a removed-map VTR " +
                        $"transition to {previous} -> {current}.");
                }
            }

            return true;
        }

        private static bool IsMultiplayerReloading()
        {
            try
            {
                return _reloadingField != null &&
                       (bool)_reloadingField.GetValue(null);
            }
            catch
            {
                // A changed Multiplayer runtime must not block a real view update.
                return false;
            }
        }

        private static int ReadLastMovedToMapId()
        {
            try
            {
                return _lastMovedToMapId == null
                    ? InvalidMapId
                    : (int)_lastMovedToMapId.GetValue(null);
            }
            catch
            {
                return InvalidMapId;
            }
        }

        /// <summary>
        /// Multiplayer's per-map/world CurrentPlayerCount is runtime-only and is not
        /// serialized by AsyncTimeComp.ExposeData. A cold rejoin can therefore start
        /// with VTR=15 while the long-running host still has VTR=1 for the same map.
        /// Pawn.UpdateRateTicks then reaches TickInterval boundaries on different
        /// ticks. Simulation must not depend on which UI map a local player views,
        /// so multiplayer always uses the fully updated rate (1).
        /// </summary>
        private static void PatchDeterministicSimulationVtr(Harmony harmony)
        {
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MultiplayerVtrContextGuard),
                nameof(StableMinimumVtrPrefix));
            if (harmony == null || prefix == null)
                return;

            int patched = 0;
            foreach (string[] typeNames in new[]
                     {
                         new[] { "Multiplayer.Client.AsyncTimeComp" },
                         new[]
                         {
                             "Multiplayer.Client.AsyncTime.AsyncWorldTimeComp",
                             "Multiplayer.Client.AsyncWorldTimeComp"
                         }
                     })
            {
                Type type = null;
                foreach (string typeName in typeNames)
                {
                    type = AccessTools.TypeByName(typeName);
                    if (type != null)
                        break;
                }
                MethodInfo getter = type == null
                    ? null
                    : AccessTools.PropertyGetter(type, "VTR");
                if (getter == null || getter.ReturnType != typeof(int))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Deterministic VTR target resolution failed: " +
                        string.Join(" | ", typeNames) + ".VTR.");
                    continue;
                }

                harmony.Patch(
                    getter,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                patched++;
            }

            if (patched > 0)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Deterministic multiplayer simulation VTR active: " +
                    $"patched={patched}, rate=1. Map/world TickInterval scheduling no longer " +
                    "depends on process-local viewed-map player counts.");
            }
        }

        private static bool StableMinimumVtrPrefix(ref int __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            __result = 1;
            return false;
        }

        private static bool Prefix(int previous, int current)
        {
            if (!MP.IsInMultiplayer)
                return true;

            bool ticking;
            try
            {
                ticking = (bool)_tickingProperty.GetValue(null, null);
            }
            catch
            {
                // Fail open if the installed Multiplayer runtime changed after
                // startup; blocking a real view update would be worse.
                return true;
            }

            if (!ticking && !MP.IsExecutingSyncCommand)
                return true;

            if (!_loggedSuppression)
            {
                _loggedSuppression = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Suppressed simulation-only VTR map transition " +
                    $"{previous}->{current}.");
            }

            return false;
        }
    }
}
