using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using RimWorld;

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
    /// safe. UI scopes explicitly watch registered fields so local changes are
    /// restored and replayed through MP. Registration alone does not watch writes.
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
        private static FieldInfo _windowComps;
        private static FieldInfo _commandComp;
        private static MethodInfo _selectedComps;

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

            // Exact worker takes precedence over MP's generic same-type list index.
            MP.RegisterSyncWorker<object>(SyncComponent, _compType);
            int registeredFields = 0;
            foreach (string fieldName in SyncFieldNames)
            {
                FieldInfo field = AccessTools.Field(_compType, fieldName);
                if (field == null)
                    continue;
                try
                {
                    var handler = MP.RegisterSyncField(field);
                    if (fieldName == "localMinDistanceToBlink")
                        handler.SetBufferChanges();
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
                var windowType = AccessTools.TypeByName("AutoBlink.Window_AutoBlinkDetail");
                var commandType = AccessTools.TypeByName("AutoBlink.Command_AutoBlinkRoot");
                _windowComps = AccessTools.Field(windowType, "comps");
                _commandComp = AccessTools.Field(commandType, "Comp");
                _selectedComps = AccessTools.Method(commandType, "SelectedComps", Type.EmptyTypes);
                var draw = AccessTools.Method(windowType, "DoWindowContents", new[] { typeof(UnityEngine.Rect) });
                var input = AccessTools.Method(commandType, "ProcessInput", new[] { typeof(UnityEngine.Event) });
                if (registeredFields != SyncFieldNames.Length || _windowComps == null ||
                    _commandComp == null || _selectedComps == null || draw == null || input == null)
                    throw new InvalidOperationException("AutoBlink UI targets incomplete; UI sync unavailable");
                var finalizer = new HarmonyMethod(typeof(Patch_AutoBlinkMp), nameof(EndUiWatch));
                harmony.Patch(draw, prefix: new HarmonyMethod(typeof(Patch_AutoBlinkMp), nameof(WindowWatchPrefix)),
                    finalizer: finalizer);
                harmony.Patch(input, prefix: new HarmonyMethod(typeof(Patch_AutoBlinkMp), nameof(CommandWatchPrefix)),
                    finalizer: finalizer);
                Log.Message(
                    "[MP-MeowOnlineShop] AutoBlink MP patch active: " +
                    $"fields={registeredFields}/{SyncFieldNames.Length}, blinkSync=true, uiWatch=true, stableOwner=true.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] AutoBlink patch installation incomplete: " + e.Message);
            }
        }

        private static void WindowWatchPrefix(object __instance, ref bool __state)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
                return;
            BeginUiWatch((IEnumerable)_windowComps.GetValue(__instance), ref __state);
        }

        private static void CommandWatchPrefix(object __instance, ref bool __state)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
                return;
            var targets = Find.Selector.NumSelected <= 1
                ? new[] { _commandComp.GetValue(__instance) }
                : ((IEnumerable)_selectedComps.Invoke(null, null)).Cast<object>().ToArray();
            BeginUiWatch(targets, ref __state);
        }

        private static void BeginUiWatch(IEnumerable targets, ref bool state)
        {
            // The window retains its own targets even after the selection changes.
            var snapshot = targets.Cast<object>().Where(c => c != null).Distinct().ToArray();
            MP.WatchBegin();
            state = true;
            foreach (var comp in snapshot)
                foreach (var fieldName in SyncFieldNames)
                    MP.Watch(comp, fieldName);
        }

        private static Exception EndUiWatch(Exception __exception, ref bool __state)
        {
            if (__state)
            {
                __state = false;
                MP.WatchEnd();
            }
            return __exception;
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

            if (!parent.AllComps.Contains(comp)) return false;
            int kind, slot; string sourceId;
            if (!TryIdentity(comp, out kind, out sourceId, out slot))
                throw new InvalidOperationException("AutoBlink component source cannot be identified");
            _syncBlink.DoSync(null, map, parent, kind, sourceId, slot, cell);
            return false;
        }

        private static void SyncComponent(SyncWorker sync, ref object value)
        {
            ThingWithComps parent = null;
            Map map = null;
            int kind = -1, slot = -1;
            string sourceId = "";
            if (sync.isWriting && value is ThingComp comp && comp.parent != null && comp.parent.AllComps.Contains(comp))
            {
                if (!TryIdentity(comp, out kind, out sourceId, out slot))
                    throw new InvalidOperationException("AutoBlink field component source cannot be identified");
                parent = comp.parent; map = parent.Map;
            }
            // A buffered UI edit may outlive its removed component: send a null
            // identity instead of throwing or redirecting to another component.
            // Read the complete identity even when an owner disappeared before execution.
            sync.Bind(ref map); sync.Bind(ref parent);
            sync.Bind(ref kind); sync.Bind(ref sourceId); sync.Bind(ref slot);
            if (!sync.isWriting)
                value = parent != null && parent.Map == map ? Resolve(parent, kind, sourceId, slot) : null;
        }
        // Stable owner IDs avoid retargeting when an earlier dynamic comp is removed.
        private static string Id(object value) => (value as ILoadReferenceable)?.GetUniqueLoadID();
        private static object Linked(ThingComp comp, string name) => AccessTools.Field(_compType, name).GetValue(comp);
        private static ThingComp Runtime(object owner) => AccessTools.Field(owner.GetType(), "runtimeComp")?.GetValue(owner) as ThingComp;
        private static bool TryIdentity(ThingComp comp, out int kind, out string sourceId, out int slot)
        {
            kind = -1; sourceId = ""; slot = -1;
            if (comp?.parent == null || !comp.parent.AllComps.Contains(comp)) return false;
            var gene = Linked(comp, "linkedGene") as Gene;
            if (gene != null && ReferenceEquals(Runtime(gene), comp))
            { kind = 1; sourceId = Id(gene); slot = 0; return sourceId != null; }
            var hediff = Linked(comp, "linkedHediff") as HediffWithComps;
            if (hediff != null)
                for (int i = 0; i < hediff.comps.Count; i++)
                    if (ReferenceEquals(Runtime(hediff.comps[i]), comp))
                    { kind = 2; sourceId = Id(hediff); slot = i; return sourceId != null; }
            // Def-owned comps retain their CompProperties identity despite dynamic additions.
            var defs = comp.parent.def.comps;
            for (int i = 0; i < defs.Count; i++)
                if (ReferenceEquals(defs[i], comp.props))
                { kind = 0; slot = i; return true; }
            return false;
        }
        private static ThingComp Resolve(ThingWithComps parent, int kind, string sourceId, int slot)
        {
            if (parent == null) return null;
            ThingComp result = null;
            if (kind == 0 && slot >= 0 && slot < parent.def.comps.Count)
                result = parent.AllComps.FirstOrDefault(c => ReferenceEquals(c.props, parent.def.comps[slot]));
            else if (parent is Pawn pawn)
            {
                if (kind == 1)
                {
                    var gene = pawn.genes?.GenesListForReading.FirstOrDefault(g => Id(g) == sourceId);
                    if (gene != null) result = Runtime(gene);
                }
                else if (kind == 2)
                {
                    var hediff = pawn.health.hediffSet.hediffs.FirstOrDefault(h => Id(h) == sourceId) as HediffWithComps;
                    if (hediff != null && slot >= 0 && slot < hediff.comps.Count) result = Runtime(hediff.comps[slot]);
                }
            }
            return result != null && _compType.IsInstanceOfType(result) && parent.AllComps.Contains(result) ? result : null;
        }
        public static void SyncBlinkToCell(Map map, ThingWithComps parent, int kind, string sourceId, int slot, IntVec3 cell)
        {
            if (_blinkToCell == null || map == null || parent?.Map != map) return;
            var comp = Resolve(parent, kind, sourceId, slot);
            // A source removed before execution cancels instead of selecting its successor.
            if (comp == null) return;
            try { _replayingBlink = true; _blinkToCell.Invoke(comp, new object[] { cell }); }
            finally { _replayingBlink = false; }
        }
    }
}
