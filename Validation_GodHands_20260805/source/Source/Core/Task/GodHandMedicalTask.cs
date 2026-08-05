using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace GodHandMod
{
    public abstract class GodHandMedicalTask : GodHandTaskBase
    {
        public Pawn Patient;
        protected Map map;
        protected float orbPulse = 0f;
        protected float currentVisualSize = 0f;

        protected List<MaterialVacuumTask> vacuumTasks = new List<MaterialVacuumTask>();

        public enum State { Idle, Scanning, Vacuuming, Tending, Surgery, Finishing, WaitingForQuiz }
        public State curState = State.Idle;

        protected float workTimer = 0f;
        protected bool quizPassed = false;
        protected bool quizWindowOpened = false;

        protected Thing targetedMedicine;
        protected Bill_Medical targetedBill;
        protected List<Thing> currentSurgeryIngredients = new List<Thing>();
        protected int scanTimer = 0;

        public static Material OrbMatBlue => GodHandResources.OrbMatBlue;

        public GodHandMedicalTask(Map map) => this.map = map;

        protected virtual void OnMedicalProcessComplete() { }
        protected virtual void OnNothingToDo() { }

        protected void TickMedicalLogic()
        {
            switch (curState)
            {
                case State.Scanning: UpdateScanning(); break;
                case State.WaitingForQuiz: UpdateQuiz(); break;
                case State.Vacuuming: UpdateVacuuming(); break;
                case State.Tending: UpdateTending(); break;
                case State.Surgery: UpdateSurgery(); break;
                case State.Finishing: OnMedicalProcessComplete(); break;
            }
        }

        private void UpdateScanning()
        {
            if (scanTimer > 0) { scanTimer--; return; }
            scanTimer = 60;

            if (Patient.health.HasHediffsNeedingTend())
            {
                StartTending();
            }
            else if (Patient.BillStack?.FirstShouldDoNow is Bill_Medical bill)
            {
                StartSurgery(bill);
            }
            else
            {
                OnNothingToDo();
            }
        }

        private void StartTending()
        {
            targetedMedicine = TryFindBestMedicine(Patient);
            if (targetedMedicine != null)
            {
                StartVacuuming(new List<Thing> { targetedMedicine });
                curState = State.Vacuuming;
            }
            else
            {
                curState = State.Tending;
                workTimer = 1.0f;
            }
        }

        private void StartSurgery(Bill_Medical bill)
        {
            if (TryFindSurgeryIngredients(bill, Patient, out var ingredients))
            {
                targetedBill = bill;
                currentSurgeryIngredients = ingredients;
                StartVacuuming(ingredients);

                if (GodHandModMain.Settings.enableSurgeryQuiz)
                {
                    curState = State.WaitingForQuiz;
                    quizPassed = false;
                    quizWindowOpened = false;
                }
                else
                {
                    curState = State.Vacuuming;
                }
            }
            else
            {
                OnNothingToDo();
            }
        }

        private void StartVacuuming(List<Thing> things)
        {
            vacuumTasks.Clear();
            Vector3 target = Patient.DrawPos + new Vector3(0, 1, 0);
            foreach (var t in things)
            {
                if (t.Spawned) t.DeSpawn();
                vacuumTasks.Add(new MaterialVacuumTask(t, t.Position, target));
            }
        }

        private void UpdateQuiz()
        {
            if (!quizWindowOpened)
            {
                quizWindowOpened = true;
                Find.WindowStack.Add(new Window_GodHandQuiz(
                    onCorrect: () => { quizPassed = true; },
                    onIncorrect: () => { ForceEnd(); }
                ));
            }
            if (quizPassed) curState = State.Vacuuming;
        }

        private void UpdateVacuuming()
        {
            if (vacuumTasks.All(t => t.IsFinished))
            {
                if (targetedBill != null)
                {
                    curState = State.Surgery;
                    workTimer = 2.0f;
                }
                else
                {
                    curState = State.Tending;
                    workTimer = 0.5f;
                }
            }
        }

        private void UpdateTending()
        {
            workTimer -= 1f / 60f;
            if (workTimer <= 0)
            {
                DoTendType(Patient, targetedMedicine);
                targetedMedicine = null;
                vacuumTasks.Clear();
                curState = State.Finishing;
            }
        }

        private void UpdateSurgery()
        {
            workTimer -= 1f / 60f;
            if (workTimer <= 0)
            {
                if (targetedBill != null)
                {
                    DoSurgery(Patient, targetedBill);
                    targetedBill = null;
                    currentSurgeryIngredients.Clear();
                }
                vacuumTasks.Clear();
                curState = State.Finishing;
            }
        }

        protected Thing TryFindBestMedicine(Pawn patient)
        {
            // 简化备份逻辑
            // 严格复制逻辑
            if (patient.playerSettings == null) return null;
            var care = patient.playerSettings.medCare;
            if (!care.AllowsMedicine(ThingDefOf.MedicineHerbal)) return null; // 药物检查

            // 完整检查
            Thing best = null;
            float bestPotency = -1f;

            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine))
            {
                if (t.IsForbidden(Faction.OfPlayer) || !t.Spawned || t.Position.Fogged(map)) continue;
                if (!care.AllowsMedicine(t.def)) continue;

                float potency = t.def.GetStatValueAbstract(StatDefOf.MedicalPotency);
                if (potency > bestPotency)
                {
                    bestPotency = potency;
                    best = t;
                }
                else if (potency == bestPotency)
                {
                    if (best == null || t.Position.DistanceToSquared(patient.Position) < best.Position.DistanceToSquared(patient.Position))
                        best = t;
                }
            }
            return best?.SplitOff(1);
        }

        protected bool TryFindSurgeryIngredients(Bill_Medical bill, Pawn patient, out List<Thing> ingredients)
        {
            ingredients = new List<Thing>();
            if (bill.recipe.ingredients.NullOrEmpty()) return true;

            var all = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);

            foreach (var ingCount in bill.recipe.ingredients)
            {
                float needed = ingCount.GetBaseCount();
                var candidates = all
                    .Where(t => t.Spawned && !t.Position.Fogged(map) && !t.IsForbidden(Faction.OfPlayer) && ingCount.filter.Allows(t))
                    .OrderBy(t => t.Position.DistanceToSquared(patient.Position));

                float found = 0;
                foreach (var t in candidates)
                {
                    int take = (int)Math.Min(needed - found, t.stackCount);
                    if (take > 0)
                    {
                        ingredients.Add(t.SplitOff(take));
                        found += take;
                    }
                    if (found >= needed) break;
                }
                if (found < needed) return false;
            }
            return true;
        }

        protected void DoTendType(Pawn patient, Thing medicine)
        {
            if (patient == null || patient.Dead) return;
            float quality = 0.7f * 1.2f;

            if (medicine != null)
            {
                quality = medicine.def.GetStatValueAbstract(StatDefOf.MedicalQualityMax) * 1.2f;
                medicine.Destroy();
            }

            foreach (var h in patient.health.hediffSet.GetHediffsTendable())
            {
                h.Tended(quality, 1.0f, 0);
            }

            SoundDefOf.TechMedicineUsed.PlayOneShot(patient);
            MoteMaker.ThrowText(patient.DrawPos, patient.Map, "God Hand Tend: " + quality.ToStringPercent());
        }

        protected void DoSurgery(Pawn patient, Bill_Medical bill)
        {
            if (patient == null || bill == null) return;

            if (patient.RaceProps.IsFlesh && !patient.health.hediffSet.HasHediff(HediffDefOf.Anesthetic))
                HealthUtility.TryAnesthetize(patient);

            SoundDefOf.TechMedicineUsed.PlayOneShot(patient);
            Pawn worker = MapComponent_GodAssistant.GodHandWorker;

            try
            {
                bill.Notify_IterationCompleted(worker, currentSurgeryIngredients);
            }
            catch (Exception ex)
            {
                Log.Error($"[GodHand] Surgery error: {ex}");
                patient.BillStack?.Delete(bill);
            }

            foreach (var ing in currentSurgeryIngredients) if (!ing.Destroyed) ing.Destroy();
        }

        public override void ForceEnd()
        {
            base.ForceEnd();
            ReturnItems(targetedMedicine);
            if (currentSurgeryIngredients != null)
                foreach (var t in currentSurgeryIngredients) ReturnItems(t);

            foreach (var t in vacuumTasks) t.ForceEnd();
        }

        private void ReturnItems(Thing t)
        {
            if (t != null && !t.Destroyed && !t.Spawned)
                GenPlace.TryPlaceThing(t, map.Center, map, ThingPlaceMode.Near);
        }
    }

    public class GodHandMedicalTask_Pawn : GodHandMedicalTask
    {
        public GodHandMedicalTask_Pawn(Pawn patient) : base(patient.Map) => Patient = patient;

        public override void Tick()
        {
            if (IsFinished) return;
            if (Patient == null || Patient.Dead || Patient.Map != map)
            {
                ForceEnd();
                return;
            }
            if (curState == State.Idle) curState = State.Scanning;
            TickMedicalLogic();
        }

        public override void Update(float deltaTime)
        {
            if (IsFinished) return;
            if (curState == State.Vacuuming)
            {
                foreach (var t in vacuumTasks) t.TargetPos = Patient.DrawPos + new Vector3(0, 1, 0);
                for (int i = vacuumTasks.Count - 1; i >= 0; i--) vacuumTasks[i].Update(deltaTime);
            }
            orbPulse += deltaTime;
        }

        protected override void OnMedicalProcessComplete() => IsFinished = true;
        protected override void OnNothingToDo() => IsFinished = true;

        public override void Draw()
        {
            if (Patient == null) return;
            Vector3 center = Patient.DrawPos;
            center.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + 0.5f;

            float time = (Find.TickManager.TicksGame / 60f) + orbPulse;
            float size = 1.0f + Mathf.Sin(time * 3f) * 0.15f;

            Matrix4x4 m = Matrix4x4.TRS(center, Quaternion.AngleAxis(time * 50f, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, m, OrbMatBlue, 0);

            foreach (var t in vacuumTasks) t.Draw();
        }
    }

    public class GodHandMedicalTask_Bed : GodHandMedicalTask
    {
        public Building_Bed Bed;
        public GodHandMedicalTask_Bed(Building_Bed bed) : base(bed.Map) => Bed = bed;

        public override void Tick()
        {
            if (Bed == null || Bed.Destroyed || !Bed.Spawned)
            {
                ForceEnd();
                return;
            }

            if (Patient == null || Patient.Dead || Patient.CurrentBed() != Bed || Patient.Map != map)
            {
                Patient = null;
                curState = State.Idle;

                Pawn occupant = Bed.CurOccupants.FirstOrDefault(p => p.Faction == Faction.OfPlayer);
                if (occupant != null)
                {
                    Patient = occupant;
                    curState = State.Scanning;
                }
            }

            if (Patient != null)
            {
                if (curState == State.Idle) curState = State.Scanning;
                TickMedicalLogic();
            }
        }

        public override void Update(float deltaTime)
        {
            if (Bed == null) return;
            orbPulse += deltaTime;

            if (curState == State.Vacuuming && Patient != null)
            {
                foreach (var t in vacuumTasks) t.TargetPos = Patient.DrawPos + new Vector3(0, 1, 0);
                for (int i = vacuumTasks.Count - 1; i >= 0; i--) vacuumTasks[i].Update(deltaTime);
            }
        }

        protected override void OnMedicalProcessComplete() => curState = State.Idle;
        protected override void OnNothingToDo() => curState = State.Idle;

        public override void Draw()
        {
            if (Bed == null) return;
            Vector3 center = Bed.Position.ToVector3Shifted();
            center.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + 0.5f;

            float time = (Find.TickManager.TicksGame / 60f) + orbPulse;
            float size = 0.8f + Mathf.Sin(time * 2f) * 0.1f;

            Matrix4x4 m = Matrix4x4.TRS(center, Quaternion.AngleAxis(time * 30f, Vector3.up), new Vector3(size, 1, size));
            Graphics.DrawMesh(MeshPool.plane10, m, OrbMatBlue, 0);

            if (Patient != null) foreach (var t in vacuumTasks) t.Draw();
        }
    }
}
