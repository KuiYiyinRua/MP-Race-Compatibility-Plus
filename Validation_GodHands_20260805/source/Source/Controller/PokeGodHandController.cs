using System;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace GodHandMod
{
    // 脑瓜崩控制器
    public class PokeGodHandController
    {
        private Pawn targetPawn;
        private Corpse targetCorpse;
        private IntVec3 startCell;
        private bool isInteracting;
        private float interactionStartTime;

        public bool IsInteractng => isInteracting;
        public bool IsAiming => isInteracting && (targetPawn != null || targetCorpse != null);

        // 开始Pawn互动
        public void StartInteraction(Map map, Pawn pawn, IntVec3 cell)
        {
            targetPawn = pawn;
            targetCorpse = null;
            startCell = cell;
            isInteracting = true;
            interactionStartTime = Time.time;
        }

        // 开始尸体互动
        public void StartInteraction(Map map, Corpse corpse, IntVec3 cell)
        {
            targetCorpse = corpse;
            targetPawn = null;
            startCell = cell;
            isInteracting = true;
            interactionStartTime = Time.time;
        }

        // 结束拖拽互动逻辑
        public void EndInteraction(Map map, IntVec3 endCell)
        {
            if (!isInteracting || (targetPawn == null && targetCorpse == null))
            {
                Reset();
                return;
            }

            float dragDist = (endCell - startCell).LengthHorizontal;
            if (dragDist < 1.5f || targetCorpse != null)
            {
                if (targetPawn != null) PokePawn(targetPawn);
                else if (targetCorpse != null) PokeCorpse(targetCorpse);
            }
            else if (targetPawn != null && GodHandModMain.Settings.pokeEnableFlick) PerformFlick(map, endCell);
            Reset();
        }

        public void Update()
        {
            if (isInteracting)
            {
                if (targetPawn != null && (targetPawn.Destroyed || targetPawn.Dead)) Reset();
                else if (targetCorpse != null && targetCorpse.Destroyed) Reset();
            }
        }

        // 绘制弹飞瞄准线
        public void DrawAimLine()
        {
            if (!IsAiming) return;
            IntVec3 currentCell = UI.MouseCell();
            float dist = (currentCell - startCell).LengthHorizontal;

            if (dist >= 1.5f)
            {
                Vector3 start = startCell.ToVector3Shifted();
                Vector3 end = currentCell.ToVector3Shifted();
                GenDraw.DrawLineBetween(start, end, SimpleColor.Red);

                Vector3 direction = (start - end).normalized;
                Vector3 flickEnd = start + direction * Mathf.Min(dist * 2f, 15f);
                GenDraw.DrawLineBetween(start, flickEnd, SimpleColor.Yellow);
                GenDraw.DrawCircleOutline(flickEnd, 0.5f, SimpleColor.Yellow);
            }
        }

        // 执行弹飞操作
        private void PerformFlick(Map map, IntVec3 endCell)
        {
            Vector3 startPos = startCell.ToVector3Shifted();
            Vector3 endPos = endCell.ToVector3Shifted();
            Vector3 direction = (startPos - endPos).normalized;
            float dragDist = Vector3.Distance(startPos, endPos);

            int maxDist = GodHandModMain.Settings.maxThrowCells;
            float force = Mathf.Clamp(dragDist * 2f, 2f, (float)maxDist);

            IntVec3 landingCell = startCell;
            for (float d = 0.5f; d <= force; d += 0.5f)
            {
                IntVec3 cell = (startPos + direction * d).ToIntVec3();
                if (!cell.InBounds(map) || cell.Impassable(map)) break;
                landingCell = cell;
            }
            GodHandMemoryTracker.AddMemory(targetPawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Flicked"));
            GodHandRimTalkIntegration.TryTriggerConversation(targetPawn, "GodHand.RimTalk.Prompt.Thrown");
            FlickPawn(map, targetPawn, landingCell);
        }

        // 生成飞行实体
        private void FlickPawn(Map map, Pawn pawn, IntVec3 landingCell)
        {
            if (pawn.Spawned) pawn.DeSpawn(DestroyMode.WillReplace);

            GodHandPawnFlyer flyer = (GodHandPawnFlyer)ThingMaker.MakeThing(GodHandDefOf.GodHand_PawnFlyer);
            flyer.InitializeFlightParams(startCell.ToVector3Shifted(), landingCell, startCell.DistanceTo(landingCell));
            flyer.SetPendingDamage(0f);

            if (flyer.GetDirectlyHeldThings().TryAdd(pawn))
            {
                GenSpawn.Spawn(flyer, startCell, map);
                Messages.Message("GodHand.Poke.Flicked".Translate(pawn.LabelShort, GodHandModMain.Settings.PlayerNameTranslated), new TargetInfo(landingCell, map), MessageTypeDefOf.NeutralEvent);
            }
            else flyer.Destroy();
        }

        private void Reset()
        {
            isInteracting = false;
            targetPawn = null;
        }

        private IntVec3 ClampToMap(IntVec3 cell, Map map)
        {
            cell.x = Mathf.Clamp(cell.x, 0, map.Size.x - 1);
            cell.z = Mathf.Clamp(cell.z, 0, map.Size.z - 1);
            return cell;
        }

        // 复活尸体逻辑
        private void PokeCorpse(Corpse corpse)
        {
            if (corpse == null || corpse.Destroyed) return;
            Map map = corpse.Map;
            if (map == null) return;

            if (!GodHandModMain.Settings.pokeEnableResurrection)
            {
                Messages.Message("GodHand.Poke.ResurrectionDisabled".Translate(), corpse, MessageTypeDefOf.RejectInput);
                return;
            }

            if (Rand.Chance(0.5f))
            {
                Pawn innerPawn = corpse.InnerPawn;
                if (innerPawn != null)
                {
                    ResurrectionUtility.TryResurrect(innerPawn);
                    HediffDef severeDef = DefDatabase<HediffDef>.GetNamed("GodHand_SevereHeadImpact", false);
                    if (severeDef != null)
                    {
                        BodyPartRecord head = innerPawn.health?.hediffSet?.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Head);
                        innerPawn.health.AddHediff(severeDef, head);
                    }
                    Messages.Message("GodHand.Poke.Resurrected".Translate(innerPawn.LabelShort), innerPawn, MessageTypeDefOf.PositiveEvent);
                    MoteMaker.ThrowText(innerPawn.DrawPos + new Vector3(0, 0, 0.5f), map, "GodHand.Poke.ResurrectedMote".Translate(), 3.0f);
                    GodHandMemoryTracker.AddMemory(innerPawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Resurrected"));
                }
            }
            else
            {
                Messages.Message("GodHand.Poke.ResurrectFailed".Translate(corpse.LabelShort), corpse, MessageTypeDefOf.NeutralEvent);
                MoteMaker.ThrowText(corpse.DrawPos + new Vector3(0, 0, 0.5f), map, "GodHand.Poke.FailedMote".Translate(), 3.0f);
            }
            SoundDefOf.Pawn_Melee_Punch_HitPawn?.PlayOneShot(new TargetInfo(corpse.Position, map));
            FleckMaker.ThrowDustPuffThick(corpse.DrawPos, map, 1.0f, new Color(0.8f, 0.8f, 0.8f));
        }

        // 脑瓜崩单体逻辑
        private void PokePawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed) return;
            Map map = pawn.Map;
            if (map == null) return;

            BodyPartRecord head = pawn.health?.hediffSet?.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Head);
            if (head != null)
            {
                GodHandMemoryTracker.AddMemory(pawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Poked"));
                HediffDef pokeDef = DefDatabase<HediffDef>.GetNamed("GodHand_PokedHead", false);
                if (pokeDef != null)
                {
                    pawn.health.AddHediff(pokeDef, head);
                    Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(pokeDef);
                    if (hediff != null)
                    {
                        hediff.Severity += 0.2f;
                        if (pawn.Downed || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))
                            GodHandRimTalkIntegration.TryTriggerConversation(pawn, "GodHand.RimTalk.Prompt.Poked");
                    }
                }
            }

            pawn.stances.stunner.StunFor(60, null, false, true);
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);

            // 打断精神崩溃
            if (GodHandModMain.Settings.pokeEnableMentalBreakInterrupt && pawn.InMentalState && Rand.Chance(0.8f))
            {
                pawn.mindState.mentalStateHandler.Reset();
                Messages.Message("GodHand.Poke.MentalBreakInterrupted".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.PositiveEvent);
                GodHandMemoryTracker.AddMemory(pawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.PokedOut"));
            }

            if (!pawn.Awake()) RestUtility.WakeUp(pawn);

            // 获得意外灵感
            if (GodHandModMain.Settings.pokeEnableInspiration && pawn.IsColonist && Rand.Chance(0.2f))
            {
                InspirationDef randomInspiration = DefDatabase<InspirationDef>.AllDefsListForReading.Where(x => x.Worker.InspirationCanOccur(pawn)).RandomElementWithFallback();
                if (randomInspiration != null)
                {
                    pawn.mindState.inspirationHandler.TryStartInspiration(randomInspiration, "GodHand.Poke.Epiphany".Translate());
                    Messages.Message("GodHand.Poke.EpiphanyMessage".Translate(pawn.LabelShort, randomInspiration.LabelCap), pawn, MessageTypeDefOf.PositiveEvent);
                    MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0, 0, 0.8f), map, "GodHand.Poke.EpiphanyMote".Translate(), 4.0f);
                    GodHandMemoryTracker.AddMemory(pawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Epiphany", randomInspiration.label));
                }
            }
            MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0, 0, 0.5f), map, "GodHand.Poke.Mote".Translate(), 3.0f);
            GodHandRimTalkIntegration.TryTriggerConversation(pawn, "GodHand.RimTalk.Prompt.Poked");
        }
    }
}
