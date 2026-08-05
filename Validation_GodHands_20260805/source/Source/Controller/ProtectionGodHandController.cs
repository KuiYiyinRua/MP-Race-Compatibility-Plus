using Verse;
using RimWorld;
using UnityEngine;
using Verse.Sound;

namespace GodHandMod
{
    // 神之庇护控制器
    public static class ProtectionGodHandController
    {
        // 执行庇护指令
        public static void Execute(IntVec3 start, Map map, int mode, IntVec3 end = default)
        {
            var settings = GodHandModMain.Settings;
            switch (mode)
            {
                case 0: // 生成穹顶
                    int duration = settings.protectionDomeInfiniteDuration ? int.MaxValue : settings.protectionDurationTicks;
                    SpawnDome(start, map, settings.protectionDomeRadius, duration);
                    break;
                case 2: // 消除护盾
                    Erase(start, map, settings.protectionDomeRadius);
                    break;
            }
        }

        // 指定区域消除护盾
        private static void Erase(IntVec3 c, Map map, float radius)
        {
            var comp = map.GetComponent<MapComponent_GodProtection>();
            comp?.EraseShieldsAt(c, radius);
        }

        // 部署神佑穹顶
        private static void SpawnDome(IntVec3 c, Map map, float radius, int duration)
        {
            var comp = map.GetComponent<MapComponent_GodProtection>();
            comp?.AddShield(c, radius, duration, true, 0f);
            Messages.Message("GodHand.Protection.DomeSpawned".Translate(), MessageTypeDefOf.PositiveEvent);
        }

        // 应用单体神佑护盾
        public static void ApplyIndividual(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Warning("[GodHand] ApplyIndividual 目标为空");
                return;
            }

            if (GodHandDefOf.GodHand_ProtectionIndividual == null)
            {
                Log.Error("[GodHand] HediffDef 个体护盾未加载");
                return;
            }

            try
            {
                var hediff = pawn.health.AddHediff(GodHandDefOf.GodHand_ProtectionIndividual);
                var compDisappear = hediff?.TryGetComp<HediffComp_Disappears>();
                if (compDisappear != null)
                {
                    compDisappear.ticksToDisappear = GodHandModMain.Settings.protectionIndividualDurationTicks;
                }
                SoundDefOf.Click.PlayOneShotOnCamera();
                Messages.Message("GodHand.Protection.IndividualApplied".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.PositiveEvent);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[GodHand] 添加Hediff失败 {ex}");
            }
        }
    }
}
