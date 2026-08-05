using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace GodHandMod
{
    // 自定义飞行器落地伤害
    public class GodHandPawnFlyer : PawnFlyer
    {
        private float pendingDamage;

        // 反射字段缓存
        private static FieldInfo destCellField;
        private static FieldInfo flightDistanceField;

        // 保存/加载数据
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pendingDamage, "pendingDamage", 0f);
        }

        static GodHandPawnFlyer()
        {
            // 初始化反射字段
            destCellField = typeof(PawnFlyer).GetField("destCell", BindingFlags.NonPublic | BindingFlags.Instance);
            flightDistanceField = typeof(PawnFlyer).GetField("flightDistance", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public void SetPendingDamage(float damage)
        {
            this.pendingDamage = damage;
        }

        // 渲染位置始终保持头部偏移
        public void InitializeFlightParams(Vector3 startPosition, IntVec3 destination, float distance)
        {
            // 直接访问保护成员
            this.startVec = startPosition;

            // 反射设置私有成员
            destCellField?.SetValue(this, destination);
            flightDistanceField?.SetValue(this, distance);
        }

        protected override void RespawnPawn()
        {
            // 保存引用防止容器移除
            Pawn pawnToLand = FlyingPawn;

            // 防御检查防崩溃
            if (pawnToLand == null)
            {
                GodHandModMain.DebugLog($"[神之手] GodHandPawnFlyer.RespawnPawn: FlyingPawn 为空，跳过落地逻辑以防止崩溃");
                return;
            }

            GodHandModMain.DebugLog($"[神之手] GodHandPawnFlyer.RespawnPawn 被调用，pendingDamage={pendingDamage}, FlyingPawn={pawnToLand.LabelCap}");

            // 调用基类方法先落地
            base.RespawnPawn();

            // 然后应用伤害
            if (pendingDamage > 0f && pawnToLand != null && !pawnToLand.Dead)
            {
                GodHandModMain.DebugLog($"[神之手] 准备对 {pawnToLand.LabelCap} 应用 {pendingDamage:F1} 点伤害");

                DamageInfo damageInfo = new DamageInfo(
                    DamageDefOf.Blunt,
                    pendingDamage,
                    0f,
                    -1f,
                    null,
                    null,
                    null,
                    DamageInfo.SourceCategory.ThingOrUnknown
                );

                pawnToLand.TakeDamage(damageInfo);

                GodHandModMain.DebugLog($"[神之手] {pawnToLand.LabelCap} 落地，受到 {pendingDamage:F1} 点伤害");
            }
            else
            {
                GodHandModMain.DebugLog($"[神之手] 未应用伤害: pendingDamage={pendingDamage}, pawnToLand={pawnToLand?.LabelCap ?? "null"}, Dead={pawnToLand?.Dead ?? false}");
            }
        }
    }
}
