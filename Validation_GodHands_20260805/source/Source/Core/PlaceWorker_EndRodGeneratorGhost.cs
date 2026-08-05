using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 异世界永动机蓝图绘制
    public class PlaceWorker_EndRodGeneratorGhost : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            // 绘制 3D 蓝图模型，模拟静态状态
            // 传入 null 建筑实例以触发蓝图渲染模式（平面视角 + 低高度偏移）
            Vector3 drawPos = center.ToVector3ShiftedWithAltitude(AltitudeLayer.Blueprint);
            GodHandGeneratorController.RenderEndRodMachine(drawPos, rot, 0f, null);
        }
    }
}
