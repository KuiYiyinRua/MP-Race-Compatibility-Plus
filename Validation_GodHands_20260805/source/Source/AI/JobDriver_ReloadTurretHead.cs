using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // 炮塔头装填驱动类
    public class JobDriver_ReloadTurretHead : JobDriver
    {
        private const TargetIndex AmmoInd = TargetIndex.A;

        private Thing Ammo => job.GetTarget(AmmoInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Ammo, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(AmmoInd);
            this.FailOnBurningImmobile(AmmoInd);

            // 前往弹药位置
            yield return Toils_Goto.GotoThing(AmmoInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(AmmoInd)
                .FailOnSomeonePhysicallyInteracting(AmmoInd);

            // 搬运弹药
            yield return Toils_Haul.StartCarryThing(AmmoInd, false, true);

            // 执行装填动作
            Toil reloadToil = new Toil();
            reloadToil.initAction = delegate
            {
                GodHandTurretHead turretHead = null;
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel is GodHandTurretHead th)
                        {
                            turretHead = th;
                            break;
                        }
                    }
                }

                if (turretHead == null) return;

                var gun = turretHead.Gun;
                var compAmmo = gun?.TryGetComp<CompChangeableProjectile>();
                if (compAmmo == null) return;

                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing != null)
                {
                    compAmmo.LoadShell(carriedThing.def, 1);
                    carriedThing.stackCount--;
                    if (carriedThing.stackCount <= 0)
                    {
                        carriedThing.Destroy();
                    }
                }
            };
            reloadToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return reloadToil;
        }
    }
}
