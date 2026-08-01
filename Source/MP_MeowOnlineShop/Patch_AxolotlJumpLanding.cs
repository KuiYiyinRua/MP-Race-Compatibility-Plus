using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Axolotl jump landing compatibility:
    /// replaces FallOnGroundDo with a null-safe equivalent in multiplayer.
    /// </summary>
    internal static class Patch_AxolotlJumpLanding
    {
        private const int MaxRuntimeLogs = 8;

        private static int _runtimeLogCount;
        private static bool _resolved;
        private static bool _available;

        private static Type _flyerType;
        private static FieldInfo _getPawnField;
        private static MethodInfo _fallOnGroundDoMethod;

        private static Type _compCultivationType;
        private static MethodInfo _getInstallSkillsByTypeMethod;
        private static MethodInfo _getMoeLotlQiSkillMethod;

        private static Type _qiSkillTypeDefOfType;
        private static FieldInfo _lightSkillTypeField;

        private static Type _lightSkillCompType;
        private static MethodInfo _skillGetCompGenericMethod;
        private static MethodInfo _notifyEndJumpMethod;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            ResolveReflection();
            if (!_available)
            {
                Log.Warning("[MP-MeowOnlineShop] Axolotl jump landing patch skipped: target signatures not fully available.");
                return;
            }

            var prefix = AccessTools.Method(typeof(Patch_AxolotlJumpLanding), nameof(FallOnGroundDoPrefix));
            if (prefix == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Axolotl jump landing patch skipped: prefix method missing.");
                return;
            }

            harmony.Patch(_fallOnGroundDoMethod, prefix: new HarmonyMethod(prefix));
            Log.Message("[MP-MeowOnlineShop] Axolotl jump landing patch active: FallOnGroundDo routed to null-safe handler.");
        }

        private static void ResolveReflection()
        {
            if (_resolved)
                return;
            _resolved = true;

            _flyerType = AccessTools.TypeByName("Axolotl.AxolotlPawnMoveFlyer");
            _fallOnGroundDoMethod = AccessTools.Method(_flyerType, "FallOnGroundDo", Type.EmptyTypes);
            _getPawnField = AccessTools.Field(_flyerType, "GetPawn");

            _compCultivationType = AccessTools.TypeByName("Axolotl.Comp_Cultivation");
            _getInstallSkillsByTypeMethod = AccessTools.Method(_compCultivationType, "GetInstallSkillsByType");
            _getMoeLotlQiSkillMethod = AccessTools.Method(_compCultivationType, "GetMoeLotlQiSkill");

            _qiSkillTypeDefOfType = AccessTools.TypeByName("Axolotl.AxolotlMoeLotlQiSkillTypeDefOf");
            _lightSkillTypeField = AccessTools.Field(_qiSkillTypeDefOfType, "Axolotl_MoeLotlQiSkillType_Ligt");

            _lightSkillCompType = AccessTools.TypeByName("Axolotl.MoeLotlQiSkillComp_LigtSkillBase");
            _notifyEndJumpMethod = AccessTools.Method(_lightSkillCompType, "Notify_EndJump", Type.EmptyTypes);

            var qiSkillType = AccessTools.TypeByName("Axolotl.MoeLotlQiSkill");
            if (qiSkillType != null)
            {
                foreach (var method in qiSkillType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!method.IsGenericMethodDefinition || method.Name != "GetComp")
                        continue;
                    if (method.GetGenericArguments().Length != 1 || method.GetParameters().Length != 0)
                        continue;
                    _skillGetCompGenericMethod = method;
                    break;
                }
            }

            _available =
                _flyerType != null &&
                _fallOnGroundDoMethod != null &&
                _getPawnField != null &&
                _compCultivationType != null &&
                _getInstallSkillsByTypeMethod != null &&
                _getMoeLotlQiSkillMethod != null &&
                _lightSkillTypeField != null &&
                _lightSkillCompType != null &&
                _skillGetCompGenericMethod != null &&
                _notifyEndJumpMethod != null;
        }

        private static bool FallOnGroundDoPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer)
                return true;

            try
            {
                RunNullSafeLanding(__instance);
            }
            catch (Exception e)
            {
                RuntimeLog("safe landing handler threw: " + e.GetType().Name);
            }

            // Skip original to avoid known null-path spam in MP tick.
            return false;
        }

        private static void RunNullSafeLanding(object instance)
        {
            if (instance == null)
            {
                RuntimeLog("skip: flyer instance is null.");
                return;
            }

            var pawn = _getPawnField.GetValue(instance) as Pawn;
            if (pawn == null)
            {
                RuntimeLog("skip: GetPawn is null.");
                return;
            }

            var comp = TryGetCultivationComp(pawn);
            if (comp == null)
            {
                RuntimeLog("skip: Comp_Cultivation missing on pawn " + pawn.ThingID + ".");
                return;
            }

            var lightTypeDef = _lightSkillTypeField.GetValue(null);
            if (lightTypeDef == null)
            {
                RuntimeLog("skip: light skill type def is null.");
                return;
            }

            var skillDefsObj = _getInstallSkillsByTypeMethod.Invoke(comp, new[] { lightTypeDef }) as IEnumerable;
            if (skillDefsObj == null)
                return;

            foreach (var skillDef in skillDefsObj)
            {
                if (skillDef == null)
                    continue;

                object skill;
                try
                {
                    skill = _getMoeLotlQiSkillMethod.Invoke(comp, new[] { skillDef });
                }
                catch
                {
                    continue;
                }

                if (skill == null)
                    continue;

                object lightSkillComp;
                try
                {
                    var getComp = _skillGetCompGenericMethod.MakeGenericMethod(_lightSkillCompType);
                    lightSkillComp = getComp.Invoke(skill, null);
                }
                catch
                {
                    continue;
                }

                if (lightSkillComp == null)
                    continue;

                try
                {
                    _notifyEndJumpMethod.Invoke(lightSkillComp, null);
                }
                catch
                {
                    // Keep behavior best-effort and continue with other skills.
                }
            }
        }

        private static object TryGetCultivationComp(Pawn pawn)
        {
            List<ThingComp> allComps;
            try
            {
                allComps = pawn.AllComps;
            }
            catch
            {
                return null;
            }

            if (allComps == null)
                return null;

            for (var i = 0; i < allComps.Count; i++)
            {
                var comp = allComps[i];
                if (comp != null && _compCultivationType.IsInstanceOfType(comp))
                    return comp;
            }

            return null;
        }

        private static void RuntimeLog(string message)
        {
            if (_runtimeLogCount >= MaxRuntimeLogs)
                return;

            _runtimeLogCount++;
            Log.Message("[MP-MeowOnlineShop] Axolotl jump landing: " + message);
        }
    }
}
