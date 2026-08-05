using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod
{
    // 流场绝杀组件
    public class MapComponent_GodJudgmentField : MapComponent
    {
        private List<SuddenDeathZone> activeZones = new List<SuddenDeathZone>();
        private List<Pawn> tmpVictims = new List<Pawn>();

        public MapComponent_GodJudgmentField(Map map) : base(map) { }

        public void RegisterSuddenDeathField(IntVec3 center, float radius, int duration)
        {
            activeZones.Add(new SuddenDeathZone { center = center, radius = radius, ticksLeft = duration });
        }

        public override void MapComponentTick()
        {
            if (activeZones.Count == 0) return;

            for (int i = activeZones.Count - 1; i >= 0; i--)
            {
                var zone = activeZones[i];
                zone.ticksLeft--;

                // 定期扫描内容物强制绝杀
                if (Find.TickManager.TicksGame % 10 == 0)
                {
                    tmpVictims.Clear();
                    var allSpawned = map.mapPawns.AllPawnsSpawned;
                    for (int j = 0; j < allSpawned.Count; j++)
                    {
                        Pawn p = allSpawned[j];
                        if (p.Position.DistanceTo(zone.center) <= zone.radius)
                        {
                            tmpVictims.Add(p);
                        }
                    }

                    foreach (var p in tmpVictims)
                    {
                        JudgmentGodHandController.CommitSuddenDeath(p);
                    }
                }

                if (zone.ticksLeft <= 0) activeZones.RemoveAt(i);
            }
        }

        private class SuddenDeathZone
        {
            public IntVec3 center;
            public float radius;
            public int ticksLeft;
        }
    }
}
