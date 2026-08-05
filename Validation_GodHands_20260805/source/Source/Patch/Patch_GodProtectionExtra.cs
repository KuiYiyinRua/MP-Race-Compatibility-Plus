using Verse;
using Verse.Sound;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace GodHandMod
{
    // 针对轨道光束的物理位置进行强力约束
    [HarmonyPatch(typeof(OrbitalStrike), "Tick")]
    public static class Patch_GodProtectionOrbital
    {
        public static bool Prefix(OrbitalStrike __instance)
        {
            if (__instance.Map == null || __instance.Destroyed) return true;

            // 如果当前的物理中心点在护盾内
            if (ShieldUtils.IsProtected(__instance.Map, __instance.Position.ToVector3Shifted(), out var shield))
            {
                if (!shield.isDome) return true;

                // 将打击实体沿地面平面移出护盾 (安全查找)
                IntVec3 newPos = ShieldUtils.GetSafeRepelCell(__instance.Map, __instance.DrawPos, shield, 0.5f);

                if (newPos.InBounds(__instance.Map))
                {
                    __instance.Position = newPos;

                    if (__instance.IsHashIntervalTick(250))
                    {
                        FleckMaker.ThrowLightningGlow(__instance.DrawPos, __instance.Map, 3.0f);
                        Messages.Message("GodHand.Protection.InterceptedOrbital".Translate(), MessageTypeDefOf.CautionInput, false);
                    }
                }
            }
            return true;
        }
    }

    // 屏障补丁拦截伤害应用
    [HarmonyPatch(typeof(DamageWorker), "Apply")]
    public static class Patch_GodProtectionDamageWorkerApply
    {
        public static bool Prefix(DamageWorker __instance, DamageInfo dinfo, Thing victim, ref DamageWorker.DamageResult __result)
        {
            if (victim?.Map == null) return true;

            if (ShieldUtils.IsProtected(victim.Map, victim.Position, out var shield))
            {
                if (!shield.isDome) return true;

                // 统一拦截逻辑
                if (dinfo.Instigator is Pawn p && p.Map == victim.Map)
                {
                    // 禁用轨道武器
                    if (dinfo.Instigator is OrbitalStrike || (dinfo.Weapon != null && dinfo.Weapon.defName.IndexOf("Orbital", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        __result = new DamageWorker.DamageResult();
                        return false;
                    }

                    // 允许内部互伤
                    if (ShieldUtils.IsProtected(p.Map, p.Position, out var instigatorShield) && instigatorShield == shield)
                    {
                        return true;
                    }
                }

                // 拦截所有外部伤害
                __result = new DamageWorker.DamageResult();
                return false;
            }
            return true;
        }
    }

    // 承伤检测拦截
    [HarmonyPatch(typeof(Thing), "TakeDamage")]
    public static class Patch_GodProtectionTakeDamage
    {
        public static bool Prefix(Thing __instance, ref DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (__instance.Map == null) return true;

            if (ShieldUtils.IsProtected(__instance.Map, __instance.Position, out var shield))
            {
                if (!shield.isDome) return true;

                if (dinfo.Instigator is Pawn p && p.Map == __instance.Map)
                {
                    if (ShieldUtils.IsProtected(p.Map, p.Position, out var instigatorShield) && instigatorShield == shield)
                    {
                        return true;
                    }
                }

                // 物理伤害归零
                dinfo.SetAmount(0f);
                __result = new DamageWorker.DamageResult();
                return false;
            }
            return true;
        }
    }

    // 终极起火屏障 (拦截地面起火)
    [HarmonyPatch(typeof(FireUtility), "TryStartFireIn")]
    public static class Patch_GodProtectionFireBarrier
    {
        public static bool Prefix(IntVec3 c, Map map)
        {
            if (ShieldUtils.IsProtected(map, c, out var shield))
            {
                if (shield.isDome) return false;
            }
            return true;
        }
    }

    // 拦截点燃效果 (解决喷火器点燃 Pawn 的问题)
    [HarmonyPatch(typeof(FireUtility), "TryAttachFire")]
    public static class Patch_GodProtectionTryAttachFire
    {
        public static bool Prefix(Thing t)
        {
            if (t?.Map != null && ShieldUtils.IsProtected(t.Map, t.Position, out var shield))
            {
                if (shield.isDome) return false;
            }
            return true;
        }
    }

    // 终极爆炸拦截
    [HarmonyPatch(typeof(GenExplosion), "DoExplosion")]
    public static class Patch_GodProtectionExplosionBarrier
    {
        public static bool Prefix(IntVec3 center, Map map)
        {
            if (ShieldUtils.IsProtected(map, center, out var shield))
            {
                if (shield.isDome) return false;
            }
            return true;
        }
    }

    // 拦截空投舱、陨石等掉落物
    [HarmonyPatch(typeof(Skyfaller), "Tick")]
    public static class Patch_GodProtectionSkyfaller
    {
        public static void Prefix(Skyfaller __instance)
        {
            if (__instance.Map == null || __instance.Destroyed) return;

            int ticksToImpact = __instance.ticksToImpact;
            if (ticksToImpact > 10 || ticksToImpact < 0) return;

            // 检查着陆点是否受保护
            IntVec3 currentPos = __instance.Position;
            if (currentPos.InBounds(__instance.Map) && ShieldUtils.IsProtected(__instance.Map, currentPos.ToVector3Shifted(), out var shield))
            {
                if (!shield.isDome) return;

                // 强制修正落点至护盾边缘外 (安全查找)
                IntVec3 newPos = ShieldUtils.GetSafeRepelCell(__instance.Map, __instance.DrawPos, shield, 0.5f);

                // 只有坐标真的发生变化且在地图内才更新
                if (newPos != currentPos && newPos.InBounds(__instance.Map))
                {
                    __instance.Position = newPos;
                    // 特效反馈
                    FleckMaker.ThrowLightningGlow(__instance.DrawPos, __instance.Map, 1.2f);
                }
            }
        }
    }
}
