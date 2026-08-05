using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    public static partial class CleaningGodHandController
    {
        // 区域治疗循环
        private static void HealPawnsAt(Map map, IntVec3 cell)
        {
            try
            {
                foreach (Thing t in cell.GetThingList(map))
                {
                    if (t is Pawn p && !p.Dead) TryHealPawn(p);
                }
            }
            catch (Exception e) { Log.Error($"[清洁] 治疗错误 {e.Message}"); }
        }

        // 尝试执行治疗逻辑
        private static void TryHealPawn(Pawn pawn)
        {
            if (!ShouldProcess(pawn)) return;

            float amount = GodHandModMain.Settings.cleaningHealAmount;
            if (amount <= 0 || pawn.health == null) return;

            HealInjuries(pawn, amount);
            CureAddictions(pawn);

            if (GodHandModMain.Settings.cleaningEnableTending) TendAllInjuries(pawn);
            if (GodHandModMain.Settings.cleaningEnableRegrowth) RegenerateMissingParts(pawn);

            if (Rand.Chance(0.05f))
                GodHandMemoryTracker.AddMemory(pawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.BeingHealed"));
        }

        // 修复伤害
        private static void HealInjuries(Pawn pawn, float totalAmount)
        {
            List<Hediff_Injury> injuries = new List<Hediff_Injury>();
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury i)
                {
                    if (GodHandModMain.Settings.cleaningRemoveScars || !i.IsPermanent()) injuries.Add(i);
                }
            }
            if (injuries.Count == 0) return;
            injuries.Sort((a, b) => a.Severity.CompareTo(b.Severity));

            foreach (var injury in injuries)
            {
                if (totalAmount <= 0) break;
                float heal = Mathf.Min(injury.Severity, totalAmount);
                injury.Heal(heal);
                totalAmount -= heal;

                if (injury.Severity <= 0.001f)
                {
                    GodHandMemoryTracker.AddMemory(pawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.Healed"));
                    GodHandRimTalkIntegration.TryTriggerConversation(pawn, "GodHand.RimTalk.Prompt.Healed");
                }
            }
        }

        // 清除成瘾
        private static void CureAddictions(Pawn pawn)
        {
            if (!GodHandModMain.Settings.cleaningCureAddiction) return;

            List<Hediff> addictions = new List<Hediff>();
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Addiction || h is Hediff_ChemicalDependency) addictions.Add(h);
            }

            foreach (var a in addictions)
            {
                pawn.health.RemoveHediff(a);
                GodHandMemoryTracker.AddMemory(pawn, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.AddictionCured"));
                GodHandRimTalkIntegration.TryTriggerConversation(pawn, "GodHand.RimTalk.Prompt.Healed");
            }
        }

        // 全自动包扎
        private static void TendAllInjuries(Pawn pawn)
        {
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury i && !i.IsPermanent()) i.Tended(1.0f, 1.0f, 1);
            }
        }

        // 缺失部位再生
        private static void RegenerateMissingParts(Pawn pawn)
        {
            if (pawn.health == null) return;
            List<Hediff_MissingPart> missing = new List<Hediff_MissingPart>();
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_MissingPart m) missing.Add(m);
            }
            foreach (var m in missing)
            {
                pawn.health.RemoveHediff(m);
                RemoveScarsOnPart(pawn, m.Part);
            }
        }

        // 移除局部疤痕
        private static void RemoveScarsOnPart(Pawn pawn, BodyPartRecord part)
        {
            if (part == null) return;
            List<Hediff> scars = new List<Hediff>();
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h.Part == part && h.IsPermanent()) scars.Add(h);
            }
            foreach (var s in scars) pawn.health.RemoveHediff(s);
        }

        // 食物与尸体腐烂回溯
        private static void ProcessFoodAndCorpsesAt(Map map, IntVec3 cell)
        {
            foreach (Thing t in cell.GetThingList(map))
            {
                if (t.def.IsIngestible) RewindRot(t);
                if (t is Corpse c) ProcessCorpse(c);
            }
        }

        private static void RewindRot(Thing t)
        {
            var rottable = t.TryGetComp<CompRottable>();
            if (rottable != null) rottable.RotProgress = Mathf.Max(0, rottable.RotProgress - 2500f);
        }

        private static void ProcessCorpse(Corpse corpse)
        {
            if (corpse == null || corpse.Destroyed || !ShouldProcess(corpse)) return;
            var rottable = corpse.TryGetComp<CompRottable>();
            if (rottable != null && (rottable.RotProgress > 0 || rottable.Stage != RotStage.Fresh)) rottable.RotProgress = 0;
        }
    }
}
