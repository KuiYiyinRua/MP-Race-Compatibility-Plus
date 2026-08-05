using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using System;

namespace GodHandMod
{
    // 修正射击精度计算
    [HarmonyPatch(typeof(ShotReport), "HitReportFor")]
    public static class ShotReport_HitReportFor_Patch
    {
        // 射击前标记
        public static void Prefix(Thing caster, Verb verb, LocalTargetInfo target)
        {
            try
            {
                // 检查是否来自炮塔头
                if (TurretHeadShootingTracker.IsVerbFromTurretHead(verb))
                {
                    if (caster is Pawn pawn)
                    {
                        TurretHeadShootingTracker.RegisterShooting(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in HitReportFor Prefix: {ex}");
            }
        }

        // 射击后清除标记
        public static void Postfix(Thing caster, Verb verb, LocalTargetInfo target)
        {
            try
            {
                if (caster is Pawn pawn)
                {
                    TurretHeadShootingTracker.UnregisterShooting(pawn);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in HitReportFor Postfix: {ex}");
            }
        }
    }

    // 修改炮塔射击精度
    [HarmonyPatch(typeof(ShotReport), "HitFactorFromShooter")]
    public static class ShotReport_HitFactorFromShooter_Patch
    {
        public static bool Prefix(Thing caster, float distance, ref float? acc, ref float __result)
        {
            try
            {
                // 仅炮塔头射击修改
                if (caster is Pawn pawn && TurretHeadShootingTracker.IsTurretHeadShooting(pawn))
                {
                    // 使用炮塔射击精度
                    float turretAccuracy = caster.GetStatValue(StatDefOf.ShootingAccuracyTurret);

                    // 计算命中率
                    float num = UnityEngine.Mathf.Pow(turretAccuracy, distance);

                    // 使用原版精度
                    __result = UnityEngine.Mathf.Max(num, 0.0201f);
                    return false; // 跳过原方法
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in HitFactorFromShooter Patch: {ex}");
            }

            return true; // 继续执行原方法
        }
    }

    // 隔离炮塔武器与主武器
    [HarmonyPatch(typeof(Pawn), "TryGetAttackVerb")]
    public static class Pawn_TryGetAttackVerb_Patch
    {
        public static void Postfix(Pawn __instance, ref Verb __result, Thing target, bool allowManualCastWeapons)
        {
            // 返回空以使用正常武器
            if (__result != null && TurretHeadShootingTracker.IsVerbFromTurretHead(__result))
            {
                __result = null;
            }
        }
    }

    // 禁止炮塔射击增加经验
    [HarmonyPatch(typeof(SkillRecord), "Learn")]
    public static class SkillRecord_Learn_Patch
    {
        public static bool Prefix(SkillRecord __instance, Pawn ___pawn, float xp)
        {
            try
            {
                // 跳过射击经验获取
                if (___pawn != null &&
                    __instance.def == SkillDefOf.Shooting &&
                    TurretHeadShootingTracker.IsAnyTurretShooting(___pawn))
                {
                    // 跳过经验获取
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in SkillRecord.Learn Patch: {ex}");
            }
            return true; // 继续执行原方法
        }
    }

    // 射击开始时注册状态
    [HarmonyPatch(typeof(Verb), "WarmupComplete")]
    public static class Verb_WarmupComplete_Patch
    {
        public static void Prefix(Verb __instance)
        {
            try
            {
                if (TurretHeadShootingTracker.IsVerbFromTurretHead(__instance))
                {
                    if (__instance.caster is Pawn pawn)
                    {
                        TurretHeadShootingTracker.RegisterShooting(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in Verb.WarmupComplete Prefix: {ex}");
            }
        }
    }

    // 重置时清除射击状态
    [HarmonyPatch(typeof(Verb), "Reset")]
    public static class Verb_Reset_Patch
    {
        public static void Postfix(Verb __instance)
        {
            try
            {
                // 只对炮塔头的verb取消注册
                if (TurretHeadShootingTracker.IsVerbFromTurretHead(__instance))
                {
                    if (__instance.caster is Pawn pawn)
                    {
                        TurretHeadShootingTracker.UnregisterShooting(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in Verb.Reset Postfix: {ex}");
            }
        }
    }

    // 修正射击源位置
    [HarmonyPatch(typeof(Projectile), "Launch",
        new Type[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Projectile_Launch_Patch
    {
        public static void Prefix(Thing launcher, ref Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget)
        {
            try
            {
                // 计算炮口位置
                var slot = TurretHeadShootingTracker.GetCurrentLaunchingSlot();
                if (slot != null && launcher is Pawn pawn)
                {
                    // 计算完整的炮口位置
                    Vector3 muzzlePos = pawn.DrawPos;

                    // 添加头部偏移
                    muzzlePos += pawn.Drawer.renderer.BaseHeadOffsetAt(pawn.Rotation);

                    // 添加炮塔堆叠偏移
                    muzzlePos += slot.DrawOffset;

                    // 添加炮口前向偏移（基于炮塔旋转角度）
                    float turretSize = slot.TurretDrawSize;
                    float muzzleForwardOffset = turretSize * 0.4f;
                    float rotationRad = slot.curRotation * Mathf.Deg2Rad;
                    muzzlePos.x += Mathf.Sin(rotationRad) * muzzleForwardOffset;
                    muzzlePos.z += Mathf.Cos(rotationRad) * muzzleForwardOffset;

                    // 残阳多炮口射击系统
                    if (slot.HasMultipleShootOffsets)
                    {
                        Vector3 shootOffset = slot.GetNextShootOffset();
                        Vector3 shootDir = (intendedTarget.CenterVector3 - pawn.DrawPos).normalized;
                        Vector3 perpendicular = Quaternion.Euler(0, 90f, 0) * shootDir;
                        muzzlePos += perpendicular * shootOffset.x;
                        muzzlePos += shootDir * shootOffset.z;
                    }
                    else if (slot.hasTopGunSystem && slot.topGuns != null && slot.topGuns.Count > 0)
                    {
                        Vector3 gunOffset = slot.topGuns[0].offsetGun;
                        muzzlePos += Quaternion.Euler(0, slot.curRotation, 0) * gunOffset;
                    }

                    origin = muzzlePos;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in Projectile_Launch Prefix: {ex}");
            }
        }
    }

    // 射击前设置发射槽位
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Verb_LaunchProjectile_TryCastShot_Patch
    {
        public static void Prefix(Verb_LaunchProjectile __instance)
        {
            try
            {
                var slot = TurretHeadShootingTracker.GetSlotForVerb(__instance);
                if (slot != null)
                {
                    TurretHeadShootingTracker.SetCurrentLaunchingSlot(slot);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in TryCastShot Prefix: {ex}");
            }
        }

        public static void Postfix()
        {
            TurretHeadShootingTracker.ClearCurrentLaunchingSlot();
        }
    }

    // 确保炮塔行为不干扰Pawn
    [HarmonyPatch(typeof(Verb), "TryStartCastOn", new Type[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Verb_TryStartCastOn_Patch
    {
        public static void Prefix(Verb __instance, LocalTargetInfo castTarg)
        {
            try
            {
                // 标记炮塔头射击状态
                if (TurretHeadShootingTracker.IsVerbFromTurretHead(__instance))
                {
                    if (__instance.caster is Pawn pawn)
                    {
                        TurretHeadShootingTracker.RegisterShooting(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in TryStartCastOn Prefix: {ex}", 847563941);
            }
        }
    }
}
