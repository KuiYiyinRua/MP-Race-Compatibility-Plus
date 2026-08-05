using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 护盾检测工具类
    public static class ShieldUtils
    {
        // 检查位置是否被护盾覆盖 (支持 Y 轴容差)
        public static bool IsProtected(Map map, Vector3 pos, out GodShieldData shield)
        {
            shield = null;
            if (map == null) return false;

            var comp = map.GetComponent<MapComponent_GodProtection>();
            if (comp == null || comp.ActiveShields.Count == 0) return false;

            foreach (var s in comp.ActiveShields)
            {
                if (s.birthProgress < 0.2f) continue;

                if (s.isDome)
                {
                    // 只查水平距离
                    float dx = pos.x - s.pos.x;
                    float dz = pos.z - s.pos.z;
                    if (dx * dx + dz * dz <= s.radius * s.radius)
                    {
                        shield = s;
                        return true;
                    }
                }
            }
            return false;
        }

        // 获取特定格子的护盾
        public static bool IsProtected(Map map, IntVec3 c, out GodShieldData shield)
        {
            return IsProtected(map, c.ToVector3Shifted(), out shield);
        }

        // 获取交点距离
        public static bool GetSphereIntersections(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float t_entry, out float t_exit)
        {
            t_entry = t_exit = 0;
            Vector3 L = center - origin;
            float tca = Vector3.Dot(L, direction);
            float d2 = Vector3.Dot(L, L) - tca * tca;
            float r2 = radius * radius;

            if (d2 > r2) return false;

            float thc = Mathf.Sqrt(r2 - d2);
            t_entry = tca - thc;
            t_exit = tca + thc;
            return true;
        }

        // 获取护盾外边缘点 (2D 平面) - 仅数学计算
        public static Vector3 GetNearestPointOutside2D(Vector3 currentPos, GodShieldData shield, float safetyMargin = 5.0f)
        {
            Vector3 center = shield.pos;
            float dx = currentPos.x - center.x;
            float dz = currentPos.z - center.z;

            Vector3 dir = new Vector3(dx, 0, dz).normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;

            return center + dir * (shield.radius + safetyMargin);
        }

        // 寻找安全落点
        public static IntVec3 GetSafeRepelCell(Map map, Vector3 fromPos, GodShieldData shield, float safetyMargin = 5.0f)
        {
            Vector3 mathPos = GetNearestPointOutside2D(fromPos, shield, safetyMargin);
            return GetSafeLandingCell(map, mathPos, shield.pos, safetyMargin);
        }

        // 通用落点查找
        public static IntVec3 GetSafeLandingCell(Map map, Vector3 targetMathPos, Vector3 originPos, float searchRadius = 3f)
        {
            IntVec3 dest = targetMathPos.ToIntVec3();

            // 合法直接返回
            if (dest.InBounds(map) && !dest.Impassable(map) && dest.Standable(map)) return dest;

            // 尝试偏转寻路
            Vector3 baseDir = (targetMathPos - originPos).normalized;
            if (baseDir == Vector3.zero) baseDir = Vector3.forward;

            // 增加采样深度
            for (float r = 0; r <= searchRadius + 2f; r += 1f)
            {
                for (float angle = 0f; angle <= 120f; angle += 15f)
                {
                    foreach (float sign in new float[] { -1f, 1f })
                    {
                        if (angle == 0 && sign == 1f) continue; // 避免重复检测正前方
                        Vector3 rotatedDir = baseDir.RotatedBy(angle * sign);

                        IntVec3 testCell = (targetMathPos + rotatedDir * r).ToIntVec3();
                        if (testCell.InBounds(map) && !testCell.Impassable(map) && testCell.Standable(map))
                        {
                            return testCell;
                        }
                    }
                }
            }

            // 深度保底
            return CellFinder.RandomClosewalkCellNear(dest, map, 2);
        }

        // 获取护盾外边缘点
        public static Vector3 GetNearestPointOutside(Vector3 pos, GodShieldData shield, float safetyMargin = 0.5f)
        {
            Vector3 center = shield.pos;
            Vector3 dir = (pos - center).normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;
            return center + dir * (shield.radius + safetyMargin);
        }

        // 检查是否为敌对且在护盾内
        public static bool ShouldRepel(Pawn pawn, GodShieldData shield)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead) return false;
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return true;
            }
            return false;
        }
    }
}
