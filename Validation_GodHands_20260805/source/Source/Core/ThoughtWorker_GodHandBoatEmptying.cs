using RimWorld;
using Verse;

namespace GodHandMod
{
    // 处理离开机器后的空虚感心情
    public class ThoughtWorker_GodHandBoatEmptying : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            // 上机时不触发
            if (p.health.hediffSet.HasHediff(GodHandDefOf.GodHand_IsActuallyABoat)) return ThoughtState.Inactive;

            // 检查扩张阶进度
            var statusHediff = p.health.hediffSet.GetFirstHediffOfDef(GodHandDefOf.GodHand_OrificeStretchingStatus);
            if (statusHediff == null) return ThoughtState.Inactive;

            var comp = statusHediff.TryGetComp<HediffComp_GodHandBoatAdaptation>();
            if (comp != null)
            {
                // 适应且离机时触发
                if (comp.GetLoosenessStage() >= 1)
                {
                    return ThoughtState.ActiveAtStage(0);
                }
            }

            return ThoughtState.Inactive;
        }
    }
}
