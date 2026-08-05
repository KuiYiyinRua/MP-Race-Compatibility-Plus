using Verse;
using Verse.Sound;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace GodHandMod
{
    // 纯代码护盾拦截补丁 - 子弹偏转版
    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Patch_GodProtectionProjectile
    {
        private static readonly FieldInfo OriginField = typeof(Projectile).GetField("origin", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DestinationField = typeof(Projectile).GetField("destination", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ImpactMethod = typeof(Projectile).GetMethod("Impact", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPostfix]
        public static void Postfix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            if (__result || __instance.Map == null) return;

            // 检查新位置是否进入护盾
            if (ShieldUtils.IsProtected(__instance.Map, newExactPos, out var shield))
            {
                // 检查外部触发
                if (!ShieldUtils.IsProtected(__instance.Map, lastExactPos, out _))
                {
                    if (shield.isDome)
                    {
                        TeleportProjectile(__instance, shield);
                        __result = true;
                    }
                    else
                    {
                        __result = true;
                        FleckMaker.ThrowLightningGlow(newExactPos, __instance.Map, 0.8f);
                        SoundDefOf.EnergyShield_AbsorbDamage.PlayOneShot(new TargetInfo(__instance.Position, __instance.Map));
                        ImpactMethod?.Invoke(__instance, new object[] { null, true });
                    }
                }
            }
        }

        private static void TeleportProjectile(Projectile projectile, GodShieldData shield)
        {
            try
            {
                Vector3 currentPos = projectile.ExactPosition;
                Vector3 origin = (Vector3)OriginField.GetValue(projectile);
                Vector3 destination = (Vector3)DestinationField.GetValue(projectile);
                Vector3 flightDir = (destination - origin).normalized;
                if (flightDir == Vector3.zero) flightDir = projectile.Rotation.FacingCell.ToVector3();

                // 检查内部发射
                if ((origin - shield.pos).sqrMagnitude < (shield.radius * shield.radius)) return;

                // 使用当前位置作为射线的起点进行交点计算
                // 修正旧值误差
                Vector3 rayOrigin = origin;
                if (ShieldUtils.GetSphereIntersections(rayOrigin, flightDir, shield.pos, shield.radius, out float t_entry, out float t_exit))
                {
                    // 确保目标点是在子弹前方且当前位置还没穿过它
                    float distToEntry = (rayOrigin + flightDir * t_entry - currentPos).magnitude;

                    // 传送到出口
                    float jumpDist = t_exit - t_entry;
                    // 重新计算向量
                    Vector3 translation = flightDir * (jumpDist + 0.5f);

                    // 平移轨迹
                    OriginField.SetValue(projectile, origin + translation);
                    DestinationField.SetValue(projectile, destination + translation);

                    // 特效显示在护盾边缘
                    Vector3 entryPos = rayOrigin + flightDir * t_entry;
                    Vector3 exitPos = rayOrigin + flightDir * t_exit + (flightDir * 0.5f);

                    FleckMaker.ThrowLightningGlow(entryPos, projectile.Map, 0.4f);
                    FleckMaker.ThrowLightningGlow(exitPos, projectile.Map, 0.3f);
                    SoundDefOf.Tick_Low.PlayOneShot(new TargetInfo(projectile.Position, projectile.Map));
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[GodHand] Projectile deflection failed: {ex}");
            }
        }
    }
}
