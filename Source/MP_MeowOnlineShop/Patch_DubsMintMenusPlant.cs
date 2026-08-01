using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Dubs Mint Menus registers SetPlant(IPlantToGrowSettable, ThingDef) through
    /// its legacy reflection bridge. In multi-map async sessions the interface
    /// argument can resolve against the wrong map and deserialize as null.
    ///
    /// Intercept the real Clicked UI boundary and dispatch an explicit Map plus
    /// a stable zone/thing ID. This avoids both the interface implementation
    /// index and any ambient current-map dependency.
    /// </summary>
    internal static class Patch_DubsMintMenusPlant
    {
        private const string DialogTypeName = "DubsMintMenus.Dialog_FancyDanPlantSetterBob";
        private static FieldInfo _settablesField;
        private static int _dispatchLogBudget = 8;
        private static bool _unsupportedLogged;

        internal static void Apply(Harmony harmony)
        {
            Type dialogType = AccessTools.TypeByName(DialogTypeName);
            MethodInfo clicked = dialogType == null
                ? null
                : AccessTools.Method(dialogType, "Clicked", new[] { typeof(ThingDef) });
            _settablesField = dialogType == null
                ? null
                : AccessTools.Field(dialogType, "settables");

            if (dialogType == null || clicked == null || _settablesField == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][DubsMintMenus] plant-menu compatibility skipped: " +
                    $"dialog={dialogType != null}, clicked={clicked != null}, settables={_settablesField != null}.");
                return;
            }

            MP.RegisterSyncMethod(typeof(Patch_DubsMintMenusPlant), nameof(SyncSetPlantById))
                .CancelIfAnyArgNull();

            harmony.Patch(
                clicked,
                prefix: new HarmonyMethod(
                    typeof(Patch_DubsMintMenusPlant),
                    nameof(ClickedPrefix)));

            Log.Message(
                "[MP-MeowOnlineShop][DubsMintMenus] concrete plant-target sync active: " +
                "Clicked -> explicit Map + stable zone/thing ID; legacy IPlantToGrowSettable sync is bypassed in multiplayer.");
        }

        private static bool ClickedPrefix(object __instance, ThingDef plantDef)
        {
            if (!MP.IsInMultiplayer)
                return true;
            if (__instance == null || plantDef == null)
                return false;

            IList rawSettles;
            try
            {
                rawSettles = _settablesField.GetValue(__instance) as IList;
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][DubsMintMenus] failed to read plant targets: " + e);
                return true;
            }

            if (rawSettles == null || rawSettles.Count == 0)
                return false;

            var zones = new List<Zone_Growing>();
            var things = new List<Thing>();

            foreach (object target in rawSettles)
            {
                if (target is Zone_Growing zone)
                {
                    zones.Add(zone);
                }
                else if (target is Thing thing && target is IPlantToGrowSettable)
                {
                    things.Add(thing);
                }
                else
                {
                    if (!_unsupportedLogged)
                    {
                        _unsupportedLogged = true;
                        Log.Warning(
                            "[MP-MeowOnlineShop][DubsMintMenus] unsupported IPlantToGrowSettable target; " +
                            "falling back to the mod's original Clicked path. type=" +
                            (target?.GetType().FullName ?? "null"));
                    }

                    return true;
                }
            }

            zones.Sort(CompareZones);
            things.Sort(CompareThings);

            foreach (Zone_Growing zone in zones)
                SyncSetPlantById(zone.Map, 0, zone.ID, plantDef);
            foreach (Thing thing in things)
                SyncSetPlantById(thing.Map, 1, thing.thingIDNumber, plantDef);

            if (_dispatchLogBudget > 0)
            {
                _dispatchLogBudget--;
                Log.Message(
                    "[MP-MeowOnlineShop][DubsMintMenus] dispatched concrete plant selection: " +
                    $"plant={plantDef.defName}, zones={zones.Count}, things={things.Count}.");
            }

            return false;
        }

        private static int CompareZones(Zone_Growing left, Zone_Growing right)
        {
            int mapCompare = CompareMapIds(left?.Map, right?.Map);
            return mapCompare != 0 ? mapCompare : (left?.ID ?? int.MinValue).CompareTo(right?.ID ?? int.MinValue);
        }

        private static int CompareThings(Thing left, Thing right)
        {
            int mapCompare = CompareMapIds(left?.Map, right?.Map);
            return mapCompare != 0
                ? mapCompare
                : (left?.thingIDNumber ?? int.MinValue).CompareTo(right?.thingIDNumber ?? int.MinValue);
        }

        private static int CompareMapIds(Map left, Map right)
        {
            return (left?.uniqueID ?? int.MinValue).CompareTo(right?.uniqueID ?? int.MinValue);
        }

        public static void SyncSetPlantById(Map map, int targetKind, int targetId, ThingDef plantDef)
        {
            if (targetKind == 0)
            {
                List<Zone> allZones = map.zoneManager.AllZones;
                for (int i = 0; i < allZones.Count; i++)
                {
                    Zone zone = allZones[i];
                    if (zone.ID == targetId && zone is Zone_Growing growingZone)
                    {
                        growingZone.SetPlantDefToGrow(plantDef);
                        return;
                    }
                }

                Log.Error(
                    "[MP-MeowOnlineShop][DubsMintMenus] synced growing zone was not found: " +
                    $"map={map.uniqueID}, zone={targetId}, plant={plantDef.defName}.");
                return;
            }

            if (targetKind == 1)
            {
                List<Thing> allThings = map.listerThings.AllThings;
                for (int i = 0; i < allThings.Count; i++)
                {
                    Thing thing = allThings[i];
                    if (thing.thingIDNumber != targetId)
                        continue;
                    if (thing is IPlantToGrowSettable settable)
                    {
                        settable.SetPlantDefToGrow(plantDef);
                        return;
                    }

                    break;
                }

                Log.Error(
                    "[MP-MeowOnlineShop][DubsMintMenus] synced plant-growing thing was not found: " +
                    $"map={map.uniqueID}, thing={targetId}, plant={plantDef.defName}.");
                return;
            }

            Log.Error(
                "[MP-MeowOnlineShop][DubsMintMenus] unknown synced plant target kind: " +
                $"map={map.uniqueID}, kind={targetKind}, id={targetId}, plant={plantDef.defName}.");
        }
    }
}
