using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // 神之扳手控制器
    public class GodWrenchController
    {
        private bool isActive;
        private Thing grabbedThing;
        private IntVec3 grabStart;
        private IntVec3 originalPosition;
        private Map currentMap;
        private TerrainDef originalTerrain;

        private readonly GodWrenchTurretHandler turretHandler;
        private readonly GodWrenchBulkHandler bulkHandler;

        public bool IsActive => isActive && (grabbedThing != null || bulkHandler.IsActive);
        public static bool IsTurretBuilding(Thing t) => t?.def?.building?.turretGunDef != null;

        public GodWrenchController()
        {
            turretHandler = new GodWrenchTurretHandler(this);
            bulkHandler = new GodWrenchBulkHandler(this);
        }

        public void TryStartGrab(Map map, Thing thing, IntVec3 start)
        {
            if (isActive) return;
            grabbedThing = thing;
            grabStart = start;
            originalPosition = thing.Position;
            originalTerrain = map.terrainGrid.TerrainAt(thing.Position);
            currentMap = map;
            isActive = true;
        }

        public void StartBulkScoop(IntVec3 start, GodWrenchBulkHandler.ScoopType type)
        {
            bulkHandler.StartSelection(start, type);
            isActive = true;
        }

        public void Update()
        {
            if (bulkHandler.IsActive) bulkHandler.Update(Find.CurrentMap);
            if (IsActive && grabbedThing != null && grabbedThing.Destroyed) ForceReleaseGrab();
        }

        private static System.Reflection.FieldInfo positionIntField = typeof(Thing).GetField("positionInt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public void UpdateDraggedThing(Map map, IntVec3 currentCell)
        {
            if (!IsActive || grabbedThing == null || !currentCell.InBounds(map)) return;

            if (grabbedThing.Position != currentCell)
            {
                IntVec3 oldPos = grabbedThing.Position;
                map.thingGrid.Deregister(grabbedThing, false);

                if (positionIntField != null) positionIntField.SetValue(grabbedThing, currentCell);
                else grabbedThing.Position = currentCell;

                map.thingGrid.Register(grabbedThing);
                MarkAllOccupiedCellsDirty(map, oldPos, grabbedThing.Rotation);
                MarkAllOccupiedCellsDirty(map, currentCell, grabbedThing.Rotation);
            }
        }

        // 标记占据格子渲染无效
        private void MarkAllOccupiedCellsDirty(Map map, IntVec3 position, Rot4 rotation)
        {
            if (grabbedThing == null) return;
            IntVec2 size = grabbedThing.def.size;
            if (rotation.IsHorizontal) size = new IntVec2(size.z, size.x);

            for (int x = 0; x < size.x; x++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    IntVec3 cell = position + new IntVec3(x, 0, z);
                    if (cell.InBounds(map))
                    {
                        map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings | MapMeshFlagDefOf.Terrain);
                        map.glowGrid.DirtyCell(cell);
                    }
                }
            }
        }

        public void ReleaseGrab(Map map, IntVec3 cell)
        {
            if (bulkHandler.IsActive)
            {
                bulkHandler.Release(map, cell);
                if (!bulkHandler.IsActive) ForceReleaseGrab();
                return;
            }

            if (grabbedThing == null) return;

            map.thingGrid.Deregister(grabbedThing, false);
            MarkAllOccupiedCellsDirty(map, originalPosition, grabbedThing.Rotation);
            MarkAllOccupiedCellsDirty(map, grabbedThing.Position, grabbedThing.Rotation);

            if (positionIntField != null) positionIntField.SetValue(grabbedThing, originalPosition);
            else grabbedThing.Position = originalPosition;

            map.thingGrid.Register(grabbedThing);
            if (grabbedThing is Building b) map.edificeGrid.Register(b);

            grabbedThing.DeSpawn(DestroyMode.Vanish);

            if (originalTerrain != null && originalPosition.InBounds(map))
            {
                if (!IsNaturalRockTerrain(originalTerrain) && IsNaturalRockTerrain(map.terrainGrid.TerrainAt(originalPosition)))
                    map.terrainGrid.SetTerrain(originalPosition, originalTerrain);
            }

            grabbedThing.Position = cell;
            grabbedThing.SpawnSetup(map, false);
            map.linkGrid.Notify_LinkerCreatedOrDestroyed(grabbedThing);

            if (grabbedThing is Building grabbedBuilding) CheckAndMarkStackedBuildings(map, cell, grabbedBuilding);

            NotifyPawnsToStopUsing(map, grabbedThing);

            // 炮塔头掉落自动佩戴合并
            if (grabbedThing is GodHandTurretHead droppedHead && droppedHead.Spawned)
            {
                Pawn p = cell.GetFirstPawn(map);
                if (p != null && p.RaceProps.Humanlike && p.apparel != null)
                {
                    GodHandTurretHead existing = p.apparel.WornApparel.OfType<GodHandTurretHead>().FirstOrDefault();
                    if (existing == null) p.apparel.Wear(droppedHead);
                    else
                    {
                        int addedCount = 0;
                        var slots = droppedHead.TurretSlots.ToList();
                        foreach (var slot in slots)
                        {
                            if (existing.TurretCount < GodHandTurretHead.MaxTurrets)
                            {
                                droppedHead.RemoveTurretSlot(slot);
                                existing.AddTurretSlot(slot);
                                addedCount++;
                            }
                        }
                        if (addedCount > 0 && droppedHead.TurretCount == 0) droppedHead.Destroy();
                    }
                }
            }
            ForceReleaseGrab();
        }

        public void ForceReleaseGrab()
        {
            grabbedThing = null;
            isActive = false;
            currentMap = null;
            originalTerrain = null;
            bulkHandler.ForceClear();
        }

        private void NotifyPawnsToStopUsing(Map map, Thing thing)
        {
            if (map == null || thing == null) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.jobs?.curJob != null)
                {
                    bool targetsThing = (pawn.jobs.curJob.targetA.Thing == thing) ||
                                       (pawn.jobs.curJob.targetB.Thing == thing) ||
                                       (pawn.jobs.curJob.targetC.Thing == thing);
                    if (targetsThing) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
                }
            }
        }

        private void CheckAndMarkStackedBuildings(Map map, IntVec3 cell, Building placedBuilding)
        {
            List<Building> buildingsAtCell = new List<Building>();
            foreach (Thing t in map.thingGrid.ThingsAt(cell))
            {
                if (t is Building b && b != placedBuilding) buildingsAtCell.Add(b);
            }
            if (buildingsAtCell.Count > 0)
            {
                WrenchStackedBuildingTracker.MarkAsStacked(placedBuilding);
                foreach (var b in buildingsAtCell) WrenchStackedBuildingTracker.MarkAsStacked(b);
            }
        }

        private bool IsNaturalRockTerrain(TerrainDef terrain)
        {
            if (terrain == null) return false;
            string defName = terrain.defName.ToLower();
            return defName.Contains("rock") || defName.Contains("rough") || defName.Contains("stone") || defName.Contains("hewn");
        }

        public void TrySnatchTurretHead(Map map, Thing t) => turretHandler.TrySnatch(map, t, null);
        public void TryRemoveTurretFromPawn(Map map, Pawn p, GodHandTurretHead head) => turretHandler.TryRemoveFromPawn(map, p, head);

        public void Draw() { if (bulkHandler.IsActive) bulkHandler.Draw(); }
    }
}
