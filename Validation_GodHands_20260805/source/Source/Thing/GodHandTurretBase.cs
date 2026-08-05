using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using System.Collections.Generic;

namespace GodHandMod
{
    // 废弃炮塔基座残骸
    public class GodHandTurretBase : Building
    {
        public Graphic overrideGraphic;
        public ThingDef sourceTurretDef;
        public ThingDef sourceStuffDef;

        // 保存原始炮塔的尺寸
        public IntVec2 originalSize = IntVec2.One;

        // 崩解倒计时（tick）
        private int collapseTicksLeft = -1;
        private const int CollapseDelayTicks = 180; // 3秒 (60 ticks/秒)

        // 材料返还比例
        private const float MaterialReturnRatio = 0.33f;

        public override Graphic Graphic
        {
            get
            {
                if (overrideGraphic != null)
                {
                    return overrideGraphic;
                }
                return base.Graphic;
            }
        }

        // 返回正确的占地尺寸
        public IntVec2 ActualSize => originalSize;

        // 重写转向后尺寸
        public new IntVec2 RotatedSize
        {
            get
            {
                if (Rotation.IsHorizontal)
                {
                    return new IntVec2(originalSize.z, originalSize.x);
                }
                return originalSize;
            }
        }

        // 覆盖绘制位置
        public override Vector3 DrawPos => GenThing.TrueCenter(Position, Rotation, originalSize, def.Altitude);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref sourceTurretDef, "sourceTurretDef");
            Scribe_Defs.Look(ref sourceStuffDef, "sourceStuffDef");
            Scribe_Values.Look(ref originalSize, "originalSize", IntVec2.One);
            Scribe_Values.Look(ref collapseTicksLeft, "collapseTicksLeft", -1);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // 读档恢复外观
            if (respawningAfterLoad && overrideGraphic == null && sourceTurretDef != null)
            {
                RestoreGraphic();
            }

            // 初始化崩解倒计时
            if (collapseTicksLeft < 0)
            {
                collapseTicksLeft = CollapseDelayTicks;
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (collapseTicksLeft > 0)
            {
                collapseTicksLeft--;

                // 在最后一秒开始抖动效果
                if (collapseTicksLeft < 60 && collapseTicksLeft % 5 == 0)
                {
                    // 产生烟雾/火花效果
                    FleckMaker.ThrowSmoke(DrawPos, Map, 0.5f);
                }

                if (collapseTicksLeft <= 0)
                {
                    Collapse();
                }
            }
        }

        // 崩解销毁
        private void Collapse()
        {
            if (Destroyed || Map == null) return;

            Map map = Map;
            IntVec3 pos = Position;
            Vector3 drawPos = DrawPos;

            // 播放爆炸/崩塌音效
            SoundDefOf.Building_Deconstructed.PlayOneShot(new TargetInfo(pos, map));

            // 产生崩塌特效
            FleckMaker.ThrowMicroSparks(drawPos, map);
            FleckMaker.ThrowDustPuff(drawPos, map, 1.5f);

            // 多个位置产生碎片效果
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = new Vector3(
                    Rand.Range(-0.5f, 0.5f),
                    0f,
                    Rand.Range(-0.5f, 0.5f)
                );
                FleckMaker.ThrowMetaPuff(drawPos + offset, map);
            }

            // 掉落三分之一的材料
            SpawnMaterials(map, pos);

            // 从追踪器移除
            TurretBaseSizeTracker.Unregister(this);

            // 销毁自身前检查
            if (!Destroyed)
            {
                Destroy(DestroyMode.Vanish);
            }
        }

        // 生成返还材料
        private void SpawnMaterials(Map map, IntVec3 pos)
        {
            if (sourceTurretDef == null) return;

            // 获取原炮塔的建造材料
            List<ThingDefCountClass> costList = sourceTurretDef.CostList;
            if (costList != null)
            {
                foreach (var cost in costList)
                {
                    int returnCount = Mathf.FloorToInt(cost.count * MaterialReturnRatio);
                    if (returnCount > 0)
                    {
                        Thing material = ThingMaker.MakeThing(cost.thingDef);
                        material.stackCount = returnCount;
                        GenPlace.TryPlaceThing(material, pos, map, ThingPlaceMode.Near);
                    }
                }
            }

            // 如果有材料类型（如钢铁炮塔用钢铁建造）
            if (sourceTurretDef.CostStuffCount > 0 && sourceStuffDef != null)
            {
                int returnCount = Mathf.FloorToInt(sourceTurretDef.CostStuffCount * MaterialReturnRatio);
                if (returnCount > 0)
                {
                    Thing stuffMaterial = ThingMaker.MakeThing(sourceStuffDef);
                    stuffMaterial.stackCount = returnCount;
                    GenPlace.TryPlaceThing(stuffMaterial, pos, map, ThingPlaceMode.Near);
                }
            }
        }

        private void RestoreGraphic()
        {
            if (sourceTurretDef == null || sourceTurretDef.graphicData == null) return;

            Graphic baseG = sourceTurretDef.graphicData.Graphic;

            // 应用材料颜色
            if (sourceStuffDef != null)
            {
                overrideGraphic = baseG.GetColoredVersion(baseG.Shader, sourceStuffDef.stuffProps.color, sourceStuffDef.stuffProps.color);
            }
            else
            {
                overrideGraphic = baseG;
            }
        }

        // 绘制带抖动效果
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Vector3 correctDrawLoc = GenThing.TrueCenter(Position, Rotation, originalSize, def.Altitude);

            // 在最后一秒添加抖动效果
            if (collapseTicksLeft > 0 && collapseTicksLeft < 60)
            {
                float shake = (60 - collapseTicksLeft) / 60f * 0.05f;
                correctDrawLoc.x += Rand.Range(-shake, shake);
                correctDrawLoc.z += Rand.Range(-shake, shake);
            }

            Graphic graphic = this.Graphic;
            if (graphic != null && graphic != BaseContent.BadGraphic)
            {
                graphic.Draw(correctDrawLoc, Rotation, this);
            }
            else
            {
                base.DrawAt(correctDrawLoc, flip);
            }
        }

        // 禁止占领
        public override AcceptanceReport ClaimableBy(Faction by)
        {
            return AcceptanceReport.WasRejected;
        }

        // 获取检查字符串
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            TurretBaseSizeTracker.Unregister(this);
            base.DeSpawn(mode);
        }

        public override string GetInspectString()
        {
            float secondsLeft = collapseTicksLeft / 60f;
            return $"即将崩解: {secondsLeft:F1}秒";
        }
    }
}
