using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // 炮塔头燃料补充驱动类
    public class JobDriver_RefuelTurretHead : JobDriver
    {
        private const TargetIndex FuelInd = TargetIndex.A;

        private Thing Fuel => job.GetTarget(FuelInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Fuel, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(FuelInd);
            this.FailOnBurningImmobile(FuelInd);

            // 满载结束检查
            AddEndCondition(() =>
            {
                GodHandTurretHead th = GetTurretHead();
                if (th == null) return JobCondition.Incompletable;
                bool allFull = true;
                foreach (var slot in th.TurretSlots)
                {
                    if (slot.fuelSystemEnabled && !slot.IsFull)
                    {
                        allFull = false;
                        break;
                    }
                }
                return allFull ? JobCondition.Succeeded : JobCondition.Ongoing;
            });

            // 自动补充检查
            AddFailCondition(() =>
            {
                if (job.playerForced) return false;
                GodHandTurretHead th = GetTurretHead();
                if (th == null) return true;
                foreach (var slot in th.TurretSlots)
                {
                    if (slot.fuelSystemEnabled && slot.ShouldAutoRefuelNow(0.5f))
                    {
                        return false;
                    }
                }
                return true;
            });

            // 前往燃料位置
            yield return Toils_Goto.GotoThing(FuelInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(FuelInd)
                .FailOnSomeonePhysicallyInteracting(FuelInd);

            // 计算拿取数量
            Toil calculateToil = new Toil();
            calculateToil.initAction = delegate
            {
                GodHandTurretHead turretHead = GetTurretHead();
                if (turretHead == null) return;

                int needed = turretHead.GetFuelCountToFullyRefuel();
                if (needed <= 0) return;
                int available = Fuel.stackCount;
                job.count = Mathf.Min(needed, available);
            };
            calculateToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return calculateToil;

            // 搬运燃料
            yield return Toils_Haul.StartCarryThing(FuelInd, false, true);

            // 执行补充动作
            Toil refuelToil = new Toil();
            refuelToil.initAction = delegate
            {
                GodHandTurretHead turretHead = GetTurretHead();
                if (turretHead == null) return;

                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing != null)
                {
                    int refuelAmount = carriedThing.stackCount;
                    turretHead.Refuel(refuelAmount);
                    carriedThing.Destroy();

                    Messages.Message(
                        "GodHand.TurretHead.Refueled".Translate(pawn.LabelShort, refuelAmount),
                        pawn,
                        MessageTypeDefOf.PositiveEvent
                    );
                }
            };
            refuelToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return refuelToil;
        }

        // 获取已穿戴炮塔头
        private GodHandTurretHead GetTurretHead()
        {
            if (pawn.apparel?.WornApparel == null) return null;

            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead th) return th;
            }
            return null;
        }
    }
}
