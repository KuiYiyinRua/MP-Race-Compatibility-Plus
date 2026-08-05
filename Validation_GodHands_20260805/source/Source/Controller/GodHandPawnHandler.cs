using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;

namespace GodHandMod
{
    // Pawn抓取逻辑处理器
    public class GodHandPawnHandler
    {
        private readonly GodHandController core;
        private readonly ThrowCalculator throwCalculator;
        private static readonly FieldInfo tweenedPosField = typeof(PawnTweener).GetField("tweenedPos", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo lastTickSpringPosField = typeof(PawnTweener).GetField("lastTickSpringPos", BindingFlags.NonPublic | BindingFlags.Instance);

        private float shakeAccumulator;
        private float lastShakeTime;
        private Vector3 lastMousePos;

        public GodHandPawnHandler(GodHandController core)
        {
            this.core = core;
            this.throwCalculator = new ThrowCalculator();
        }

        public void OnGrabbed(Pawn p)
        {
            if (p == null) return;
            p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            p.pather?.StopDead();
            p.stances?.CancelBusyStanceHard();

            // 保持静止动作
            Job waitJob = JobMaker.MakeJob(JobDefOf.Wait, 99999);
            p.jobs?.StartJob(waitJob, JobCondition.InterruptForced);

            if (GodHandModMain.Settings.playMemeSound)
                SoundDefOf_GodHand.MEME?.PlayOneShot(new TargetInfo(p.Position, p.Map));

            GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Grabbed"));
            GodHandRimTalkIntegration.TryTriggerConversation(p, "GodHand.RimTalk.Prompt.Grabbed");
        }

        public void OnStart(IntVec3 cell)
        {
            lastMousePos = cell.ToVector3Shifted();
            shakeAccumulator = 0;
            lastShakeTime = Time.time;
        }

        public void Update(Map map, Vector3 mousePos, float time)
        {
            IntVec3 cell = mousePos.ToIntVec3();
            if (!cell.InBounds(map)) return;

            foreach (var p in core.GrabbedPawns)
            {
                core.GrabOffsets.TryGetValue(p, out Vector3 offset);
                Vector3 targetPos = mousePos + offset;
                IntVec3 targetCell = targetPos.ToIntVec3();
                if (targetCell.InBounds(map) && p.Position != targetCell) p.Position = targetCell;
                UpdateTweener(p, targetPos);
            }

            UpdateShake(mousePos, time);
        }

        private void UpdateTweener(Pawn p, Vector3 pos)
        {
            if (p.Drawer?.tweener == null || tweenedPosField == null) return;
            tweenedPosField.SetValue(p.Drawer.tweener, pos);
            lastTickSpringPosField?.SetValue(p.Drawer.tweener, pos);
        }

        private void UpdateShake(Vector3 pos, float time)
        {
            if (!GodHandModMain.Settings.godHandEnableShake) return;
            float dt = time - lastShakeTime;
            if (dt < 0.01f) return;

            float speed = (pos - lastMousePos).magnitude / dt;
            if (speed > 10f) shakeAccumulator += speed * dt;
            shakeAccumulator = Mathf.Max(0, shakeAccumulator - 30f * dt);

            if (shakeAccumulator > 25f && Rand.Chance(0.05f)) TriggerShakeEffects();

            lastMousePos = pos;
            lastShakeTime = time;
        }

        private void TriggerShakeEffects()
        {
            foreach (var p in core.GrabbedPawns)
            {
                if (Rand.Chance(0.5f))
                {
                    p.jobs?.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced);
                    GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Vomited"));
                }
                else p.stances?.stunner.StunFor(60, null);
            }
            shakeAccumulator = 0;
        }

        public void Release(Map map, IntVec3 cell, float time)
        {
            bool enableThrow = GodHandModMain.Settings.enableThrow;
            foreach (var p in core.GrabbedPawns)
            {
                core.GrabOffsets.TryGetValue(p, out Vector3 offset);
                IntVec3 targetCell = (UI.MouseMapPosition() + offset).ToIntVec3();
                if (!targetCell.InBounds(map)) { HandleExit(map, p, targetCell); continue; }

                if (enableThrow)
                {
                    var res = throwCalculator.CalculateThrow(core.MouseTrajectory, map, p, core.GrabStartCell, targetCell);
                    if (res.ShouldThrow) { PerformThrow(map, p, res); continue; }
                }

                if (p.Spawned) p.Notify_Teleported(false, true);
                p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        private void HandleExit(Map map, Pawn p, IntVec3 cell)
        {
            p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            bool isPlayer = p.Faction == Faction.OfPlayer || p.IsColonist;
            Rot4 dir = GrabbingUtils.GetDirection(map, cell);
            if (isPlayer)
            {
                CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan(p, dir);
                GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.ThrownToCaravan"));
            }
            else
            {
                p.ExitMap(false, dir);
                GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.ThrownOutOfMap"));
            }
        }

        private void PerformThrow(Map map, Pawn p, ThrowResult res)
        {
            GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Thrown"));
            GodHandRimTalkIntegration.TryTriggerConversation(p, "GodHand.RimTalk.Prompt.Thrown");

            GodHandPawnFlyer flyer = (GodHandPawnFlyer)ThingMaker.MakeThing(GodHandDefOf.GodHand_PawnFlyer);
            flyer.InitializeFlightParams(p.TrueCenter(), res.LandingCell, res.Distance);
            flyer.SetPendingDamage(res.Damage);

            IntVec3 oldPos = p.Position;
            if (p.Spawned) p.DeSpawn();
            if (flyer.GetDirectlyHeldThings().TryAdd(p)) GenSpawn.Spawn(flyer, oldPos, map);
        }

        public void ForceClear() { shakeAccumulator = 0; }
    }
}
