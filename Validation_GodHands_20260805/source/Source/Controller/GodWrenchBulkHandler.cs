using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 批量铲起处理器
    public class GodWrenchBulkHandler
    {
        public enum ScoopType { Building, Roof, DeepResource, All }

        private readonly GodWrenchController core;
        private IntVec3 startCell;
        private bool isScooped;
        private ScoopType currentType;
        private Map scoopedMap;

        private List<ScoopedThingEntry> scoopedThings = new List<ScoopedThingEntry>();
        private List<Pair<IntVec3, RoofDef>> scoopedRoofs = new List<Pair<IntVec3, RoofDef>>();
        private List<Pair<IntVec3, ThingDef>> scoopedResources = new List<Pair<IntVec3, ThingDef>>();
        private List<int> scoopedResourceCounts = new List<int>();

        public bool IsActive => isScooped;
        public GodWrenchBulkHandler(GodWrenchController core) => this.core = core;

        private class ScoopedThingEntry
        {
            public Thing thing;
            public IntVec3 offsetFromStart;
            public Vector3 drawOffsetFromStart;
            public Rot4 rotation;
            public Graphic cachedGraphic;
            public bool isBuilding;
        }

        public void StartSelection(IntVec3 start, ScoopType type)
        {
            startCell = start;
            currentType = type;
            scoopedMap = Find.CurrentMap;
            DoScoop(scoopedMap);
            isScooped = true;
        }

        public void Update(Map map) { }

        private void DoScoop(Map map)
        {
            ClearData();
            int r = Mathf.RoundToInt(GodHandModMain.Settings.godHandGrabRadius);
            HashSet<Thing> collected = new HashSet<Thing>();

            foreach (IntVec3 cell in CellRect.CenteredOn(startCell, r))
            {
                if (!cell.InBounds(map)) continue;
                if (currentType != ScoopType.Roof) CollectThingsAt(map, cell, collected);
                if (currentType != ScoopType.Building) ScoopEnvironmentAt(map, cell);
            }

            Vector3 startWorldPos = startCell.ToVector3Shifted();
            foreach (var t in collected)
            {
                if (t == null || t.Destroyed || !t.Spawned) continue;

                var entry = new ScoopedThingEntry
                {
                    thing = t,
                    offsetFromStart = t.Position - startCell,
                    drawOffsetFromStart = t.DrawPos - startWorldPos,
                    rotation = t.Rotation,
                    cachedGraphic = t.Graphic,
                    isBuilding = t.def.category == ThingCategory.Building
                };
                scoopedThings.Add(entry);
                try { t.DeSpawn(DestroyMode.Vanish); } catch (Exception) { }
            }
        }

        private void CollectThingsAt(Map map, IntVec3 c, HashSet<Thing> collected)
        {
            foreach (var t in c.GetThingList(map).ToList())
            {
                if (!t.Destroyed && (t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Plant))
                    collected.Add(t);
            }
        }

        private void ScoopEnvironmentAt(Map map, IntVec3 c)
        {
            if (map.roofGrid.Roofed(c))
            {
                scoopedRoofs.Add(new Pair<IntVec3, RoofDef>(c - startCell, map.roofGrid.RoofAt(c)));
                map.roofGrid.SetRoof(c, null);
            }
            ThingDef res = map.deepResourceGrid.ThingDefAt(c);
            if (res != null)
            {
                scoopedResources.Add(new Pair<IntVec3, ThingDef>(c - startCell, res));
                scoopedResourceCounts.Add(map.deepResourceGrid.CountAt(c));
                map.deepResourceGrid.SetAt(c, null, 0);
            }
        }

        public void Release(Map map, IntVec3 cell)
        {
            if (!isScooped) return;
            IntVec3 offset = cell - startCell;

            // 放置建筑
            foreach (var entry in scoopedThings)
            {
                if (!entry.isBuilding) continue;
                if (entry.thing == null || entry.thing.Destroyed) continue;
                IntVec3 targetPos = startCell + entry.offsetFromStart + offset;
                if (!targetPos.InBounds(map)) continue;

                try
                {
                    entry.thing.SetPositionDirect(targetPos);
                    entry.thing.Rotation = entry.rotation;
                    entry.thing.SpawnSetup(map, false);
                    WrenchStackedBuildingTracker.MarkAsStacked(entry.thing);
                }
                catch (Exception e) { Log.Error($"[GodHand] 建筑放置失败 {entry.thing?.def?.defName} {e.Message}"); }
            }

            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();

            // 放置植物
            foreach (var entry in scoopedThings)
            {
                if (entry.isBuilding) continue;
                if (entry.thing == null || entry.thing.Destroyed) continue;
                IntVec3 targetPos = startCell + entry.offsetFromStart + offset;
                if (!targetPos.InBounds(map)) continue;

                try
                {
                    entry.thing.SetPositionDirect(targetPos);
                    entry.thing.Rotation = entry.rotation;
                    entry.thing.SpawnSetup(map, false);
                }
                catch (Exception e) { Log.Error($"[GodHand] 植物放置失败 {entry.thing?.def?.defName} {e.Message}"); }
            }

            PlaceEnvironment(map, offset);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            map.roofCollapseBuffer.Clear();
            ForceClear();
        }

        private void PlaceEnvironment(Map map, IntVec3 offset)
        {
            foreach (var r in scoopedRoofs)
            {
                IntVec3 target = startCell + r.First + offset;
                if (target.InBounds(map)) map.roofGrid.SetRoof(target, r.Second);
            }
            for (int i = 0; i < scoopedResources.Count; i++)
            {
                IntVec3 target = startCell + scoopedResources[i].First + offset;
                if (target.InBounds(map))
                    map.deepResourceGrid.SetAt(target, scoopedResources[i].Second, scoopedResourceCounts[i]);
            }
        }

        public void Draw()
        {
            if (!isScooped || scoopedThings.Count == 0) return;
            IntVec3 mouseCell = UI.MouseCell();
            Vector3 mouseWorldPos = mouseCell.ToVector3Shifted();
            Map map = Find.CurrentMap;
            if (map == null) return;

            foreach (var entry in scoopedThings)
            {
                if (entry.thing == null) continue;
                Vector3 drawPos = mouseWorldPos + entry.drawOffsetFromStart;
                drawPos.y = entry.thing.def.Altitude + 0.05f;
                Graphic graphic = entry.cachedGraphic ?? entry.thing.Graphic ?? entry.thing.def.graphic;
                if (graphic == null) continue;
                try { graphic.Draw(drawPos, entry.rotation, entry.thing); } catch (Exception) { }
            }
            int r = Mathf.RoundToInt(GodHandModMain.Settings.godHandGrabRadius);
            GenDraw.DrawFieldEdges(CellRect.CenteredOn(mouseCell, r).Cells.ToList(), Color.white);
        }

        private void ClearData()
        {
            scoopedThings.Clear();
            scoopedRoofs.Clear();
            scoopedResources.Clear();
            scoopedResourceCounts.Clear();
            scoopedMap = null;
        }

        public void ForceClear()
        {
            if (isScooped && scoopedThings.Count > 0 && scoopedMap != null)
            {
                foreach (var entry in scoopedThings)
                {
                    if (entry.thing == null || entry.thing.Destroyed || entry.thing.Spawned) continue;
                    IntVec3 op = startCell + entry.offsetFromStart;
                    if (op.InBounds(scoopedMap))
                    {
                        try
                        {
                            entry.thing.SetPositionDirect(op);
                            entry.thing.Rotation = entry.rotation;
                            entry.thing.SpawnSetup(scoopedMap, false);
                        }
                        catch (Exception) { }
                    }
                }
                scoopedMap.roofCollapseBuffer.Clear();
            }
            isScooped = false;
            ClearData();
        }
    }
}
