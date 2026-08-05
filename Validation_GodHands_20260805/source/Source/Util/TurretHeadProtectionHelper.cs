using Verse;
using RimWorld;

namespace GodHandMod
{
    // 炮塔头保护辅助类
    public static class TurretHeadProtectionHelper
    {
        public static bool HasTurretHead(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null) return false;
            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead) return true;
            }
            return false;
        }

        public static bool IsHeadPart(BodyPartRecord part)
        {
            if (part == null) return false;
            BodyPartRecord current = part;
            while (current != null)
            {
                if (current.def.defName == "Head" ||
                    current.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
