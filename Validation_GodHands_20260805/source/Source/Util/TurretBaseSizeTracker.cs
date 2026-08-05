using System.Collections.Generic;
using Verse;

namespace GodHandMod
{
    // 追踪炮塔底座的实际尺寸
    public static class TurretBaseSizeTracker
    {
        private static Dictionary<GodHandTurretBase, IntVec2> baseSizes = new Dictionary<GodHandTurretBase, IntVec2>();

        public static void Register(GodHandTurretBase turretBase, IntVec2 size)
        {
            baseSizes[turretBase] = size;
        }

        public static void Unregister(GodHandTurretBase turretBase)
        {
            baseSizes.Remove(turretBase);
        }

        public static bool TryGetSize(GodHandTurretBase turretBase, out IntVec2 size)
        {
            return baseSizes.TryGetValue(turretBase, out size);
        }

        public static IntVec2 GetSize(Thing thing)
        {
            if (thing is GodHandTurretBase turretBase)
            {
                if (baseSizes.TryGetValue(turretBase, out var size))
                    return size;
                return turretBase.originalSize;
            }
            return thing.def.size;
        }
    }
}
