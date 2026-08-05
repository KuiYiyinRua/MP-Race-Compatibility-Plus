using Verse;
using RimWorld;

namespace GodHandMod
{
    // 地图加载时自动检查并清理被禁用的发电机
    public class MapComponent_GeneratorCleanup : MapComponent
    {
        private bool cleanedOnLoad = false;

        public MapComponent_GeneratorCleanup(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            // 仅在加载后执行一次
            if (!cleanedOnLoad)
            {
                cleanedOnLoad = true;
                CheckAndCleanup();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // 每次加载必须检查
        }

        private void CheckAndCleanup()
        {
            // 禁用时清理发电机
            if (!GodHandModMain.Settings.enableHandCrankGenerator)
            {
                GeneratorCleanupUtility.CleanupAllGenerators(true);
            }

            // 禁用时清理永动机
            if (GodHandModMain.Settings.disableEndRodGenerator)
            {
                EndRodCleanupUtility.CleanupAllEndRods(true);
            }
        }
    }
}
