using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // 物品抓取处理器
    public class GodHandItemHandler
    {
        private readonly GodHandController core;

        public GodHandItemHandler(GodHandController core) => this.core = core;

        public void OnGrabbed(Thing t)
        {
            if (t == null) return;
            if (t.Spawned) t.DeSpawn(DestroyMode.Vanish);
        }

        public void Draw()
        {
            Vector3 mousePos = UI.MouseMapPosition();
            mousePos.y = AltitudeLayer.Skyfaller.AltitudeFor();

            int count = core.GrabbedThings.Count;
            for (int i = 0; i < count; i++)
            {
                Thing t = core.GrabbedThings[i];
                if (t == null || t.Destroyed) continue;

                core.GrabOffsets.TryGetValue(t, out Vector3 offset);
                Vector3 drawPos = mousePos + offset;
                drawPos.y = mousePos.y;

                t.Graphic?.Draw(drawPos, t.Rotation, t);
            }
        }

        public void Release(Map map, IntVec3 cell)
        {
            Vector3 mousePos = UI.MouseMapPosition();
            foreach (var t in core.GrabbedThings)
            {
                if (t == null || t.Destroyed) continue;
                core.GrabOffsets.TryGetValue(t, out Vector3 offset);
                IntVec3 targetCell = (mousePos + offset).ToIntVec3();
                if (!targetCell.InBounds(map)) targetCell = core.GrabStartCell;
                GenPlace.TryPlaceThing(t, targetCell, map, ThingPlaceMode.Near);
                if (t.Spawned) TryInteract(map, t);
            }
        }

        private void TryInteract(Map map, Thing t)
        {
            if (!t.def.IsIngestible || !GodHandModMain.Settings.godHandEnableForceIngest) return;
            Pawn p = GrabbingUtils.GetPawnAt(map, t.Position);
            if (p == null || !p.RaceProps.CanEverEat(t.def)) return;

            Job job = JobMaker.MakeJob(JobDefOf.Ingest, t);
            job.count = 1;
            p.jobs?.TryTakeOrderedJob(job, JobTag.Misc);

            GodHandMemoryTracker.AddMemory(p, GodHandModMain.Settings.GetCustomMemory("GodHand.Memory.ForcedIngest", t.LabelShort));
            GodHandRimTalkIntegration.TryTriggerConversation(p, "GodHand.RimTalk.Prompt.ForcedIngest");
        }

        public void ForceClear() { }
    }
}
