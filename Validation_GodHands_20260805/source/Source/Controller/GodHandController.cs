using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 神之手中心控制器
    public class GodHandController
    {
        private bool isActive;
        private IntVec3 grabStartCell;
        private List<Pawn> grabbedPawns = new List<Pawn>();
        private List<Thing> grabbedThings = new List<Thing>();
        private readonly List<MouseSample> mouseTrajectory = new List<MouseSample>();

        private readonly GodHandPawnHandler pawnHandler;
        private readonly GodHandItemHandler itemHandler;
        private readonly GodHandWeaponHandler weaponHandler;

        private readonly Dictionary<Thing, Vector3> grabOffsets = new Dictionary<Thing, Vector3>();

        public bool IsActive => isActive && (grabbedPawns.Any() || grabbedThings.Any());
        public List<Pawn> GrabbedPawns => grabbedPawns;
        public List<Thing> GrabbedThings => grabbedThings;
        public Dictionary<Thing, Vector3> GrabOffsets => grabOffsets;
        public IntVec3 GrabStartCell => grabStartCell;
        public List<MouseSample> MouseTrajectory => mouseTrajectory;

        // API 兼容属性
        public bool IsShootingMode => weaponHandler.IsShootingMode;
        public bool IsMeleeMode => weaponHandler.IsMeleeMode;
        public Vector3 FixedWeaponPos => weaponHandler.FixedWeaponPos;
        public bool IsRangeGrab { get; private set; }

        public GodHandController()
        {
            pawnHandler = new GodHandPawnHandler(this);
            itemHandler = new GodHandItemHandler(this);
            weaponHandler = new GodHandWeaponHandler(this);
        }

        public void TryStartGrab(Map map, Pawn target, IntVec3 startCell) => TryStartGrabReal(map, target, startCell, GrabbingUtils.IsShiftSelected);
        public void TryStartGrabItem(Map map, Thing target, IntVec3 startCell) => TryStartGrabItemReal(map, target, startCell, GrabbingUtils.IsShiftSelected);

        private void TryStartGrabReal(Map map, Pawn target, IntVec3 startCell, bool rangeMode)
        {
            if (isActive) return;
            IsRangeGrab = rangeMode;
            grabStartCell = startCell;
            grabOffsets.Clear();
            if (rangeMode)
            {
                var ts = GrabbingUtils.GetValidPawnsInRadius(map, startCell, GodHandModMain.Settings.godHandGrabRadius);
                ts.ForEach(p => { grabbedPawns.Add(p); grabOffsets[p] = p.DrawPos - startCell.ToVector3Shifted(); pawnHandler.OnGrabbed(p); });
            }
            else if (target != null) { grabbedPawns.Add(target); grabOffsets[target] = Vector3.zero; pawnHandler.OnGrabbed(target); }
            if (grabbedPawns.Any()) StartCommon();
        }

        private void TryStartGrabItemReal(Map map, Thing target, IntVec3 startCell, bool rangeMode)
        {
            if (isActive) return;
            IsRangeGrab = rangeMode;
            grabStartCell = startCell;
            grabOffsets.Clear();
            if (rangeMode)
            {
                var ts = GrabbingUtils.GetValidItemsInRadius(map, startCell, GodHandModMain.Settings.godHandGrabRadius);
                ts.ForEach(t => { grabbedThings.Add(t); grabOffsets[t] = t.DrawPos - startCell.ToVector3Shifted(); itemHandler.OnGrabbed(t); });
            }
            else if (target != null) { grabbedThings.Add(target); grabOffsets[target] = Vector3.zero; itemHandler.OnGrabbed(target); }
            if (grabbedThings.Any()) StartCommon();
        }

        private void StartCommon()
        {
            isActive = true;
            mouseTrajectory.Clear();
            pawnHandler.OnStart(grabStartCell);
        }

        public void Update()
        {
            if (!IsActive) return;
            weaponHandler.Update(Find.CurrentMap, UI.MouseMapPosition());
            grabbedPawns.RemoveAll(p => p.Destroyed || p.Dead);
            grabbedThings.RemoveAll(t => t.Destroyed);
            if (!IsActive) ForceReleaseGrab();
        }

        public void UpdateDragged(Map map, IntVec3 cell, float time)
        {
            if (!IsActive) return;
            Vector3 pos = UI.MouseMapPosition();
            mouseTrajectory.Add(new MouseSample { worldPosition = cell, time = time });
            if (mouseTrajectory.Count > 30) mouseTrajectory.RemoveAt(0);
            pawnHandler.Update(map, pos, time);
        }

        public void DrawDraggedThing() => Draw();
        public void Draw() { if (!IsActive) return; itemHandler.Draw(); weaponHandler.Draw(); }

        public void ReleaseGrab(Map map, IntVec3 cell, float time)
        {
            if (!IsActive) return;
            pawnHandler.Release(map, cell, time);
            itemHandler.Release(map, cell);
            ForceReleaseGrab();
        }

        public void ForceReleaseGrab()
        {
            pawnHandler.ForceClear();
            itemHandler.ForceClear();
            weaponHandler.ForceClear();
            grabbedPawns.Clear();
            grabbedThings.Clear();
            grabOffsets.Clear();
            isActive = false;
            IsRangeGrab = false;
        }

        public void ToggleShootingMode() => weaponHandler.ToggleShootingMode();
        public void ToggleMeleeMode() => weaponHandler.ToggleMeleeMode();
        public void ResetMouseRelease() => weaponHandler.ResetMouseRelease();
        public void ShootInShootingMode(Map map, IntVec3 target, float time) => weaponHandler.TryShoot(map, target);
        public void UpdateMeleeMode(Map map, float time) { }

        public float GetWeaponRange() => weaponHandler.GetWeaponRange();
        public bool IsHoldingWeapon() => weaponHandler.IsHoldingWeapon();
        public bool IsHoldingMeleeWeapon() => weaponHandler.IsHoldingMeleeWeapon();
        public bool IsHoldingRangedWeapon() => weaponHandler.IsHoldingRangedWeapon();
        public bool IsHoldingHybridWeapon() => weaponHandler.IsHoldingHybridWeapon();
    }

    public struct MouseSample { public IntVec3 worldPosition; public float time; }
}
