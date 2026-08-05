using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.AI;
using Verse.Sound;

namespace GodHandMod
{
    // 船只捕获属性
    public class CompProperties_PawnCaptureOnTouch : CompProperties
    {
        public CompProperties_PawnCaptureOnTouch()
        {
            this.compClass = typeof(CompPawnCaptureOnTouch);
        }
    }

    // 船只捕获逻辑
    public class CompPawnCaptureOnTouch : ThingComp
    {
        public static HashSet<Pawn> allCapturedPawns = new HashSet<Pawn>();
        public static Dictionary<Pawn, Building_EndRodGenerator> pawnToGenerator = new Dictionary<Pawn, Building_EndRodGenerator>();
        private List<Pawn> capturedPawns = new List<Pawn>();
        private Dictionary<Pawn, int> jobRetryCooldown = new Dictionary<Pawn, int>();
        private Building_EndRodGenerator ParentGenerator => parent as Building_EndRodGenerator;

        public static void RefreshAll()
        {
            foreach (var p in allCapturedPawns)
            {
                if (p != null && p.Spawned)
                {
                    p.health?.Notify_HediffChanged(null);
                }
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            foreach (var p in capturedPawns)
                allCapturedPawns.Add(p);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;

            // 清理失效捕获者
            for (int i = capturedPawns.Count - 1; i >= 0; i--)
            {
                Pawn p = capturedPawns[i];
                if (p == null || !p.Spawned || p.Dead || p.Map != parent.Map)
                {
                    if (p != null && !p.Dead)
                    {
                        var h = p.health.hediffSet.GetFirstHediffOfDef(GodHandDefOf.GodHand_IsActuallyABoat);
                        if (h != null) p.health.RemoveHediff(h);
                    }

                    if (p != null)
                    {
                        allCapturedPawns.Remove(p);
                        pawnToGenerator.Remove(p);
                        jobRetryCooldown.Remove(p);
                    }
                    capturedPawns.RemoveAt(i);
                }
            }

            if (ParentGenerator == null || !ParentGenerator.IsObserverRemoved)
            {
                IntVec3 boatCell = parent.Position + parent.Rotation.FacingCell * 2;

                // 性能优化每10tick
                if (parent.IsHashIntervalTick(10))
                {
                    // 扫描船对周围一圈 (3x3区域)
                    foreach (IntVec3 checkCell in CellRect.CenteredOn(boatCell, 1).Cells)
                    {
                        // 已捕获则跳过
                        if (capturedPawns.Count >= 1) break;

                        if (!checkCell.InBounds(parent.Map)) continue;

                        var pawns = checkCell.GetThingList(parent.Map).OfType<Pawn>().ToList();
                        foreach (var p in pawns)
                        {
                            // 仅捕获第一个符合条件的
                            if (p.RaceProps.Humanlike && !capturedPawns.Contains(p))
                            {
                                Capture(p);
                                break;
                            }
                        }
                    }
                }
            }

            // 维持捕获状态
            UpdateCapturedPawns();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;

            if (capturedPawns.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "GodHand.EndRod.ReleasePrisoners".Translate(),
                    defaultDesc = "GodHand.EndRod.ReleasePrisonersDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Release"),
                    action = () =>
                    {
                        ReleaseAll();
                        SoundDefOf.Click.PlayOneShotOnCamera(null);
                    }
                };
            }
        }

        private void Capture(Pawn pawn)
        {
            if (pawn == null) return;
            capturedPawns.Add(pawn);
            allCapturedPawns.Add(pawn);
            if (ParentGenerator != null) pawnToGenerator[pawn] = ParentGenerator;

            // 给 Pawn 添加专用 Job
            TryGiveJob(pawn);

            // 仅在未禁用不适宜内容时添加相关状态
            if (!GodHandModMain.Settings.disableNSFW)
            {
                // 添加进度标记
                if (!pawn.health.hediffSet.HasHediff(GodHandDefOf.GodHand_OrificeStretchingStatus))
                {
                    pawn.health.AddHediff(GodHandDefOf.GodHand_OrificeStretchingStatus);
                }
            }

            // 添加船只状态
            if (!pawn.health.hediffSet.HasHediff(GodHandDefOf.GodHand_IsActuallyABoat))
            {
                pawn.health.AddHediff(GodHandDefOf.GodHand_IsActuallyABoat);
            }

            Messages.Message("GodHand_PawnCapturedByMachine".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.CautionInput);
        }

        private void TryGiveJob(Pawn pawn)
        {
            // 避免重复任务
            if (pawn.CurJobDef == GodHandDefOf.GodHand_BeingFucked)
                return;

            // 冷却检查防死循环
            if (jobRetryCooldown.TryGetValue(pawn, out int cooldown) && Find.TickManager.TicksGame < cooldown)
                return;

            Job job = JobMaker.MakeJob(GodHandDefOf.GodHand_BeingFucked, parent);
            job.forceSleep = false;

            // 尝试强制覆盖任务
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                // 失败则冷却 60 ticks
                jobRetryCooldown[pawn] = Find.TickManager.TicksGame + 60;
            }
            else
            {
                // 成功则移除冷却
                jobRetryCooldown.Remove(pawn);
            }
        }

        private void UpdateCapturedPawns()
        {
            if (capturedPawns.Count == 0) return;

            // 获取船的实时位置
            Vector3 finalBoatPos = (ParentGenerator != null)
                ? ParentGenerator.GetPistonTipPosition()
                : parent.DrawPos;

            foreach (var p in capturedPawns)
            {
                // 补发丢失任务
                if (p.CurJobDef != GodHandDefOf.GodHand_BeingFucked)
                {
                    TryGiveJob(p);
                }

                // 调用兼容性钩子
                CompatibilityBridge.OnPawnCapturedTick?.Invoke(p);

                // 播放欲望图标
                if (p.IsHashIntervalTick(120) && !GodHandModMain.Settings.disableNSFW)
                {
                    if (CompatibilityBridge.ShowCustomIcon != null)
                        CompatibilityBridge.ShowCustomIcon(p);
                    else
                        FleckMaker.ThrowMetaIcon(p.Position, p.Map, FleckDefOf.Heart);
                }

                // 强制同步位置
                p.Position = finalBoatPos.ToIntVec3();
                p.Rotation = parent.Rotation;

                // 依赖姿势补丁
                // 自动调整姿态
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map);
            ReleaseAll();
        }

        private void ReleaseAll()
        {
            foreach (var p in capturedPawns)
            {
                if (p != null)
                {
                    var h = p.health.hediffSet.GetFirstHediffOfDef(GodHandDefOf.GodHand_IsActuallyABoat);
                    if (h != null) p.health.RemoveHediff(h);

                    // 强制结束 Job
                    p.jobs?.EndCurrentJob(JobCondition.InterruptForced);

                    GraphicTransformManager.Remove(p);
                    allCapturedPawns.Remove(p);
                    pawnToGenerator.Remove(p);
                    jobRetryCooldown.Remove(p);
                }
            }
            capturedPawns.Clear();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            ReleaseAll();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // 存读档支持
            Scribe_Collections.Look(ref capturedPawns, "capturedPawns", LookMode.Reference);
            if (capturedPawns == null) capturedPawns = new List<Pawn>();

            // 读档后初始化逻辑
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                foreach (var p in capturedPawns)
                {
                    if (p != null)
                    {
                        allCapturedPawns.Add(p);
                        // 重建同步表
                        if (ParentGenerator != null) pawnToGenerator[p] = ParentGenerator;
                    }
                }
            }
        }
    }
}
