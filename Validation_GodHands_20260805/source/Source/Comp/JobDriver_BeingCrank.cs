using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // Pawn 被捕捉的 Job
    public class JobDriver_BeingCrank : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        public override bool CanBeginNowWhileLyingDown() => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil beingCrank = new Toil();
            beingCrank.initAction = () =>
            {
                pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;
            };
            beingCrank.tickAction = () =>
            {
                // 每帧锁定位置
                pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;

                // 10 tick 检查一次状态
                if (pawn.IsHashIntervalTick(10))
                {
                    if (!pawn.health.hediffSet.HasHediff(GodHandDefOf.GodHand_IsActuallyABoat))
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                    }
                }
            };
            beingCrank.defaultCompleteMode = ToilCompleteMode.Never;
            beingCrank.handlingFacing = true;
            beingCrank.AddFinishAction(() =>
            {
                // 预留恢复逻辑
            });

            yield return beingCrank;
        }
    }
}
