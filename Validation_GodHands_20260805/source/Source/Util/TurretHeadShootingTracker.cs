using Verse;
using UnityEngine;
using System.Collections.Generic;

namespace GodHandMod
{
    // 追踪炮塔头射击状态
    public static class TurretHeadShootingTracker
    {
        // 炮塔头射击列表
        private static HashSet<Pawn> pawnsWithActiveTurretHeadShooting = new HashSet<Pawn>();

        // 存储当前正在使用浮游炮塔射击的Pawn列表
        private static HashSet<Pawn> pawnsWithFloatingTurretShooting = new HashSet<Pawn>();

        public static void RegisterFloatingTurretShooting(Pawn pawn)
        {
            if (pawn != null)
                pawnsWithFloatingTurretShooting.Add(pawn);
        }

        public static void UnregisterFloatingTurretShooting(Pawn pawn)
        {
            if (pawn != null)
                pawnsWithFloatingTurretShooting.Remove(pawn);
        }

        public static bool IsFloatingTurretShooting(Pawn pawn)
        {
            return pawn != null && pawnsWithFloatingTurretShooting.Contains(pawn);
        }

        // 检查任何炮塔射击状态
        public static bool IsAnyTurretShooting(Pawn pawn)
        {
            return IsTurretHeadShooting(pawn) || IsFloatingTurretShooting(pawn);
        }

        // 存储当前射击槽位
        private static Dictionary<Verb, TurretSlot> verbToSlot = new Dictionary<Verb, TurretSlot>();

        // 存储当前发射槽位
        private static TurretSlot currentLaunchingSlot = null;

        public static void RegisterVerbSlot(Verb verb, TurretSlot slot)
        {
            if (verb != null && slot != null)
                verbToSlot[verb] = slot;
        }

        public static void UnregisterVerbSlot(Verb verb)
        {
            if (verb != null)
                verbToSlot.Remove(verb);
        }

        public static TurretSlot GetSlotForVerb(Verb verb)
        {
            if (verb != null && verbToSlot.TryGetValue(verb, out var slot))
                return slot;
            return null;
        }

        public static void SetCurrentLaunchingSlot(TurretSlot slot)
        {
            currentLaunchingSlot = slot;
        }

        public static TurretSlot GetCurrentLaunchingSlot()
        {
            return currentLaunchingSlot;
        }

        public static void ClearCurrentLaunchingSlot()
        {
            currentLaunchingSlot = null;
        }

        // 获取炮口发射位置
        public static Vector3 GetMuzzlePosition(Pawn pawn, TurretSlot slot)
        {
            if (pawn == null || slot == null) return Vector3.zero;

            // 获取头部偏移
            Vector3 pos = pawn.DrawPos;
            pos += pawn.Drawer.renderer.BaseHeadOffsetAt(pawn.Rotation);

            // 添加堆叠偏移
            pos += slot.DrawOffset;

            // 添加炮口前向偏移
            float turretSize = slot.TurretDrawSize;
            float muzzleForwardOffset = turretSize * 0.4f; // 计算炮口位置
            float rotationRad = slot.curRotation * Mathf.Deg2Rad;
            pos.x += Mathf.Sin(rotationRad) * muzzleForwardOffset;
            pos.z += Mathf.Cos(rotationRad) * muzzleForwardOffset;

            // 应用炮管偏移
            if (slot.hasTopGunSystem && slot.topGuns != null && slot.topGuns.Count > 0)
            {
                Vector3 gunOffset = slot.topGuns[0].offsetGun;
                pos += Quaternion.Euler(0, slot.curRotation, 0) * gunOffset;
            }

            return pos;
        }

        // 获取射击位置偏移
        public static Vector3 GetShootingOffset(Verb verb)
        {
            var slot = GetSlotForVerb(verb);
            if (slot != null)
            {
                return slot.DrawOffset;
            }
            return Vector3.zero;
        }

        public static void RegisterShooting(Pawn pawn)
        {
            if (pawn != null)
                pawnsWithActiveTurretHeadShooting.Add(pawn);
        }

        public static void UnregisterShooting(Pawn pawn)
        {
            if (pawn != null)
                pawnsWithActiveTurretHeadShooting.Remove(pawn);
        }

        public static bool IsTurretHeadShooting(Pawn pawn)
        {
            return pawn != null && pawnsWithActiveTurretHeadShooting.Contains(pawn);
        }

        // 检查是否来自炮塔头
        public static bool IsVerbFromTurretHead(Verb verb)
        {
            if (verb == null || verb.caster == null) return false;

            // 检查炮塔头归属
            if (verb.caster is Pawn pawn && pawn.apparel?.WornApparel != null)
            {
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    if (apparel is GodHandTurretHead turretHead)
                    {
                        // 检查这个verb是否属于炮塔头的gun
                        var turretGun = turretHead.Gun;
                        if (turretGun != null)
                        {
                            var comp = turretGun.TryGetComp<CompEquippable>();
                            if (comp != null && comp.AllVerbs.Contains(verb))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
