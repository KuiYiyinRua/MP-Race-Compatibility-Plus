using Verse;
using RimWorld;
using System.Collections.Generic;

namespace GodHandMod
{
    // 追踪头部HP加成
    public static class TurretHeadHPBonus
    {
        // 存储HP加成与原始HP总和
        private struct HPBonusCache
        {
            public float turretBonus;      // 炮塔HP加成（总HP的一半）
            public float headTotalBaseHP;  // 头部所有部位原始HP总和
        }

        private static Dictionary<Pawn, HPBonusCache> cachedData = new Dictionary<Pawn, HPBonusCache>();

        // 获取部位分摊HP加成
        public static float GetPartHPBonus(Pawn pawn, BodyPartDef partDef, float partBaseHP)
        {
            if (pawn?.apparel?.WornApparel == null) return 0f;

            // 获取或计算缓存数据
            if (!cachedData.TryGetValue(pawn, out HPBonusCache cache))
            {
                cache = CalculateCache(pawn);
                cachedData[pawn] = cache;
            }

            if (cache.turretBonus <= 0f || cache.headTotalBaseHP <= 0f) return 0f;

            // 按比例分摊HP加成
            float ratio = partBaseHP / cache.headTotalBaseHP;
            return cache.turretBonus * ratio;
        }

        // 获取总HP加成显示
        public static float GetTotalBonus(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null) return 0f;

            if (!cachedData.TryGetValue(pawn, out HPBonusCache cache))
            {
                cache = CalculateCache(pawn);
                cachedData[pawn] = cache;
            }

            return cache.turretBonus;
        }

        // 计算数据缓存
        private static HPBonusCache CalculateCache(Pawn pawn)
        {
            HPBonusCache cache = new HPBonusCache();

            // 计算炮塔HP加成
            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead turretHead)
                {
                    cache.turretBonus = CalculateTurretHPBonus(turretHead);
                    break;
                }
            }

            // 计算头部原始HP总和
            if (pawn.health?.hediffSet != null)
            {
                foreach (var part in pawn.RaceProps.body.AllParts)
                {
                    if (IsHeadOrChildOfHead(part))
                    {
                        // 使用部位定义的基础HP（不考虑体型）
                        cache.headTotalBaseHP += part.def.hitPoints;
                    }
                }
            }

            return cache;
        }

        // 计算炮塔加成总量
        private static float CalculateTurretHPBonus(GodHandTurretHead turretHead)
        {
            if (turretHead == null || turretHead.TurretSlots == null) return 0f;

            float totalHP = 0f;
            foreach (var slot in turretHead.TurretSlots)
            {
                if (slot.sourceTurretDef != null)
                {
                    float turretMaxHP = slot.sourceTurretDef.GetStatValueAbstract(StatDefOf.MaxHitPoints);
                    totalHP += turretMaxHP;
                }
            }

            return totalHP / 2f;
        }

        // 清除HP加成缓存
        public static void InvalidateCache(Pawn pawn)
        {
            if (pawn != null)
            {
                cachedData.Remove(pawn);
            }
        }

        // 检查部位是否属头部
        public static bool IsHeadOrChildOfHead(BodyPartRecord part)
        {
            if (part == null) return false;

            BodyPartRecord current = part;
            while (current != null)
            {
                if (current.def.defName == "Head" ||
                    current.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
