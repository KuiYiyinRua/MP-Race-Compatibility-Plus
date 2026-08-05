using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 神之助手地图组件
    public class MapComponent_GodAssistant : MapComponent
    {
        private List<GodHandTaskBase> tasks = new List<GodHandTaskBase>();
        public static Map currentMap;

        private static Pawn _godHandWorker;
        public static Pawn GodHandWorker
        {
            get
            {
                if (_godHandWorker == null || _godHandWorker.Destroyed)
                {
                    _godHandWorker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    _godHandWorker.Name = new NameTriple("", "GodHand.Assistant.Name".Translate(), "");

                    foreach (var skill in _godHandWorker.skills.skills)
                    {
                        skill.Level = 20;
                    }
                }
                return _godHandWorker;
            }
        }

        public MapComponent_GodAssistant(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            currentMap = map;

            if (tasks.Count == 0) return;

            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                var task = tasks[i];
                try
                {
                    task.Tick();
                }
                catch (Exception ex)
                {
                    Log.Error($"[GodHand] 任务更新错误 {ex}");
                    task.ForceEnd();
                }

                if (task.IsFinished) tasks.RemoveAt(i);
            }
        }

        public override void MapComponentUpdate()
        {
            if (tasks.Count == 0) return;
            float dt = Time.deltaTime * Find.TickManager.TickRateMultiplier;

            for (int i = 0; i < tasks.Count; i++)
            {
                try
                {
                    tasks[i].Update(dt);
                    tasks[i].Draw();
                }
                catch { }
            }
        }

        public void AddTask(GodHandTaskBase task) => tasks.Add(task);

        public List<GodHandCraftingTask> GetTasksFor(Building_WorkTable table)
        {
            return tasks.OfType<GodHandCraftingTask>().Where(t => t.Table == table).ToList();
        }

        public bool IsTableBusy(Building_WorkTable table)
        {
            return tasks.OfType<GodHandCraftingTask>().Any(t => t.Table == table);
        }

        public void RemoveTaskFor(Building_WorkTable table)
        {
            var targets = GetTasksFor(table);
            foreach (var t in targets)
            {
                t.ForceEnd();
                tasks.Remove(t);
            }
        }

        public void RemoveTaskFor(Pawn patient)
        {
            var targets = tasks.OfType<GodHandMedicalTask_Pawn>().Where(t => t.Patient == patient).ToList();
            foreach (var t in targets)
            {
                t.ForceEnd();
                tasks.Remove(t);
            }
        }

        public void RemoveTaskFor(Building_Bed bed)
        {
            var targets = tasks.OfType<GodHandMedicalTask_Bed>().Where(t => t.Bed == bed).ToList();
            foreach (var t in targets)
            {
                t.ForceEnd();
                tasks.Remove(t);
            }
        }

        public int GetCraftingTaskCount() => tasks.OfType<GodHandCraftingTask>().Count();

        public bool HasMedicalTaskFor(Pawn patient) => tasks.OfType<GodHandMedicalTask_Pawn>().Any(t => t.Patient == patient);

        public bool HasMedicalTaskFor(Building_Bed bed) => tasks.OfType<GodHandMedicalTask_Bed>().Any(t => t.Bed == bed);
    }
}
