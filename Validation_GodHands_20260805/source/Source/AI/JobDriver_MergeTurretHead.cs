using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 炮塔头合并驱动类
    public class JobDriver_MergeTurretHead : JobDriver
    {
        private const TargetIndex TurretHeadInd = TargetIndex.A;

        private GodHandTurretHead TargetTurretHead => job.GetTarget(TurretHeadInd).Thing as GodHandTurretHead;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetTurretHead, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 失败条件
            this.FailOnDestroyedOrNull(TurretHeadInd);
            this.FailOnBurningImmobile(TurretHeadInd);

            // 前往炮塔头位置
            yield return Toils_Goto.GotoThing(TurretHeadInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(TurretHeadInd)
                .FailOnSomeonePhysicallyInteracting(TurretHeadInd);

            // 合并炮塔
            Toil mergeToil = new Toil();
            mergeToil.initAction = delegate
            {
                GodHandTurretHead sourceTurretHead = TargetTurretHead;
                if (sourceTurretHead == null || !sourceTurretHead.Spawned)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                GodHandTurretHead existingHead = GetTurretHead();
                if (existingHead == null)
                {
                    // 无现有头装配
                    if (pawn.apparel != null && pawn.apparel.CanWearWithoutDroppingAnything(sourceTurretHead.def))
                    {
                        sourceTurretHead.DeSpawn();
                        pawn.apparel.Wear(sourceTurretHead, false, false);

                        Messages.Message(
                            "GodHand.TurretHead.Equipped".Translate(pawn.LabelShort, sourceTurretHead.Label),
                            pawn,
                            MessageTypeDefOf.PositiveEvent
                        );
                    }
                    else
                    {
                        Messages.Message("GodHand.TurretHead.CannotWear".Translate(), MessageTypeDefOf.RejectInput);
                        EndJobWith(JobCondition.Incompletable);
                    }
                    return;
                }

                // 合并槽位逻辑
                int merged = 0;

                // 逐个转移槽位
                while (sourceTurretHead.TurretSlots.Count > 0 && existingHead.TurretCount < GodHandTurretHead.MaxTurrets)
                {
                    var slot = sourceTurretHead.TurretSlots[0];
                    sourceTurretHead.TurretSlots.RemoveAt(0);

                    // 转移到目标
                    if (existingHead.AddTurretSlot(slot))
                    {
                        merged++;
                    }
                }

                if (merged > 0)
                {
                    // 销毁源头
                    sourceTurretHead.Destroy();

                    Messages.Message(
                        "GodHand.TurretHead.Merged".Translate(pawn.LabelShort, merged, existingHead.TurretCount),
                        pawn,
                        MessageTypeDefOf.PositiveEvent
                    );

                    SoundDef.Named("MetalHitImportant").PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                }
                else
                {
                    Messages.Message("GodHand.TurretHead.MergeFailed".Translate(), MessageTypeDefOf.RejectInput);
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            mergeToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return mergeToil;
        }

        // 获取已穿戴炮塔头
        private GodHandTurretHead GetTurretHead()
        {
            if (pawn.apparel?.WornApparel == null) return null;

            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead th)
                {
                    return th;
                }
            }
            return null;
        }
    }
}
