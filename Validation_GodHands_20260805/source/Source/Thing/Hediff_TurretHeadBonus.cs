using System.Text;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 炮塔之力提供的头部加成
    public class Hediff_TurretHeadBonus : HediffWithComps
    {
        // 缓存的加成数据
        private float cachedTotalBonus = 0f;
        private int cachedTurretCount = 0;
        private int lastUpdateTick = -1;

        public override string Label => "GodHand.Hediff.TurretPower".Translate();

        public override string LabelInBrackets
        {
            get
            {
                UpdateCache();
                if (cachedTurretCount > 0)
                {
                    return "GodHand.Hediff.TurretPowerBracket".Translate(cachedTurretCount, Mathf.FloorToInt(cachedTotalBonus));
                }
                return base.LabelInBrackets;
            }
        }

        private string cachedTipString;

        public override string TipStringExtra
        {
            get
            {
                UpdateCache();
                return cachedTipString;
            }
        } // 替换后的TipStringExra使用缓存

        public override bool ShouldRemove => !HasTurretHead();

        private bool HasTurretHead()
        {
            if (pawn?.apparel?.WornApparel == null) return false;

            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead turretHead && turretHead.TurretCount > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateCache()
        {
            // 更新缓存并检查
            if (Find.TickManager.TicksGame - lastUpdateTick < 60 && cachedTipString != null) return;
            lastUpdateTick = Find.TickManager.TicksGame;

            cachedTotalBonus = 0f;
            cachedTurretCount = 0;
            StringBuilder sb = new StringBuilder();

            if (pawn?.apparel?.WornApparel != null)
            {
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    if (apparel is GodHandTurretHead turretHead && turretHead.TurretSlots != null)
                    {
                        cachedTurretCount = turretHead.TurretCount;

                        // 计算总HP
                        float totalTurretHP = 0f;
                        foreach (var slot in turretHead.TurretSlots)
                        {
                            if (slot.sourceTurretDef != null)
                            {
                                totalTurretHP += slot.sourceTurretDef.GetStatValueAbstract(StatDefOf.MaxHitPoints);
                            }
                        }
                        cachedTotalBonus = totalTurretHP / 2f;

                        // 构建提示字符串
                        // 计算头部所有部位的原始HP总和
                        float headTotalBaseHP = 0f;
                        List<BodyPartRecord> headParts = new List<BodyPartRecord>();

                        foreach (var part in pawn.RaceProps.body.AllParts)
                        {
                            if (TurretHeadHPBonus.IsHeadOrChildOfHead(part))
                            {
                                headTotalBaseHP += part.def.hitPoints;
                                headParts.Add(part);
                            }
                        }

                        // 显示已安装的炮塔
                        sb.AppendLine("GodHand.Hediff.InstalledTurrets".Translate());
                        foreach (var slot in turretHead.TurretSlots)
                        {
                            if (slot.sourceTurretDef != null)
                            {
                                sb.AppendLine($"  - {slot.sourceTurretDef.LabelCap}");
                            }
                        }

                        // 显示各部位的具体加成
                        sb.AppendLine();
                        sb.AppendLine("GodHand.Hediff.PartBonusDetail".Translate());
                        foreach (var part in headParts)
                        {
                            float partBonus = (headTotalBaseHP > 0)
                                ? cachedTotalBonus * (part.def.hitPoints / headTotalBaseHP)
                                : 0f; // 基础增加一半每槽位加成
                            sb.AppendLine($"  {part.def.LabelCap}: +{Mathf.FloorToInt(partBonus)}");
                        }

                        break;
                    }
                }
            }

            cachedTipString = sb.ToString().TrimEndNewlines();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // 不需要保存缓存
        }
    }
}
