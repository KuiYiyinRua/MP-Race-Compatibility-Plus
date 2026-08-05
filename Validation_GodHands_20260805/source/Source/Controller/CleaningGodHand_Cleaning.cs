using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    public static partial class CleaningGodHandController
    {
        // 清洁指定区域内容
        public static void CleanAreaAtPosition(Map map, IntVec3 center)
        {
            if (map == null) return;

            int r = cleanRadius;
            for (int x = center.x - r; x <= center.x + r; x++)
            {
                for (int z = center.z - r; z <= center.z + r; z++)
                {
                    IntVec3 cell = new IntVec3(x, center.y, z);
                    if (cell.InBounds(map)) CleanCell(map, cell);
                }
            }
        }

        // 单格综合清理
        private static void CleanCell(Map map, IntVec3 cell)
        {
            CleanFilthAt(map, cell);
            ExtinguishFireAt(map, cell);

            if (currentMode != CleaningMode.Cleaning) DrainWaterAt(map, cell);

            CleanPollutionAt(map, cell);

            if (GodHandModMain.Settings.cleaningEnableHealing) HealPawnsAt(map, cell);
            if (GodHandModMain.Settings.cleaningEnablePlantGrowth) BoostPlantsAt(map, cell);
            if (GodHandModMain.Settings.cleaningEnableFoodPreservation) ProcessFoodAndCorpsesAt(map, cell);
            if (GodHandModMain.Settings.cleaningEnableRepair) RepairItemsAt(map, cell);
        }

        // 移除污渍
        private static void CleanFilthAt(Map map, IntVec3 cell)
        {
            List<Thing> list = cell.GetThingList(map);
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] is Filth f) f.Destroy();
        }

        // 扑灭火灾
        private static void ExtinguishFireAt(Map map, IntVec3 cell)
        {
            List<Thing> list = cell.GetThingList(map);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Thing t = list[i];
                if (t is Fire f) f.Destroy();
                else if (t is Pawn p) p.GetAttachment(ThingDefOf.Fire)?.Destroy();
            }
        }

        // 净化环境污染
        private static void CleanPollutionAt(Map map, IntVec3 cell)
        {
            if (!ModsConfig.BiotechActive || map.pollutionGrid == null) return;
            if (map.pollutionGrid.IsPolluted(cell)) map.pollutionGrid.SetPolluted(cell, false);
        }

        // 催生植物生长
        private static void BoostPlantsAt(Map map, IntVec3 cell)
        {
            Plant p = cell.GetPlant(map);
            if (p == null || p.Destroyed || p.Growth < 0.01f || !ShouldProcess(p)) return;

            float bonus = GodHandModMain.Settings.cleaningPlantGrowthBonus;
            p.Growth = Mathf.Min(1f, p.Growth + bonus);

            if (p.def.plant.Sowable && p.Growth >= 0.01f)
                p.Growth = Mathf.Min(1f, p.Growth + bonus * 0.5f);
        }

        // 修补格子内物品
        private static void RepairItemsAt(Map map, IntVec3 cell)
        {
            List<Thing> list = cell.GetThingList(map);
            foreach (Thing t in list)
            {
                if (t is Pawn || t is Plant) continue;
                if (t.def.useHitPoints && t.HitPoints < t.MaxHitPoints) RepairSingleItem(t);
            }
        }

        // 执行单体修复
        private static void RepairSingleItem(Thing t)
        {
            if (!ShouldProcess(t)) return;
            int amount = Mathf.Max(10, Mathf.CeilToInt(t.MaxHitPoints * 0.05f));
            t.HitPoints = Mathf.Min(t.MaxHitPoints, t.HitPoints + amount);
        }
    }
}
