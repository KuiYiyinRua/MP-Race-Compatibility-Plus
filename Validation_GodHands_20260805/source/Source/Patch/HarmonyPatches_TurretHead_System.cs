using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace GodHandMod
{
    // 戴炮塔头时跳过头部渲染
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Head), "CanDrawNow")]
    public static class PawnRenderNodeWorker_Head_CanDrawNow_Patch
    {
        public static bool Prefix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            try
            {
                Pawn pawn = parms.pawn;
                if (pawn?.apparel?.WornApparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel is GodHandTurretHead turretHead && turretHead.HasTurrets)
                        {
                            // 只有当炮塔头实际有炮塔时才跳过头部渲染
                            __result = false;
                            return false; // 跳过原方法
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in Head CanDrawNow Patch: {ex}");
            }

            return true; // 继续执行原方法
        }
    }

    // 修正底座占地尺寸
    [HarmonyPatch(typeof(GenAdj), "OccupiedRect", new Type[] { typeof(Thing) })]
    public static class GenAdj_OccupiedRect_Patch
    {
        public static bool Prefix(Thing t, ref CellRect __result)
        {
            try
            {
                // 处理炮塔底座
                if (t is GodHandTurretBase turretBase)
                {
                    IntVec2 size = turretBase.originalSize;
                    __result = GenAdj.OccupiedRect(t.Position, t.Rotation, size);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in OccupiedRect Patch: {ex}");
            }
            return true;
        }
    }

    // 修正底座旋转尺寸
    [HarmonyPatch(typeof(Thing), "get_RotatedSize")]
    public static class Thing_RotatedSize_Patch
    {
        public static bool Prefix(Thing __instance, ref IntVec2 __result)
        {
            try
            {
                // 处理炮塔底座
                if (__instance is GodHandTurretBase turretBase)
                {
                    IntVec2 size = turretBase.originalSize;
                    if (__instance.Rotation.IsHorizontal)
                    {
                        __result = new IntVec2(size.z, size.x);
                    }
                    else
                    {
                        __result = size;
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in RotatedSize Patch: {ex}");
            }
            return true;
        }
    }

    // 销毁时移除追踪状态
    [HarmonyPatch(typeof(Thing), "Destroy")]
    public static class Thing_Destroy_TurretBase_Patch
    {
        public static void Prefix(Thing __instance)
        {
            try
            {
                if (__instance is GodHandTurretBase turretBase)
                {
                    TurretBaseSizeTracker.Unregister(turretBase);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHandTurretHead] Error in Thing.Destroy TurretBase Patch: {ex}");
            }
        }
    }


    // 统一护盾拦截检查
    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Projectile_CheckForFreeInterceptBetween_Patch
    {
        public static bool Prefix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            try
            {
                Map map = __instance.Map;
                if (map == null) return true;

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.apparel?.WornApparel == null) continue;

                    GodHandTurretHead turretHead = null;
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel is GodHandTurretHead th && th.HasShield)
                        {
                            turretHead = th;
                            break;
                        }
                    }

                    if (turretHead == null) continue;

                    Thing launcher = __instance.Launcher;
                    if (launcher != null && !launcher.HostileTo(pawn))
                        continue;

                    Vector3 shieldCenter = pawn.DrawPos;
                    float radius = turretHead.ShieldRadius;

                    if (radius <= 0)
                    {
                        turretHead.RecalculateCombinedShield();
                        radius = turretHead.ShieldRadius;
                        if (radius <= 0) continue;
                    }

                    float distSq = (shieldCenter - newExactPos).sqrMagnitude;
                    float maxDist = radius + __instance.def.projectile.SpeedTilesPerTick + 0.5f;
                    if (distSq > maxDist * maxDist) continue;

                    Vector2 center2D = new Vector2(shieldCenter.x, shieldCenter.z);
                    Vector2 lastPos2D = new Vector2(lastExactPos.x, lastExactPos.z);
                    Vector2 newPos2D = new Vector2(newExactPos.x, newExactPos.z);

                    if ((lastPos2D - center2D).sqrMagnitude <= radius * radius)
                        continue;

                    if (GenGeo.IntersectLineCircleOutline(center2D, radius, lastPos2D, newPos2D))
                    {
                        int damage = __instance.DamageAmount;
                        turretHead.DamageCombinedShield(damage);

                        FleckMaker.Static(newExactPos, map, FleckDefOf.ExplosionFlash, 6f);
                        for (int i = 0; i < 3; i++)
                        {
                            FleckMaker.ThrowDustPuff(newExactPos, map, Rand.Range(0.8f, 1.2f));
                        }

                        var impactMethod = typeof(Projectile).GetMethod("Impact",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        if (impactMethod != null)
                        {
                            impactMethod.Invoke(__instance, new object[] { null, true });
                        }

                        __result = true;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[护盾模块] 拦截检查异常: {ex.Message}", 847563930);
            }

            return true;
        }
    }

    // 炮塔射击时锁定姿态
    [HarmonyPatch(typeof(Pawn_StanceTracker), "SetStance")]
    public static class Pawn_StanceTracker_SetStance_Patch
    {
        public static bool Prefix(Pawn_StanceTracker __instance, Stance newStance)
        {
            try
            {
                Pawn pawn = __instance.pawn;
                if (pawn == null) return true;

                if (TurretHeadShootingTracker.IsAnyTurretShooting(pawn))
                {
                    if (newStance is Stance_Warmup || newStance is Stance_Cooldown)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in SetStance Patch: {ex}", 847563940);
            }
            return true;
        }
    }

    // 炮塔射击不影响任务预约
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    public static class Pawn_JobTracker_StartJob_Patch
    {
        public static bool Prefix(Pawn_JobTracker __instance, Pawn ___pawn, Job newJob)
        {
            try
            {
                if (newJob == null) return true;
                if (___pawn == null) return true;

                if (TurretHeadShootingTracker.IsAnyTurretShooting(___pawn))
                {
                    if (newJob.def == JobDefOf.AttackStatic ||
                        newJob.def == JobDefOf.Wait_Combat)
                    {
                        // 拦截逻辑
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[GodHandTurretHead] Error in StartJob Patch: {ex}", 847563943);
            }
            return true;
        }
    }
}
