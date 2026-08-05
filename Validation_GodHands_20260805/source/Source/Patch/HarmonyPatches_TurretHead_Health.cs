using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using System;

namespace GodHandMod
{
    // 增加头部HP上限分摊
    [HarmonyPatch(typeof(BodyPartDef), "GetMaxHealth")]
    public static class BodyPartDef_GetMaxHealth_Patch
    {
        public static void Postfix(BodyPartDef __instance, Pawn pawn, ref float __result)
        {
            try
            {
                if (pawn == null) return;

                // 找到当前部位对应的BodyPartRecord
                if (pawn.health?.hediffSet != null)
                {
                    foreach (var part in pawn.RaceProps.body.AllParts)
                    {
                        if (part.def == __instance && TurretHeadHPBonus.IsHeadOrChildOfHead(part))
                        {
                            // 头部位置加成
                            float bonus = TurretHeadHPBonus.GetPartHPBonus(pawn, __instance, __instance.hitPoints);
                            if (bonus > 0f)
                            {
                                __result += bonus;
                            }
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in GetMaxHealth Patch: {ex}", 847563921);
            }
        }
    }

    // 头部伤口不流血检查
    [HarmonyPatch(typeof(Hediff_Injury), "get_BleedRate")]
    public static class Hediff_Injury_BleedRate_Patch
    {
        public static void Postfix(Hediff_Injury __instance, ref float __result)
        {
            try
            {
                if (__result <= 0f) return;

                Pawn pawn = __instance.pawn;
                if (pawn == null) return;

                // 检查是否戴着炮塔头且伤口在头部
                if (TurretHeadProtectionHelper.HasTurretHead(pawn) &&
                    TurretHeadProtectionHelper.IsHeadPart(__instance.Part))
                {
                    __result = 0f; // 头部伤口不流血
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in BleedRate Patch: {ex}", 847563922);
            }
        }
    }

    // 头部伤口不产生疼痛
    [HarmonyPatch(typeof(Hediff_Injury), "get_PainOffset")]
    public static class Hediff_Injury_PainOffset_Patch
    {
        public static void Postfix(Hediff_Injury __instance, ref float __result)
        {
            try
            {
                if (__result <= 0f) return;

                Pawn pawn = __instance.pawn;
                if (pawn == null) return;

                // 检查是否戴着炮塔头且伤口在头部
                if (TurretHeadProtectionHelper.HasTurretHead(pawn) &&
                    TurretHeadProtectionHelper.IsHeadPart(__instance.Part))
                {
                    __result = 0f; // 头部伤口不疼痛
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in PainOffset Patch: {ex}", 847563923);
            }
        }
    }

    // 防止头部因过量伤害击毁
    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ReduceDamageToPreserveOutsideParts")]
    public static class DamageWorker_AddInjury_ReduceDamageToPreserveOutsideParts_Patch
    {
        public static bool Prefix(float postArmorDamage, DamageInfo dinfo, Pawn pawn, ref float __result)
        {
            try
            {
                if (pawn == null) return true;
                if (!TurretHeadProtectionHelper.HasTurretHead(pawn)) return true;

                // 检查是否是头部
                BodyPartRecord hitPart = dinfo.HitPart;
                if (hitPart == null) return true;

                if (TurretHeadProtectionHelper.IsHeadPart(hitPart))
                {
                    // 获取部位当前HP
                    float partHealth = pawn.health.hediffSet.GetPartHealth(hitPart);

                    // 强制限制过量伤害
                    if (postArmorDamage >= partHealth && partHealth > 1f)
                    {
                        __result = partHealth - 1f;
                        return false; // 跳过原方法
                    }

                    // 正常返回未超限伤害
                    __result = postArmorDamage;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in ReduceDamageToPreserveOutsideParts Patch: {ex}", 847563924);
            }
            return true; // 继续执行原方法
        }
    }

    // 确保头部HP最小为1
    [HarmonyPatch(typeof(HediffSet), "GetPartHealth")]
    public static class HediffSet_GetPartHealth_Patch
    {
        public static void Postfix(HediffSet __instance, BodyPartRecord part, ref float __result)
        {
            try
            {
                // 忽略HP大于1的部位
                if (__result > 1f) return;

                Pawn pawn = __instance.pawn;
                if (pawn == null) return;

                // 检查是否戴着炮塔头且部位是头部
                if (TurretHeadProtectionHelper.HasTurretHead(pawn) &&
                    TurretHeadProtectionHelper.IsHeadPart(part))
                {
                    // 确保头部部位HP最小为1
                    __result = Mathf.Max(__result, 1f);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in GetPartHealth Patch: {ex}", 847563926);
            }
        }
    }

    // 头部伤口不破坏部位
    [HarmonyPatch(typeof(HediffSet), "AddDirect")]
    public static class HediffSet_AddDirect_Patch
    {
        public static void Prefix(HediffSet __instance, Hediff hediff)
        {
            try
            {
                if (hediff is Hediff_Injury injury)
                {
                    Pawn pawn = __instance.pawn;
                    if (pawn == null) return;

                    // 检查是否戴着炮塔头且伤口在头部
                    if (TurretHeadProtectionHelper.HasTurretHead(pawn) &&
                        TurretHeadProtectionHelper.IsHeadPart(injury.Part))
                    {
                        // 禁止此伤口摧毁部位
                        injury.destroysBodyParts = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in AddDirect Patch: {ex}", 847563927);
            }
        }
    }
}
