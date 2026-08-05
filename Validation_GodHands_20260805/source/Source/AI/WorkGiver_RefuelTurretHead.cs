using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace GodHandMod
{
    // 自动补充炮塔燃料提供者
    public class WorkGiver_RefuelTurretHead : WorkGiver_Scanner
    {
        // 自动装填阈值
        private const float AutoRefuelThreshold = 0.5f;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            // 扫描地图友方小人
            foreach (Pawn p in pawn.Map.mapPawns.FreeColonistsSpawned)
            {
                if (p != pawn && HasTurretHeadNeedingRefuel(p))
                {
                    yield return p;
                }
            }

            // 检查自身
            if (HasTurretHeadNeedingRefuel(pawn))
            {
                yield return pawn;
            }
        }

        private bool HasTurretHeadNeedingRefuel(Pawn p)
        {
            if (p?.apparel?.WornApparel == null) return false;

            foreach (var apparel in p.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead turretHead)
                {
                    foreach (var slot in turretHead.TurretSlots)
                    {
                        if (slot.fuelSystemEnabled && slot.ShouldAutoRefuelNow(AutoRefuelThreshold))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn targetPawn = t as Pawn;
            if (targetPawn == null) return false;

            if (!CanRefuelTarget(pawn, targetPawn, forced)) return false;
            if (pawn.CurJobDef == GodHandDefOf.GodHand_RefuelTurretHead) return false;

            GodHandTurretHead turretHead = GetTurretHeadNeedingRefuel(targetPawn);
            if (turretHead == null) return false;

            TurretSlot slotToRefuel = null;
            foreach (var slot in turretHead.TurretSlots)
            {
                if (slot.fuelSystemEnabled && (forced || slot.ShouldAutoRefuelNow(AutoRefuelThreshold)))
                {
                    slotToRefuel = slot;
                    break;
                }
            }

            if (slotToRefuel == null || slotToRefuel.fuelThingDef == null) return false;

            Thing fuelThing = FindBestFuel(pawn, slotToRefuel);
            if (fuelThing == null)
            {
                JobFailReason.Is("NoFuelToRefuel".Translate(slotToRefuel.fuelThingDef.LabelCap));
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn targetPawn = t as Pawn;
            if (targetPawn == null) return null;

            if (!CanRefuelTarget(pawn, targetPawn, forced)) return null;
            if (pawn.CurJobDef == GodHandDefOf.GodHand_RefuelTurretHead) return null;

            GodHandTurretHead turretHead = GetTurretHeadNeedingRefuel(targetPawn);
            if (turretHead == null) return null;

            TurretSlot slotToRefuel = null;
            foreach (var slot in turretHead.TurretSlots)
            {
                if (slot.fuelSystemEnabled && (forced || slot.ShouldAutoRefuelNow(AutoRefuelThreshold)))
                {
                    slotToRefuel = slot;
                    break;
                }
            }

            if (slotToRefuel == null) return null;

            Thing fuelThing = FindBestFuel(pawn, slotToRefuel);
            if (fuelThing == null) return null;

            Job job = JobMaker.MakeJob(GodHandDefOf.GodHand_RefuelTurretHead, fuelThing);
            job.count = GetFuelCountNeeded(slotToRefuel, fuelThing.stackCount);
            return job;
        }

        private GodHandTurretHead GetTurretHeadNeedingRefuel(Pawn p)
        {
            if (p?.apparel?.WornApparel == null) return null;

            foreach (var apparel in p.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead turretHead)
                {
                    foreach (var slot in turretHead.TurretSlots)
                    {
                        if (slot.fuelSystemEnabled && !slot.IsFull)
                        {
                            return turretHead;
                        }
                    }
                }
            }
            return null;
        }

        private Thing FindBestFuel(Pawn pawn, TurretSlot slot)
        {
            if (slot.fuelThingDef == null) return null;

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(slot.fuelThingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                (Thing t) => !t.IsForbidden(pawn) && pawn.CanReserve(t)
            );
        }

        private int GetFuelCountNeeded(TurretSlot slot, int available)
        {
            int needed = UnityEngine.Mathf.CeilToInt(slot.fuelCapacity - slot.fuel);
            return UnityEngine.Mathf.Min(needed, available);
        }

        // 目标可补充检查
        private bool CanRefuelTarget(Pawn worker, Pawn target, bool forced)
        {
            if (target == null || target.Dead || !target.Spawned) return false;
            if (target != worker && target.Drafted) return false;
            if (target.Downed || target.InMentalState) return false;
            return true;
        }
    }
}
