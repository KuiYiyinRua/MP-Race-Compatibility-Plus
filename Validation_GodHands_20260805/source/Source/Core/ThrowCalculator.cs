using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 甩飞计算器
    public class ThrowCalculator
    {
        // 计算甩飞结果
        public ThrowResult CalculateThrow(List<MouseSample> trajectory, Map map, Pawn pawn, IntVec3 startCell, IntVec3 releaseCell)
        {
            var result = new ThrowResult { LandingCell = releaseCell };
            if (trajectory.Count < 2) return result;

            var throwVector = CalculateThrowVector(trajectory);
            float throwSpeed = throwVector.magnitude;
            if (throwSpeed < GodHandModMain.Settings.minThrowSpeed) return result;

            // 计算飞行距离
            float speedFactor = Mathf.Sqrt(throwVector.magnitude);
            float rawDistance = speedFactor * GodHandModMain.Settings.ActualThrowDistanceFactor * 0.33f;
            int throwCells = Mathf.Max(1, Mathf.RoundToInt(rawDistance));

            // 计算落点并获取结果
            IntVec3 landingCell = CalculateLandingCell(map, releaseCell, throwVector.normalized, throwCells);
            int actualDistance = Mathf.RoundToInt(Mathf.Sqrt(startCell.DistanceToSquared(landingCell)));

            result.LandingCell = landingCell;
            result.Distance = actualDistance;
            result.Damage = CalculateDamage(pawn, actualDistance, throwSpeed);
            result.ShouldThrow = true;
            return result;
        }

        // 计算甩动向量
        private Vector2 CalculateThrowVector(List<MouseSample> trajectory)
        {
            int samplesToUse = Mathf.Min(5, trajectory.Count);
            var recent = trajectory.TakeLast(samplesToUse).ToList();
            if (recent.Count < 2) return Vector2.zero;

            Vector2 totalVel = Vector2.zero;
            int count = 0;
            for (int i = 1; i < recent.Count; i++)
            {
                float dt = recent[i].time - recent[i - 1].time;
                if (dt <= 0.001f) continue;
                totalVel += (recent[i].worldPosition.ToVector2() - recent[i - 1].worldPosition.ToVector2()) / dt;
                count++;
            }
            return count > 0 ? totalVel / count : Vector2.zero;
        }

        // 计算落点
        private IntVec3 CalculateLandingCell(Map map, IntVec3 startCell, Vector2 direction, int maxDistance)
        {
            if (!startCell.InBounds(map)) return map.Center;

            IntVec3 lastValid = startCell;
            for (float d = 0.5f; d <= maxDistance; d += 0.5f)
            {
                IntVec3 cell = (startCell.ToVector2() + direction * d).ToIntVec3();
                if (!cell.InBounds(map) || cell.Impassable(map)) break;
                lastValid = cell;
            }
            return lastValid;
        }

        // 计算碰撞伤害
        private float CalculateDamage(Pawn pawn, int distance, float throwSpeed)
        {
            if (pawn == null) return 0f;
            float baseDmg = GodHandModMain.Settings.baseDamage * 0.1f;
            float distDmg = distance * GodHandModMain.Settings.damagePerCell * 0.1f;
            float bodyFactor = Mathf.Sqrt(pawn.BodySize);
            float speedFactor = Mathf.Clamp(throwSpeed / 5f, 0.5f, 3f);
            return (baseDmg + distDmg) * bodyFactor * speedFactor;
        }
    }

    // 甩飞结果数据
    public struct ThrowResult
    {
        public IntVec3 LandingCell;
        public int Distance;
        public float Damage;
        public bool ShouldThrow;
    }

    internal static class Extensions
    {
        public static Vector2 ToVector2(this Vector3 v) => new Vector2(v.x, v.z);
        public static Vector3 ToVector3(this Vector2 v) => new Vector3(v.x, 0, v.y);
        public static IntVec3 ToIntVec3(this Vector2 v) => new IntVec3(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.y));
    }
}
