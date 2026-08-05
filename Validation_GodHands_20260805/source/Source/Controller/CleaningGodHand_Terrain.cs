using System;
using RimWorld;
using Verse;

namespace GodHandMod
{
    public static partial class CleaningGodHandController
    {
        // 排除积水入口
        private static void DrainWaterAt(Map map, IntVec3 cell)
        {
            if (currentMode == CleaningMode.Cleaning) return;
            DrainTopTerrain(map, cell);
            DrainUnderTerrain(map, cell);
        }

        // 处理表层地形排水
        private static void DrainTopTerrain(Map map, IntVec3 cell)
        {
            TerrainDef top = map.terrainGrid.TopTerrainAt(cell);
            TerrainDef dried = GetDriedTerrainForMode(map, top);
            if (dried != null) map.terrainGrid.SetTerrain(cell, dried);
        }

        // 处理深层地形排水
        private static void DrainUnderTerrain(Map map, IntVec3 cell)
        {
            TerrainDef under = map.terrainGrid.UnderTerrainAt(cell);
            TerrainDef dried = GetDriedTerrainForMode(map, under);
            if (dried != null) map.terrainGrid.SetUnderTerrain(cell, dried);
        }

        // 根据当前模式检索干燥地形
        private static TerrainDef GetDriedTerrainForMode(Map map, TerrainDef t)
        {
            if (t == null) return null;
            if (currentMode == CleaningMode.Pumping) return GetPumpDriedTerrain(map, t);
            if (currentMode == CleaningMode.WaterErase) return GetForceDriedTerrain(map, t);
            return null;
        }

        // 模拟原版排水
        private static TerrainDef GetPumpDriedTerrain(Map map, TerrainDef t)
        {
            if (t.driesTo != null)
            {
                if (map.Biome == BiomeDefOf.SeaIce) return TerrainDefOf.Ice;
                return t.driesTo;
            }
            return null;
        }

        // 强力强制排水
        private static TerrainDef GetForceDriedTerrain(Map map, TerrainDef t)
        {
            var pump = GetPumpDriedTerrain(map, t);
            if (pump != null) return pump;
            if (IsWaterTerrain(t)) return GetDefaultDriedTerrain(t);
            return null;
        }

        // 识别水体地形
        private static bool IsWaterTerrain(TerrainDef t)
        {
            if (t == null || IsArtificialFloor(t)) return false;
            string defName = t.defName.ToLower();
            return IsWaterName(defName);
        }

        // 识别玩家铺设地板
        private static bool IsArtificialFloor(TerrainDef t)
        {
            if (t.affordances == null) return false;
            if (t.affordances.Contains(TerrainAffordanceDefOf.Heavy)) return true;
            if (t.affordances.Contains(TerrainAffordanceDefOf.Medium)) return true;
            return t.BuildableByPlayer && t.designationCategory != null;
        }

        // 地形名称模式匹配
        private static bool IsWaterName(string name)
        {
            if (name.Contains("bridge")) return false;
            if (name.Contains("water")) return true;
            if (name.Contains("marsh") && !name.Contains("floor")) return true;
            return false;
        }

        // 获取后备干燥地
        private static TerrainDef GetDefaultDriedTerrain(TerrainDef water)
        {
            if (water == null) return TerrainDefOf.Soil;
            string name = water.defName.ToLower();
            if (name.Contains("shallow") || name.Contains("marsh") || name.Contains("moving"))
                return TerrainDef.Named("Gravel") ?? TerrainDefOf.Soil;
            return TerrainDefOf.Soil;
        }
    }
}
