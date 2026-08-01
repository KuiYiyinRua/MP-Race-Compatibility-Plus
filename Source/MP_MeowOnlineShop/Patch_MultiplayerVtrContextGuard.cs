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
        private static PropertyInfo _tickingProperty;
        private static bool _loggedSuppression;

        internal static void Apply(Harmony harmony)
        {
            PatchDeterministicSimulationVtr(harmony);

            Type vtrType = AccessTools.TypeByName("Multiplayer.Client.Patches.VTRSync");
            MethodInfo target = vtrType == null
                ? null
                : AccessTools.Method(
                    vtrType,
                    "SendViewedMapUpdate",
                    new[] { typeof(int), typeof(int) });

            Type multiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
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
            foreach (string typeName in new[]
                     {
                         "Multiplayer.Client.AsyncTimeComp",
                         "Multiplayer.Client.AsyncWorldTimeComp"
                     })
            {
                Type type = AccessTools.TypeByName(typeName);
                MethodInfo getter = type == null
                    ? null
                    : AccessTools.PropertyGetter(type, "VTR");
                if (getter == null || getter.ReturnType != typeof(int))
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Deterministic VTR target resolution failed: " +
                        typeName + ".VTR.");
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
