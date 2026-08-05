using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 齿轮箱发电机建筑
    public class Building_GearboxGenerator : Building
    {
        private CrankState state = new CrankState();
        private CompPowerPlant powerComp;

        // 角加速度基准
        private const float ACCELERATION_BASE = 50f;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerPlant>();
        }

        // 转速平滑下降
        public void AddWork(float workFactor)
        {
            // 根据操作能力设定转速
            float targetVelocity = 360f * workFactor;

            // 平滑追踪目标转速
            state.smoothedVelocity = Mathf.Lerp(state.smoothedVelocity, targetVelocity, 0.2f);
            lastWorkTick = Find.TickManager.TicksGame;
        }

        private int lastWorkTick = -1;

        protected override void Tick()
        {
            base.Tick();

            // 自动停机检查 (增加宽容度至 5 Tick)
            if (Find.TickManager.TicksGame > lastWorkTick + 5)
            {
                state.smoothedVelocity = Mathf.Lerp(state.smoothedVelocity, 0f, 0.1f);
                if (state.smoothedVelocity < 1f) state.smoothedVelocity = 0;
            }

            // 更新角度同步逻辑步长
            state.currentAngle = (state.currentAngle + state.smoothedVelocity * (1f / 60f)) % 360f;

            // 更新电力
            UpdatePowerOutput();
        }

        private static GodHandSettings settingsCached;
        private static GodHandSettings Settings
        {
            get
            {
                if (settingsCached == null) settingsCached = GodHandModMain.Settings;
                return settingsCached;
            }
        }

        private void UpdatePowerOutput()
        {
            float normalizedSpeed = state.smoothedVelocity / 360f; // 1转s为基准
            var s = Settings;

            // 输出功率计算
            float watts = normalizedSpeed * 2000f * s.handCrankPowerMultiplier;
            if (state.smoothedVelocity < 10f) watts = 0;

            if (powerComp != null)
            {
                powerComp.PowerOutput = watts;
            }
        }

        // 获取曲柄世界坐标
        public Vector3 GetCrankWorldPosition()
        {
            // 手柄坐标偏移
            Vector3 offset = this.Rotation.FacingCell.ToVector3() * 1.5f;
            return this.DrawPos + offset;
        }

        // 获取有效交互格子
        public IEnumerable<IntVec3> GetValidInteractionCells()
        {
            // 手柄末端格子位置
            IntVec3 crankCell = this.Position + this.Rotation.FacingCell;

            // 前方交互格
            yield return crankCell + this.Rotation.FacingCell;

            // 左侧交互格
            yield return crankCell + this.Rotation.Rotated(RotationDirection.Counterclockwise).FacingCell;

            // 右侧交互格
            yield return crankCell + this.Rotation.Rotated(RotationDirection.Clockwise).FacingCell;
        }

        public new CompPowerPlant PowerComp => powerComp;

        public override void Print(SectionLayer layer)
        {
            // 回退到动态渲染
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // 渲染齿轮箱模型
            GodHandGeneratorController.RenderGearbox(drawLoc, this.Rotation, state.currentAngle);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref state.currentAngle, "currentAngle", 0f);
            Scribe_Values.Look(ref state.smoothedVelocity, "smoothedVelocity", 0f);
            Scribe_Values.Look(ref lastWorkTick, "lastWorkTick", -1);
        }
    }
}
