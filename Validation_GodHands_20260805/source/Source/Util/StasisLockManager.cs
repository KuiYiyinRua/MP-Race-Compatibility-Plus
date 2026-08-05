using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace GodHandMod
{
    // 永恒停滞管理器
    public class StasisLockManager : WorldComponent
    {
        public static StasisLockManager Current;
        public static int activeLockCount = 0;
        public static HashSet<int> lockedPawnIDs = new HashSet<int>();

        // 存储处于停滞状态的 Pawn 及其结束 Tick
        private Dictionary<Pawn, int> pawnExpiryTicks = new Dictionary<Pawn, int>();

        public StasisLockManager(World world) : base(world)
        {
            Current = this;
        }

        public static StasisLockManager Instance => Current ?? Find.World?.GetComponent<StasisLockManager>();

        // 检查 Pawn 是否处于停滞状态
        public bool IsLocked(Pawn pawn)
        {
            if (pawn == null) return false;
            return pawnExpiryTicks.ContainsKey(pawn);
        }

        // 增加锁定
        public void Lock(Pawn pawn, int durationTicks = 60000)
        {
            if (pawn == null) return;

            pawnExpiryTicks[pawn] = Find.TickManager.TicksGame + durationTicks;
            activeLockCount = pawnExpiryTicks.Count;
            lockedPawnIDs.Add(pawn.thingIDNumber);

            // 同时给予描述性的 Hediff
            if (pawn.health != null && !pawn.health.hediffSet.HasHediff(GodHandDefOf.GodHand_StasisLock))
            {
                pawn.health.AddHediff(GodHandDefOf.GodHand_StasisLock);
            }
        }

        // 宽恕/解除锁定
        public void Unlock(Pawn pawn)
        {
            if (pawn == null) return;
            pawnExpiryTicks.Remove(pawn);
            activeLockCount = pawnExpiryTicks.Count;
            lockedPawnIDs.Remove(pawn.thingIDNumber);

            // 移除相关的 Hediff
            // 确保移除所有相关的 Hediff
            var hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs != null)
            {
                for (int i = hediffs.Count - 1; i >= 0; i--)
                {
                    if (hediffs[i].def == GodHandDefOf.GodHand_StasisLock)
                    {
                        pawn.health.RemoveHediff(hediffs[i]);
                    }
                }
            }

            // 强制刷新健康状态确保恢复
            pawn.health?.Notify_HediffChanged(null);
        }

        public override void WorldComponentTick()
        {
            // 每 250 Ticks 检查一次过期情况
            if (Find.TickManager.TicksGame % 250 == 0 && pawnExpiryTicks.Count > 0)
            {
                int currentTick = Find.TickManager.TicksGame;
                List<Pawn> toUnlock = new List<Pawn>();

                foreach (var kvp in pawnExpiryTicks)
                {
                    if (currentTick >= kvp.Value || kvp.Key == null || kvp.Key.Destroyed)
                    {
                        toUnlock.Add(kvp.Key);
                    }
                }

                foreach (var p in toUnlock)
                {
                    if (p != null && !p.Destroyed) Unlock(p);
                    else pawnExpiryTicks.Remove(p);
                }
                activeLockCount = pawnExpiryTicks.Count;
                lockedPawnIDs.Clear();
                foreach (var k in pawnExpiryTicks.Keys)
                {
                    if (k != null) lockedPawnIDs.Add(k.thingIDNumber);
                }
            }
        }

        private List<Pawn> pawnKeysWorkingList;
        private List<int> expiryValuesWorkingList;

        public override void ExposeData()
        {
            base.ExposeData();
            // 序列化字典
            Scribe_Collections.Look(ref pawnExpiryTicks, "pawnExpiryTicks", LookMode.Reference, LookMode.Value, ref pawnKeysWorkingList, ref expiryValuesWorkingList);
            if (pawnExpiryTicks == null) pawnExpiryTicks = new Dictionary<Pawn, int>();
            activeLockCount = pawnExpiryTicks.Count;
            lockedPawnIDs.Clear();
            foreach (var k in pawnExpiryTicks.Keys)
            {
                if (k != null) lockedPawnIDs.Add(k.thingIDNumber);
            }
            Current = this;
        }
    }
}
