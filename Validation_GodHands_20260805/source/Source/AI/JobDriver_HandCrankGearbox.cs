using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 手摇发电驱动类
    public class JobDriver_HandCrankGearbox : JobDriver
    {
        private const TargetIndex BuildingInd = TargetIndex.A;
        private const TargetIndex CellInd = TargetIndex.B; // 目标工作格

        protected Building_GearboxGenerator Building => (Building_GearboxGenerator)job.GetTarget(BuildingInd).Thing;

        public override bool TryMakePreToilReservations(bool forced)
        {
            return pawn.Reserve(job.GetTarget(BuildingInd), job, 1, -1, null, forced);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(BuildingInd);

            // 前往最佳交互格
            yield return Toils_Goto.GotoCell(CellInd, PathEndMode.OnCell);

            // 持续工作
            Toil work = new Toil();
            work.tickAction = delegate
            {
                Pawn actor = work.actor;
                Building_GearboxGenerator building = Building;
                if (building == null || !building.Spawned)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                // 视线锁定曲柄手柄
                actor.rotationTracker.FaceCell(building.GetCrankWorldPosition().ToIntVec3());

                // 增加能量 (基于操作速度 * 操纵能力)
                float factor = actor.GetStatValue(StatDefOf.WorkSpeedGlobal) * actor.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
                building.AddWork(factor);

                // 播放效果 (使用机械动力音效)
                if (GenTicks.IsTickInterval(30))
                {
                    GodHandDefOf.GodHand_CogsLoop?.PlayOneShot(new TargetInfo(building.Position, actor.Map));
                }
            };
            work.defaultCompleteMode = ToilCompleteMode.Never;

            yield return work;
        }
    }
}
