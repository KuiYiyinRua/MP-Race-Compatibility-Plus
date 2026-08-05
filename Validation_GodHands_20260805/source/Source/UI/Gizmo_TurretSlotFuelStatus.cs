using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 炮塔槽位燃料状态
    [StaticConstructorOnStartup]
    public class Gizmo_TurretSlotFuelStatus : Gizmo
    {
        public TurretSlot turretSlot;
        public int slotIndex;

        private static readonly Texture2D FullBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.35f, 0.35f, 0.2f));
        private static readonly Texture2D EmptyBarTex = SolidColorMaterials.NewSolidColorTexture(Color.black);
        private static readonly Texture2D LowFuelBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.5f, 0.2f, 0.2f));

        public Gizmo_TurretSlotFuelStatus()
        {
            this.Order = -100f;
        }

        public override float GetWidth(float maxWidth)
        {
            return 140f;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect overRect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Find.WindowStack.ImmediateWindow(1523289473 + slotIndex, overRect, WindowLayer.GameUI, delegate
            {
                Rect rect = overRect.AtZero().ContractedBy(6f);

                // 标题区域
                Rect labelRect = rect;
                labelRect.height = overRect.height / 2f;
                Text.Font = GameFont.Tiny;

                // 显示炮塔名称和燃料类型
                string turretName = turretSlot.sourceTurretDef?.LabelCap ?? "炮塔";
                string fuelLabel = turretSlot.fuelThingDef?.LabelCap ?? "弹药";
                Widgets.Label(labelRect, $"[{slotIndex + 1}] {turretName}: {fuelLabel}");

                // 燃料条区域
                Rect barRect = rect;
                barRect.yMin = overRect.height / 2f;

                float fillPercent = turretSlot.fuelCapacity > 0 ? turretSlot.fuel / turretSlot.fuelCapacity : 0f;

                // 燃料不足时使用红色条
                Texture2D barTex = fillPercent < 0.2f ? LowFuelBarTex : FullBarTex;
                Widgets.FillableBar(barRect, fillPercent, barTex, EmptyBarTex, false);

                // 显示燃料数值
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;

                // 显示剩余射击次数
                int remainingShots = turretSlot.consumeFuelPerShot > 0
                    ? Mathf.FloorToInt(turretSlot.fuel / turretSlot.consumeFuelPerShot)
                    : 0;
                int maxShots = turretSlot.consumeFuelPerShot > 0
                    ? Mathf.FloorToInt(turretSlot.fuelCapacity / turretSlot.consumeFuelPerShot)
                    : 0;

                string displayText = $"{remainingShots} / {maxShots}";
                Widgets.Label(barRect, displayText);

                Text.Anchor = TextAnchor.UpperLeft;
            }, true, false, 1f);

            return new GizmoResult(GizmoState.Clear);
        }
    }
}
