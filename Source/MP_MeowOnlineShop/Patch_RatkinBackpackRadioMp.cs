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
    /// Ratkin Underground's backpack radio launches bunker busters and
    /// driller guns from targeter callbacks. The launch methods create and
    /// fire projectiles and the callbacks write cooldown ticks, so the two
    /// stable launch executors are redirected to sync commands only when
    /// Multiplayer.ShouldSync is true (UI action). Deterministic auto-fire
    /// from CompTick keeps running locally.
    /// </summary>
    internal static class Patch_RatkinBackpackRadioMp
    {
        private const string PackageId = "rku.ratkinunderground";
        private const string CompTypeName = "RatkinUnderground.Comp_RKU_BackpackRadio";

        private static bool _applied;
        private static Type _compType;
        private static MethodInfo _launchBunkerBusterMethod;
        private static MethodInfo _launchDrillerGunMethod;
        private static FieldInfo _lastBunkerBusterTickField;
        private static FieldInfo _lastDrillerGunTickField;
        private static Type _multiplayerClientType;
        private static PropertyInfo _shouldSyncProperty;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Ratkin backpack radio sync skipped (target mod not active).");
                    return;
                }

                _compType = AccessTools.TypeByName(CompTypeName);
                _launchBunkerBusterMethod = _compType == null
                    ? null
                    : AccessTools.Method(_compType, "LaunchBunkerBuster", new[] { typeof(IntVec3) });
                _launchDrillerGunMethod = _compType == null
                    ? null
                    : AccessTools.Method(_compType, "LaunchDrillerGun", new[] { typeof(IntVec3) });
                _lastBunkerBusterTickField = _compType == null
                    ? null
                    : AccessTools.Field(_compType, "lastBunkerBusterTick");
                _lastDrillerGunTickField = _compType == null
                    ? null
                    : AccessTools.Field(_compType, "lastDrillerGunTick");

                if (_compType == null || _launchBunkerBusterMethod == null ||
                    _launchDrillerGunMethod == null || _lastBunkerBusterTickField == null ||
                    _lastDrillerGunTickField == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin backpack radio targets not resolved; patch skipped.");
                    return;
                }

                MethodInfo syncBunker = AccessTools.Method(
                    typeof(Patch_RatkinBackpackRadioMp),
                    nameof(SyncLaunchBunkerBuster),
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
                MethodInfo syncDriller = AccessTools.Method(
                    typeof(Patch_RatkinBackpackRadioMp),
                    nameof(SyncLaunchDrillerGun),
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
                if (syncBunker == null || syncDriller == null)
                    return;

                try
                {
                    MP.RegisterSyncMethod(syncBunker, null);
                    MP.RegisterSyncMethod(syncDriller, null);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin backpack radio sync registration failed: " + e.Message);
                    return;
                }

                MethodInfo bunkerPrefix = AccessTools.Method(
                    typeof(Patch_RatkinBackpackRadioMp),
                    nameof(LaunchBunkerBusterPrefix));
                MethodInfo drillerPrefix = AccessTools.Method(
                    typeof(Patch_RatkinBackpackRadioMp),
                    nameof(LaunchDrillerGunPrefix));
                if (bunkerPrefix == null || drillerPrefix == null)
                    return;

                harmony.Patch(
                    _launchBunkerBusterMethod,
                    prefix: new HarmonyMethod(bunkerPrefix));
                harmony.Patch(
                    _launchDrillerGunMethod,
                    prefix: new HarmonyMethod(drillerPrefix));

                Log.Message("[MP-MeowOnlineShop] Ratkin backpack radio MP patch active: 2 launch executors synced.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin backpack radio MP compat restore failed: " + e.Message);
            }
        }

        private static bool LaunchBunkerBusterPrefix(object __instance, IntVec3 targetCell)
        {
            if (!IsUiSyncContext())
                return true;

            ThingComp comp = __instance as ThingComp;
            Thing parent = comp?.parent;
            Map map = parent?.Map;
            if (parent == null || map == null)
                return true;

            SyncLaunchBunkerBuster(map.Index, parent.thingIDNumber, targetCell.x, targetCell.z);
            return false;
        }

        private static bool LaunchDrillerGunPrefix(object __instance, IntVec3 targetCell)
        {
            if (!IsUiSyncContext())
                return true;

            ThingComp comp = __instance as ThingComp;
            Thing parent = comp?.parent;
            Map map = parent?.Map;
            if (parent == null || map == null)
                return true;

            SyncLaunchDrillerGun(map.Index, parent.thingIDNumber, targetCell.x, targetCell.z);
            return false;
        }

        public static void SyncLaunchBunkerBuster(int mapIndex, int thingId, int cellX, int cellZ)
        {
            object comp = FindComp(mapIndex, thingId);
            if (comp == null || _launchBunkerBusterMethod == null || _lastBunkerBusterTickField == null)
                return;

            _launchBunkerBusterMethod.Invoke(comp, new object[] { new IntVec3(cellX, 0, cellZ) });
            _lastBunkerBusterTickField.SetValue(comp, Find.TickManager.TicksGame);
        }

        public static void SyncLaunchDrillerGun(int mapIndex, int thingId, int cellX, int cellZ)
        {
            object comp = FindComp(mapIndex, thingId);
            if (comp == null || _launchDrillerGunMethod == null || _lastDrillerGunTickField == null)
                return;

            _launchDrillerGunMethod.Invoke(comp, new object[] { new IntVec3(cellX, 0, cellZ) });
            _lastDrillerGunTickField.SetValue(comp, Find.TickManager.TicksGame);
        }

        private static object FindComp(int mapIndex, int thingId)
        {
            Map map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map?.listerThings?.AllThings == null || _compType == null)
                return null;

            for (int i = 0; i < map.listerThings.AllThings.Count; i++)
            {
                Thing thing = map.listerThings.AllThings[i];
                if (!(thing is ThingWithComps thingWithComps) ||
                    thingWithComps.thingIDNumber != thingId || thingWithComps.AllComps == null)
                    continue;

                for (int c = 0; c < thingWithComps.AllComps.Count; c++)
                {
                    if (_compType.IsInstanceOfType(thingWithComps.AllComps[c]))
                        return thingWithComps.AllComps[c];
                }
            }

            return null;
        }

        private static bool IsUiSyncContext()
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return false;

            try
            {
                if (_shouldSyncProperty == null)
                {
                    _multiplayerClientType = _multiplayerClientType ??
                                             AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
                    _shouldSyncProperty = AccessTools.Property(_multiplayerClientType, "ShouldSync");
                }

                return _shouldSyncProperty != null &&
                       _shouldSyncProperty.GetValue(null, null) is bool sync && sync;
            }
            catch
            {
                return false;
            }
        }
    }
}
