using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Debug-gated async Rand-state recorder for Desync-104/105 replays. It is a no-op unless
    /// both peers start with MP_RAND_DIAG=1. It records per-tick map/world Rand states plus the
    /// first hidden Rand draws (inside a pushed Rand scope, which Multiplayer's own tracing does
    /// not record) in a bounded window, so a replay can identify the first divergent stream.
    /// </summary>
    internal static class Patch_AsyncRandStateDiagnostic
    {
        private const string EnvVarName = "MP_RAND_DIAG";
        private const int MaxRecords = 200000;
        private const int MaxHiddenDraws = 20000;

        internal static readonly bool Enabled =
            string.Equals(Environment.GetEnvironmentVariable(EnvVarName), "1", StringComparison.OrdinalIgnoreCase);

        private static int _recordCount;
        private static int _hiddenDrawCount;
        private static bool _installed;

        private static Type _asyncMapCompType;
        private static Type _asyncWorldCompType;
        private static FieldInfo _mapField;
        private static FieldInfo _mapTicksField;
        private static FieldInfo _mapRandStateField;
        private static FieldInfo _worldTicksField;
        private static FieldInfo _worldRandStateField;
        private static FieldInfo _randStateStackField;

        internal static void Apply(Harmony harmony)
        {
            if (!Enabled || _installed || harmony == null)
                return;

            _installed = true;
            try
            {
                _asyncMapCompType = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
                _asyncWorldCompType = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
                _mapField = AccessTools.Field(_asyncMapCompType, "map");
                _mapTicksField = AccessTools.Field(_asyncMapCompType, "mapTicks");
                _mapRandStateField = AccessTools.Field(_asyncMapCompType, "randState");
                _worldTicksField = AccessTools.Field(_asyncWorldCompType, "worldTicks");
                _worldRandStateField = AccessTools.Field(_asyncWorldCompType, "randState");
                _randStateStackField = AccessTools.Field(typeof(Rand), "stateStack");

                if (_asyncMapCompType == null || _asyncWorldCompType == null ||
                    _mapField == null || _mapTicksField == null || _mapRandStateField == null ||
                    _worldTicksField == null || _worldRandStateField == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] MP_RAND_DIAG targets unresolved; diagnostic not installed.");
                    return;
                }

                PatchIfResolved(harmony, _asyncMapCompType, "Tick", nameof(MapTickPostfix));
                PatchIfResolved(harmony, _asyncWorldCompType, "Tick", nameof(WorldTickPostfix));
                PatchIfResolved(harmony, _asyncMapCompType, "ExecuteCmd", nameof(MapCmdPostfix));
                PatchIfResolved(harmony, _asyncWorldCompType, "ExecuteCmd", nameof(WorldCmdPostfix));

                MethodInfo getValue = AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value));
                MethodInfo getInt = AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Int));
                var hiddenPostfix = new HarmonyMethod(
                    AccessTools.Method(typeof(Patch_AsyncRandStateDiagnostic), nameof(HiddenRandPostfix)));
                if (getValue != null)
                    harmony.Patch(getValue, postfix: hiddenPostfix);
                if (getInt != null)
                    harmony.Patch(getInt, postfix: hiddenPostfix);

                Log.Message(
                    "[MP-MeowOnlineShop] MP_RAND_DIAG active: per-tick map/world Rand states " +
                    "and hidden draws are logged (bounded).");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MP_RAND_DIAG init failed: " + e);
            }
        }

        private static void PatchIfResolved(Harmony harmony, Type owner, string methodName, string postfixName)
        {
            MethodInfo target = AccessTools.Method(owner, methodName);
            MethodInfo postfix = AccessTools.Method(typeof(Patch_AsyncRandStateDiagnostic), postfixName);
            if (target != null && postfix != null)
            {
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(postfix)
                    {
                        priority = Priority.Last
                    });
            }
        }

        private static bool CanRecord()
        {
            return _recordCount < MaxRecords && MP.IsInMultiplayer;
        }

        private static void MapTickPostfix(object __instance)
        {
            if (!CanRecord())
                return;

            try
            {
                Map map = _mapField.GetValue(__instance) as Map;
                int ticks = (int)_mapTicksField.GetValue(__instance);
                ulong state = (ulong)_mapRandStateField.GetValue(__instance);
                _recordCount++;
                Log.Message(
                    $"[MP_RAND_DIAG] mapTick map={(map != null ? map.uniqueID : -1)} " +
                    $"mapTicks={ticks} rand={state} low={(uint)state} high={(uint)(state >> 32)}");
            }
            catch
            {
                // diagnostics must never break the tick
            }
        }

        private static void WorldTickPostfix(object __instance)
        {
            if (!CanRecord())
                return;

            try
            {
                int ticks = (int)_worldTicksField.GetValue(__instance);
                ulong state = (ulong)_worldRandStateField.GetValue(__instance);
                _recordCount++;
                Log.Message(
                    $"[MP_RAND_DIAG] worldTick worldTicks={ticks} rand={state} " +
                    $"low={(uint)state} high={(uint)(state >> 32)}");
            }
            catch
            {
                // diagnostics must never break the tick
            }
        }

        private static void MapCmdPostfix(object __instance)
        {
            if (!CanRecord())
                return;

            try
            {
                Map map = _mapField.GetValue(__instance) as Map;
                ulong state = (ulong)_mapRandStateField.GetValue(__instance);
                _recordCount++;
                Log.Message(
                    $"[MP_RAND_DIAG] mapCmd map={(map != null ? map.uniqueID : -1)} " +
                    $"rand={state} low={(uint)state} high={(uint)(state >> 32)}");
            }
            catch
            {
                // diagnostics must never break the command
            }
        }

        private static void WorldCmdPostfix(object __instance)
        {
            if (!CanRecord())
                return;

            try
            {
                ulong state = (ulong)_worldRandStateField.GetValue(__instance);
                _recordCount++;
                Log.Message(
                    $"[MP_RAND_DIAG] worldCmd rand={state} low={(uint)state} high={(uint)(state >> 32)}");
            }
            catch
            {
                // diagnostics must never break the command
            }
        }

        private static void HiddenRandPostfix()
        {
            if (!Enabled || !MP.IsInMultiplayer || _hiddenDrawCount >= MaxHiddenDraws)
                return;

            try
            {
                object stack = _randStateStackField?.GetValue(null);
                int depth = stack is ICollection collection ? collection.Count : 0;
                if (depth <= 1)
                    return;

                _hiddenDrawCount++;
                string caller = "unknown";
                try
                {
                    string[] lines = Environment.StackTrace.Split('\n');
                    if (lines.Length > 3)
                        caller = lines[3].Trim();
                }
                catch
                {
                    // keep "unknown"
                }

                Log.Message($"[MP_RAND_DIAG] hiddenRand depth={depth} caller={caller}");
            }
            catch
            {
                // diagnostics must never break the game
            }
        }
    }
}
