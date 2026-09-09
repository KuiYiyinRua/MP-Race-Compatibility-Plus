using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MP_MeowOnlineShop.MiliraAddonCompat;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Ancot Library's MechAutoFight gizmo and pawn-column checkbox write the
    /// saved autoFight state directly from UI code. The gizmo additionally
    /// adds/removes the combat hediff, while the checkbox only flips the
    /// AutoFight property. Multiplayer has no built-in coverage for the custom
    /// Ancot comp, so a click on one peer leaves the other peer's mech in a
    /// different combat mode; the resulting one-sided jobs (e.g. only one peer
    /// hauling/despawning a Milian mech) are the first trace mismatch in
    /// Desync-491.
    ///
    /// The gizmo action is replaced by one synchronized method that performs
    /// the full outcome (AutoFight property + hediff) on every peer. The
    /// pawn-column checkbox is watched through a SyncField whose PostApply
    /// runs the public property setter, keeping the alert target list
    /// consistent without replaying the gizmo-only hediff.
    /// </summary>
    internal static class Patch_AncotMechAutoFightMp
    {
        private const string PackageId = "Ancot.AncotLibrary";
        private const string CompTypeName = "AncotLibrary.CompMechAutoFight";
        private const string PropsTypeName = "AncotLibrary.CompProperties_MechAutoFight";
        private const string ColumnWorkerTypeName = "AncotLibrary.PawnColumnWorker_AutoFightMech";
        private const string FieldName = "autoFight";

        private static Type _compType;
        private static Type _propsType;
        private static FieldInfo _autoFightField;
        private static PropertyInfo _autoFightProperty;
        private static FieldInfo _hediffDefField;
        private static ISyncMethod _syncToggle;
        private static bool _fieldRegistered;

        [ThreadStatic] private static bool _watchingColumn;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            _propsType = AccessTools.TypeByName(PropsTypeName);
            _autoFightField = _compType == null ? null : AccessTools.Field(_compType, FieldName);
            _autoFightProperty = _compType == null ? null : AccessTools.Property(_compType, "AutoFight");
            _hediffDefField = _propsType == null ? null : AccessTools.Field(_propsType, "hediffDef");

            if (_compType == null || _propsType == null || _autoFightField == null ||
                _autoFightProperty == null || _hediffDefField == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot mech auto-fight target resolution failed; patch skipped.");
                return;
            }

            int registered = 0;
            try
            {
                MP.RegisterSyncField(_autoFightField)
                    .PostApply((target, value) =>
                    {
                        if (target is ThingComp comp && value is bool autoFightValue)
                            _autoFightProperty.SetValue(comp, autoFightValue);
                    });
                _fieldRegistered = true;
                registered++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot mech auto-fight field registration failed: " + e.Message);
            }

            try
            {
                _syncToggle = MP.RegisterSyncMethod(
                        typeof(Patch_AncotMechAutoFightMp),
                        nameof(SyncedToggleAutoFight))
                    .SetContext(SyncContext.CurrentMap);
                registered++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot mech auto-fight sync method registration failed: " + e.Message);
                _syncToggle = null;
            }

            int callbacks = 0;
            if (_syncToggle != null)
            {
                MethodInfo gizmoMethod = AccessTools.Method(_compType, "CompGetGizmosExtra", Type.EmptyTypes);
                MethodInfo gizmoPostfix = AccessTools.Method(typeof(Patch_AncotMechAutoFightMp), nameof(GizmoPostfix));
                if (gizmoMethod != null && gizmoPostfix != null)
                {
                    try
                    {
                        harmony.Patch(gizmoMethod, postfix: new HarmonyMethod(gizmoPostfix));
                        callbacks++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Ancot mech auto-fight gizmo patch failed: " + e.Message);
                    }
                }
            }

            int column = 0;
            if (_fieldRegistered)
            {
                Type columnWorkerType = AccessTools.TypeByName(ColumnWorkerTypeName);
                MethodInfo doCell = columnWorkerType == null ? null : AccessTools.Method(columnWorkerType, "DoCell");
                MethodInfo prefix = AccessTools.Method(typeof(Patch_AncotMechAutoFightMp), nameof(DoCellPrefix));
                MethodInfo postfix = AccessTools.Method(typeof(Patch_AncotMechAutoFightMp), nameof(DoCellPostfix));
                MethodInfo finalizer = AccessTools.Method(typeof(Patch_AncotMechAutoFightMp), nameof(DoCellFinalizer));
                if (doCell != null && prefix != null && postfix != null && finalizer != null)
                {
                    try
                    {
                        harmony.Patch(
                            doCell,
                            prefix: new HarmonyMethod(prefix),
                            postfix: new HarmonyMethod(postfix),
                            finalizer: new HarmonyMethod(finalizer));
                        column++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Ancot mech auto-fight pawn-column patch failed: " + e.Message);
                    }
                }
            }

            Log.Message(
                "[MP-MeowOnlineShop] Ancot mech auto-fight MP patch active: " +
                $"syncRegistrations={registered}, gizmoWrapped={callbacks}/1, pawnColumnWatched={column}/1.");
        }

        private static void GizmoPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || _syncToggle == null ||
                !_compType.IsInstanceOfType(__instance) || __instance.parent == null)
                return;

            List<Gizmo> list = __result.ToList();
            bool wrapped = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Command_Toggle toggle) || toggle.toggleAction == null)
                    continue;

                Action original = toggle.toggleAction;
                ThingComp comp = __instance;
                toggle.toggleAction = () =>
                {
                    Thing thing = comp.parent;
                    if (!MP.IsInMultiplayer || thing == null || thing.Map == null || _syncToggle == null)
                    {
                        original?.Invoke();
                        return;
                    }

                    bool next;
                    try
                    {
                        next = !(bool)_autoFightField.GetValue(comp);
                    }
                    catch
                    {
                        next = !(bool)_autoFightProperty.GetValue(comp, null);
                    }

                    try
                    {
                        _syncToggle.DoSync(null, thing.Map.Index, thing.thingIDNumber, next);
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Ancot mech auto-fight sync dispatch failed: " + e.Message);
                    }
                };
                wrapped = true;
                break;
            }

            if (wrapped)
                __result = list;
        }

        private static void DoCellPrefix(Pawn pawn)
        {
            if (!MP.IsInMultiplayer || !_fieldRegistered || pawn == null || pawn.AllComps == null)
                return;

            ThingComp comp = null;
            for (int i = 0; i < pawn.AllComps.Count; i++)
            {
                if (_compType.IsInstanceOfType(pawn.AllComps[i]))
                {
                    comp = pawn.AllComps[i];
                    break;
                }
            }
            if (comp == null)
                return;

            try
            {
                MP.WatchBegin();
                _watchingColumn = true;
                MP.Watch(comp, FieldName);
            }
            catch
            {
                // The field may not be registered on this peer; the postfix
                // still pops the watch marker.
            }
        }

        private static void DoCellPostfix()
        {
            if (_watchingColumn)
            {
                _watchingColumn = false;
                MP.WatchEnd();
            }
        }

        private static void DoCellFinalizer(Exception __exception)
        {
            if (_watchingColumn)
            {
                _watchingColumn = false;
                MP.WatchEnd();
            }
        }

        public static void SyncedToggleAutoFight(int mapIndex, int thingId, bool value)
        {
            Thing thing = CompatUtility.FindThingByMapAndID(mapIndex, thingId);
            ThingWithComps withComps = thing as ThingWithComps;
            ThingComp comp = withComps == null || withComps.AllComps == null
                ? null
                : withComps.AllComps.FirstOrDefault(c => c != null && _compType.IsInstanceOfType(c));
            if (comp == null)
                return;

            try
            {
                _autoFightProperty.SetValue(comp, value);
            }
            catch
            {
                _autoFightField.SetValue(comp, value);
            }

            ApplyAutoFightHediff(comp, value);
        }

        private static void ApplyAutoFightHediff(ThingComp comp, bool value)
        {
            if (!(comp.parent is Pawn pawn) || pawn.health?.hediffSet == null)
                return;

            HediffDef hediffDef;
            try
            {
                hediffDef = _hediffDefField.GetValue(comp.props) as HediffDef;
            }
            catch
            {
                hediffDef = null;
            }
            if (hediffDef == null)
                return;

            if (value)
            {
                if (pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef, false) == null)
                    pawn.health.AddHediff(hediffDef, null, null, null);
            }
            else
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef, false);
                if (hediff != null)
                    pawn.health.RemoveHediff(hediff);
            }
        }
    }
}
