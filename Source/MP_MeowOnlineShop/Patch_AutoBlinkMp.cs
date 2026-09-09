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
    /// AutoBlink (`rabiosus.autoblink`) stores player-facing toggles in
    /// CompAutoBlink and teleports the pawn through BlinkToCellDirect. The
    /// toggles are only changed by the gizmo/detail window on the clicking
    /// peer, and the manual teleport is only issued from the gizmo targeting
    /// callback.
    ///
    /// The scheduled blink path inside CompTick does not call
    /// BlinkToCellDirect, so registering that method as the UI boundary is
    /// safe. The player-facing fields are registered as sync fields; writes
    /// from deterministic simulation are not in MP's interface context and are
    /// therefore not re-broadcast.
    /// </summary>
    internal static class Patch_AutoBlinkMp
    {
        private const string PackageId = "rabiosus.autoblink";
        private const string CompTypeName = "AutoBlink.CompAutoBlink";

        private static readonly string[] SyncFieldNames =
        {
            "autoBlinkMaster",
            "autoBlinkDrafted",
            "autoBlinkIdle",
            "jumpAsFarAsPossible",
            "localMinDistanceToBlink"
        };

        private static Type _compType;
        private static MethodInfo _blinkToCell;
        private static ISyncMethod _syncBlink;

        [ThreadStatic] private static bool _replayingBlink;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            if (_compType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] AutoBlink target type not resolved; patch skipped.");
                return;
            }

            int registeredFields = 0;
            foreach (string fieldName in SyncFieldNames)
            {
                FieldInfo field = AccessTools.Field(_compType, fieldName);
                if (field == null)
                    continue;
                try
                {
                    MP.RegisterSyncField(field);
                    registeredFields++;
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] AutoBlink sync field failed on " +
                                fieldName + ": " + e.Message);
                }
            }

            _blinkToCell = AccessTools.Method(_compType, "BlinkToCellDirect", new[] { typeof(IntVec3) });
            if (_blinkToCell == null)
            {
                Log.Warning("[MP-MeowOnlineShop] AutoBlink BlinkToCellDirect not resolved; blink sync skipped.");
                return;
            }

            try
            {
                _syncBlink = MP.RegisterSyncMethod(
                        typeof(Patch_AutoBlinkMp),
                        nameof(SyncBlinkToCell))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] AutoBlink blink sync registration failed: " + e.Message);
                return;
            }

            var prefix = AccessTools.Method(
                typeof(Patch_AutoBlinkMp),
                nameof(BlinkToCellDirectPrefix));
            if (prefix == null)
                return;

            try
            {
                harmony.Patch(_blinkToCell, prefix: new HarmonyMethod(prefix));
                Log.Message(
                    "[MP-MeowOnlineShop] AutoBlink MP patch active: " +
                    $"fields={registeredFields}/{SyncFieldNames.Length}, blinkSync=true.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] AutoBlink blink prefix patch failed: " + e.Message);
            }
        }

        private static bool BlinkToCellDirectPrefix(object __instance, IntVec3 cell)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _replayingBlink || _syncBlink == null)
                return true;

            var comp = __instance as ThingComp;
            var parent = comp?.parent;
            var map = parent?.Map;
            if (parent == null || map == null)
                return true;

            _syncBlink.DoSync(null, map.Index, parent.thingIDNumber, cell.x, cell.z);
            return false;
        }

        public static void SyncBlinkToCell(int mapIndex, int thingId, int cellX, int cellZ)
        {
            if (_blinkToCell == null)
                return;

            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            var parent = FindThingById(map, thingId) as ThingWithComps;
            var comp = parent?.AllComps?.FirstOrDefault(c => _compType.IsInstanceOfType(c));
            if (comp == null)
                return;

            try
            {
                _replayingBlink = true;
                _blinkToCell.Invoke(comp, new object[] { new IntVec3(cellX, 0, cellZ) });
            }
            finally
            {
                _replayingBlink = false;
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
