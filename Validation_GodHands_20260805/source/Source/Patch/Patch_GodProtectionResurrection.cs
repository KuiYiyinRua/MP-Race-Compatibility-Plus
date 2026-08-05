using System;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;
using HarmonyLib;

namespace GodHandMod
{
    // 神佑之护 - 绝对不死与空间锚定逻辑

    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class Patch_GodProtection_Pawn_Kill
    {
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            if (__instance.Destroyed && !__instance.Spawned) return true;

            if (__instance.health != null && __instance.health.hediffSet.HasHediff(GodHandDefOf.GodHand_ProtectionIndividual))
            {
                // 阻止死亡并重置状态
                // 阻止死亡并重置状态
                HealAndReset(__instance);

                // 撕裂空间特效
                PlayTearSpaceEffect(__instance);
                Messages.Message("GodHand.Protection.Resurrected".Translate(__instance.LabelShort), __instance, MessageTypeDefOf.PositiveEvent, true);

                // 中断任务防止自杀
                __instance.jobs?.EndCurrentJob(JobCondition.InterruptForced);

                return false;
            }
            return true;
        }

        public static void PlayTearSpaceEffect(Pawn p)
        {
            if (p.Map == null) return;

            // 播放强力特效
            FleckMaker.ThrowLightningGlow(p.DrawPos, p.Map, 3.0f);
            FleckMaker.ThrowSmoke(p.DrawPos, p.Map, 2.0f);
            EffecterDefOf.Interceptor_BlockedProjectile.Spawn(p.Position, p.Map).Cleanup();

            // 播放声音
            SoundDefOf.EnergyShield_Reset.PlayOneShot(new TargetInfo(p.Position, p.Map));
        }

        // 治愈伤口移除负面状态
        public static void HealAndReset(Pawn p)
        {
            if (p.health == null) return;

            // 移除所有伤害 (Hediff_Injury)
            var hediffs = p.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (hediffs[i] is Hediff_Injury || hediffs[i] is Hediff_MissingPart)
                {
                    p.health.RemoveHediff(hediffs[i]);
                }
                // 移除更多负面状态
                else if (hediffs[i].def.isBad)
                {
                    p.health.RemoveHediff(hediffs[i]);
                }
            }

            // 确保血量显示更新
            p.health.Notify_HediffChanged(null);
        }
    }

    // 记录并验证离地
    [HarmonyPatch(typeof(Pawn), "DeSpawn")]
    public static class Patch_GodProtection_Pawn_DeSpawn
    {
        public class State
        {
            public Map map;
            public IntVec3 pos;
            public bool active;
        }

        public static void Prefix(Pawn __instance, out State __state)
        {
            __state = new State();
            var comp = Find.World.GetComponent<WorldComponent_GodProtection>();
            // 空间锚定核心：只有显式保护名单中的 Pawn（非载具）才会被记录位置以便进行离地异步拉回
            // 这样不再拦截复杂的 Destroy 流程，能彻底解决任何底层的补丁冲突
            if (__instance.Spawned && __instance.Map != null && comp != null && comp.IsProtected(__instance))
            {
                __state.map = __instance.Map;
                __state.pos = __instance.Position;
                __state.active = true;
            }
        }

        public static void Postfix(Pawn __instance, State __state)
        {
            if (__state != null && __state.active && __state.map != null)
            {
                // 检查是否安全
                bool isSafe = __instance.Spawned || __instance.holdingOwner != null || Find.WorldPawns.Contains(__instance);

                if (!isSafe)
                {
                    // 地图销毁会自动转移
                    // 避免重复调用报错
                    // 注册到组件下一帧检查
                    // 若在世界中则安全
                    // 否则视为非正常离地拉回
                }

                // 调度离地检查逻辑
                var comp = Find.World.GetComponent<WorldComponent_GodProtection>();
                comp?.RequestRecovery(__instance, __state.map, __state.pos);
            }
        }
    }

}
