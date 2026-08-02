using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer already assigns negative IDs to objects created in the normal
    /// interface. Some long-event/update callbacks run while the game is playing,
    /// but outside both the interface marker and synchronized simulation. A Thing
    /// created there would otherwise consume a positive simulation ID on only one
    /// peer and shift every later ThingID-based schedule.
    /// </summary>
    internal static class Patch_UniqueIdSimulationBoundary
    {
        private static FieldInfo _localIdsOverrideField;
        private static PropertyInfo _clientProperty;
        private static PropertyInfo _tickingProperty;
        private static PropertyInfo _executingCmdsProperty;
        private static PropertyInfo _inInterfaceProperty;
        private static FieldInfo _reloadingField;
        private static int _authoritativeDeferredSimulationDepth;
        private static bool _loggedInterception;
        private static bool _loggedRuntimeFailure;
        private static bool _loggedAmbushExemption;
        private static bool _loggedGravshipExemption;

        private struct ScopeState
        {
            internal bool active;
            internal bool previousOverride;
        }

        internal static void Apply(Harmony harmony)
        {
            Type multiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            Type uniqueIdsPatchType =
                AccessTools.TypeByName("Multiplayer.Client.Patches.UniqueIdsPatch");

            _localIdsOverrideField = AccessTools.Field(uniqueIdsPatchType, "useLocalIdsOverride");
            _clientProperty = AccessTools.Property(multiplayerType, "Client");
            _tickingProperty = AccessTools.Property(multiplayerType, "Ticking");
            _executingCmdsProperty = AccessTools.Property(multiplayerType, "ExecutingCmds");
            _inInterfaceProperty = AccessTools.Property(multiplayerType, "InInterface");
            _reloadingField = AccessTools.Field(multiplayerType, "reloading");

            MethodInfo target = AccessTools.Method(
                typeof(UniqueIDsManager),
                "GetNextID",
                new[] { typeof(int).MakeByRefType() });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_UniqueIdSimulationBoundary), nameof(Prefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_UniqueIdSimulationBoundary), nameof(Finalizer));

            if (harmony == null || target == null || prefix == null || finalizer == null ||
                _localIdsOverrideField == null || _clientProperty == null ||
                _tickingProperty == null || _executingCmdsProperty == null ||
                _inInterfaceProperty == null || _reloadingField == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Unique-ID simulation boundary guard target " +
                    "resolution failed; non-simulation callbacks may still consume positive IDs.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

            bool factionCreatorScopePatched =
                TryPatchFactionCreatorDeferredSimulation(harmony);
            Log.Warning(
                "[MP-MeowOnlineShop] Unique-ID simulation boundary guard active: " +
                "playing-state allocations outside tick/command/reload use MP local IDs; " +
                "multifaction deferred simulation scope=" +
                (factionCreatorScopePatched ? "active." : "unavailable."));
        }

        private static void Prefix(ref ScopeState __state)
        {
            if (!MP.IsInMultiplayer)
                return;

            try
            {
                // A joining client re-runs Game.FinalizeInit/Notify_GameStarted
                // while Scribe is still loading the snapshot. Those allocations
                // must consume the deserialized positive counters so the client
                // matches the host's load-time IDs. Intercepting them here is
                // what made each rejoin drift the client's unique-ID counter by
                // one (Desync-193 through Desync-198). Scribe.mode is already
                // Inactive by FinalizeInit, so also treat any running LongEvent
                // as a load/generation boundary.
                if (Scribe.mode != LoadSaveMode.Inactive ||
                    IsLongEventActive())
                    return;

                // The Odyssey takeoff/landing and the caravan ambush map are
                // deterministic synchronized flows whose WorldObject/MapParent
                // creation runs inside a LongEvent queued by the synchronized
                // flow. Those must keep positive shared IDs because
                // Multiplayer serializes WorldObject arguments by ID. Local
                // negative IDs are not comparable across peers and made the
                // client desync right after the map was created.
                if (IsGravshipSynchronizedAllocation())
                    return;
                if (IsAmbushMapAllocation())
                    return;

                if (_authoritativeDeferredSimulationDepth > 0 ||
                    _clientProperty.GetValue(null, null) == null ||
                    ReadBool(_tickingProperty) ||
                    ReadBool(_executingCmdsProperty) ||
                    (bool)_reloadingField.GetValue(null) ||
                    Current.ProgramState != ProgramState.Playing)
                {
                    return;
                }

                __state.active = true;
                __state.previousOverride = (bool)_localIdsOverrideField.GetValue(null);
                _localIdsOverrideField.SetValue(null, true);

                // Normal interface allocations are already local in Multiplayer.
                // Log only the newly covered gap, once, so the next bundle identifies
                // the mod callback that attempted to pollute a positive counter.
                if (!_loggedInterception && !ReadBool(_inInterfaceProperty))
                {
                    _loggedInterception = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Prevented a positive unique-ID allocation " +
                        "outside synchronized simulation. Caller:\n" +
                        new StackTrace(2, false));
                }
            }
            catch (Exception e)
            {
                if (__state.active)
                {
                    try
                    {
                        _localIdsOverrideField.SetValue(null, __state.previousOverride);
                    }
                    catch
                    {
                    }
                    __state.active = false;
                }

                if (!_loggedRuntimeFailure)
                {
                    _loggedRuntimeFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Unique-ID simulation boundary guard failed " +
                        "open at runtime: " + e.Message);
                }
            }
        }

        private static bool IsGravshipSynchronizedAllocation()
        {
            try
            {
                var stack = new StackTrace(2, false);
                string[] executorFragments =
                {
                    "GenerateGravship",
                    "ArriveNewMap",
                    "ArriveExistingMap",
                    "RemoveGravshipFromMap",
                    "TakeoffEnded",
                    "LandingEnded",
                    "InitiateTakeoff",
                    "InitiateLanding",
                    "PlaceGravship",
                    "TravelTo",
                    "AbandonMap",
                    "BeginLanding",
                    "OnGravshipCaptureComplete"
                };
                for (int i = 0; i < stack.FrameCount; i++)
                {
                    MethodBase method = stack.GetFrame(i).GetMethod();
                    if (method == null)
                        continue;

                    string methodText = method.ToString();
                    if (methodText.IndexOf(
                            "Gravship",
                            StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    bool matchedExecutor = false;
                    for (int j = 0; j < executorFragments.Length; j++)
                    {
                        if (methodText.IndexOf(
                                executorFragments[j],
                                StringComparison.Ordinal) >= 0)
                        {
                            matchedExecutor = true;
                            break;
                        }
                    }

                    if (matchedExecutor)
                    {
                        if (!_loggedGravshipExemption)
                        {
                            _loggedGravshipExemption = true;
                            Log.Message(
                                "[MP-MeowOnlineShop] Gravship synchronized " +
                                "world-object allocation exemption active: " +
                                methodText);
                        }
                        return true;
                    }
                }
            }
            catch
            {
                // Failing open here only skips the exemption; the caller still
                // applies the normal simulation-boundary behavior.
            }
            return false;
        }

        private static bool IsAmbushMapAllocation()
        {
            try
            {
                var stack = new StackTrace(2, false);
                bool ambushIncident = false;
                bool caravanAttackMap = false;
                for (int i = 0; i < stack.FrameCount; i++)
                {
                    string methodText =
                        stack.GetFrame(i).GetMethod()?.ToString() ?? string.Empty;
                    if (methodText.IndexOf(
                            "IncidentWorker_Ambush",
                            StringComparison.Ordinal) >= 0)
                    {
                        ambushIncident = true;
                    }

                    if (methodText.IndexOf(
                            "CaravanIncidentUtility",
                            StringComparison.Ordinal) >= 0 &&
                        (methodText.IndexOf(
                             "SetupCaravanAttackMap",
                             StringComparison.Ordinal) >= 0 ||
                         methodText.IndexOf(
                             "GetOrGenerateMapForIncident",
                             StringComparison.Ordinal) >= 0 ||
                         methodText.IndexOf(
                             "GetOrGenerateMap",
                             StringComparison.Ordinal) >= 0))
                    {
                        caravanAttackMap = true;
                    }
                }

                if (ambushIncident && caravanAttackMap)
                {
                    if (!_loggedAmbushExemption)
                    {
                        _loggedAmbushExemption = true;
                        Log.Message(
                            "[MP-MeowOnlineShop] Synchronized caravan ambush map " +
                            "world-object allocation exemption active.");
                    }
                    return true;
                }
            }
            catch
            {
                // Failing open only skips the exemption.
            }
            return false;
        }

        private static Exception Finalizer(Exception __exception, ScopeState __state)
        {
            if (__state.active)
            {
                try
                {
                    _localIdsOverrideField.SetValue(null, __state.previousOverride);
                }
                catch (Exception e)
                {
                    if (!_loggedRuntimeFailure)
                    {
                        _loggedRuntimeFailure = true;
                        Log.Warning(
                            "[MP-MeowOnlineShop] Unique-ID simulation boundary guard " +
                            "could not restore MP's local-ID override: " + e.Message);
                    }
                }
            }

            return __exception;
        }

        private static bool ReadBool(PropertyInfo property)
        {
            return (bool)property.GetValue(null, null);
        }

        private static bool IsLongEventActive()
        {
            try
            {
                PropertyInfo property =
                    AccessTools.Property(typeof(LongEventHandler), "currentEvent") ??
                    AccessTools.Property(typeof(LongEventHandler), "CurrentEvent");
                if (property != null)
                    return property.GetValue(null, null) != null;

                FieldInfo field =
                    AccessTools.Field(typeof(LongEventHandler), "currentEvent");
                return field != null && field.GetValue(null) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryPatchFactionCreatorDeferredSimulation(Harmony harmony)
        {
            try
            {
                Type factionCreatorType =
                    AccessTools.TypeByName("Multiplayer.Client.Factions.FactionCreator");
                MethodInfo deferredBody = factionCreatorType?
                    .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(type => type.GetMethods(
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic))
                    .SingleOrDefault(method =>
                        string.Equals(
                            method.Name,
                            "<CreateFaction>b__0",
                            StringComparison.Ordinal) &&
                        method.ReturnType == typeof(void) &&
                        method.GetParameters().Length == 0);
                MethodInfo scopePrefix = AccessTools.Method(
                    typeof(Patch_UniqueIdSimulationBoundary),
                    nameof(AuthoritativeDeferredSimulationPrefix));
                MethodInfo scopeFinalizer = AccessTools.Method(
                    typeof(Patch_UniqueIdSimulationBoundary),
                    nameof(AuthoritativeDeferredSimulationFinalizer));

                if (deferredBody == null || scopePrefix == null || scopeFinalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Multiplayer FactionCreator deferred body " +
                        "was not resolved exactly; its synchronized long event is not " +
                        "exempted from the local-ID boundary guard.");
                    return false;
                }

                harmony.Patch(
                    deferredBody,
                    prefix: new HarmonyMethod(scopePrefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(scopeFinalizer) { priority = Priority.Last });
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Multiplayer FactionCreator deferred simulation " +
                    "scope patch failed: " + e.Message);
                return false;
            }
        }

        private static void AuthoritativeDeferredSimulationPrefix()
        {
            _authoritativeDeferredSimulationDepth++;
        }

        private static Exception AuthoritativeDeferredSimulationFinalizer(
            Exception __exception)
        {
            if (_authoritativeDeferredSimulationDepth > 0)
                _authoritativeDeferredSimulationDepth--;
            else
                _authoritativeDeferredSimulationDepth = 0;

            return __exception;
        }
    }
}
