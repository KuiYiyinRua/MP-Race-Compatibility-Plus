using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 固定符号解析：只解析已在 rwmt/Multiplayer 源码中确认的类型与成员，
    /// 避免全程序集猜测式扫描导致的兼容脆弱性。
    /// </summary>
    internal static class MpRuntimeInfo
    {
        private static bool _initialized;
        private static bool _loggedInitSummary;
        private static bool _loggedHostFallback;
        private static bool _loggedGameCompMissing;
        private static bool _loggedMapCompMissing;

        private static Type _multiplayerStaticType;
        private static PropertyInfo _gameCompProperty;
        private static FieldInfo _asyncTimeField;
        private static FieldInfo _multifactionField;

        private static Type _extensionsType;
        private static MethodInfo _mpCompMethod;
        private static FieldInfo _mapCompFactionDataField;
        private static FieldInfo _mapCompCustomFactionDataField;

        private static Type _apiBridgeType;
        private static FieldInfo _apiBridgeInstanceField;
        private static PropertyInfo _apiBridgeIsHostingProperty;

        internal static void EnsureInitialized(bool forceLog = false)
        {
            if (_initialized)
            {
                if (forceLog && !_loggedInitSummary)
                    LogInitSummary();
                return;
            }

            _initialized = true;

            try
            {
                _multiplayerStaticType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
                _gameCompProperty = AccessTools.Property(_multiplayerStaticType, "GameComp");
                if (_gameCompProperty != null)
                {
                    var gameCompType = _gameCompProperty.PropertyType;
                    _asyncTimeField = AccessTools.Field(gameCompType, "asyncTime");
                    _multifactionField = AccessTools.Field(gameCompType, "multifaction");
                }

                _extensionsType = AccessTools.TypeByName("Multiplayer.Client.Extensions");
                _mpCompMethod = AccessTools.Method(_extensionsType, "MpComp", new[] { typeof(Map) });
                if (_mpCompMethod != null && _mpCompMethod.ReturnType != null)
                {
                    var mapCompType = _mpCompMethod.ReturnType;
                    _mapCompFactionDataField = AccessTools.Field(mapCompType, "factionData");
                    _mapCompCustomFactionDataField = AccessTools.Field(mapCompType, "customFactionData");
                }

                _apiBridgeType = AccessTools.TypeByName("Multiplayer.Common.MultiplayerAPIBridge");
                _apiBridgeInstanceField = AccessTools.Field(_apiBridgeType, "Instance");
                var apiInterfaceType = AccessTools.TypeByName("Multiplayer.API.IAPI");
                _apiBridgeIsHostingProperty = AccessTools.Property(apiInterfaceType, "IsHosting");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MP runtime info init failed: " + e.Message);
            }

            if (forceLog || !_loggedInitSummary)
                LogInitSummary();
        }

        internal static bool TryGetAsyncTimeActive(out bool active)
        {
            EnsureInitialized();
            active = false;
            if (!TryGetGameComp(out var gameComp))
                return false;

            if (_asyncTimeField == null || _asyncTimeField.FieldType != typeof(bool))
                return false;

            try
            {
                active = (bool)_asyncTimeField.GetValue(gameComp);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Broad simulation/UI optimizations in this compatibility mod are only
        /// allowed in the simple single-map lockstep case. Multiplayer owns the
        /// per-map tick/Rand contexts when async time is enabled, while multiple
        /// maps can make local UI evaluation and dynamic map initialization run
        /// in a different order on each peer even with async time disabled.
        /// </summary>
        internal static bool RequiresVanillaPerMapPipelines(out string reason)
        {
            if (TryGetAsyncTimeActive(out bool asyncTime) && asyncTime)
            {
                reason = "async-time";
                return true;
            }

            if ((Find.Maps?.Count ?? 0) > 1)
            {
                reason = "multiple maps";
                return true;
            }

            reason = null;
            return false;
        }

        internal static bool TryGetMultifactionActive(out bool active)
        {
            EnsureInitialized();
            active = false;
            if (!TryGetGameComp(out var gameComp))
                return false;

            if (_multifactionField == null || _multifactionField.FieldType != typeof(bool))
                return false;

            try
            {
                active = (bool)_multifactionField.GetValue(gameComp);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsHostAuthority()
        {
            if (!MP.IsInMultiplayer)
                return true;

            EnsureInitialized();
            try
            {
                var bridgeInstance = _apiBridgeInstanceField?.GetValue(null);
                if (bridgeInstance == null || _apiBridgeIsHostingProperty == null)
                    throw new InvalidOperationException("MultiplayerAPIBridge.Instance/IsHosting not resolved");

                var value = _apiBridgeIsHostingProperty.GetValue(bridgeInstance, null);
                if (value is bool host)
                    return host;
            }
            catch (Exception e)
            {
                if (!_loggedHostFallback)
                {
                    _loggedHostFallback = true;
                    Log.Warning("[MP-MeowOnlineShop] MP host authority fallback to false: " + e.Message);
                }
            }

            return false;
        }

        internal static bool TryMapCompContainsAnyPlayerFaction(Map map, HashSet<int> playerFactionIds, out bool matchedFactionData, out bool matchedCustomFactionData)
        {
            matchedFactionData = false;
            matchedCustomFactionData = false;
            if (map == null || playerFactionIds == null || playerFactionIds.Count == 0)
                return false;

            if (!TryGetMapComp(map, out var mapComp))
                return false;

            if (ContainsAnyPlayerFaction(_mapCompFactionDataField, mapComp, playerFactionIds))
                matchedFactionData = true;
            if (ContainsAnyPlayerFaction(_mapCompCustomFactionDataField, mapComp, playerFactionIds))
                matchedCustomFactionData = true;

            return matchedFactionData || matchedCustomFactionData;
        }

        internal static bool TryGetMapComp(Map map, out object mapComp)
        {
            EnsureInitialized();
            mapComp = null;
            if (map == null || _mpCompMethod == null)
                return false;

            try
            {
                mapComp = _mpCompMethod.Invoke(null, new object[] { map });
                bool ok = mapComp != null;
                if (!ok && !_loggedMapCompMissing)
                {
                    _loggedMapCompMissing = true;
                    Log.Warning("[MP-MeowOnlineShop] MP map comp is unavailable, fallback to IsPlayerHome-only candidate policy.");
                }
                return ok;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetGameComp(out object gameComp)
        {
            gameComp = null;
            if (_gameCompProperty == null)
                return false;

            try
            {
                gameComp = _gameCompProperty.GetValue(null, null);
                bool ok = gameComp != null;
                if (!ok && !_loggedGameCompMissing)
                {
                    _loggedGameCompMissing = true;
                    Log.Warning("[MP-MeowOnlineShop] MP GameComp is unavailable, fallback to conservative defaults.");
                }
                return ok;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsAnyPlayerFaction(FieldInfo dictField, object mapComp, HashSet<int> playerFactionIds)
        {
            if (dictField == null || mapComp == null || playerFactionIds == null || playerFactionIds.Count == 0)
                return false;
            if (!typeof(IDictionary).IsAssignableFrom(dictField.FieldType))
                return false;

            IDictionary dict;
            try
            {
                dict = dictField.GetValue(mapComp) as IDictionary;
            }
            catch
            {
                return false;
            }

            if (dict == null)
                return false;

            foreach (var key in dict.Keys)
            {
                if (!(key is int factionId))
                    continue;
                if (playerFactionIds.Contains(factionId))
                    return true;
            }

            return false;
        }

        private static void LogInitSummary()
        {
            _loggedInitSummary = true;
            Log.Message(
                "[MP-MeowOnlineShop] MP runtime symbols resolved: " +
                $"gameComp={(_gameCompProperty != null)}, asyncField={(_asyncTimeField != null)}, multifactionField={(_multifactionField != null)}, " +
                $"mpCompMethod={(_mpCompMethod != null)}, factionDataField={(_mapCompFactionDataField != null)}, customFactionDataField={(_mapCompCustomFactionDataField != null)}, " +
                $"apiBridgeHost={(_apiBridgeInstanceField != null && _apiBridgeIsHostingProperty != null)}.");
        }
    }
}
