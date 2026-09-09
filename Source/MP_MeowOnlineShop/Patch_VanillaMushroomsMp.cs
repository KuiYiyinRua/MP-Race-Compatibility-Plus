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
    /// Vanilla Plants Expanded - Mushrooms adds allowSow/allowCut toggles to
    /// Zone_GrowingMushroom. Both are saved fields that drive WorkGiver
    /// eligibility, and the gizmo lambdas write them directly, so the two
    /// toggle actions are rewritten to registered sync methods.
    /// </summary>
    internal static class Patch_VanillaMushroomsMp
    {
        private const string ZoneTypeName = "VanillaPlantsExpandedMushrooms.Zone_GrowingMushroom";

        private static bool _applied;
        private static FieldInfo _allowSowField;
        private static FieldInfo _allowCutField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type zoneType = AccessTools.TypeByName(ZoneTypeName);
                _allowSowField = zoneType == null ? null : AccessTools.Field(zoneType, "allowSow");
                _allowCutField = zoneType == null ? null : AccessTools.Field(zoneType, "allowCut");
                if (zoneType == null || _allowSowField == null || _allowCutField == null)
                {
                    Log.Message("[MP-MeowOnlineShop] Vanilla Mushrooms sync skipped (target type not resolved).");
                    return;
                }

                int registered = 0;
                registered += TryRegister(
                    AccessTools.Method(
                        typeof(Patch_VanillaMushroomsMp),
                        nameof(SyncToggleAllowSow),
                        new[] { typeof(Zone) }));
                registered += TryRegister(
                    AccessTools.Method(
                        typeof(Patch_VanillaMushroomsMp),
                        nameof(SyncToggleAllowCut),
                        new[] { typeof(Zone) }));
                if (registered == 0)
                {
                    Log.Warning("[MP-MeowOnlineShop] Vanilla Mushrooms sync registration failed; patch skipped.");
                    return;
                }

                MethodInfo getGizmos = AccessTools.Method(zoneType, "GetGizmos", Type.EmptyTypes);
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_VanillaMushroomsMp),
                    nameof(GizmosPostfix));
                if (getGizmos == null || postfix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Vanilla Mushrooms gizmo target not resolved; patch skipped.");
                    return;
                }

                harmony.Patch(getGizmos, postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] Vanilla Mushrooms MP patch active: 2 sync methods, toggles rewritten.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Vanilla Mushrooms MP compat restore failed: " + e.Message);
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
                Log.Warning("[MP-MeowOnlineShop] Vanilla Mushrooms sync register failed on " + method.Name + ": " + e.Message);
                return 0;
            }
        }

        public static void SyncToggleAllowSow(Zone zone)
        {
            if (zone == null || _allowSowField == null)
                return;
            _allowSowField.SetValue(zone, !(bool)_allowSowField.GetValue(zone));
        }

        public static void SyncToggleAllowCut(Zone zone)
        {
            if (zone == null || _allowCutField == null)
                return;
            _allowCutField.SetValue(zone, !(bool)_allowCutField.GetValue(zone));
        }

        private static IEnumerable<Gizmo> GizmosPostfix(
            IEnumerable<Gizmo> __result,
            Zone __instance)
        {
            if (__result == null || __instance == null)
                return __result;

            string sowLabel = Translator.Translate("CommandAllowSow").ToString();
            string cutLabel = Translator.Translate("CommandAllowCut").ToString();
            Zone zone = __instance;
            List<Gizmo> list = __result.ToList();

            foreach (Gizmo gizmo in list)
            {
                if (gizmo is Command_Toggle toggle && toggle.toggleAction != null)
                {
                    string label = toggle.defaultLabel?.ToString();
                    if (label == sowLabel)
                        toggle.toggleAction = () => SyncToggleAllowSow(zone);
                    else if (label == cutLabel)
                        toggle.toggleAction = () => SyncToggleAllowCut(zone);
                }
            }

            return list;
        }
    }
}
