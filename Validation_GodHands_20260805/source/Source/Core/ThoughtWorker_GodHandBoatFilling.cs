using RimWorld;
using Verse;

namespace GodHandMod
{
    // 根据器官松弛阶段决定心情阶段
    public class ThoughtWorker_GodHandBoatFilling : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            // 基于Hediff判断
            var statusHediff = p.health.hediffSet.GetFirstHediffOfDef(GodHandDefOf.GodHand_OrificeStretchingStatus);
            if (statusHediff == null) return ThoughtState.Inactive;

            // 必须在机器上才产生该心情 (通过船只标记判断)
            var boatHediff = p.health.hediffSet.GetFirstHediffOfDef(GodHandDefOf.GodHand_IsActuallyABoat);
            if (boatHediff == null) return ThoughtState.Inactive;

            // 优先外部兼容
            if (CompatibilityBridge.GetOrificeLoosenessStage != null)
            {
                int stage = CompatibilityBridge.GetOrificeLoosenessStage(p);
                return ThoughtState.ActiveAtStage(stage);
            }

            // 降级使用组件
            var comp = statusHediff.TryGetComp<HediffComp_GodHandBoatAdaptation>();
            if (comp != null)
            {
                return ThoughtState.ActiveAtStage(comp.GetLoosenessStage());
            }

            return ThoughtState.ActiveAtStage(0);
        }
    }
}
