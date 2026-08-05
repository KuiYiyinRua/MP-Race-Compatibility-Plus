using System;
using System.Linq;
using System.Collections.Generic;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace GodHandMod
{
    // 爱抚控制器
    [StaticConstructorOnStartup]
    public class CaressGodHandController
    {
        private static List<string> originalLabels = new List<string>();
        private static List<string> originalDescriptions = new List<string>();
        private static bool originalSaved = false;

        static CaressGodHandController()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                UpdateThoughtDefTranslations();
            });
        }

        // 抚摸物体
        public static void CaressPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed) return;

            // 人形检查
            if (!pawn.RaceProps.Humanlike)
            {
                Messages.Message("CaressGodHand_NotHumanlike".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput);
                return;
            }

            // 效果处理
            SoundDefOf.Click.PlayOneShotOnCamera();
            ShowHeadPatAnimation(pawn);
            ApplyMoodBuff(pawn);

            // 记忆与对话
            GodHandMemoryTracker.AddMemory(pawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Caressed"));
            GodHandRimTalkIntegration.TryTriggerConversation(pawn, "GodHand.RimTalk.Prompt.Caressed");

            // 提示消息
            if (!pawn.IsPrisonerOfColony && !pawn.HostileTo(Faction.OfPlayer))
                Messages.Message("CaressGodHand_Caressed".Translate(pawn.LabelShort, GodHandModMain.Settings.PlayerNameTranslated), pawn, MessageTypeDefOf.PositiveEvent);

            // 特殊逻辑
            if (pawn.IsPrisonerOfColony && GodHandModMain.Settings.caressEnablePrisonerResistanceReduction)
                ReducePrisonerResistance(pawn);
            else if (pawn.HostileTo(Faction.OfPlayer) && GodHandModMain.Settings.caressEnableEnemySurrender)
                TrySurrenderEnemy(pawn);
        }

        // 播摸头动画
        private static void ShowHeadPatAnimation(Pawn pawn)
        {
            try
            {
                if (ThingDefOf_GodHand.Mote_HeadPat == null) return;
                MoteHeadPat mote = (MoteHeadPat)ThingMaker.MakeThing(ThingDefOf_GodHand.Mote_HeadPat);
                mote.Scale = 0.75f;
                mote.Attach(pawn);
                GenSpawn.Spawn(mote, pawn.Position, pawn.Map);
            }
            catch (Exception e)
            {
                Log.Error($"[爱抚] 动画显示失败 {e.Message}");
            }
        }

        // 应用心情加成
        private static void ApplyMoodBuff(Pawn pawn)
        {
            try
            {
                if (pawn.needs?.mood?.thoughts?.memories == null) return;
                ThoughtDef thoughtDef = ThoughtDefOf_GodHand.CaressedByGodHand;
                if (thoughtDef == null) return;

                int maxStages = GodHandModMain.Settings.caressMaxStages;
                UpdateThoughtDefTranslations(); // 确保阶段和文本始终为最新

                var memoryHandler = pawn.needs.mood.thoughts.memories;
                Thought_Memory existing = memoryHandler.Memories.FirstOrDefault(m => m.def == thoughtDef);

                if (existing == null) memoryHandler.TryGainMemory(thoughtDef);
                else
                {
                    int nextStage = Mathf.Min(existing.CurStageIndex + 1, maxStages - 1);
                    Thought_Memory newThought = (Thought_Memory)ThoughtMaker.MakeThought(thoughtDef);
                    newThought.pawn = pawn;
                    newThought.SetForcedStage(nextStage);
                    memoryHandler.RemoveMemory(existing);
                    memoryHandler.TryGainMemory(newThought);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[爱抚] 心情应用失败 {e.Message}");
            }
        }

        // 同步心情文本占位符
        public static void UpdateThoughtDefTranslations()
        {
            var def = ThoughtDefOf_GodHand.CaressedByGodHand;
            if (def == null || def.stages == null) return;

            if (!originalSaved)
            {
                foreach (var s in def.stages)
                {
                    originalLabels.Add(s.label);
                    originalDescriptions.Add(s.description);
                }
                originalSaved = true;
            }

            int maxStages = GodHandModMain.Settings.caressMaxStages;
            int moodPerStage = GodHandModMain.Settings.caressMoodBonus;
            string playerName = GodHandModMain.Settings.PlayerNameTranslated;

            while (def.stages.Count < maxStages)
            {
                def.stages.Add(new ThoughtStage
                {
                    label = "",
                    description = "",
                    baseMoodEffect = 0
                });
            }

            for (int i = 0; i < def.stages.Count; i++)
            {
                string rawLabel, rawDesc;
                if (i < originalLabels.Count)
                {
                    rawLabel = originalLabels[i];
                    rawDesc = originalDescriptions[i];
                }
                else
                {
                    rawLabel = "GodHand.Mood.Label".Translate(i + 1, "{1}");
                    rawDesc = "GodHand.Mood.Desc".Translate(i + 1, "{1}");
                }

                if (!string.IsNullOrEmpty(rawLabel))
                    def.stages[i].label = rawLabel.Replace("{1}", playerName).Replace("{0}", playerName);

                if (!string.IsNullOrEmpty(rawDesc))
                    def.stages[i].description = rawDesc.Replace("{1}", playerName).Replace("{0}", playerName);

                if (i < maxStages)
                {
                    def.stages[i].baseMoodEffect = (i + 1) * moodPerStage;
                }
            }
        }

        // 降低囚犯抗性
        private static void ReducePrisonerResistance(Pawn prisoner)
        {
            if (prisoner.guest == null) return;
            float reduction = GodHandModMain.Settings.caressPrisonerResistanceReduction;
            prisoner.guest.resistance = Mathf.Max(0f, prisoner.guest.resistance - reduction);

            // 尝试消除追随死忠
            if (GodHandModMain.Settings.caressEnableRemoveLoyalty && !prisoner.guest.Recruitable && Rand.Chance(GodHandModMain.Settings.caressRemoveLoyaltyChance))
            {
                prisoner.guest.Recruitable = true;
                Messages.Message("GodHand.Caress.NoLongerLoyal".Translate(prisoner.LabelCap), prisoner, MessageTypeDefOf.PositiveEvent);
                GodHandMemoryTracker.AddMemory(prisoner, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.NoLongerLoyal"));
            }
            GodHandMemoryTracker.AddMemory(prisoner, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.ResistanceReduced"));
        }

        // 尝试劝降敌对目标
        private static void TrySurrenderEnemy(Pawn enemy)
        {
            if (!enemy.RaceProps.Humanlike) return;
            if (Rand.Chance(GodHandModMain.Settings.caressEnemySurrenderChance))
            {
                if (enemy.Faction != null) enemy.SetFaction(null);
                if (enemy.guest == null) enemy.guest = new Pawn_GuestTracker(enemy);
                enemy.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);

                HediffDef surrenderDef = DefDatabase<HediffDef>.GetNamed("GodHand_ForcedDowned", false);
                if (surrenderDef != null) enemy.health.AddHediff(surrenderDef);

                enemy.mindState?.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee);
                Messages.Message("GodHand.Caress.Surrender".Translate(enemy.LabelCap), enemy, MessageTypeDefOf.PositiveEvent);
                GodHandMemoryTracker.AddMemory(enemy, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Surrendered"));
            }
        }
    }
}
