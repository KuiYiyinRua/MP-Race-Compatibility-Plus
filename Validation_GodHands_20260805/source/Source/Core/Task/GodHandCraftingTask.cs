using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    public class GodHandCraftingTask : GodHandTaskBase
    {
        public Building_WorkTable Table { get; private set; }
        private Bill_Production bill;
        private Map map;

        private enum State { Idle, Searching, Vacuuming, Crafting, Finishing }
        private State curState = State.Idle;

        private List<Thing> ingredients = new List<Thing>();
        private List<MaterialVacuumTask> vacuumTasks = new List<MaterialVacuumTask>();

        private float workAmountTotal;
        private float craftTimer;
        private float orbPulse = 0f;
        private float currentVisualSize = 0f;

        public GodHandCraftingTask(Building_WorkTable table)
        {
            this.Table = table;
            this.map = table.Map;
        }

        public override void Tick()
        {
            if (IsFinished) return;
            if (Table == null || Table.Destroyed || !Table.Spawned)
            {
                ForceEnd();
                return;
            }

            switch (curState)
            {
                case State.Idle: UpdateIdle(); break;
                case State.Searching: UpdateSearching(); break;
                case State.Vacuuming: CheckVacuumingCompletion(); break;
                case State.Crafting: UpdateCrafting(); break;
                case State.Finishing: UpdateFinishing(); break;
            }
        }

        public override void Update(float deltaTime)
        {
            if (IsFinished) return;

            if (curState == State.Vacuuming)
            {
                for (int i = vacuumTasks.Count - 1; i >= 0; i--)
                    vacuumTasks[i].Update(deltaTime);
            }

            orbPulse += deltaTime;
            UpdateVisualSize(deltaTime);
        }

        private void UpdateVisualSize(float dt)
        {
            float target = 1.0f;
            if (curState == State.Vacuuming) target = 1.25f;
            else if (curState == State.Crafting) target = 1.5f;
            else if (curState == State.Finishing) target = 1.4f;

            currentVisualSize = Mathf.Lerp(currentVisualSize, target, dt * 3f);
        }

        private void UpdateIdle()
        {
            if (Table.BillStack == null) return;
            var nextBill = Table.BillStack.FirstShouldDoNow as Bill_Production;
            if (nextBill != null)
            {
                this.bill = nextBill;
                curState = State.Searching;
            }
        }

        private int lastSearchTick = -999;
        private const int SearchCooldownTicks = 300; // 5秒搜寻冷却

        private void UpdateSearching()
        {
            if (bill == null || !bill.ShouldDoNow())
            {
                ResetTask();
                return;
            }

            // 冷却期跳过
            if (Find.TickManager.TicksGame < lastSearchTick + SearchCooldownTicks)
            {
                return;
            }

            lastSearchTick = Find.TickManager.TicksGame;

            if (!GodAssistantController.TryFindIngredients(bill, Table, out var ingredientsMap))
            {
                ResetTask(); // 重置并受冷确限制
                return;
            }

            StartVacuuming(ingredientsMap);
        }

        private void StartVacuuming(List<Pair<Thing, IntVec3>> map)
        {
            ingredients.Clear();
            vacuumTasks.Clear();
            Vector3 target = Table.Position.ToVector3Shifted() + new Vector3(0, 1f, 0);

            foreach (var pair in map)
            {
                Thing t = pair.First;
                if (t.Spawned) t.DeSpawn();
                ingredients.Add(t);
                vacuumTasks.Add(new MaterialVacuumTask(t, pair.Second.ToVector3Shifted(), target));
            }

            workAmountTotal = bill.recipe.WorkAmountTotal(null);
            curState = State.Vacuuming;
        }

        private void CheckVacuumingCompletion()
        {
            if (vacuumTasks.All(t => t.IsFinished))
            {
                curState = State.Crafting;
                craftTimer = Mathf.Min(2f, workAmountTotal / 500f);
            }
        }

        private void UpdateCrafting()
        {
            craftTimer -= 1f / 60f;
            if (craftTimer <= 0f) curState = State.Finishing;
        }

        private void UpdateFinishing()
        {
            Pawn worker = MapComponent_GodAssistant.GodHandWorker;
            var products = GodAssistantController.MakeGodProducts(bill.recipe, worker, ingredients, Table);

            SpawnProducts(products);
            bill.Notify_IterationCompleted(worker, ingredients);

            foreach (var t in ingredients) if (!t.Destroyed) t.Destroy();
            ResetTask();
        }

        private void SpawnProducts(List<Thing> products)
        {
            Vector3 center = Table.Position.ToVector3Shifted() + new Vector3(0, 1f, 0);
            foreach (var p in products)
            {
                GenPlace.TryPlaceThing(p, Table.Position, map, ThingPlaceMode.Near);
                if (p.Spawned)
                {
                    map.GetComponent<MapComponent_GodAssistant>().AddTask(new GodHandProductPopTask(p, center));
                }
            }
        }

        private void ResetTask()
        {
            curState = State.Idle;
            ingredients.Clear();
            vacuumTasks.Clear();
            bill = null;
        }

        public override void ForceEnd()
        {
            base.ForceEnd();
            ReturnIngredients();
        }

        private void ReturnIngredients()
        {
            foreach (var t in ingredients)
            {
                if (t != null && !t.Destroyed && !t.Spawned)
                {
                    IntVec3 pos = (Table != null && Table.Spawned) ? Table.Position : map.Center;
                    GenPlace.TryPlaceThing(t, pos, map, ThingPlaceMode.Near);
                }
            }
        }

        public override void Draw()
        {
            // 根据组件绘制
            // 引用辅助类
            // 获取任务列表
            // 获取当前索引

            var comp = map.GetComponent<MapComponent_GodAssistant>();
            var allTasks = comp.GetTasksFor(Table);
            int index = allTasks.IndexOf(this);
            if (index < 0) return;

            DrawOrb(index, allTasks.Count);

            if (curState == State.Vacuuming)
            {
                Vector3 drawPos = GetOrbPosition(index, allTasks.Count);
                foreach (var t in vacuumTasks)
                {
                    t.TargetPos = drawPos;
                    t.Draw();
                }
            }
        }

        private void DrawOrb(int index, int count)
        {
            Vector3 pos = GetOrbPosition(index, count);
            float size = GetOrbSize(index) * Mathf.Max(currentVisualSize, 0.1f);

            // Pulse
            float time = (Find.TickManager.TicksGame / 60f) + orbPulse;
            size += Mathf.Sin(time * (index == 0 ? 3f : 5f)) * 0.1f;

            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.AngleAxis(time * (index == 0 ? 80f : 200f), Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, m, OrbMat, 0);
        }

        private Vector3 GetOrbPosition(int index, int count)
        {
            Vector3 center = Table.Position.ToVector3Shifted();
            center.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + 0.2f;
            float time = (Find.TickManager.TicksGame / 60f) + orbPulse;

            if (index == 0)
            {
                return center + new Vector3(0, Mathf.Sin(time) * 0.06f, 0);
            }

            // Orbit logic
            int subIndex = index - 1;
            int subCount = count - 1;
            float angle = (time * 60f); // Speed

            Vector3 axis = Vector3.up;
            if (subIndex % 3 == 0) axis = new Vector3(1f, 1f, 0.2f).normalized;
            else if (subIndex % 3 == 1) axis = new Vector3(-0.8f, 1f, 0.6f).normalized;
            else axis = new Vector3(0.4f, 1f, -0.9f).normalized;

            float startAngle = (360f / subCount) * subIndex;
            Vector3 startOffset = Quaternion.AngleAxis(startAngle, axis) * Vector3.forward * 0.9f;
            Vector3 offset = Quaternion.AngleAxis(angle, axis) * startOffset;

            return center + offset;
        }

        private float GetOrbSize(int index) => index == 0 ? 1.3f : 0.6f;
    }
}
