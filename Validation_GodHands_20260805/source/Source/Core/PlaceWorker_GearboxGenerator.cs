using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 检查发电机交互格
    public class PlaceWorker_GearboxGenerator : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef def = (ThingDef)checkingDef;
            // 计算曲柄手柄所在的格子 (相对于建筑向外延伸 1 格)
            IntVec3 crankCell = loc + rot.FacingCell;

            // 检查边界
            if (!crankCell.InBounds(map))
            {
                return "GodHand_HandleOutOfBounds".Translate();
            }

            // 检查曲柄格是否有任何建筑
            List<Thing> thingList = crankCell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                // 必须无任何建筑
                if (thingList[i].def.category == ThingCategory.Building)
                {
                    return "GodHand_HandleBlocked".Translate();
                }
            }

            return true;
        }

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            // 绘制蓝图模型
            Vector3 drawPos = center.ToVector3ShiftedWithAltitude(AltitudeLayer.Blueprint);
            GodHandGeneratorController.RenderGearbox(drawPos + new Vector3(0, 0.5f, 0), rot, 0f, true);
        }
    }
}
