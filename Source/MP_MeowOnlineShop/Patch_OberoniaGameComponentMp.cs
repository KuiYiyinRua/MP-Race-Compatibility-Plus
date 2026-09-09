using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-327/661: OberoniaAurea.GameComponent_OberoniaAurea seeds
    /// interactHashOffset from Rand.Range in its constructor and never
    /// serializes it. The loadout also constructs several GameComponent
    /// instances during a rejoin, so each instance can run TickDay on a
    /// peer-local schedule and consume the shared world Rand stream at
    /// different points. In multiplayer, tick only the canonical Instance and
    /// drive TickDay from a fixed 60,000-tick world cadence.
    ///
    /// The Odyssey science department's annual interaction additionally calls
    /// a nested GiveQuest incident whose CanFireNow result can differ between
    /// peers. Its automatic TickDay path therefore must not execute the annual
    /// action locally. Publish a host-only in-game-loop SyncField command and
    /// replay the exact method on every peer under Multiplayer's command
    /// context; singleplayer keeps the original path.
    /// </summary>
    internal static class Patch_OberoniaGameComponentMp
    {
        private const string ComponentTypeName =
            "OberoniaAurea.GameComponent_OberoniaAurea";
        private const int DayIntervalTicks = 60000;

        private static bool _applied;
        private static Type _componentType;
        private static PropertyInfo _instanceProperty;
        private static FieldInfo _interactHandlerField;
        private static FieldInfo _scienceDepartmentField;
        private static FieldInfo _specialManagerField;
        private static MethodInfo _interactTickDayMethod;
        private static MethodInfo _scienceDepartmentTickDayMethod;
        private static MethodInfo _scienceAnnualInteractionMethod;
        private static MethodInfo _specialManagerTickMethod;
        private static ISyncField _annualInteractionSyncField;
        private static int _annualInteractionCommandTick;
        private static bool _annualSyncFailureLogged;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                _componentType = AccessTools.TypeByName(ComponentTypeName);
                MethodInfo target = _componentType == null
                    ? null
                    : AccessTools.Method(
                        _componentType,
                        "GameComponentTick",
                        Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_OberoniaGameComponentMp),
                    nameof(GameComponentTickPrefix));
                if (target == null || prefix == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Oberonia Aurea GameComponent " +
                        "determinism skipped: component or tick method was " +
                        "not resolved.");
                    return;
                }

                _instanceProperty =
                    AccessTools.Property(_componentType, "Instance");
                _interactHandlerField =
                    AccessTools.Field(_componentType, "interactHandler");
                _scienceDepartmentField =
                    AccessTools.Field(_componentType, "sdInteractHandler");
                _specialManagerField =
                    AccessTools.Field(_componentType, "specialGlobalEventManager");

                _interactTickDayMethod = _interactHandlerField == null
                    ? null
                    : AccessTools.Method(
                        _interactHandlerField.FieldType,
                        "TickDay",
                        Type.EmptyTypes);
                _scienceDepartmentTickDayMethod =
                    _scienceDepartmentField == null
                        ? null
                        : AccessTools.Method(
                            _scienceDepartmentField.FieldType,
                            "TickDay",
                            Type.EmptyTypes);
                _scienceAnnualInteractionMethod =
                    _scienceDepartmentField == null
                        ? null
                        : AccessTools.Method(
                            _scienceDepartmentField.FieldType,
                            "TryTriggerAnnualInteractionQuest",
                            Type.EmptyTypes);
                _specialManagerTickMethod = _specialManagerField == null
                    ? null
                    : AccessTools.Method(
                        _specialManagerField.FieldType,
                        "Tick",
                        Type.EmptyTypes);

                if (_instanceProperty == null ||
                    _interactHandlerField == null ||
                    _scienceDepartmentField == null ||
                    _specialManagerField == null ||
                    _interactTickDayMethod == null ||
                    _scienceDepartmentTickDayMethod == null ||
                    _specialManagerTickMethod == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Oberonia Aurea GameComponent " +
                        "determinism skipped: required handler fields/methods " +
                        "were not resolved.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });

                TryInstallAnnualInteractionSync(harmony);

                Log.Message(
                    "[MP-MeowOnlineShop] Oberonia Aurea GameComponent " +
                    "determinism active: only the canonical singleton ticks; " +
                    "TickDay uses a fixed 60000-tick world cadence in multiplayer; " +
                    "annual interaction replay=" +
                    (_annualInteractionSyncField != null) + ".");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Oberonia Aurea GameComponent " +
                    "determinism install failed: " + e.Message);
            }
        }

        private static void TryInstallAnnualInteractionSync(Harmony harmony)
        {
            if (_scienceAnnualInteractionMethod == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Oberonia annual interaction sync " +
                    "skipped: TryTriggerAnnualInteractionQuest was not resolved.");
                return;
            }

            try
            {
                FieldInfo commandTickField = AccessTools.Field(
                    typeof(Patch_OberoniaGameComponentMp),
                    nameof(_annualInteractionCommandTick));
                MethodInfo annualPrefix = AccessTools.Method(
                    typeof(Patch_OberoniaGameComponentMp),
                    nameof(AnnualInteractionPrefix));
                if (commandTickField == null || annualPrefix == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Oberonia annual interaction sync " +
                        "skipped: local sync field or prefix was not resolved.");
                    return;
                }

                _annualInteractionSyncField = MP.RegisterSyncField(commandTickField)
                    .InGameLoop()
                    .SetHostOnly()
                    .PostApply(AnnualInteractionCommandApplied);

                harmony.Patch(
                    _scienceAnnualInteractionMethod,
                    prefix: new HarmonyMethod(annualPrefix)
                    {
                        priority = Priority.First
                    });
            }
            catch (Exception e)
            {
                _annualInteractionSyncField = null;
                Log.Warning(
                    "[MP-MeowOnlineShop] Oberonia annual interaction sync " +
                    "install failed: " + e.Message);
            }
        }

        private static bool AnnualInteractionPrefix()
        {
            if (!MP.IsInMultiplayer || _annualInteractionSyncField == null ||
                MP.IsExecutingSyncCommand)
                return true;

            if (!MP.IsHosting)
                return false;

            try
            {
                _annualInteractionCommandTick = Find.TickManager?.TicksGame ?? 0;
                _annualInteractionSyncField.DoSync(
                    null,
                    _annualInteractionCommandTick);
            }
            catch (Exception e)
            {
                LogAnnualSyncFailure(
                    "dispatch failed: " + e.Message);
            }

            // The local automatic call is always suppressed once the in-game
            // loop field is registered. If dispatch is temporarily unavailable,
            // dropping this one annual attempt is safer than letting the host
            // execute a quest that clients did not replay.
            return false;
        }

        private static void AnnualInteractionCommandApplied(
            object target,
            object value)
        {
            if (!(value is int commandTick))
                return;

            _annualInteractionCommandTick = commandTick;

            try
            {
                object canonical = _instanceProperty?.GetValue(null, null);
                object science = canonical == null ||
                    _scienceDepartmentField == null
                    ? null
                    : _scienceDepartmentField.GetValue(canonical);
                if (science == null)
                {
                    LogAnnualSyncFailure(
                        "replay skipped because the canonical science handler " +
                        "was not available.");
                    return;
                }

                _scienceAnnualInteractionMethod.Invoke(science, null);
            }
            catch (Exception e)
            {
                LogAnnualSyncFailure(
                    "replay failed: " + (e.InnerException?.Message ?? e.Message));
            }
        }

        private static void LogAnnualSyncFailure(string message)
        {
            if (_annualSyncFailureLogged)
                return;
            _annualSyncFailureLogged = true;
            Log.Warning(
                "[MP-MeowOnlineShop] Oberonia annual interaction sync " +
                message);
        }

        private static bool GameComponentTickPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            try
            {
                object canonical = _instanceProperty.GetValue(null, null);
                if (canonical == null || !ReferenceEquals(canonical, __instance))
                    return false;

                int ticks = Find.TickManager?.TicksGame ?? 0;
                bool dayTick = ticks % DayIntervalTicks == 0;

                object interact = _interactHandlerField.GetValue(__instance);
                object science =
                    _scienceDepartmentField.GetValue(__instance);
                object special = _specialManagerField.GetValue(__instance);
                if (interact == null || science == null || special == null)
                    return true;

                if (dayTick)
                {
                    _interactTickDayMethod.Invoke(interact, null);
                    if (ModsConfig.OdysseyActive)
                        _scienceDepartmentTickDayMethod.Invoke(science, null);
                }

                _specialManagerTickMethod.Invoke(special, null);
                return false;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Oberonia Aurea GameComponent " +
                    "determinism prefix failed open: " + e.Message);
                return true;
            }
        }
    }
}
