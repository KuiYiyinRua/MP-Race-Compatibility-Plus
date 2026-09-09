using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// WVC More Mechanoids Work Modes saves mech work-mode and shutdown-zone
    /// settings on CompMechSettings / Zone_MechanoidShutdown. Several gizmo
    /// lambdas mutate those fields directly, so the stable ModeSwitch executor
    /// is registered and the remaining lambda writes are rewritten to sync
    /// methods.
    /// </summary>
    internal static class Patch_MoreMechanoidsWorkModesMp
    {
        private const string PackageId = "wvc.sergkart.biotech.MoreMechanoidsWorkModes";
        private const string CompTypeName = "WVC_WorkModes.CompMechSettings";
        private const string ZoneTypeName = "WVC_WorkModes.Zone_MechanoidShutdown";

        private static bool _applied;
        private static FieldInfo _restrictField;
        private static FieldInfo _escortField;
        private static FieldInfo _allowWorkersField;
        private static FieldInfo _allowSafeField;
        private static FieldInfo _allowCombatantsField;
        private static FieldInfo _allowAmbushField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] More Mechanoids Work Modes sync skipped (target mod not active).");
                    return;
                }

                Type compType = AccessTools.TypeByName(CompTypeName);
                Type zoneType = AccessTools.TypeByName(ZoneTypeName);
                if (compType == null || zoneType == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] More Mechanoids Work Modes target types not resolved; patch skipped.");
                    return;
                }

                _restrictField = AccessTools.Field(compType, "restrictZoneByGroup");
                _escortField = AccessTools.Field(compType, "escortTarget");
                _allowWorkersField = AccessTools.Field(zoneType, "allowWorkers");
                _allowSafeField = AccessTools.Field(zoneType, "allowSafe");
                _allowCombatantsField = AccessTools.Field(zoneType, "allowCombatants");
                _allowAmbushField = AccessTools.Field(zoneType, "allowAmbush");

                if (_restrictField == null || _escortField == null || _allowWorkersField == null ||
                    _allowSafeField == null || _allowCombatantsField == null || _allowAmbushField == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] More Mechanoids Work Modes field resolution failed; patch skipped.");
                    return;
                }

                int registered = 0;
                registered += TryRegister(
                    AccessTools.Method(
                        typeof(Patch_MoreMechanoidsWorkModesMp),
                        nameof(SyncToggleRestrictZoneByGroup),
                        new[] { typeof(ThingComp) }));
                registered += TryRegister(
                    AccessTools.Method(
                        typeof(Patch_MoreMechanoidsWorkModesMp),
                        nameof(SyncSetEscortTarget),
                        new[] { typeof(ThingComp), typeof(Pawn) }));
                registered += TryRegister(
                    AccessTools.Method(
                        typeof(Patch_MoreMechanoidsWorkModesMp),
                        nameof(SyncSetZoneAllow),
                        new[] { typeof(Zone), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }));
                registered += TryRegister(
                    AccessTools.Method(
                        zoneType,
                        "ModeSwitch",
                        new[] { typeof(bool), typeof(Pawn), typeof(int?) }));

                if (registered == 0)
                {
                    Log.Warning("[MP-MeowOnlineShop] More Mechanoids Work Modes sync registration failed; patch skipped.");
                    return;
                }

                MethodInfo compGizmos = AccessTools.Method(compType, "CompGetGizmosExtra", Type.EmptyTypes);
                MethodInfo zoneGizmos = AccessTools.Method(zoneType, "GetGizmos", Type.EmptyTypes);
                MethodInfo compPostfix = AccessTools.Method(
                    typeof(Patch_MoreMechanoidsWorkModesMp),
                    nameof(CompGizmosPostfix));
                MethodInfo zonePostfix = AccessTools.Method(
                    typeof(Patch_MoreMechanoidsWorkModesMp),
                    nameof(ZoneGizmosPostfix));

                if (compGizmos != null && compPostfix != null)
                    harmony.Patch(compGizmos, postfix: new HarmonyMethod(compPostfix));
                if (zoneGizmos != null && zonePostfix != null)
                    harmony.Patch(zoneGizmos, postfix: new HarmonyMethod(zonePostfix));

                Log.Message(
                    "[MP-MeowOnlineShop] More Mechanoids Work Modes MP patch active: " +
                    registered + " sync methods, gizmo actions rewritten.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] More Mechanoids Work Modes MP compat restore failed: " + e.Message);
            }
        }

        private static int TryRegister(MethodInfo method)
        {
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] More Mechanoids Work Modes sync register failed on " +
                    method.Name + ": " + e.Message);
                return 0;
            }
        }

        public static void SyncToggleRestrictZoneByGroup(ThingComp comp)
        {
            if (comp == null || _restrictField == null)
                return;
            _restrictField.SetValue(comp, !(bool)_restrictField.GetValue(comp));
        }

        public static void SyncSetEscortTarget(ThingComp comp, Pawn target)
        {
            if (comp == null || _escortField == null)
                return;
            _escortField.SetValue(comp, target);
        }

        public static void SyncSetZoneAllow(
            Zone zone,
            bool workers,
            bool safe,
            bool combatants,
            bool ambush)
        {
            if (zone == null)
                return;
            _allowWorkersField?.SetValue(zone, workers);
            _allowSafeField?.SetValue(zone, safe);
            _allowCombatantsField?.SetValue(zone, combatants);
            _allowAmbushField?.SetValue(zone, ambush);
        }

        private static IEnumerable<Gizmo> CompGizmosPostfix(
            IEnumerable<Gizmo> __result,
            ThingComp __instance)
        {
            if (__result == null || __instance == null)
                return __result;

            string restrictLabel = Translator.Translate("WVC_WorkModes_RestrictZoneByGroupLabel").ToString();
            string assignLabel = Translator.Translate("WVC_WorkModes_AssignToPawnEscortLabel").ToString();
            string resetLabel = Translator.Translate("WVC_WorkModes_AssignToPawnEscortLabelReset").ToString();
            ThingComp comp = __instance;
            List<Gizmo> list = __result.ToList();

            foreach (Gizmo gizmo in list)
            {
                if (gizmo is Command_Action command && command.action != null)
                {
                    string label = command.defaultLabel?.ToString();
                    if (label == restrictLabel)
                        command.action = () => SyncToggleRestrictZoneByGroup(comp);
                    else if (label == assignLabel)
                        command.action = () => OpenEscortAssignMenu(comp);
                    else if (label == resetLabel)
                        command.action = () => SyncSetEscortTarget(comp, null);
                }
            }

            return list;
        }

        private static IEnumerable<Gizmo> ZoneGizmosPostfix(
            IEnumerable<Gizmo> __result,
            Zone __instance)
        {
            if (__result == null || __instance == null)
                return __result;

            string workersLabel = Translator.Translate("WVC_ShutdownZone_AllowWorkers").ToString();
            string safeLabel = Translator.Translate("WVC_ShutdownZone_AllowSafe").ToString();
            string combatantsLabel = Translator.Translate("WVC_ShutdownZone_AllowCombatants").ToString();
            string ambushLabel = Translator.Translate("WVC_ShutdownZone_AllowAmbush").ToString();
            Zone zone = __instance;
            List<Gizmo> list = __result.ToList();

            foreach (Gizmo gizmo in list)
            {
                if (gizmo is Command_Action command && command.action != null)
                {
                    string label = command.defaultLabel?.ToString();
                    if (label == workersLabel)
                        command.action = () => SyncSetZoneAllow(
                            zone,
                            !(bool)_allowWorkersField.GetValue(zone),
                            (bool)_allowSafeField.GetValue(zone),
                            (bool)_allowCombatantsField.GetValue(zone),
                            (bool)_allowAmbushField.GetValue(zone));
                    else if (label == safeLabel)
                        command.action = () => SyncSetZoneAllow(
                            zone,
                            (bool)_allowWorkersField.GetValue(zone),
                            !(bool)_allowSafeField.GetValue(zone),
                            (bool)_allowCombatantsField.GetValue(zone),
                            (bool)_allowAmbushField.GetValue(zone));
                    else if (label == combatantsLabel)
                        command.action = () => SyncSetZoneAllow(
                            zone,
                            (bool)_allowWorkersField.GetValue(zone),
                            (bool)_allowSafeField.GetValue(zone),
                            !(bool)_allowCombatantsField.GetValue(zone),
                            (bool)_allowAmbushField.GetValue(zone));
                    else if (label == ambushLabel)
                        command.action = () => SyncSetZoneAllow(
                            zone,
                            (bool)_allowWorkersField.GetValue(zone),
                            (bool)_allowSafeField.GetValue(zone),
                            (bool)_allowCombatantsField.GetValue(zone),
                            !(bool)_allowAmbushField.GetValue(zone));
                }
            }

            return list;
        }

        private static void OpenEscortAssignMenu(ThingComp comp)
        {
            Pawn mech = comp?.parent as Pawn;
            Map map = mech?.Map;
            if (mech == null || map == null)
                return;

            Pawn overseer = null;
            try
            {
                overseer = MechanitorUtility.GetOverseer(mech);
            }
            catch
            {
                // Biotech overseer lookup is optional; fall back to no exclusion.
            }

            var options = new List<FloatMenuOption>();
            foreach (Pawn colonist in map.mapPawns.FreeColonists)
            {
                if (colonist != overseer && colonist != mech)
                {
                    Pawn local = colonist;
                    options.Add(new FloatMenuOption(
                        local.Name.ToStringFull,
                        () => SyncSetEscortTarget(comp, local)));
                }
            }

            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
