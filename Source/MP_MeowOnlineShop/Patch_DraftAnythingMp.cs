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
    /// Draft Anything (`talesoftherim.draftanything`) lets non-colonist pawns
    /// be drafted through a custom Command_Toggle added to Pawn.GetGizmos. The
    /// callback sets CompControlPawn.isControled (saved), calls
    /// Utils.AssignComponents on the pawn, and flips Pawn_DraftController.
    /// Drafted. Only the clicking peer runs that callback.
    ///
    /// Multiplayer already synchronizes the Drafted setter itself, but
    /// isControled and the component assignment are local-only. Replace the
    /// toggle action with one replay command that performs the whole outcome
    /// on every peer; knowledge/lesson feedback stays local.
    /// </summary>
    internal static class Patch_DraftAnythingMp
    {
        private const string PackageId = "talesoftherim.draftanything";
        private const string CompTypeName = "DraftAnything.CompControlPawn";
        private const string UtilsTypeName = "DraftAnything.Utils";

        private static Type _compType;
        private static FieldInfo _isControledField;
        private static MethodInfo _assignComponents;
        private static ISyncMethod _syncToggleControl;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            _isControledField = _compType == null ? null : AccessTools.Field(_compType, "isControled");
            var utilsType = AccessTools.TypeByName(UtilsTypeName);
            _assignComponents = utilsType == null
                ? null
                : AccessTools.Method(utilsType, "AssignComponents", new[] { typeof(Pawn) });

            if (_compType == null || _isControledField == null || _assignComponents == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Draft Anything target resolution failed; patch skipped.");
                return;
            }

            try
            {
                _syncToggleControl = MP.RegisterSyncMethod(
                        typeof(Patch_DraftAnythingMp),
                        nameof(SyncToggleControl))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Draft Anything sync registration failed: " + e.Message);
                return;
            }

            var getGizmos = AccessTools.Method(typeof(Pawn), "GetGizmos", Type.EmptyTypes);
            var postfix = AccessTools.Method(typeof(Patch_DraftAnythingMp), nameof(GizmoPostfix));
            if (getGizmos == null || postfix == null)
                return;

            try
            {
                harmony.Patch(
                    getGizmos,
                    postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
                Log.Message("[MP-MeowOnlineShop] Draft Anything MP patch active: control toggle syncs.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Draft Anything gizmo patch failed: " + e.Message);
            }
        }

        private static void GizmoPostfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null || __instance == null || _syncToggleControl == null)
                return;

            var comp = __instance.AllComps?.FirstOrDefault(c => _compType.IsInstanceOfType(c));
            if (comp == null)
                return;

            var list = __result.ToList();
            foreach (var gizmo in list)
            {
                if (!(gizmo is Command_Toggle toggle) ||
                    toggle.hotKey != KeyBindingDefOf.Command_ColonistDraft ||
                    toggle.groupKey != 81729172)
                {
                    continue;
                }

                var originalAction = toggle.toggleAction;
                toggle.toggleAction = () =>
                {
                    if (!MP.IsInMultiplayer)
                    {
                        originalAction?.Invoke();
                        return;
                    }

                    bool forceControl = !__instance.IsColonistPlayerControlled;
                    bool drafted = !__instance.Drafted;
                    _syncToggleControl.DoSync(null, __instance, forceControl, drafted);
                };
            }

            __result = list;
        }

        public static void SyncToggleControl(Pawn pawn, bool forceControl, bool drafted)
        {
            if (pawn == null)
                return;

            var comp = pawn.AllComps?.FirstOrDefault(c => _compType.IsInstanceOfType(c));
            if (comp == null)
                return;

            if (!pawn.IsColonistPlayerControlled)
                _isControledField?.SetValue(comp, forceControl);

            if (pawn.drafter == null)
                _assignComponents?.Invoke(null, new object[] { pawn });

            if (pawn.drafter != null)
                pawn.drafter.Drafted = drafted;

            if (_isControledField?.GetValue(comp) is bool controlled && controlled)
                _assignComponents?.Invoke(null, new object[] { pawn });
        }
    }
}
