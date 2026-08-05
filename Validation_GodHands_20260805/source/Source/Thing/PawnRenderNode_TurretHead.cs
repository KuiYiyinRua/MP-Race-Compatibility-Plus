using Verse;
using UnityEngine;
using System.Collections.Generic;
using RimWorld;

namespace GodHandMod
{
    // 炮塔头渲染节点系统

    // 炮塔头渲染节点逻辑
    // 炮塔主体渲染节点
    public class PawnRenderNode_TurretHead : PawnRenderNode
    {
        public TurretSlot turretSlot;
        public int slotIndex;

        // 缓存的图形
        private Graphic cachedTurretGraphic;

        // 内部构造函数
        public PawnRenderNode_TurretHead(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree, TurretSlot slot, int index)
            : base(pawn, props, tree)
        {
            this.turretSlot = slot;
            this.slotIndex = index;
        }

        // 标准构造函数转换
        public PawnRenderNode_TurretHead(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
            // 从服饰获取槽位信息
            // 用于反射兼容
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            // 使用标准的1x1 plane mesh
            return MeshPool.GetMeshSetForSize(1f, 1f);
        }

        public override Mesh GetMesh(PawnDrawParms parms)
        {
            // 始终使用 plane10 mesh
            return MeshPool.plane10;
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (cachedTurretGraphic != null)
                return cachedTurretGraphic;

            if (turretSlot?.sourceTurretDef?.building?.turretGunDef?.graphicData != null)
            {
                string texPath = turretSlot.sourceTurretDef.building.turretGunDef.graphicData.texPath;
                float size = turretSlot.sourceTurretDef.building.turretTopDrawSize;

                cachedTurretGraphic = GraphicDatabase.Get<Graphic_Single>(
                    texPath,
                    ShaderDatabase.Cutout,
                    new Vector2(size, size),
                    Color.white);
            }

            return cachedTurretGraphic;
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            var graphic = GraphicFor(pawn);
            if (graphic != null)
            {
                yield return graphic;
            }
        }
    }

    // 炮塔渲染工作类
    public class PawnRenderNodeWorker_TurretHead : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
                return false;

            // 确保炮塔槽位有效
            if (node is PawnRenderNode_TurretHead turretNode)
            {
                return turretNode.turretSlot?.sourceTurretDef != null;
            }
            return false;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            if (node is PawnRenderNode_TurretHead turretNode && turretNode.turretSlot != null)
            {
                // 使用炮塔实际角度
                if (!parms.Portrait)
                {
                    float rotation = turretNode.turretSlot.curRotation - 90f; // 调整角度偏移
                    return Quaternion.AngleAxis(rotation, Vector3.up);
                }
                else
                {
                    // 肖像模式下使用固定角度
                    return Quaternion.AngleAxis(0f, Vector3.up);
                }
            }
            return base.RotationFor(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            pivot = Vector3.zero;
            Vector3 offset = base.OffsetFor(node, parms, out pivot);

            if (node is PawnRenderNode_TurretHead turretNode && turretNode.turretSlot != null)
            {
                // 手动添加头部偏移
                if (parms.pawn?.Drawer?.renderer != null)
                {
                    offset += parms.pawn.Drawer.renderer.BaseHeadOffsetAt(parms.facing);
                }

                // 添加炮塔自身的堆叠垂直偏移
                offset.z += turretNode.turretSlot.HeightOffset;
            }

            return offset;
        }

        // 高度方法不可重写
        // 通过基础层控制高度

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            if (node is PawnRenderNode_TurretHead turretNode && turretNode.turretSlot != null)
            {
                float size = turretNode.turretSlot.TurretDrawSize;
                return new Vector3(size, 1f, size);
            }
            return base.ScaleFor(node, parms);
        }
    }

    // 残阳炮管渲染节点
    public class PawnRenderNode_TurretTopGun : PawnRenderNode
    {
        public TurretSlot turretSlot;
        public TopGunData topGunData;
        public int gunIndex;

        private Graphic cachedGraphic;

        public PawnRenderNode_TurretTopGun(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree,
            TurretSlot slot, TopGunData gunData, int index)
            : base(pawn, props, tree)
        {
            this.turretSlot = slot;
            this.topGunData = gunData;
            this.gunIndex = index;
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return MeshPool.GetMeshSetForSize(1f, 1f);
        }

        public override Mesh GetMesh(PawnDrawParms parms)
        {
            return MeshPool.plane10;
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (cachedGraphic != null)
                return cachedGraphic;

            if (topGunData?.graphicDataGun != null)
            {
                try
                {
                    cachedGraphic = topGunData.graphicDataGun.Graphic;
                }
                catch { }
            }

            return cachedGraphic;
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            var graphic = GraphicFor(pawn);
            if (graphic != null)
            {
                yield return graphic;
            }
        }
    }

    // 残阳炮管渲染工作类
    public class PawnRenderNodeWorker_TurretTopGun : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
                return false;

            if (node is PawnRenderNode_TurretTopGun gunNode)
            {
                return gunNode.turretSlot?.sourceTurretDef != null && gunNode.topGunData != null;
            }
            return false;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            if (node is PawnRenderNode_TurretTopGun gunNode && gunNode.turretSlot != null)
            {
                if (!parms.Portrait)
                {
                    // 炮管跟随炮塔旋转
                    // 参考原版旋转实现方式
                    float rotation = gunNode.turretSlot.curRotation - 90f;
                    return Quaternion.AngleAxis(rotation, Vector3.up);
                }
                else
                {
                    return Quaternion.AngleAxis(0f, Vector3.up);
                }
            }
            return base.RotationFor(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            pivot = Vector3.zero;
            Vector3 offset = base.OffsetFor(node, parms, out pivot);

            if (node is PawnRenderNode_TurretTopGun gunNode && gunNode.turretSlot != null)
            {
                // 添加头部偏移
                if (parms.pawn?.Drawer?.renderer != null)
                {
                    offset += parms.pawn.Drawer.renderer.BaseHeadOffsetAt(parms.facing);
                }

                // 添加炮塔堆叠垂直偏移
                offset.z += gunNode.turretSlot.HeightOffset;

                // 设置炮管渲染层级偏移
                offset.y -= 0.7f;

                // 炮管偏移需要跟随炮塔旋转
                // 统一旋转计算方式
                if (!parms.Portrait && gunNode.topGunData != null)
                {
                    float curRotation = gunNode.turretSlot.curRotation;

                    // 炮管位置偏移量
                    Vector3 gunOffset = gunNode.topGunData.offsetGun;
                    offset += Quaternion.Euler(0, curRotation, 0) * gunOffset;

                    // 计算后坐力偏移
                    if (gunNode.topGunData.retarderV3 != Vector3.zero)
                    {
                        float retarderAngle = curRotation + gunNode.topGunData.angle;
                        offset += Quaternion.Euler(0, retarderAngle, 0) * gunNode.topGunData.retarderV3;
                    }
                }
            }

            return offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            // 参考绘图尺寸计算方式
            if (node is PawnRenderNode_TurretTopGun gunNode && gunNode.turretSlot?.sourceTurretDef != null)
            {
                float size = gunNode.turretSlot.TurretDrawSize;
                return new Vector3(size, 1f, size);
            }
            return base.ScaleFor(node, parms);
        }
    }
}
