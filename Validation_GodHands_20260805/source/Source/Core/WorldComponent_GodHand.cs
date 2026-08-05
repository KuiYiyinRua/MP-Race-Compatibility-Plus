using Verse;
using RimWorld.Planet;

namespace GodHandMod
{
    // 全局世界组件
    public class WorldComponent_GodHand : WorldComponent
    {
        public WorldComponent_GodHand(World world) : base(world)
        {
            // 清理静态缓存
            CompPawnCaptureOnTouch.allCapturedPawns.Clear();
            CompPawnCaptureOnTouch.pawnToGenerator.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // 保存神之扳手的变换数据
            GraphicTransformManager.ExposeData();
        }
    }
}
