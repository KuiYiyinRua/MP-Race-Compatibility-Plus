using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    public enum JudgmentMode
    {
        Catastrophic = 0,     // 神裁天罚
        SuddenDeathField = 1,  // 骤死流场
        StasisLock = 2,       // 永恒停滞
        Forgiveness = 3       // 宽恕模式
    }

    // 神之裁决控制器
    public static class JudgmentGodHandController
    {
        private const int DAMAGE_RADIUS = 16;
        private const float BASE_CRUSH_RADIUS = 5f;
        private const int SHOCKWAVE_EXPAND_TICKS = 30;
        private const int FALLING_DURATION_TICKS = 60;
        private const float MAX_HAND_SIZE = 22f;

        // 执行裁决逻辑
        public static void ExecuteJudgment(IntVec3 targetCell, Map map)
        {
            if (map == null || !targetCell.InBounds(map)) return;

            JudgmentMode mode = (JudgmentMode)GodHandModMain.Settings.currentJudgmentMode;
            switch (mode)
            {
                case JudgmentMode.Catastrophic:
                    ExecuteCatastrophic(targetCell, map);
                    break;
                case JudgmentMode.SuddenDeathField:
                    ExecuteSuddenDeathField(targetCell, map);
                    break;
                case JudgmentMode.StasisLock:
                    ExecuteStasisLock(targetCell, map);
                    break;
                case JudgmentMode.Forgiveness:
                    ExecuteForgiveness(targetCell, map);
                    break;
            }
        }

        // 标准降落天罚
        private static void ExecuteCatastrophic(IntVec3 targetCell, Map map)
        {
            ShowTargetMarker(targetCell, map);
            JudgmentStrike strike = new JudgmentStrike(targetCell, map, FALLING_DURATION_TICKS, JudgmentMode.Catastrophic);
            strike.StartStrike();
        }

        // 追踪骤死打击
        private static void ExecuteSuddenDeathField(IntVec3 targetCell, Map map)
        {
            FleckMaker.Static(targetCell, map, FleckDefOf.PsycastAreaEffect, 10f);
            float radius = (DAMAGE_RADIUS / 2f) * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;

            foreach (var t in GenRadial.RadialDistinctThingsAround(targetCell, map, radius, true).ToList())
                if (t is Pawn p) CommitSuddenDeath(p);

            var comp = map.GetComponent<MapComponent_GodJudgmentField>();
            comp?.RegisterSuddenDeathField(targetCell, radius, 600);
        }

        // 全属性停滞锁定
        private static void ExecuteStasisLock(IntVec3 targetCell, Map map)
        {
            FleckMaker.Static(targetCell, map, FleckDefOf.PsycastAreaEffect, 8f);
            float radius = (DAMAGE_RADIUS / 2f) * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;

            foreach (var t in GenRadial.RadialDistinctThingsAround(targetCell, map, radius, true).ToList())
            {
                if (t is Pawn p && !p.Dead)
                {
                    StasisLockManager.Instance?.Lock(p);
                    GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.JudgmentStasis"));
                    GodHandRimTalkIntegration.TryTriggerConversation(p, "GodHand.RimTalk.Prompt.JudgmentStasis");
                }
            }
        }

        // 宽恕模式 - 解除锁定
        private static void ExecuteForgiveness(IntVec3 targetCell, Map map)
        {
            FleckMaker.Static(targetCell, map, FleckDefOf.PsycastAreaEffect, 8f);
            // 绿色光环作为反馈
            FleckMaker.ThrowLightningGlow(targetCell.ToVector3Shifted(), map, 6f);
            float radius = (DAMAGE_RADIUS / 2f) * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;

            foreach (var t in GenRadial.RadialDistinctThingsAround(targetCell, map, radius, true).ToList())
            {
                if (t is Pawn p)
                {
                    StasisLockManager.Instance?.Unlock(p);
                }
            }
        }

        // 显示冲击波预警
        private static void ShowTargetMarker(IntVec3 pos, Map map)
        {
            float damageRadius = DAMAGE_RADIUS * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;
            FleckMaker.Static(pos.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, damageRadius * 2f);
        }

        // 冲击波爆发视觉
        private static void ShowImpactEffect(IntVec3 center, Map map)
        {
            float maxRadius = DAMAGE_RADIUS * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;
            // 主要闪光
            FleckMaker.ThrowExplosionCell(center, map, FleckDefOf.ExplosionFlash, new Color(1f, 0.1f, 0.1f));

            // 环形扩张冲击波
            FleckCreationData shockwave = FleckMaker.GetDataStatic(center.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, maxRadius * 2f);
            shockwave.instanceColor = new Color(1f, 0.9f, 0.8f, 0.6f);
            map.flecks.CreateFleck(shockwave);

            // 额外的细节电光
            for (int i = 0; i < 10; i++) FleckMaker.ThrowLightningGlow(center.ToVector3Shifted(), map, 5f);
        }

        // 裁决打击表现实体
        [StaticConstructorOnStartup]
        private class JudgmentStrike : Mote
        {
            private IntVec3 targetCell;
            private Map targetMap;
            private int ticksRemaining;
            private JudgmentMode mode;

            public JudgmentStrike(IntVec3 target, Map map, int duration, JudgmentMode mode)
            {
                targetCell = target;
                targetMap = map;
                ticksRemaining = duration;
                this.mode = mode;
                def = ThingDefOf.Mote_Text;
            }

            public void StartStrike()
            {
                if (!GodHandModMain.Settings.judgmentReviewMode)
                {
                    MoteGodJudgment mote = (MoteGodJudgment)ThingMaker.MakeThing(ThingDefOf_GodHand.Mote_GodJudgmentFalling);
                    mote.Setup(targetCell.ToVector3Shifted(), MAX_HAND_SIZE);
                    GenSpawn.Spawn(mote, targetCell, targetMap);
                }
                GenSpawn.Spawn(this, targetCell, targetMap);
            }

            protected override void DrawAt(Vector3 drawLoc, bool flip = false)
            {
                if (GodHandModMain.Settings.judgmentReviewMode) DrawHarmonyBeam(drawLoc);
            }

            private void DrawHarmonyBeam(Vector3 drawLoc)
            {
                int elapsedTicks = FALLING_DURATION_TICKS - ticksRemaining;
                if (elapsedTicks < 5)
                {
                    Vector3 beamStart = drawLoc + new Vector3(0f, 0f, 50f);
                    GenDraw.DrawLineBetween(drawLoc, beamStart, SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0.1f, 0.1f, 0.6f)), 2.5f);
                }
            }

            protected override void Tick()
            {
                ticksRemaining--;
                if (ticksRemaining == FALLING_DURATION_TICKS - MoteGodJudgment.DROP_TICKS) ExecuteImpact();
                if (ticksRemaining <= 0) Destroy();
            }

            private void ExecuteImpact()
            {
                if (targetMap == null) return;
                ShowImpactEffect(targetCell, targetMap);
                SoundDef bombSound = SoundDef.Named("Explosion_GiantBomb") ?? SoundDefOf.Thunder_OffMap;
                bombSound.PlayOneShot(new TargetInfo(targetCell, targetMap));

                HashSet<Thing> centerVictims = ApplyCenterDamage(targetCell, targetMap);
                new ShockwaveExpander(targetCell, targetMap, centerVictims).Start();
            }

            private HashSet<Thing> ApplyCenterDamage(IntVec3 center, Map map)
            {
                HashSet<Thing> victims = new HashSet<Thing>();
                float crushRadius = BASE_CRUSH_RADIUS * GodHandModMain.Settings.judgmentCrushRadiusMultiplier;
                foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, crushRadius, true))
                {
                    if (thing == null || thing.Destroyed || thing.def.category == ThingCategory.Item) continue;
                    victims.Add(thing);

                    if (posInCross(thing.Position, center, 1)) thing.TakeDamage(new DamageInfo(DamageDefOf.Crush, 9999f));
                    else if (thing is Pawn p) ApplyMultiPartDamage(p, 1000f * GodHandModMain.Settings.judgmentCrushDamageMultiplier);
                    else thing.TakeDamage(new DamageInfo(DamageDefOf.Crush, 500f * GodHandModMain.Settings.judgmentCrushDamageMultiplier));
                }
                return victims;
            }

            private bool posInCross(IntVec3 p, IntVec3 c, int r) => (p.x == c.x && Math.Abs(p.z - c.z) <= r) || (p.z == c.z && Math.Abs(p.x - c.x) <= r);

            private void ApplyMultiPartDamage(Pawn p, float dmg)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (p.Dead) break;
                    p.TakeDamage(new DamageInfo(DamageDefOf.Crush, dmg / 5f));
                }
            }
        }

        // 冲击波扩散处理其
        private class ShockwaveExpander : Mote
        {
            private IntVec3 center;
            private Map map;
            private int startTick;
            private float maxRadius;
            private HashSet<Thing> processed;

            public ShockwaveExpander(IntVec3 c, Map m, HashSet<Thing> p)
            {
                center = c; map = m; processed = p ?? new HashSet<Thing>();
                maxRadius = DAMAGE_RADIUS * GodHandModMain.Settings.judgmentShockwaveRadiusMultiplier;
                def = ThingDefOf.Mote_Text;
            }

            public void Start() { startTick = Find.TickManager.TicksGame; GenSpawn.Spawn(this, center, map); }

            protected override void Tick()
            {
                int elapsed = Find.TickManager.TicksGame - startTick;
                float curR = elapsed * (maxRadius / SHOCKWAVE_EXPAND_TICKS);

                foreach (var t in GenRadial.RadialDistinctThingsAround(center, map, curR, true))
                {
                    if (processed.Contains(t) || t.def.category == ThingCategory.Item) continue;
                    if (t is Pawn p)
                    {
                        p.TakeDamage(new DamageInfo(DamageDefOf.Crush, 150f));
                        if (!p.Dead && p.Spawned)
                        {
                            GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.JudgmentShockwave"));
                            GodHandRimTalkIntegration.TryTriggerConversation(p, "GodHand.RimTalk.Prompt.JudgmentShockwave");
                        }
                    }
                    else t.TakeDamage(new DamageInfo(DamageDefOf.Crush, 100f));
                    processed.Add(t);
                }
                if (elapsed >= SHOCKWAVE_EXPAND_TICKS) Destroy();
            }
        }

        // 执行底层即死斩杀
        public static void CommitSuddenDeath(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return;
            try
            {
                var lethalTags = new List<BodyPartTagDef> { BodyPartTagDefOf.ConsciousnessSource, BodyPartTagDefOf.BloodPumpingSource };
                var partsToKill = pawn.health.hediffSet.GetNotMissingParts()
                    .Where(part => part.def.tags != null && lethalTags.Any(tag => part.def.tags.Contains(tag))).ToList();

                foreach (var part in partsToKill) pawn.health.AddHediff(HediffDefOf.MissingBodyPart, part, null, null);
                if (!pawn.Dead) pawn.health.SetDead();
            }
            catch (Exception ex)
            {
                Log.Warning($"骤死打击报错 {ex.Message}");
                if (!pawn.Dead) pawn.Kill(null);
            }
        }
    }
}
