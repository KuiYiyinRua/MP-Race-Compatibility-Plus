using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 提供抓取逻辑相关的通用工具方法
    public static class GrabbingUtils
    {
        public static List<Pawn> GetValidPawnsInRadius(Map map, IntVec3 center, float radius)
        {
            return GenRadial.RadialCellsAround(center, radius, true)
                .SelectMany(c => c.GetThingList(map))
                .OfType<Pawn>()
                .Where(p => !p.Dead && p.Spawned)
                .Distinct()
                .ToList();
        }

        public static List<Thing> GetValidItemsInRadius(Map map, IntVec3 center, float radius)
        {
            return GenRadial.RadialCellsAround(center, radius, true)
                .SelectMany(c => c.GetThingList(map))
                .Where(t => t.def.category == ThingCategory.Item && t.Spawned)
                .Distinct()
                .ToList();
        }

        public static Pawn GetPawnAt(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return null;
            return cell.GetThingList(map).OfType<Pawn>().FirstOrDefault(p => !p.Dead);
        }

        public static bool IsShiftSelected => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public static bool IsLeftMouseDown => Input.GetMouseButton(0);

        public static bool IsRightMouseDown => Input.GetMouseButton(1);

        public static Rot4 GetDirection(Map map, IntVec3 target)
        {
            IntVec3 center = map.Center;
            int dx = target.x - center.x;
            int dz = target.z - center.z;
            return Mathf.Abs(dx) > Mathf.Abs(dz) ? (dx > 0 ? Rot4.East : Rot4.West) : (dz > 0 ? Rot4.North : Rot4.South);
        }
    }
}
