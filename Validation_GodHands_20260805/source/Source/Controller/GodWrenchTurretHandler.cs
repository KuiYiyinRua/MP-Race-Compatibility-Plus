using System;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 炮塔强拆逻辑处理器
    public class GodWrenchTurretHandler
    {
        private readonly GodWrenchController core;
        public GodWrenchTurretHandler(GodWrenchController core) => this.core = core;

        // 尝试抓取炮塔
        public void TrySnatch(Map map, Thing target, Pawn user)
        {
            if (target?.def?.building?.turretGunDef == null)
            {
                Messages.Message("GodHand.Wrench.NotATurret".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Building turret = (Building)target;
            IntVec3 pos = turret.Position;
            Rot4 rot = turret.Rotation;
            ThingDef turretDef = turret.def;
            float hp = turret.HitPoints;
            Graphic baseGraphic = turret.Graphic;

            GodHandTurretHead head = user?.apparel?.WornApparel.OfType<GodHandTurretHead>().FirstOrDefault();
            if (head == null)
            {
                head = (GodHandTurretHead)ThingMaker.MakeThing(GodHandDefOf.GodHand_TurretHead);
                GenSpawn.Spawn(head, pos, map);
                core.TryStartGrab(map, head, pos);
            }

            if (head.AddTurret(turretDef, GetFuel(turret), turret))
            {
                turret.Destroy(DestroyMode.Vanish);
                SpawnBase(map, pos, rot, turretDef, baseGraphic, hp);
                SoundDefOf.MetalHitImportant.PlayOneShot(new TargetInfo(pos, map));
                FleckMaker.ThrowMicroSparks(pos.ToVector3Shifted(), map);
            }
        }

        // 从Pawn身上移除炮塔头
        public void TryRemoveFromPawn(Map map, Pawn pawn, GodHandTurretHead wornHead)
        {
            if (wornHead == null || wornHead.TurretSlots.Count == 0) return;
            var topSlot = wornHead.TurretSlots.OrderByDescending(s => s.stackIndex).First();
            wornHead.RemoveTurretSlot(topSlot);
            if (wornHead.TurretSlots.Count == 0) { pawn.apparel.Remove(wornHead); wornHead.Destroy(); }

            GodHandTurretHead newHead = (GodHandTurretHead)ThingMaker.MakeThing(GodHandDefOf.GodHand_TurretHead);
            newHead.AddTurretSlot(topSlot);
            GenSpawn.Spawn(newHead, pawn.Position, map);
            core.TryStartGrab(map, newHead, pawn.Position);

            SoundDefOf.MetalHitImportant.PlayOneShot(new TargetInfo(pawn.Position, map));
            FleckMaker.ThrowMicroSparks(pawn.DrawPos, map);
        }

        // 提取炮塔燃料
        private float GetFuel(Building b) => b.TryGetComp<CompRefuelable>()?.Fuel ?? -1f;

        // 生成炮塔残留基座
        private void SpawnBase(Map map, IntVec3 pos, Rot4 rot, ThingDef def, Graphic graphic, float hp)
        {
            if (GodHandDefOf.GodHand_TurretBase == null) return;
            IntVec2 oldSize = GodHandDefOf.GodHand_TurretBase.size;
            GodHandDefOf.GodHand_TurretBase.size = def.size;
            try
            {
                GodHandTurretBase b = (GodHandTurretBase)ThingMaker.MakeThing(GodHandDefOf.GodHand_TurretBase);
                b.overrideGraphic = graphic;
                b.sourceTurretDef = def;
                TurretBaseSizeTracker.Register(b, def.size);
                GenSpawn.Spawn(b, pos, map, rot);
                b.HitPoints = Mathf.RoundToInt(hp * 0.5f);
            }
            finally { GodHandDefOf.GodHand_TurretBase.size = oldSize; }
        }
    }
}
