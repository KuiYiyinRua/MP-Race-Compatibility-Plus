using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 神之庇护地图组件 管理护盾系统
    [StaticConstructorOnStartup]
    public class MapComponent_GodProtection : MapComponent
    {
        private List<GodShieldData> shields = new List<GodShieldData>();

        // 缓存材质
        // 缓存材质
        // 使用 GodHandResources 获取材质

        public MapComponent_GodProtection(Map map) : base(map) { }

        public void AddShield(IntVec3 pos, float radius, int duration, bool isDome, float angle)
        {
            shields.Add(new GodShieldData
            {
                pos = pos.ToVector3Shifted(),
                radius = radius,
                ticksLeft = duration,
                isDome = isDome,
                angle = angle,
                birthProgress = 0f
            });
        }

        public void EraseShieldsAt(IntVec3 pos, float radius)
        {
            Vector3 targetPos = pos.ToVector3Shifted();
            float rSqr = radius * radius;
            shields.RemoveAll(s => (s.pos - targetPos).sqrMagnitude <= rSqr);
            // 绘制清除效果
            FleckMaker.ThrowLightningGlow(targetPos, map, 0.5f);
        }

        public override void MapComponentTick()
        {
            for (int i = shields.Count - 1; i >= 0; i--)
            {
                var s = shields[i];
                s.ticksLeft--;

                // 定期检查敌对单位并弹飞
                if (map.IsHashIntervalTick(30) && s.isDome && s.birthProgress > 0.5f)
                {
                    RepelHostiles(s);
                }

                // 处理出生动画
                if (s.birthProgress < 1f)
                {
                    s.birthProgress += 1f / 60f;
                    if (s.birthProgress > 1f) s.birthProgress = 1f;
                }

                if (s.ticksLeft <= 0) shields.RemoveAt(i);
            }
        }

        private static readonly List<Pawn> tmpPawns = new List<Pawn>();

        private void RepelHostiles(GodShieldData s)
        {
            float rSqr = s.radius * s.radius;
            tmpPawns.Clear();

            // 收集符合条件的物体
            var allPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn p = allPawns[i];
                if (p.GetType().Name.Contains("Vehicle")) continue;

                if (p.Position.DistanceToSquared(s.pos.ToIntVec3()) <= rSqr)
                {
                    if (ShieldUtils.ShouldRepel(p, s))
                    {
                        tmpPawns.Add(p);
                    }
                }
            }

            // 统一处理避免集合修改异常
            for (int j = 0; j < tmpPawns.Count; j++)
            {
                EjectPawn(tmpPawns[j], s);
            }
            tmpPawns.Clear();
        }

        private void EjectPawn(Pawn p, GodShieldData s)
        {
            // 使用强化的安全落点查找逻辑
            IntVec3 dest = ShieldUtils.GetSafeRepelCell(map, p.DrawPos, s, 5.0f);

            // 使用自定义飞行器弹飞
            var flyer = (GodHandPawnFlyer)PawnFlyer.MakeFlyer(GodHandDefOf.GodHand_PawnFlyer, p, dest, null, null);
            if (flyer != null)
            {
                flyer.InitializeFlightParams(p.DrawPos, dest, p.Position.DistanceTo(dest));
                GenSpawn.Spawn(flyer, p.Position, map);
                // 屏蔽系统消息
            }
        }

        public override void MapComponentUpdate()
        {
            if (shields.Count == 0) return;

            foreach (var s in shields)
            {
                DrawShield(s);
            }
        }

        private static readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        private void DrawShield(GodShieldData s)
        {
            Material mat = GodHandResources.ShieldDomeMat;
            if (mat == null) return;

            float r = s.radius;
            Color color = s.isDome ? new Color(0.1f, 0.7f, 1.0f, 0.4f) : new Color(0.1f, 1.0f, 0.7f, 0.6f);
            color.a *= s.birthProgress;

            propertyBlock.Clear();
            propertyBlock.SetColor(ShaderPropertyIDs.Color, color);

            var dissolveShader = GodHandResources.DissolveShader;
            if (dissolveShader != null)
            {
                propertyBlock.SetFloat("_Dissolve", 1f - s.birthProgress);
                propertyBlock.SetFloat("_BlockSize", 25f);
                propertyBlock.SetFloat("_EdgeGlow", 5f);
                propertyBlock.SetFloat("_RadiusScale", s.radius);
            }

            Vector3 drawPos = s.pos;
            drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();

            float scale = r * 2f * 1.16f;
            Matrix4x4 matrix = Matrix4x4.TRS(drawPos, Quaternion.identity, new Vector3(scale, 1f, scale));

            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0, null, 0, propertyBlock);
        }

        private Material GetMaterial(bool isDome)
        {
            return GodHandResources.ShieldDomeMat;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // 保存护盾列表
            Scribe_Collections.Look(ref shields, "shields", LookMode.Deep);
            if (shields == null) shields = new List<GodShieldData>();
        }

        // 暴露接口供补丁检测
        public List<GodShieldData> ActiveShields => shields;
    }

    // 护盾数据类
    public class GodShieldData : IExposable
    {
        public Vector3 pos;
        public float radius;
        public int ticksLeft;
        public bool isDome;
        public float angle;
        public float birthProgress;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pos, "pos");
            Scribe_Values.Look(ref radius, "radius");
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
            Scribe_Values.Look(ref isDome, "isDome", true);
            Scribe_Values.Look(ref birthProgress, "birthProgress");
        }
    }
}
