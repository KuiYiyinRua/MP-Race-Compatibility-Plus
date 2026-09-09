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
    /// Tactical Crawling (`np.tacticalcrawling`) toggles CompTacticalCrawl.
    /// isCrawling from its drafted-pawn gizmo. The field is saved and also
    /// mirrored into the static TacticalCrawlTracker set, so a click on one
    /// peer must be replayed everywhere.
    ///
    /// CompTickRare, PostSpawnSetup/PostDeSpawn and the Drafted-setter postfix
    /// call SetCrawling deterministically on both peers; those paths are left
    /// local. Only the gizmo toggle action is replaced with a sync command.
    /// </summary>
    internal static class Patch_TacticalCrawlingMp
    {
        private const string PackageId = "np.tacticalcrawling";
        private const string CompTypeName = "MyTacticalCrawl.CompTacticalCrawl";

        private static Type _compType;
        private static FieldInfo _isCrawlingField;
        private static MethodInfo _setCrawling;
        private static ISyncMethod _syncSetCrawling;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            _isCrawlingField = _compType == null ? null : AccessTools.Field(_compType, "isCrawling");
            _setCrawling = _compType == null ? null : AccessTools.Method(_compType, "SetCrawling", new[] { typeof(bool) });
            if (_compType == null || _isCrawlingField == null || _setCrawling == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Tactical Crawling target resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncSetCrawling = MP.RegisterSyncMethod(
                        typeof(Patch_TacticalCrawlingMp),
                        nameof(SyncSetCrawling))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Tactical Crawling sync registration failed: " + e.Message);
                return;
            }

            var gizmoMethod = AccessTools.Method(_compType, "CompGetGizmosExtra", Type.EmptyTypes);
            var postfix = AccessTools.Method(typeof(Patch_TacticalCrawlingMp), nameof(GizmoPostfix));
            if (gizmoMethod == null || postfix == null)
                return;

            try
            {
                harmony.Patch(gizmoMethod, postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] Tactical Crawling MP patch active: crawl toggle syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Tactical Crawling gizmo patch failed: " + e.Message);
            }
        }

        private static void GizmoPostfix(object __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || _syncSetCrawling == null)
                return;

            var comp = __instance as ThingComp;
            var parent = comp?.parent;
            if (parent == null)
                return;

            var list = __result.ToList();
            foreach (var gizmo in list)
            {
                if (!(gizmo is Command_Toggle toggle))
                    continue;

                toggle.toggleAction = () =>
                {
                    if (!MP.IsInMultiplayer)
                    {
                        _setCrawling.Invoke(comp, new object[] { !GetCrawling(comp) });
                        return;
                    }

                    _syncSetCrawling.DoSync(null, parent, !GetCrawling(comp));
                };
            }

            __result = list;
        }

        public static void SyncSetCrawling(Thing parent, bool value)
        {
            var comp = (parent as ThingWithComps)?.AllComps?.FirstOrDefault(c => _compType.IsInstanceOfType(c));
            if (comp == null)
                return;

            _setCrawling.Invoke(comp, new object[] { value });
        }

        private static bool GetCrawling(ThingComp comp)
        {
            return comp != null && _isCrawlingField != null &&
                   _isCrawlingField.GetValue(comp) is bool value && value;
        }
    }
}
