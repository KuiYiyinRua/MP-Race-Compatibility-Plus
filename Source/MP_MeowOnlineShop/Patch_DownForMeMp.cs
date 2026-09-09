using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Down For Me (`aRandomKiwi.DownForMe`) adds gizmos that directly call
    /// Pawn_HealthTracker.AddHediff/RemoveHediff from compiler-generated
    /// lambda actions. Those health mutations are simulation state and are not
    /// covered by Multiplayer's job/draft sync, so the two gizmo actions are
    /// rewritten to invoke one registered sync method.
    /// </summary>
    internal static class Patch_DownForMeMp
    {
        private const string PackageId = "aRandomKiwi.DownForMe";
        private const string HediffDefName = "aRandomKiwi_DFM_WTBD";

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Down For Me sync skipped (target mod not active).");
                    return;
                }

                MethodInfo syncMethod = AccessTools.Method(
                    typeof(Patch_DownForMeMp),
                    nameof(SyncSetForceDown),
                    new[] { typeof(Pawn), typeof(bool) });
                if (syncMethod == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Down For Me sync method resolution failed; patch skipped.");
                    return;
                }

                MP.RegisterSyncMethod(syncMethod, null);

                MethodInfo getGizmos = AccessTools.Method(typeof(Pawn_MindState), "GetGizmos");
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_DownForMeMp),
                    nameof(GizmosPostfix));
                if (getGizmos == null || postfix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Down For Me gizmo target not resolved; patch skipped.");
                    return;
                }

                harmony.Patch(getGizmos, postfix: new HarmonyMethod(postfix));
                Log.Message("[MP-MeowOnlineShop] Down For Me MP patch active: 1 sync method, 2 gizmo actions rewritten.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Down For Me MP compat restore failed: " + e.Message);
            }
        }

        public static void SyncSetForceDown(Pawn pawn, bool down)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(HediffDefName);
            if (def == null || pawn?.health?.hediffSet == null)
                return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def, false);
            if (down && existing == null)
                pawn.health.AddHediff(def, null, null, null);
            else if (!down && existing != null)
                pawn.health.RemoveHediff(existing);
        }

        private static IEnumerable<Gizmo> GizmosPostfix(
            IEnumerable<Gizmo> __result,
            Pawn_MindState __instance)
        {
            if (__result == null || __instance?.pawn == null)
                return __result;

            string downLabel = Translator.Translate("aRandomKiwi_DFM_ForceDownLabel").ToString();
            string undownLabel = Translator.Translate("aRandomKiwi_DFM_ForceUndownLabel").ToString();
            Pawn pawn = __instance.pawn;
            List<Gizmo> list = __result.ToList();

            foreach (Gizmo gizmo in list)
            {
                if (gizmo is Command_Action command && command.action != null)
                {
                    string label = command.defaultLabel?.ToString();
                    if (label == downLabel)
                        command.action = () => SyncSetForceDown(pawn, true);
                    else if (label == undownLabel)
                        command.action = () => SyncSetForceDown(pawn, false);
                }
            }

            return list;
        }
    }
}
