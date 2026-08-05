using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // 手摇发电任务提供者
    public class WorkGiver_HandCrankGearbox : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(GodHandDefOf.GodHand_GearboxGenerator);

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!GodHandModMain.Settings.enableHandCrankGenerator) return false;
            if (!(t is Building_GearboxGenerator building)) return false;

            // 基础检查
            if (building.IsForbidden(pawn) || !pawn.CanReserve(building, 1, -1, null, forced)) return false;

            // 开关检查
            var flick = building.TryGetComp<CompFlickable>();
            if (flick != null && !flick.SwitchIsOn) return false;

            // 交互格检查
            bool canReachAny = false;
            foreach (var cell in building.GetValidInteractionCells())
            {
                if (cell.Standable(pawn.Map) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                {
                    canReachAny = true;
                    break;
                }
            }
            if (!canReachAny) return false;

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var building = t as Building_GearboxGenerator;
            if (building == null) return null;

            // 查找最佳交互格
            IntVec3 bestCell = IntVec3.Invalid;
            float bestDist = float.MaxValue;
            foreach (var cell in building.GetValidInteractionCells())
            {
                if (cell.Standable(pawn.Map) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                {
                    float dist = pawn.Position.DistanceToSquared(cell);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestCell = cell;
                    }
                }
            }

            if (!bestCell.IsValid) return null;

            Job job = JobMaker.MakeJob(GodHandDefOf.GodHand_HandCrankGearbox, t, bestCell);
            // 2000 Tick 后检查是否有更高优先级工作
            job.expiryInterval = 2000;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
