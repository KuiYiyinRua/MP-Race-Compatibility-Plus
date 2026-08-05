using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod
{
    // 炮塔头渲染设置器渲染核心图形
    public class DynamicPawnRenderNodeSetup_TurretHead : DynamicPawnRenderNodeSetup
    {
        public override bool HumanlikeOnly => true;

        public override IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> GetDynamicNodes(Pawn pawn, PawnRenderTree tree)
        {
            if (pawn?.apparel?.WornApparel == null)
                yield break;

            GodHandTurretHead turretHead = null;
            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel is GodHandTurretHead th && th.TurretSlots.Count > 0)
                {
                    turretHead = th;
                    break;
                }
            }

            if (turretHead == null)
                yield break;

            PawnRenderNode parentNode = tree.rootNode;

            for (int i = 0; i < turretHead.TurretSlots.Count; i++)
            {
                var slot = turretHead.TurretSlots[i];
                if (slot.sourceTurretDef == null)
                    continue;

                // 每个槽位占用层级范围
                float slotBaseLayer = 90f + i * 2f;

                // 先渲染炮管
                if (slot.hasTopGunSystem && slot.topGuns != null && slot.topGuns.Count > 0)
                {
                    for (int g = 0; g < slot.topGuns.Count; g++)
                    {
                        var gunData = slot.topGuns[g];
                        if (gunData?.graphicDataGun == null)
                            continue;

                        var gunProps = new PawnRenderNodeProperties
                        {
                            debugLabel = $"TurretHead_Slot_{i}_Gun_{g}",
                            nodeClass = typeof(PawnRenderNode_TurretTopGun),
                            workerClass = typeof(PawnRenderNodeWorker_TurretTopGun),
                            baseLayer = slotBaseLayer - 0.5f + g * 0.1f, // 在炮塔下方
                            drawSize = Vector2.one,
                            pawnType = PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly
                        };

                        var gunNode = new PawnRenderNode_TurretTopGun(pawn, gunProps, tree, slot, gunData, g);
                        gunNode.apparel = turretHead;

                        yield return (gunNode, parentNode);
                    }
                }

                // 再渲染炮塔主体
                var props = new PawnRenderNodeProperties
                {
                    debugLabel = $"TurretHead_Slot_{i}",
                    nodeClass = typeof(PawnRenderNode_TurretHead),
                    workerClass = typeof(PawnRenderNodeWorker_TurretHead),
                    baseLayer = slotBaseLayer, // 炮塔在炮管上方
                    drawSize = Vector2.one,
                    pawnType = PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly
                };

                var turretNode = new PawnRenderNode_TurretHead(pawn, props, tree, slot, i);
                turretNode.apparel = turretHead;

                yield return (turretNode, parentNode);
            }
        }
    }
}
