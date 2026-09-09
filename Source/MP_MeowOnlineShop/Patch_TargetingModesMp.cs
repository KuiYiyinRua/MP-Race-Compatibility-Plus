using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Targeting Modes (`mlie.xndtargetingmodes`) stores the selected mode in
    /// CompTargetingMode.targetingMode and only mutates it from two places:
    ///
    /// 1. The float-menu action in Command_SetTargetingMode.ProcessInput. This
    ///    runs on the clicking peer only and must be replicated.
    /// 2. CompTick (mode reset) and TryAssignRandomTargetingMode (pawn
    ///    generation). Those run identically on every peer inside deterministic
    ///    simulation and must stay local, otherwise both peers send duplicate
    ///    reset commands.
    ///
    /// The patch therefore registers one replay method for the UI path and
    /// suppresses it around the deterministic callers, keeping the narrowest
    /// sync boundary instead of registering the whole setter.
    /// </summary>
    internal static class Patch_TargetingModesMp
    {
        private const string PackageId = "mlie.xndtargetingmodes";
        private const string CompTypeName = "TargetingModes.CompTargetingMode";
        private const string UtilityTypeName = "TargetingModes.TargetingModesUtility";
        private const string DefTypeName = "TargetingModes.TargetingModeDef";

        private static Type _compType;
        private static Type _defType;
        private static MethodInfo _setTargetingMode;
        private static MethodInfo _getNamedSilentFail;
        private static ISyncMethod _syncSetTargetingMode;

        [ThreadStatic] private static bool _suppressSync;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            _defType = AccessTools.TypeByName(DefTypeName);
            _setTargetingMode = _compType == null || _defType == null
                ? null
                : AccessTools.Method(_compType, "SetTargetingMode", new[] { _defType });

            if (_compType == null || _defType == null || _setTargetingMode == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Targeting Modes target resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncSetTargetingMode = MP.RegisterSyncMethod(
                        typeof(Patch_TargetingModesMp),
                        nameof(SyncSetTargetingMode))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Targeting Modes sync registration failed: " + e.Message);
                return;
            }

            var setterPrefix = AccessTools.Method(
                typeof(Patch_TargetingModesMp),
                nameof(SetTargetingModePrefix));
            if (setterPrefix != null)
            {
                try
                {
                    harmony.Patch(_setTargetingMode, prefix: new HarmonyMethod(setterPrefix));
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Targeting Modes setter prefix failed: " + e.Message);
                }
            }

            PatchSuppressContext(harmony, _compType, "CompTick");

            var utilityType = AccessTools.TypeByName(UtilityTypeName);
            if (utilityType != null)
                PatchSuppressContext(harmony, utilityType, "TryAssignRandomTargetingMode");

            Log.Message("[MP-MeowOnlineShop] Targeting Modes MP patch active: UI mode changes sync, tick/generation resets stay local.");
        }

        private static void PatchSuppressContext(Harmony harmony, Type type, string methodName)
        {
            if (type == null)
                return;
            var method = AccessTools.Method(type, methodName);
            var prefix = AccessTools.Method(typeof(Patch_TargetingModesMp), nameof(SuppressSyncPrefix));
            var finalizer = AccessTools.Method(typeof(Patch_TargetingModesMp), nameof(SuppressSyncFinalizer));
            if (method == null || prefix == null || finalizer == null)
                return;

            try
            {
                harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(prefix),
                    finalizer: new HarmonyMethod(finalizer));
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Targeting Modes deterministic context patch failed on " +
                            type.FullName + "." + methodName + ": " + e.Message);
            }
        }

        private static void SuppressSyncPrefix()
        {
            _suppressSync = true;
        }

        private static void SuppressSyncFinalizer()
        {
            _suppressSync = false;
        }

        private static bool SetTargetingModePrefix(object __instance, object targetMode)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _suppressSync)
                return true;

            var comp = __instance as ThingComp;
            var parent = comp?.parent;
            var map = parent?.Map;
            if (parent == null || map == null || _syncSetTargetingMode == null)
                return true;

            var defName = (targetMode as Def)?.defName;
            if (string.IsNullOrEmpty(defName))
                return true;

            _syncSetTargetingMode.DoSync(null, map.Index, parent.thingIDNumber, defName);
            return false;
        }

        public static void SyncSetTargetingMode(int mapIndex, int thingId, string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return;

            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            var thing = FindThingById(map, thingId) as ThingWithComps;
            var comp = thing?.AllComps?.FirstOrDefault(c => _compType.IsInstanceOfType(c));
            var def = ResolveDef(defName);
            if (comp == null || def == null)
                return;

            _setTargetingMode.Invoke(comp, new[] { def });
        }

        private static object ResolveDef(string defName)
        {
            try
            {
                if (_getNamedSilentFail == null)
                {
                    var dbType = typeof(DefDatabase<>).MakeGenericType(_defType);
                    _getNamedSilentFail = dbType.GetMethod(
                        "GetNamedSilentFail",
                        new[] { typeof(string) });
                }

                return _getNamedSilentFail?.Invoke(null, new object[] { defName });
            }
            catch
            {
                return null;
            }
        }

        private static Thing FindThingById(Map map, int thingId)
        {
            if (map?.listerThings?.AllThings == null)
                return null;

            for (var i = 0; i < map.listerThings.AllThings.Count; i++)
            {
                var thing = map.listerThings.AllThings[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;
            }

            return null;
        }
    }
}
