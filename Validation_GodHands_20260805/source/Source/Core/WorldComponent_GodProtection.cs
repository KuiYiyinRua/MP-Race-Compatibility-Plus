using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace GodHandMod
{
    // 全局神佑组件
    public class WorldComponent_GodProtection : WorldComponent
    {
        // 待恢复队列
        private List<VoidRecoveryData> recoveryQueue = new List<VoidRecoveryData>();

        // 持续追踪的流浪神佑单位
        private List<Pawn> lostPawns = new List<Pawn>();

        // 显式维护的受保护 Pawn 白名单 (空间锚定核心)
        private HashSet<Pawn> protectedPawns = new HashSet<Pawn>();

        public WorldComponent_GodProtection(World world) : base(world) { }

        // 管理受保护状态
        public bool IsProtected(Pawn p) => p != null && protectedPawns.Contains(p);
        public void RegisterProtection(Pawn p) 
        {
            if (p != null && !protectedPawns.Contains(p)) protectedPawns.Add(p);
        }
        public void UnregisterProtection(Pawn p) => protectedPawns.Remove(p);

        public void RequestRecovery(Pawn p, Map lastMap, IntVec3 lastPos)
        {
            if (p == null) return;
            if (recoveryQueue.Any(x => x.pawn == p)) return;
            if (lostPawns.Contains(p)) return;

            recoveryQueue.Add(new VoidRecoveryData
            {
                pawn = p,
                lastMap = lastMap,
                lastPos = lastPos,
                registerTick = Find.TickManager.TicksGame
            });
        }

        public override void WorldComponentTick()
        {
            int currentTick = Find.TickManager.TicksGame;

            // 处理恢复队列
            for (int i = recoveryQueue.Count - 1; i >= 0; i--)
            {
                var data = recoveryQueue[i];
                if (currentTick > data.registerTick)
                {
                    try
                    {
                        ProcessRecovery(data);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[GodHand] 恢复失败 {data.pawn} {ex}");
                    }
                    recoveryQueue.RemoveAt(i);
                }
            }

            // 周期性检查流浪单位
            if (currentTick % 200 == 0 && lostPawns.Count > 0)
            {
                for (int i = lostPawns.Count - 1; i >= 0; i--)
                {
                    Pawn p = lostPawns[i];
                    if (TryRespawnOnAnyMap(p))
                    {
                        lostPawns.RemoveAt(i);
                    }
                }
            }
        }

        private void ProcessRecovery(VoidRecoveryData data)
        {
            Pawn p = data.pawn;
            // 允许恢复已销毁的单位：如果单位已被标记为销毁，强制重置其销毁状态以便重新生成
            // 这样我们就可以安全地让出 Destroy 补丁，彻底解决与载具框架的兼容性问题
            if (p.Destroyed)
            {
                var field = typeof(Thing).GetField("destroyed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                field?.SetValue(p, false);
            }

            // 检查当前状态安全
            bool isSafe = p.Spawned || p.holdingOwner != null;
            if (!isSafe && Find.WorldPawns.Contains(p))
            {
                if (p.IsCaravanMember()) isSafe = true;
            }

            if (isSafe) return;

            // 尝试回到原地图
            if (data.lastMap != null && !data.lastMap.Index.Equals(-1) && data.lastPos.InBounds(data.lastMap))
            {
                GenSpawn.Spawn(p, data.lastPos, data.lastMap, WipeMode.Vanish);
                OnRecovered(p);
                return;
            }

            // 尝试回到任意玩家地图
            if (TryRespawnOnAnyMap(p)) return;

            // 加入流浪列表
            if (!lostPawns.Contains(p))
            {
                lostPawns.Add(p);
                // 确保在世界角色管理器中
                if (!Find.WorldPawns.Contains(p))
                {
                    Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
                }
                Messages.Message("GodHand.Protection.DriftingInVoid".Translate(p.LabelShort), MessageTypeDefOf.CautionInput);
            }
        }

        private bool TryRespawnOnAnyMap(Pawn p)
        {
            // 查找玩家定居点
            Map targetMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (targetMap == null) targetMap = Find.Maps.FirstOrDefault();

            if (targetMap != null)
            {
                IntVec3 spot = DropCellFinder.TradeDropSpot(targetMap);
                GenSpawn.Spawn(p, spot, targetMap, WipeMode.Vanish);
                OnRecovered(p);
                return true;
            }
            return false;
        }

        private void OnRecovered(Pawn p)
        {
            // 移出世界角色列表
            if (Find.WorldPawns.Contains(p))
            {
                Find.WorldPawns.RemovePawn(p);
            }

            // 强制修正派系
            if (p.Faction != Faction.OfPlayer)
            {
                p.SetFaction(Faction.OfPlayer);
                Log.Message($"[GodHand] 恢复单位派系至玩家 {p.LabelShort}");
            }

            Patch_GodProtection_Pawn_Kill.PlayTearSpaceEffect(p);
            Patch_GodProtection_Pawn_Kill.HealAndReset(p);
            Messages.Message("GodHand.Protection.SpaceAnchoredReturned".Translate(p.LabelShort), p, MessageTypeDefOf.PositiveEvent, true);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref recoveryQueue, "recoveryQueue", LookMode.Deep);
            Scribe_Collections.Look(ref lostPawns, "lostPawns", LookMode.Reference);
            Scribe_Collections.Look(ref protectedPawns, "protectedPawns", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (recoveryQueue == null) recoveryQueue = new List<VoidRecoveryData>();
                if (lostPawns == null) lostPawns = new List<Pawn>();
                if (protectedPawns == null) protectedPawns = new HashSet<Pawn>();
            }
        }
    }

    public class VoidRecoveryData : IExposable
    {
        public Pawn pawn;
        public Map lastMap;
        public IntVec3 lastPos;
        public int registerTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref lastMap, "lastMap");
            Scribe_Values.Look(ref lastPos, "lastPos");
            Scribe_Values.Look(ref registerTick, "registerTick");
        }
    }
}
